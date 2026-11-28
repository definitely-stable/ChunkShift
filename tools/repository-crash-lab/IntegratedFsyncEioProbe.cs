using System.Security.Cryptography;
using System.Text.Json;
using ChunkShift.Repository;

internal static class IntegratedFsyncEioProbe
{
    private const string Name = "0123456789abcdef0123456789abcdef.prepare";
    private static readonly RawPackReadLimits Limits = new(1024 * 1024, 8, 544, 512 * 1024);

    internal static async Task RunAsync(string pinned, string parent)
    {
        Require(OperatingSystem.IsLinux() && NativeDirectoryBarrier.IsSupportedPlatform, "Unsupported ABI.");
        Require(new FileInfo(pinned).Length == 65850, "Unexpected fixture extent.");
        byte[] payload = File.ReadAllBytes(pinned);
        using var source = new MemoryStream(payload, writable: false);
        RawPackDescriptor descriptor = (await RawPackReader.VerifyAsync(source, Limits).ConfigureAwait(false)).Descriptor;
        source.Position = 0;
        byte[] initial = Array.ConvertAll(payload, static value => (byte)(value ^ 255));
        string initialSha = Convert.ToHexStringLower(SHA256.HashData(initial));
        int checkpoints = 0;
        bool laterStage = false;
        ExperimentalFilePreparation? attempt = null;
        attempt = new ExperimentalFilePreparation(new FilePreparationHooks
        {
            // Deliberate lab-only preallocation on the SAME actual writer,
            // before the default copy/flush, to obtain stable allocated blocks.
            Create = path =>
            {
                var writer = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                try
                {
                    writer.Write(initial);
                    writer.Flush(flushToDisk: true);
                    writer.Position = 0;
                    return writer;
                }
                catch
                {
                    writer.Dispose();
                    throw;
                }
            },
            Checkpoint = (stage, after) =>
            {
                if (stage is PersistencePreparationStage.CloseWriter or PersistencePreparationStage.VerifyClosedObject
                    or PersistencePreparationStage.FlushParentDirectory)
                {
                    laterStage = true;
                }

                if (stage != PersistencePreparationStage.CreateExclusive || !after)
                {
                    return;
                }

                var current = attempt ?? throw new InvalidOperationException("Missing attempt.");
                Require(++checkpoints == 1 && current.OwnershipConfirmed && current.ParentOwnershipConfirmed &&
                    source.Position == 0, "Incorrect allocation checkpoint.");
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    kind = "fsync-checkpoint", pid = Environment.ProcessId, path = current.CandidatePath,
                    state = current.State.ToString(), attempted = current.LastAttempted.ToString(),
                    completed = current.LastCompleted.ToString(), owned = current.OwnershipConfirmed,
                    writer_closed = current.WriterCloseConfirmed, reader_closed = current.ReaderCloseConfirmed,
                    parent_owned = current.ParentOwnershipConfirmed, parent_close_attempted = current.ParentCloseAttempted,
                    parent_closed = current.ParentCloseConfirmed, source_position = source.Position,
                    pack_sha256 = descriptor.PackDigest.Value.ToHexLower(), initial_sha256 = initialSha,
                }));
                Console.Out.Flush();
                Require(Console.ReadLine() == "continue", "Controller did not authorize copy.");
            },
        });
        try
        {
            await attempt.PreparePackWithDirectoryBarrierAsync(parent, Name, source, descriptor, Limits)
                .ConfigureAwait(false);
        }
        catch (IOException error) when (error.HResult == 5)
        {
            Require(checkpoints == 1 && !laterStage &&
                attempt.State == ExperimentalFilePreparationState.ReconciliationRequired &&
                attempt.LastAttempted == PersistencePreparationStage.FlushFile &&
                attempt.LastCompleted == PersistencePreparationStage.WriteVerifiedObject &&
                attempt.OwnershipConfirmed && attempt.WriterCloseConfirmed && !attempt.ReaderCloseConfirmed &&
                attempt.ParentOwnershipConfirmed && attempt.ParentCloseAttempted && attempt.ParentCloseConfirmed &&
                source.CanRead && source.Position == descriptor.PhysicalBytes, "Flush EIO lifecycle differs.");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                kind = "integrated-fsync-eio", state = attempt.State.ToString(),
                attempted = attempt.LastAttempted.ToString(), completed = attempt.LastCompleted.ToString(),
                owned = attempt.OwnershipConfirmed, writer_closed = attempt.WriterCloseConfirmed,
                reader_closed = attempt.ReaderCloseConfirmed, parent_owned = attempt.ParentOwnershipConfirmed,
                parent_close_attempted = attempt.ParentCloseAttempted, parent_closed = attempt.ParentCloseConfirmed,
                later_stage_started = laterStage, source_open = source.CanRead, source_position = source.Position,
                pack_sha256 = descriptor.PackDigest.Value.ToHexLower(), hresult = error.HResult, type = error.GetType().Name,
            }));
            return;
        }

        throw new InvalidOperationException("Faulted integrated flush unexpectedly succeeded.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
