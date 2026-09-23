using System.Buffers;
using ChunkShift.Chunking;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

internal readonly record struct ScannerObservedChunk(
    long Index,
    long Offset,
    int Length,
    ChunkId Id);

internal static class ScannerApiPrototypeTestFacade
{
    internal static async Task<ScannerObservedChunk[]> CollectCanonicalAsync(
        byte[] data,
        int[]? shortReadPattern = null)
    {
        ChunkingKernelProfile profile = ChunkingKernelProfile.FastCdcGear(
            FastCdcProfile.CreateM1Candidate(64 * 1024));

        using Stream source = CreateSource(data, shortReadPattern);
        var chunks = new List<ScannerObservedChunk>();
        long index = 0;

        await ChunkingKernel.ScanAsync(
            source,
            profile,
            HashSuiteIds.Blake3256V1,
            (chunk, _, _) =>
            {
                chunks.Add(new ScannerObservedChunk(
                    index++,
                    chunk.Offset,
                    chunk.Length,
                    chunk.Id));
                return ValueTask.CompletedTask;
            });

        return chunks.ToArray();
    }

    internal static async Task<ScannerObservedChunk[]> CollectTaskCallbackAsync(
        byte[] data,
        int[]? shortReadPattern = null)
    {
        ChunkingKernelProfile profile = ChunkingKernelProfile.FastCdcGear(
            FastCdcProfile.CreateM1Candidate(64 * 1024));

        using Stream source = CreateSource(data, shortReadPattern);
        var chunks = new List<ScannerObservedChunk>();

        await TaskCallbackScannerPrototype.ScanAsync(
            source,
            profile,
            HashSuiteIds.Blake3256V1,
            (chunk, _, _) =>
            {
                chunks.Add(ToObserved(chunk));
                return Task.CompletedTask;
            });

        return chunks.ToArray();
    }

    internal static async Task<ScannerObservedChunk[]> CollectPullAsync(
        byte[] data,
        int[]? shortReadPattern = null)
    {
        ChunkingKernelProfile profile = ChunkingKernelProfile.FastCdcGear(
            FastCdcProfile.CreateM1Candidate(64 * 1024));

        using Stream source = CreateSource(data, shortReadPattern);
        using var reader = new ChunkPullReaderPrototype(
            source,
            profile,
            HashSuiteIds.Blake3256V1);

        var chunks = new List<ScannerObservedChunk>();

        while (true)
        {
            PullChunkResult result = await reader.ReadAsync();
            if (!result.HasChunk)
            {
                break;
            }

            chunks.Add(ToObserved(result.Chunk));

            Hash256 recomputed = Hashing.HashSuiteHasher.Hash(
                HashSuiteIds.Blake3256V1,
                result.Content.Span);

            if (recomputed != result.Chunk.Id.Value)
            {
                throw new InvalidOperationException(
                    "Pull prototype returned payload/hash mismatch.");
            }
        }

        return chunks.ToArray();
    }

    internal static async Task<ScannerObservedChunk[]> CollectSegmentedAsync(
        byte[] data,
        int[]? shortReadPattern = null)
    {
        ChunkingKernelProfile profile = ChunkingKernelProfile.FastCdcGear(
            FastCdcProfile.CreateM1Candidate(64 * 1024));

        using Stream source = CreateSource(data, shortReadPattern);
        var chunks = new List<ScannerObservedChunk>();

        await SegmentedChunkScannerPrototype.ScanAsync(
            source,
            profile,
            HashSuiteIds.Blake3256V1,
            (chunk, content, _) =>
            {
                if (content.Length != chunk.Length)
                {
                    throw new InvalidOperationException(
                        "Segmented prototype returned incorrect payload length.");
                }

                using var hasher = Blake3.Hasher.New();
                foreach (ReadOnlyMemory<byte> segment in content)
                {
                    hasher.Update(segment.Span);
                }

                Blake3.Hash digest = hasher.Finalize();
                Hash256 recomputed = Hash256.FromBytes(digest.AsSpan());
                if (recomputed != chunk.Id.Value)
                {
                    throw new InvalidOperationException(
                        "Segmented prototype returned payload/hash mismatch.");
                }

                chunks.Add(ToObserved(chunk));
                return ValueTask.CompletedTask;
            });

        return chunks.ToArray();
    }

    private static Stream CreateSource(byte[] data, int[]? shortReadPattern)
    {
        return shortReadPattern is null
            ? new MemoryStream(data, writable: false)
            : new PatternReadStream(data, shortReadPattern);
    }

    private static ScannerObservedChunk ToObserved(ChunkInfo chunk) =>
        new(chunk.Index, chunk.Offset, chunk.Length, chunk.Id);
}

internal sealed class PatternReadStream : Stream
{
    private readonly byte[] _data;
    private readonly int[] _pattern;
    private int _position;
    private int _patternIndex;

    internal PatternReadStream(byte[] data, int[] pattern)
    {
        _data = data;
        _pattern = pattern.Length == 0 ? [1] : pattern;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadCore(buffer.AsSpan(offset, count));

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(ReadCore(buffer.Span));
    }

    private int ReadCore(Span<byte> destination)
    {
        if (_position == _data.Length)
        {
            return 0;
        }

        int requested = _pattern[_patternIndex++ % _pattern.Length];
        int count = Math.Min(
            requested,
            Math.Min(destination.Length, _data.Length - _position));

        _data.AsSpan(_position, count).CopyTo(destination);
        _position += count;
        return count;
    }

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
