using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ChunkShift.Primitives;
using ChunkShift.Repository;

namespace ChunkShift.Repository.NativeAotProbe;

internal static class Program
{
    private const string Name = "0123456789abcdef0123456789abcdef.prepare";
    private static readonly RawPackReadLimits Limits = new(1024 * 1024, 8, 544, 512 * 1024);

    private static async Task Main(string[] args)
    {
        Require(args.Length == 2 && args[0] is "jit" or "aot", "Expected jit|aot and evidence path.");
        bool aot = args[0] == "aot";
        Require(RuntimeFeature.IsDynamicCodeSupported == !aot &&
            RuntimeFeature.IsDynamicCodeCompiled == !aot, "Unexpected dynamic code mode.");
        Require(NativeDirectoryBarrier.IsSupportedPlatform, "Unsupported native ABI.");
        Console.WriteLine($"mode={args[0]}; architecture={RuntimeInformation.ProcessArchitecture}; dynamic={RuntimeFeature.IsDynamicCodeSupported}");
        var rows = new List<string>();
        using var fixture = new Fixture();
        rows.Add(await FileCaseAsync(fixture.Case("file")));
        rows.Add(await PackCaseAsync(fixture.Case("sha256"), false));
        rows.Add(await PackCaseAsync(fixture.Case("blake3"), true));
        rows.Add(await CollisionAsync(fixture.Case("collision")));
        rows.Add(await WrongDescriptorAsync(fixture.Case("descriptor")));
        rows.Add(await ReplacementAsync(fixture.Case("replacement")));
        rows.Add(await CancellationAsync(fixture.Case("cancel")));
        rows.Add(NativeOpenFailure(fixture.Case("native")));
        // No success evidence is written until every case and fixture cleanup
        // completes. The workflow also checks the process exit code.
        fixture.Dispose();
        File.WriteAllLines(args[1], rows);
    }

    private static async Task<string> FileCaseAsync(string parent)
    {
        byte[] bytes = Payload();
        using var source = new MemoryStream(bytes, writable: false);
        var attempt = new ExperimentalFilePreparation();
        await attempt.PrepareWithDirectoryBarrierAsync(parent, Name, source, bytes.Length, Digest(bytes), bytes.Length);
        Prepared(attempt, ExperimentalFilePreparationState.ExperimentalFilePrepared);
        Require(source.CanRead, "Source ownership lost.");
        byte[] observed = File.ReadAllBytes(Path.Combine(parent, Name));
        Require(observed.AsSpan().SequenceEqual(bytes), "Physical file differs.");
        return $"file|prepared|{Hex(observed)}";
    }

    private static async Task<string> PackCaseAsync(string parent, bool blake)
    {
        (byte[] bytes, RawPackDescriptor descriptor, byte[] first, byte[] second) = await BuildPackAsync(blake);
        using var source = new MemoryStream(bytes, writable: false);
        var attempt = new ExperimentalFilePreparation();
        await attempt.PreparePackWithDirectoryBarrierAsync(parent, Name, source, descriptor, Limits);
        Prepared(attempt, ExperimentalFilePreparationState.ExperimentalPackPrepared);
        Require(source.CanRead, "Pack source ownership lost.");
        byte[] observed = File.ReadAllBytes(Path.Combine(parent, Name));
        Require(observed.AsSpan().SequenceEqual(bytes) && Digest(observed) == descriptor.PackDigest.Value,
            "Physical Pack differs from pinned descriptor.");
        using var check = new MemoryStream(observed, writable: false);
        VerifiedRawPack verified = await RawPackReader.VerifyAsync(check, Limits, descriptor);
        RawPackLocation[] locations = verified.GetLocations();
        Require(verified.Descriptor == descriptor && locations.Length == 2, "Pack descriptor or count differs.");
        byte[] restoredFirst = ReadFrame(observed, locations[0]);
        byte[] restoredSecond = ReadFrame(observed, locations[1]);
        Require(restoredFirst.AsSpan().SequenceEqual(first) && restoredSecond.AsSpan().SequenceEqual(second),
            "Restored frame differs.");
        string suite = blake ? "blake3" : "sha256";
        // Opaque PackId is intentionally random. Physical digest is verified
        // above and recorded separately, not used for cross-process equality.
        Console.WriteLine($"pack-{suite}; physical-sha256={Hex(observed)}");
        return $"pack-{suite}|prepared|{Hex(restoredFirst)}|{Hex(restoredSecond)}";
    }

    private static byte[] ReadFrame(byte[] bytes, RawPackLocation location) =>
        bytes.AsSpan(checked((int)location.Offset), checked((int)location.Length)).ToArray();

    private static async Task<string> CollisionAsync(string parent)
    {
        string path = Path.Combine(parent, Name);
        byte[] winner = [7, 8];
        File.WriteAllBytes(path, winner);
        using var source = new MemoryStream([1]);
        var attempt = new ExperimentalFilePreparation();
        _ = await ExpectAsync<IOException>(() => PrepareOneAsync(attempt, parent, source));
        Failed(attempt, PersistencePreparationStage.CreateExclusive, PersistencePreparationStage.None);
        Require(!attempt.OwnershipConfirmed && !attempt.WriterCloseConfirmed && !attempt.ReaderCloseConfirmed &&
            source.Position == 0 && source.CanRead && File.ReadAllBytes(path).AsSpan().SequenceEqual(winner),
            "Collision winner or ownership changed.");
        return $"collision|reconciliation|{Hex(File.ReadAllBytes(path))}";
    }

    private static async Task<string> WrongDescriptorAsync(string parent)
    {
        (byte[] bytes, RawPackDescriptor descriptor, _, _) = await BuildPackAsync(false);
        using var source = new MemoryStream(bytes, writable: false);
        var attempt = new ExperimentalFilePreparation();
        var wrong = descriptor with { PackId = new RawPackId(Digest([9])) };
        Require(wrong.PackId != descriptor.PackId, "Negative control PackId collision.");
        _ = await ExpectAsync<InvalidDataException>(() =>
            attempt.PreparePackWithDirectoryBarrierAsync(parent, Name, source, wrong, Limits));
        Failed(attempt, PersistencePreparationStage.VerifyClosedObject, PersistencePreparationStage.CloseWriter);
        Require(attempt.OwnershipConfirmed && attempt.WriterCloseConfirmed && attempt.ReaderCloseConfirmed &&
            source.CanRead && File.ReadAllBytes(Path.Combine(parent, Name)).AsSpan().SequenceEqual(bytes),
            "Descriptor failure did not retain the artifact or close handles.");
        return "wrong-descriptor|reconciliation|retained";
    }

    private static async Task<string> ReplacementAsync(string parent)
    {
        string path = Path.Combine(parent, Name);
        string original = path + ".original";
        using var source = new MemoryStream([1]);
        var attempt = new ExperimentalFilePreparation(new FilePreparationHooks
        {
            Checkpoint = (stage, after) =>
            {
                if (stage == PersistencePreparationStage.CloseWriter && after)
                {
                    File.Move(path, original);
                    File.WriteAllBytes(path, [1]);
                }
            },
        });
        _ = await ExpectAsync<InvalidDataException>(() => PrepareOneAsync(attempt, parent, source));
        Failed(attempt, PersistencePreparationStage.VerifyClosedObject, PersistencePreparationStage.CloseWriter);
        Require(attempt.OwnershipConfirmed && attempt.WriterCloseConfirmed && attempt.ReaderCloseConfirmed &&
            source.CanRead && File.ReadAllBytes(path).AsSpan().SequenceEqual(new byte[] { 1 }) &&
            File.ReadAllBytes(original).AsSpan().SequenceEqual(new byte[] { 1 }), "Replacement evidence differs.");
        using var first = File.OpenHandle(original);
        using var second = File.OpenHandle(path);
        NativeHandleIdentity left = NativeHandleProvenance.Observe(first);
        NativeHandleIdentity right = NativeHandleProvenance.Observe(second);
        Require(left.SameMountOrVolume(right) && !left.SameObject(right), "Replacement is not a distinct object.");
        return "same-byte-replacement|reconciliation|distinct-object";
    }

    private static async Task<string> CancellationAsync(string parent)
    {
        using var cts = new CancellationTokenSource();
        using var source = new MemoryStream([1]);
        var attempt = new ExperimentalFilePreparation(new FilePreparationHooks
        {
            Checkpoint = (stage, after) =>
            {
                if (stage == PersistencePreparationStage.VerifyClosedObject && after)
                {
                    cts.Cancel();
                }
            },
        });
        OperationCanceledException error = await ExpectAsync<OperationCanceledException>(() =>
            PrepareOneAsync(attempt, parent, source, cts.Token));
        Failed(attempt, PersistencePreparationStage.VerifyClosedObject, PersistencePreparationStage.VerifyClosedObject);
        Require(error.CancellationToken == cts.Token && attempt.OwnershipConfirmed &&
            attempt.WriterCloseConfirmed && attempt.ReaderCloseConfirmed && source.CanRead &&
            File.ReadAllBytes(Path.Combine(parent, Name)).AsSpan().SequenceEqual(new byte[] { 1 }),
            "Cancellation did not settle with retained artifact.");
        return "cancel-before-parent-flush|reconciliation|retained";
    }

    private static string NativeOpenFailure(string parent)
    {
        var barrier = new NativeDirectoryBarrier();
        try
        {
            barrier.Execute(Path.Combine(parent, "missing"));
        }
        catch (NativeDirectoryBarrierException error)
        {
            bool linux = OperatingSystem.IsLinux();
            Require(error.Operation == (linux ? "open" : "CreateFileW") &&
                error.ErrorDomain == (linux ? "errno" : "Win32") &&
                (linux ? error.NativeError == 2 : error.NativeError is 2 or 3),
                "Unexpected native open failure.");
            Require(barrier.State == DirectoryBarrierState.FailedAfterOpenAttempt &&
                barrier.LastAttempted == DirectoryBarrierStage.Open &&
                barrier.LastCompleted == DirectoryBarrierStage.None &&
                !barrier.OwnershipConfirmed && !barrier.CloseAttempted && !barrier.CloseConfirmed,
                "Failed native open claimed ownership.");
            Console.WriteLine($"native-open; operation={error.Operation}; domain={error.ErrorDomain}; error={error.NativeError}");
            return "native-open-missing|rejected|no-ownership";
        }

        throw new InvalidOperationException("Missing directory unexpectedly opened.");
    }

    private static Task PrepareOneAsync(ExperimentalFilePreparation attempt, string parent, Stream source,
        CancellationToken cancellationToken = default) =>
        attempt.PrepareWithDirectoryBarrierAsync(parent, Name, source, 1, Digest([1]), 1, cancellationToken);

    private static void Prepared(ExperimentalFilePreparation attempt, ExperimentalFilePreparationState state)
    {
        Require(attempt.State == state &&
            attempt.LastAttempted == PersistencePreparationStage.FlushParentDirectory &&
            attempt.LastCompleted == PersistencePreparationStage.FlushParentDirectory &&
            attempt.OwnershipConfirmed && attempt.WriterCloseConfirmed && attempt.ReaderCloseConfirmed &&
            attempt.ParentOwnershipConfirmed && attempt.ParentCloseAttempted && attempt.ParentCloseConfirmed,
            "Prepared lifecycle incomplete.");
    }

    private static void Failed(ExperimentalFilePreparation attempt, PersistencePreparationStage attempted,
        PersistencePreparationStage completed)
    {
        Require(attempt.State == ExperimentalFilePreparationState.ReconciliationRequired &&
            attempt.LastAttempted == attempted && attempt.LastCompleted == completed &&
            attempt.ParentOwnershipConfirmed && attempt.ParentCloseAttempted && attempt.ParentCloseConfirmed,
            "Failure lifecycle incomplete.");
    }

    private static async Task<T> ExpectAsync<T>(Func<Task> operation) where T : Exception
    {
        try
        {
            await operation();
        }
        catch (T expected)
        {
            return expected;
        }

        throw new InvalidOperationException("Expected operation failure.");
    }

    private static async Task<(byte[] Bytes, RawPackDescriptor Descriptor, byte[] First, byte[] Second)> BuildPackAsync(bool blake)
    {
        HashSuiteId suite = blake ? HashSuiteIds.Blake3256V1 : HashSuiteIds.Sha256V1;
        byte[] first = Payload();
        byte[] second = [1, 2, 3];
        using var output = new MemoryStream();
        using var writer = new PrivateRawPackWriter(output, new RawPackPolicy(1024 * 1024, 1024 * 1024, 8, 544), suite);
        using var firstSource = new MemoryStream(first, writable: false);
        using var secondSource = new MemoryStream(second, writable: false);
        Require(await writer.AppendAsync(firstSource, Key(first, suite), first.Length) == RawPackPlacement.Append,
            "First frame not appended.");
        Require(await writer.AppendAsync(secondSource, Key(second, suite), second.Length) == RawPackPlacement.Append,
            "Second frame not appended.");
        RawPackDescriptor descriptor = await writer.CompleteAsync();
        byte[] bytes = output.ToArray();
        Require(Digest(bytes) == descriptor.PackDigest.Value && descriptor.FrameCount == 2, "Writer descriptor differs.");
        return (bytes, descriptor, first, second);
    }

    private static ChunkObjectKey Key(byte[] bytes, HashSuiteId suite)
    {
        using var digest = new ChunkDigest(suite);
        digest.Append(bytes);
        return new ChunkObjectKey(suite, digest.Finish());
    }

    private static byte[] Payload()
    {
        byte[] bytes = new byte[ExperimentalFilePreparation.BufferBytes + 7];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = unchecked((byte)(i * 31 + 7));
        }

        return bytes;
    }

    private static Hash256 Digest(ReadOnlySpan<byte> bytes) => Hash256.FromBytes(SHA256.HashData(bytes));
    private static string Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("chunkshift-aot-probe-").FullName;
        private bool _disposed;

        internal string Case(string name) => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
