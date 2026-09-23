using System.Buffers;
using System.Security.Cryptography;
using Blake3;
using ChunkShift.Chunking;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

internal delegate ValueTask MetadataChunkSink(
    ChunkKernelChunk chunk,
    CancellationToken cancellationToken);

internal static class CsmMetadataOnlyPrototype
{
    private const int IoBufferSize = 64 * 1024;

    internal static async ValueTask ScanAsync(
        Stream source,
        ChunkingKernelProfile profile,
        HashSuiteId hashSuite,
        MetadataChunkSink sink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sink);

        if (!source.CanRead)
        {
            throw new ArgumentException("Source stream must be readable.", nameof(source));
        }

        byte[] ioBuffer = ArrayPool<byte>.Shared.Rent(
            Math.Min(IoBufferSize, Math.Max(1, profile.Maximum)));

        try
        {
            var boundaryState = new ChunkBoundaryState(profile);
            using var hasher = new IncrementalSuiteHasher(hashSuite);

            long chunkOffset = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int read = await source
                    .ReadAsync(
                        ioBuffer.AsMemory(
                            0,
                            Math.Min(IoBufferSize, Math.Max(1, profile.Maximum))),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }

                int inputIndex = 0;

                while (inputIndex < read)
                {
                    ChunkBoundaryScanResult result =
                        boundaryState.Scan(ioBuffer.AsSpan(inputIndex, read - inputIndex));

                    if (result.Consumed > 0)
                    {
                        hasher.Update(ioBuffer.AsSpan(inputIndex, result.Consumed));
                        inputIndex += result.Consumed;
                    }

                    if (!result.HasBoundary)
                    {
                        continue;
                    }

                    await EmitAsync(
                        hasher,
                        chunkOffset,
                        result.CompletedChunkLength,
                        sink,
                        cancellationToken).ConfigureAwait(false);

                    chunkOffset = checked(chunkOffset + result.CompletedChunkLength);
                }
            }

            int finalLength = boundaryState.Finish();
            if (finalLength != 0)
            {
                await EmitAsync(
                    hasher,
                    chunkOffset,
                    finalLength,
                    sink,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(ioBuffer);
        }
    }

    private static ValueTask EmitAsync(
        IncrementalSuiteHasher hasher,
        long offset,
        int length,
        MetadataChunkSink sink,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Hash256 hash = hasher.FinalizeAndReset();
        return sink(new ChunkKernelChunk(offset, length, new ChunkId(hash)), cancellationToken);
    }

    private sealed class IncrementalSuiteHasher : IDisposable
    {
        private readonly Hasher? _blake3;
        private readonly IncrementalHash? _sha256;

        internal IncrementalSuiteHasher(HashSuiteId hashSuite)
        {
            if (hashSuite == HashSuiteIds.Blake3256V1)
            {
                _blake3 = Hasher.New();
                return;
            }

            if (hashSuite == HashSuiteIds.Sha256V1)
            {
                _sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                return;
            }

            throw new NotSupportedException($"Unsupported HashSuiteId '{hashSuite}'.");
        }

        internal void Update(ReadOnlySpan<byte> bytes)
        {
            if (_blake3 is not null)
            {
                _blake3.Update(bytes);
                return;
            }

            _sha256!.AppendData(bytes);
        }

        internal Hash256 FinalizeAndReset()
        {
            if (_blake3 is not null)
            {
                Hash hash = _blake3.Finalize();
                _blake3.Reset();
                return Hash256.FromBytes(hash.AsSpan());
            }

            Span<byte> digest = stackalloc byte[32];
            if (!_sha256!.TryGetHashAndReset(digest, out int written) || written != digest.Length)
            {
                throw new CryptographicException("Could not finalize incremental SHA-256.");
            }

            return Hash256.FromBytes(digest);
        }

        public void Dispose()
        {
            _blake3?.Dispose();
            _sha256?.Dispose();
        }
    }
}
