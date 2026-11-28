using System.Text.Json;
using ChunkShift.Repository;

internal static class NativeEnospcProbe
{
    private const string Name = "0123456789abcdef0123456789abcdef.prepare";
    private const int LinuxEnospc = 28;
    private static readonly RawPackReadLimits Limits = new(1024 * 1024, 8, 544, 512 * 1024);

    internal static async Task RunAsync(string pinned, string parent, string phase)
    {
        Require(OperatingSystem.IsLinux() && NativeDirectoryBarrier.IsSupportedPlatform,
            "Native ENOSPC probe is Linux x64/ARM64 only.");
        PersistencePreparationStage expectedStage = phase switch
        {
            "write" => PersistencePreparationStage.WriteVerifiedObject,
            "flush" => PersistencePreparationStage.FlushFile,
            _ => throw new ArgumentException("Unknown ENOSPC phase.", nameof(phase)),
        };
        Require(new FileInfo(pinned).Length == 65850, "Unexpected fixture extent.");
        using var source = new MemoryStream(File.ReadAllBytes(pinned), writable: false);
        RawPackDescriptor descriptor = (await RawPackReader.VerifyAsync(source, Limits).ConfigureAwait(false)).Descriptor;
        source.Position = 0;

        // No Create, Write, Flush, Close or ParentOperations hook: real
        // FileStream and native directory lifecycle on a bounded filesystem.
        var attempt = new ExperimentalFilePreparation();
        Exception? failure = null;
        try
        {
            await attempt.PreparePackWithDirectoryBarrierAsync(parent, Name, source, descriptor, Limits)
                .ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or AggregateException)
        {
            failure = error;
        }

        Require(failure is not null, "ENOSPC attempt unexpectedly succeeded.");
        Exception[] errors = failure is AggregateException aggregate
            ? aggregate.InnerExceptions.ToArray()
            : [failure!];
        Require(errors.Length is 1 or 2 &&
            errors.All(static error => error is IOException && error.HResult == LinuxEnospc),
            "Expected raw Linux ENOSPC on primary and any cleanup error.");
        Require(attempt.State == ExperimentalFilePreparationState.ReconciliationRequired &&
            attempt.LastAttempted == expectedStage &&
            attempt.LastCompleted == (phase == "write" ? PersistencePreparationStage.CreateExclusive :
                PersistencePreparationStage.WriteVerifiedObject) &&
            attempt.OwnershipConfirmed && !attempt.ReaderCloseConfirmed &&
            attempt.ParentOwnershipConfirmed && attempt.ParentCloseAttempted && attempt.ParentCloseConfirmed &&
            attempt.WriterCloseConfirmed == (errors.Length == 1) && source.CanRead,
            "ENOSPC lifecycle mismatch.");

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            kind = "native-enospc", phase, state = attempt.State.ToString(),
            attempted = attempt.LastAttempted.ToString(), completed = attempt.LastCompleted.ToString(),
            owned = attempt.OwnershipConfirmed, writer_closed = attempt.WriterCloseConfirmed,
            reader_closed = attempt.ReaderCloseConfirmed, parent_owned = attempt.ParentOwnershipConfirmed,
            parent_close_attempted = attempt.ParentCloseAttempted, parent_closed = attempt.ParentCloseConfirmed,
            source_open = source.CanRead, source_position = source.Position,
            pack_sha256 = descriptor.PackDigest.Value.ToHexLower(),
            errors = errors.Select(error => new { type = error.GetType().Name, hresult = error.HResult }).ToArray(),
        }));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
