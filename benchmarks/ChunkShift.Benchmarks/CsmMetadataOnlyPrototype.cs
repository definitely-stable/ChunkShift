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

        byte[] ioBuffer = ArrayPool<byte>.Shared.Rent(
            Math.Min(IoBufferSize, Math.Max(1, profile.Maximum)));

        try
        {
            using var hasher = new IncrementalSuiteHasher(hashSuite);

            if (profile.Kind == ChunkingKernelKind.Fixed)
            {
                await ScanFixedAsync(
                    source,
                    ioBuffer,
                    profile.Target,
                    hasher,
                    sink,
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            if (profile.Kind != ChunkingKernelKind.FastCdcGearV1)
            {
                throw new InvalidOperationException($"Unsupported chunking kernel '{profile.Kind}'.");
            }

            await ScanFastCdcAsync(
                source,
                ioBuffer,
                profile.FastCdc,
                hasher,
                sink,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(ioBuffer);
        }
    }

    private static async ValueTask ScanFastCdcAsync(
        Stream source,
        byte[] ioBuffer,
        FastCdcProfile profile,
        IncrementalSuiteHasher hasher,
        MetadataChunkSink sink,
        CancellationToken cancellationToken)
    {
        int chunkLength = 0;
        long chunkOffset = 0;
        ulong gearHash = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int read = await source
                .ReadAsync(ioBuffer.AsMemory(0, IoBufferSize), cancellationToken)
                .ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            int inputIndex = 0;
            int hashStart = 0;

            while (inputIndex < read)
            {
                byte candidate = ioBuffer[inputIndex];

                if (chunkLength >= profile.Minimum)
                {
                    gearHash = unchecked((gearHash << 1) + FastCdcGearTable.Get(candidate));
                    ulong mask = chunkLength < profile.Target
                        ? profile.StrictMask
                        : profile.RelaxedMask;

                    if ((gearHash & mask) == 0)
                    {
                        if (inputIndex > hashStart)
                        {
                            hasher.Update(ioBuffer.AsSpan(hashStart, inputIndex - hashStart));
                        }

                        await EmitAsync(
                            hasher,
                            chunkOffset,
                            chunkLength,
                            sink,
                            cancellationToken).ConfigureAwait(false);

                        chunkOffset = checked(chunkOffset + chunkLength);
                        chunkLength = 0;
                        gearHash = 0;
                        hashStart = inputIndex;

                        // Re-evaluate the candidate as the first byte of the next chunk.
                        continue;
                    }
                }

                chunkLength++;
                inputIndex++;

                if (chunkLength == profile.Maximum)
                {
                    if (inputIndex > hashStart)
                    {
                        hasher.Update(ioBuffer.AsSpan(hashStart, inputIndex - hashStart));
                    }

                    await EmitAsync(
                        hasher,
                        chunkOffset,
                        chunkLength,
                        sink,
                        cancellationToken).ConfigureAwait(false);

                    chunkOffset = checked(chunkOffset + chunkLength);
                    chunkLength = 0;
                    gearHash = 0;
                    hashStart = inputIndex;
                }
            }

            if (inputIndex > hashStart)
            {
                hasher.Update(ioBuffer.AsSpan(hashStart, inputIndex - hashStart));
            }
        }

        if (chunkLength != 0)
        {
            await EmitAsync(
                hasher,
                chunkOffset,
                chunkLength,
                sink,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask ScanFixedAsync(
        Stream source,
        byte[] ioBuffer,
        int chunkSize,
        IncrementalSuiteHasher hasher,
        MetadataChunkSink sink,
        CancellationToken cancellationToken)
    {
        int chunkLength = 0;
        long chunkOffset = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int read = await source
                .ReadAsync(ioBuffer.AsMemory(0, Math.Min(IoBufferSize, chunkSize)), cancellationToken)
                .ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            int inputIndex = 0;

            while (inputIndex < read)
            {
                int take = Math.Min(chunkSize - chunkLength, read - inputIndex);
                hasher.Update(ioBuffer.AsSpan(inputIndex, take));
                inputIndex += take;
                chunkLength += take;

                if (chunkLength == chunkSize)
                {
                    await EmitAsync(
                        hasher,
                        chunkOffset,
                        chunkLength,
                        sink,
                        cancellationToken).ConfigureAwait(false);

                    chunkOffset = checked(chunkOffset + chunkLength);
                    chunkLength = 0;
                }
            }
        }

        if (chunkLength != 0)
        {
            await EmitAsync(
                hasher,
                chunkOffset,
                chunkLength,
                sink,
                cancellationToken).ConfigureAwait(false);
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
