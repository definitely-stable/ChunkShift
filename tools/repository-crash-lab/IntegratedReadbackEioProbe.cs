using System.Text.Json;
using ChunkShift.Repository;

internal static class IntegratedReadbackEioProbe
{
    private const string Name = "0123456789abcdef0123456789abcdef.prepare";
    private static readonly RawPackReadLimits Limits = new(1024 * 1024, 8, 544, 512 * 1024);

    internal static async Task RunAsync(string pinned, string parent)
    {
        Require(OperatingSystem.IsLinux() && NativeDirectoryBarrier.IsSupportedPlatform, "Unsupported ABI.");
        Require(new FileInfo(pinned).Length == 65850, "Unexpected fixture extent.");
        using var source = new MemoryStream(File.ReadAllBytes(pinned), writable: false);
        RawPackDescriptor descriptor = (await RawPackReader.VerifyAsync(source, Limits).ConfigureAwait(false)).Descriptor;
        source.Position = 0;
        int checkpoints = 0;
        bool parentBarrierStarted = false;
        ExperimentalFilePreparation? attempt = null;
        attempt = new ExperimentalFilePreparation(new FilePreparationHooks
        {
            Checkpoint = (stage, after) =>
            {
                if (stage == PersistencePreparationStage.FlushParentDirectory)
                {
                    parentBarrierStarted = true;
                }

                if (stage != PersistencePreparationStage.VerifyClosedObject || after)
                {
                    return;
                }

                var current = attempt ?? throw new InvalidOperationException("Missing attempt.");
                Require(++checkpoints == 1 && current.WriterCloseConfirmed &&
                    current.LastCompleted == PersistencePreparationStage.CloseWriter &&
                    current.ParentOwnershipConfirmed && !current.ParentCloseAttempted,
                    "Incorrect readback checkpoint.");
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    kind = "readback-checkpoint", pid = Environment.ProcessId, path = current.CandidatePath,
                    state = current.State.ToString(), attempted = current.LastAttempted.ToString(),
                    completed = current.LastCompleted.ToString(), owned = current.OwnershipConfirmed,
                    writer_closed = current.WriterCloseConfirmed, reader_closed = current.ReaderCloseConfirmed,
                    parent_owned = current.ParentOwnershipConfirmed, parent_close_attempted = current.ParentCloseAttempted,
                    parent_closed = current.ParentCloseConfirmed, source_position = source.Position,
                    pack_sha256 = descriptor.PackDigest.Value.ToHexLower(),
                }));
                Console.Out.Flush();
                Require(Console.ReadLine() == "continue", "Controller did not authorize readback.");
            },
        });
        try
        {
            await attempt.PreparePackWithDirectoryBarrierAsync(parent, Name, source, descriptor, Limits)
                .ConfigureAwait(false);
        }
        catch (IOException error) when (error.HResult == 5)
        {
            Require(checkpoints == 1 && !parentBarrierStarted &&
                attempt.State == ExperimentalFilePreparationState.ReconciliationRequired &&
                attempt.LastAttempted == PersistencePreparationStage.VerifyClosedObject &&
                attempt.LastCompleted == PersistencePreparationStage.CloseWriter &&
                attempt.OwnershipConfirmed && attempt.WriterCloseConfirmed && attempt.ReaderCloseConfirmed &&
                attempt.ParentOwnershipConfirmed && attempt.ParentCloseAttempted && attempt.ParentCloseConfirmed &&
                source.CanRead && source.Position == descriptor.PhysicalBytes,
                "Readback EIO did not settle fail-closed.");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                kind = "integrated-readback-eio", state = attempt.State.ToString(),
                attempted = attempt.LastAttempted.ToString(), completed = attempt.LastCompleted.ToString(),
                owned = attempt.OwnershipConfirmed, writer_closed = attempt.WriterCloseConfirmed,
                reader_closed = attempt.ReaderCloseConfirmed, parent_owned = attempt.ParentOwnershipConfirmed,
                parent_close_attempted = attempt.ParentCloseAttempted, parent_closed = attempt.ParentCloseConfirmed,
                parent_barrier_started = parentBarrierStarted, source_open = source.CanRead,
                source_position = source.Position, pack_sha256 = descriptor.PackDigest.Value.ToHexLower(),
                hresult = error.HResult, type = error.GetType().Name,
            }));
            return;
        }

        throw new InvalidOperationException("Faulted integrated readback unexpectedly succeeded.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
