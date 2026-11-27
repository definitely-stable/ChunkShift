using System.Buffers;
using System.Security.Cryptography;
using ChunkShift.Primitives;

namespace ChunkShift.Repository;

internal enum ExperimentalFilePreparationState
{
    NotStarted,
    Running,
    RejectedBeforeCreate,
    ReconciliationRequired,
    ExperimentalFileVerified,
    ExperimentalPackVerified,
    ExperimentalFilePrepared,
    ExperimentalPackPrepared,
}

/// <summary>
/// One private physical file, optionally with experimental parent barrier, not a repository publication or a
/// durability receipt. The caller exclusively controls a stable existing parent
/// directory and supplies an independently expected physical SHA-256. No path
/// is a security boundary; no adoption, rename or deletion. The optional
/// barrier mode is observation only under the same trusted-parent assumption.
/// Observe diagnostics only after the task settles.
/// </summary>
internal sealed class ExperimentalFilePreparation
{
    internal const int BufferBytes = 64 * 1024;

    private readonly FilePreparationHooks? _hooks;
    private int _started;
    private ExperimentalParentPreparation? _parent;

    internal ExperimentalFilePreparation(FilePreparationHooks? hooks = null) => _hooks = hooks;

    internal ExperimentalFilePreparationState State { get; private set; }
    internal PersistencePreparationStage LastAttempted { get; private set; }
    internal PersistencePreparationStage LastCompleted { get; private set; }
    internal string? CandidatePath { get; private set; }
    internal bool OwnershipConfirmed { get; private set; }
    internal bool WriterCloseConfirmed { get; private set; }
    internal bool ReaderCloseConfirmed { get; private set; }
    internal bool ParentOwnershipConfirmed => _parent?.OwnershipConfirmed == true;
    internal bool ParentCloseAttempted => _parent?.CloseAttempted == true;
    internal bool ParentCloseConfirmed => _parent?.CloseConfirmed == true;

    // A deliberately narrow, portable temporary basename; not a PackId or
    // a format contract. An existing file is never opened for writing.
    internal Task PrepareAsync(string parentDirectory, string basename, Stream source,
        long expectedLength, Hash256 expectedSha256, long maximumBytes,
        CancellationToken cancellationToken = default) =>
        PrepareCoreAsync(parentDirectory, basename, source, expectedLength, expectedSha256,
            maximumBytes, null, null, false, cancellationToken);

    // A distinct success state requires full RawPackReader verification tied
    // to the caller-pinned descriptor. Never infer a descriptor from the file.
    internal Task PreparePackAsync(string parentDirectory, string basename, Stream source,
        RawPackDescriptor expected, RawPackReadLimits limits,
        CancellationToken cancellationToken = default) =>
        PrepareCoreAsync(parentDirectory, basename, source, expected.PhysicalBytes,
            expected.PackDigest.Value, limits?.MaximumPhysicalBytes ?? 0,
            expected, limits, false, cancellationToken);

    internal Task PrepareWithDirectoryBarrierAsync(string parentDirectory, string basename, Stream source,
        long expectedLength, Hash256 expectedSha256, long maximumBytes,
        CancellationToken cancellationToken = default) =>
        PrepareCoreAsync(parentDirectory, basename, source, expectedLength, expectedSha256,
            maximumBytes, null, null, true, cancellationToken);

    internal Task PreparePackWithDirectoryBarrierAsync(string parentDirectory, string basename, Stream source,
        RawPackDescriptor expected, RawPackReadLimits limits, CancellationToken cancellationToken = default) =>
        PrepareCoreAsync(parentDirectory, basename, source, expected.PhysicalBytes,
            expected.PackDigest.Value, limits?.MaximumPhysicalBytes ?? 0,
            expected, limits, true, cancellationToken);

    private async Task PrepareCoreAsync(string parentDirectory, string basename, Stream source,
        long expectedLength, Hash256 expectedSha256, long maximumBytes,
        RawPackDescriptor? expectedPack, RawPackReadLimits? packLimits, bool directoryBarrier,
        CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
        {
            throw new InvalidOperationException("File preparation is single-use.");
        }

        State = ExperimentalFilePreparationState.Running;
        FileStream? writer = null;
        FileStream? reader = null;
        bool writerCloseAttempted = false;
        bool readerCloseAttempted = false;
        byte[]? buffer = null;
        NativeFilesystemObservation? writerObservation = null;
        NativeFilesystemObservation? readerObservation = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);
            ArgumentException.ThrowIfNullOrWhiteSpace(basename);
            ArgumentNullException.ThrowIfNull(source);
            if (expectedPack is { } pack)
            {
                ArgumentNullException.ThrowIfNull(packLimits);
                ValidatePackAdmission(pack, packLimits);
            }

            ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
            ArgumentOutOfRangeException.ThrowIfNegative(expectedLength);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(expectedLength, maximumBytes);

            if (!source.CanRead)
            {
                throw new ArgumentException("Source must be readable.", nameof(source));
            }

            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            {
                throw new PlatformNotSupportedException("Experimental file preparation is scoped to Windows and Linux.");
            }

            if (!Path.IsPathFullyQualified(parentDirectory))
            {
                throw new ArgumentException("Parent must be an absolute path.", nameof(parentDirectory));
            }

            if (basename.Length != 40 || !basename.EndsWith(".prepare", StringComparison.Ordinal) ||
                !Guid.TryParseExact(basename[..32], "N", out Guid name) ||
                !string.Equals(basename, name.ToString("N") + ".prepare", StringComparison.Ordinal))
            {
                throw new ArgumentException("Expected canonical lowercase GUID basename with .prepare suffix.", nameof(basename));
            }

            string parent = directoryBarrier
                ? NativeDirectoryBarrier.ValidatePath(parentDirectory)
                : Path.GetFullPath(parentDirectory);
            if (!Directory.Exists(parent))
            {
                throw new DirectoryNotFoundException("Private parent must already exist.");
            }

            if (directoryBarrier)
            {
                _parent = new ExperimentalParentPreparation(_hooks?.ParentOperations);
                _parent.Open(parent, cancellationToken);
            }

            string path = Path.Combine(parent, basename);
            CandidatePath = path;
            buffer = ArrayPool<byte>.Shared.Rent(BufferBytes);

            Begin(PersistencePreparationStage.CreateExclusive, cancellationToken);
            writer = _hooks?.Create is { } create
                ? create(path)
                : new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            OwnershipConfirmed = true;
            Complete(PersistencePreparationStage.CreateExclusive);

            Begin(PersistencePreparationStage.WriteVerifiedObject, cancellationToken);
            await CopyAndVerifyAsync(source, writer, buffer, expectedLength, expectedSha256,
                cancellationToken).ConfigureAwait(false);
            Complete(PersistencePreparationStage.WriteVerifiedObject);

            Begin(PersistencePreparationStage.FlushFile, cancellationToken);
            // This synchronous barrier cannot be forcibly cancelled. Do not
            // return while it is running, and do not infer power-loss durability.
            if (_hooks?.Flush is { } flush)
            {
                flush(writer);
            }
            else
            {
                writer.Flush(flushToDisk: true);
            }
            Complete(PersistencePreparationStage.FlushFile);
            if (_parent is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                writerObservation = _parent.ObserveChild(writer.SafeFileHandle);
            }

            Begin(PersistencePreparationStage.CloseWriter, cancellationToken);
            writerCloseAttempted = true;
            await CloseAsync(writer, isReader: false).ConfigureAwait(false);
            WriterCloseConfirmed = true;
            Complete(PersistencePreparationStage.CloseWriter);

            Begin(PersistencePreparationStage.VerifyClosedObject, cancellationToken);
            reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                16 * 1024, FileOptions.Asynchronous |
                    (expectedPack.HasValue ? FileOptions.RandomAccess : FileOptions.SequentialScan));
            if (_parent is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                readerObservation = _parent.ObserveChild(reader.SafeFileHandle);
                if (writerObservation != readerObservation)
                {
                    throw new InvalidDataException("Writer and readback observations differ.");
                }
            }

            if (expectedPack is { } descriptor)
            {
                // RawPackReader independently validates the exact physical SHA-256,
                // structure, descriptor and all logical frames in this handle.
                // It uses its own bounded payload window and count-bounded metadata.
                // Release the copy window before that operation, not during I/O.
                ArrayPool<byte>.Shared.Return(buffer);
                buffer = null;
                _ = await RawPackReader.VerifyAsync(reader, packLimits!, descriptor, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await CopyAndVerifyAsync(reader, null, buffer, expectedLength, expectedSha256,
                    cancellationToken).ConfigureAwait(false);
            }
            if (_parent is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (readerObservation != _parent.ObserveChild(reader.SafeFileHandle))
                {
                    throw new InvalidDataException("Readback observation changed during verification.");
                }
            }
            readerCloseAttempted = true;
            await CloseAsync(reader, isReader: true).ConfigureAwait(false);
            ReaderCloseConfirmed = true;
            Complete(PersistencePreparationStage.VerifyClosedObject);

            if (_parent is not null)
            {
                Begin(PersistencePreparationStage.FlushParentDirectory, cancellationToken);
                _parent.FlushAndClose(cancellationToken);
                Complete(PersistencePreparationStage.FlushParentDirectory);
            }

            cancellationToken.ThrowIfCancellationRequested();
            State = directoryBarrier
                ? (expectedPack.HasValue ? ExperimentalFilePreparationState.ExperimentalPackPrepared
                    : ExperimentalFilePreparationState.ExperimentalFilePrepared)
                : (expectedPack.HasValue ? ExperimentalFilePreparationState.ExperimentalPackVerified
                    : ExperimentalFilePreparationState.ExperimentalFileVerified);
        }
        catch (Exception primary)
        {
            State = LastAttempted == PersistencePreparationStage.None
                ? ExperimentalFilePreparationState.RejectedBeforeCreate
                : ExperimentalFilePreparationState.ReconciliationRequired;
            List<Exception> failures = [primary];
            try
            {
                // Never retry an attempted close with an unknown outcome.
                // Cleanup has no cancellation token and does not advance the
                // protocol markers. Only one handle can need cleanup here.
                if (reader is not null && !readerCloseAttempted)
                {
                    readerCloseAttempted = true;
                    await CloseAsync(reader, isReader: true).ConfigureAwait(false);
                    ReaderCloseConfirmed = true;
                }
                else if (writer is not null && !writerCloseAttempted)
                {
                    writerCloseAttempted = true;
                    await CloseAsync(writer, isReader: false).ConfigureAwait(false);
                    WriterCloseConfirmed = true;
                }
            }
            catch (Exception cleanup)
            {
                failures.Add(cleanup);
            }

            try
            {
                _parent?.CloseIfNeeded();
            }
            catch (Exception cleanup)
            {
                failures.Add(cleanup);
            }

            if (failures.Count > 1)
            {
                throw new AggregateException("File preparation and handle cleanup failed.", failures);
            }

            throw;
        }
        finally
        {
            if (buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    private static void ValidatePackAdmission(RawPackDescriptor expected, RawPackReadLimits limits)
    {
        if (expected.HashSuite != HashSuiteIds.Sha256V1 && expected.HashSuite != HashSuiteIds.Blake3256V1)
        {
            throw new NotSupportedException("Unsupported expected raw pack hash suite.");
        }

        if (expected.FrameCount <= 0 || expected.FrameCount > limits.MaximumFrames ||
            expected.FrameCount > Array.MaxLength ||
            expected.PhysicalBytes > limits.MaximumPhysicalBytes)
        {
            throw new ArgumentException("Expected raw pack descriptor exceeds admission limits.", nameof(expected));
        }

        // Only cheap descriptor geometry here. Actual footer, frame extents
        // and hashes must be independently verified after closing the writer.
        if (!RawPackLayout.TryMeasure(0, expected.FrameCount, out RawPackSections minimum) ||
            minimum.PhysicalBytes > expected.PhysicalBytes ||
            minimum.IndexBytes > limits.MaximumIndexBytes || minimum.IndexBytes > Array.MaxLength)
        {
            throw new ArgumentException("Expected raw pack descriptor has impossible or excessive geometry.", nameof(expected));
        }
    }

    private void Begin(PersistencePreparationStage stage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastAttempted = stage;
        _hooks?.Checkpoint?.Invoke(stage, false);
    }

    private void Complete(PersistencePreparationStage stage)
    {
        _hooks?.Checkpoint?.Invoke(stage, true);
        LastCompleted = stage;
    }

    private ValueTask CloseAsync(FileStream file, bool isReader) =>
        _hooks?.Close is { } close ? close(file, isReader) : file.DisposeAsync();

    private async Task CopyAndVerifyAsync(Stream source, FileStream? destination, byte[] buffer,
        long expectedLength, Hash256 expectedSha256, CancellationToken cancellationToken)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long remaining = expectedLength;
        while (remaining != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int requested = (int)Math.Min(BufferBytes, remaining);
            int count = await source.ReadAsync(buffer.AsMemory(0, requested), cancellationToken).ConfigureAwait(false);
            if ((uint)count > (uint)requested)
            {
                throw new InvalidOperationException("Source returned an invalid read count.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (count == 0)
            {
                throw new EndOfStreamException("Physical object ended before expected length.");
            }

            digest.AppendData(buffer.AsSpan(0, count));
            if (destination is not null)
            {
                ReadOnlyMemory<byte> bytes = buffer.AsMemory(0, count);
                if (_hooks?.Write is { } write)
                {
                    await write(destination, bytes, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await destination.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                }
            }

            remaining -= count;
        }

        cancellationToken.ThrowIfCancellationRequested();
        int trailing = await source.ReadAsync(buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
        if ((uint)trailing > 1u)
        {
            throw new InvalidOperationException("Source returned an invalid read count.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (trailing != 0)
        {
            throw new InvalidDataException("Physical object exceeds expected length.");
        }

        if (!expectedSha256.FixedTimeEquals(Hash256.FromBytes(digest.GetHashAndReset())))
        {
            throw new InvalidDataException("Physical SHA-256 mismatch.");
        }
    }
}

// Internal deterministic test seams. Checkpoint failures are boundary
// observations, not native errors. A close hook owns the simulated operation;
// a throw does not confirm closure, even if it performed the close.
internal sealed class FilePreparationHooks
{
    internal Action<PersistencePreparationStage, bool>? Checkpoint { get; init; }
    internal Func<string, FileStream>? Create { get; init; }
    internal Func<FileStream, ReadOnlyMemory<byte>, CancellationToken, ValueTask>? Write { get; init; }
    internal Action<FileStream>? Flush { get; init; }
    internal Func<FileStream, bool, ValueTask>? Close { get; init; }
    internal IDirectoryBarrierOperations? ParentOperations { get; init; }
}
