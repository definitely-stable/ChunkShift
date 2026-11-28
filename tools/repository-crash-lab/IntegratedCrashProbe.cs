using System.Security.Cryptography;
using System.Text.Json;
using ChunkShift.Primitives;
using ChunkShift.Repository;

internal static class IntegratedCrashProbe
{
    private const string Name = "0123456789abcdef0123456789abcdef.prepare";
    private static readonly RawPackReadLimits Limits = new(1024 * 1024, 8, 544, 512 * 1024);

    internal static async Task RunAsync(string[] args)
    {
        Require(NativeDirectoryBarrier.IsSupportedPlatform, "Unsupported native ABI.");
        if (args is ["integrated-fixture", var suite, var source])
        {
            await FixtureAsync(suite, source).ConfigureAwait(false);
        }
        else if (args is ["integrated-build", var input, var parent, var boundary])
        {
            await BuildAsync(input, parent, boundary).ConfigureAwait(false);
        }
        else if (args is ["integrated-inspect", var expected, var candidate])
        {
            await InspectAsync(expected, candidate).ConfigureAwait(false);
        }
        else
        {
            throw new ArgumentException("Invalid integrated crash command.", nameof(args));
        }
    }

    private static async Task FixtureAsync(string suiteName, string path)
    {
        HashSuiteId suite = suiteName switch
        {
            "sha256" => HashSuiteIds.Sha256V1,
            "blake3" => HashSuiteIds.Blake3256V1,
            _ => throw new ArgumentException("Unknown suite.", nameof(suiteName)),
        };
        byte[] first = new byte[ExperimentalFilePreparation.BufferBytes + 7];
        for (int i = 0; i < first.Length; i++)
        {
            first[i] = unchecked((byte)(i * 31 + 7));
        }

        byte[] second = [1, 2, 3];
        using var output = new MemoryStream();
        using var writer = new PrivateRawPackWriter(output, new RawPackPolicy(1024 * 1024, 1024 * 1024, 8, 544), suite);
        using var firstSource = new MemoryStream(first, writable: false);
        using var secondSource = new MemoryStream(second, writable: false);
        Require(await writer.AppendAsync(firstSource, Key(first, suite), first.Length).ConfigureAwait(false) ==
            RawPackPlacement.Append, "First frame not appended.");
        Require(await writer.AppendAsync(secondSource, Key(second, suite), second.Length).ConfigureAwait(false) ==
            RawPackPlacement.Append, "Second frame not appended.");
        RawPackDescriptor descriptor = await writer.CompleteAsync().ConfigureAwait(false);
        byte[] bytes = output.ToArray();
        Require(Hash256.FromBytes(SHA256.HashData(bytes)) == descriptor.PackDigest.Value, "Fixture digest mismatch.");
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            file.Write(bytes);
            file.Flush(flushToDisk: true);
        }

        output.Position = 0;
        VerifiedRawPack verified = await RawPackReader.VerifyAsync(output, Limits, descriptor).ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            kind = "fixture", suite = suiteName, bytes = bytes.Length,
            pack_id = descriptor.PackId.Value.ToHexLower(), sha256 = descriptor.PackDigest.Value.ToHexLower(),
            frames = verified.GetLocations().Select(l => new
            {
                key = l.Key.ChunkId.Value.ToHexLower(), l.Ordinal, l.Offset, l.Length, l.Crc32C
            }).ToArray(),
        }));
    }

    private static async Task BuildAsync(string sourcePath, string parent, string boundary)
    {
        using var source = File.OpenRead(sourcePath);
        VerifiedRawPack verified = await RawPackReader.VerifyAsync(source, Limits).ConfigureAwait(false);
        source.Position = 0;
        ExperimentalFilePreparation? attempt = null;
        int writes = 0;
        void Stop(string point)
        {
            if (boundary != point)
            {
                return;
            }

            var current = attempt ?? throw new InvalidOperationException("Missing preparation attempt.");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                kind = "checkpoint", boundary, pid = Environment.ProcessId,
                path = current.CandidatePath, state = current.State.ToString(),
                attempted = current.LastAttempted.ToString(), completed = current.LastCompleted.ToString(),
                owned = current.OwnershipConfirmed, writer_closed = current.WriterCloseConfirmed,
                reader_closed = current.ReaderCloseConfirmed, parent_owned = current.ParentOwnershipConfirmed,
                parent_close_attempted = current.ParentCloseAttempted, parent_closed = current.ParentCloseConfirmed,
                pack_sha256 = verified.Descriptor.PackDigest.Value.ToHexLower(),
            }));
            Console.Out.Flush();
            using var gate = new ManualResetEventSlim(false);
            gate.Wait();
            throw new InvalidOperationException("Unexpected checkpoint continuation.");
        }

        attempt = new ExperimentalFilePreparation(new FilePreparationHooks
        {
            Checkpoint = (stage, after) => Stop($"{stage}:{(after ? "after" : "before")}"),
            Write = async (file, bytes, token) =>
            {
                await file.WriteAsync(bytes, token).ConfigureAwait(false);
                if (++writes == 1)
                {
                    Stop("WriteVerifiedObject:partial");
                }
            },
        });
        await attempt.PreparePackWithDirectoryBarrierAsync(parent, Name, source, verified.Descriptor, Limits)
            .ConfigureAwait(false);
        Require(attempt.State == ExperimentalFilePreparationState.ExperimentalPackPrepared &&
            attempt.WriterCloseConfirmed && attempt.ReaderCloseConfirmed && attempt.ParentCloseConfirmed,
            "Incomplete integrated preparation.");
        Stop("Prepared");
        Require(boundary == "none", "Requested checkpoint was not reached.");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            kind = "complete", state = attempt.State.ToString(),
            sha256 = verified.Descriptor.PackDigest.Value.ToHexLower(),
            writer_closed = attempt.WriterCloseConfirmed, reader_closed = attempt.ReaderCloseConfirmed,
            parent_closed = attempt.ParentCloseConfirmed,
        }));
    }

    private static async Task InspectAsync(string sourcePath, string path)
    {
        using var source = File.OpenRead(sourcePath);
        RawPackDescriptor expected = (await RawPackReader.VerifyAsync(source, Limits).ConfigureAwait(false)).Descriptor;
        using var candidate = File.OpenRead(path);
        bool accepted;
        try
        {
            VerifiedRawPack pack = await RawPackReader.VerifyAsync(candidate, Limits, expected).ConfigureAwait(false);
            Require(pack.Descriptor == expected, "Inspector descriptor mismatch.");
            accepted = true;
        }
        catch (Exception error) when (error is InvalidDataException or EndOfStreamException)
        {
            accepted = false;
        }

        candidate.Position = 0;
        string digest = Convert.ToHexStringLower(await SHA256.HashDataAsync(candidate).ConfigureAwait(false));
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            kind = "inspection", accepted, bytes = candidate.Length, sha256 = digest,
            expected_sha256 = expected.PackDigest.Value.ToHexLower(),
        }));
    }

    private static ChunkObjectKey Key(byte[] bytes, HashSuiteId suite)
    {
        using var digest = new ChunkDigest(suite);
        digest.Append(bytes);
        return new ChunkObjectKey(suite, digest.Finish());
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
