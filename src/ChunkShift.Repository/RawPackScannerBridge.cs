using ChunkShift.Primitives;

namespace ChunkShift.Repository;

/// <summary>
/// Single-pack research bridge from the real public Core scanner. Not a
/// multi-pack coordinator, deduplicating ingest, or publication API.
/// Caller owns both independent streams and must discard output on any failure.
/// </summary>
internal static class RawPackScannerBridge
{
    internal static async Task<RawPackDescriptor?> WriteSinglePackAsync(Stream source,
        Stream destination, RawPackPolicy policy, HashSuiteId suite,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (!source.CanRead || ReferenceEquals(source, destination))
        {
            throw new ArgumentException("Source must be independently readable.", nameof(source));
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var writer = new PrivateRawPackWriter(destination, policy, suite);
        long count = 0;
        await ChunkScanner.ScanAsync(source, ConsumeAsync,
            new ChunkScanOptions { HashSuite = suite }, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        // The scanner does not emit a frame for empty content; no empty pack.
        if (count == 0)
        {
            return null;
        }

        return await writer.CompleteAsync(cancellationToken).ConfigureAwait(false);

        async ValueTask ConsumeAsync(ChunkInfo chunk, ReadOnlyMemory<byte> content, CancellationToken ct)
        {
            if (chunk.Index != count || chunk.Length != content.Length || content.IsEmpty)
            {
                throw new InvalidDataException("Unexpected Core chunk metadata.");
            }

            // Await the append inside the callback. The scanner's borrowed
            // memory is never queued, retained, or used after callback completion.
            RawPackPlacement placement = await writer.AppendMemoryAsync(content,
                new ChunkObjectKey(suite, chunk.Id), ct).ConfigureAwait(false);
            if (placement is not (RawPackPlacement.Append or RawPackPlacement.AppendDedicated))
            {
                // No partial success, no rewind/retry with a borrowed buffer.
                // Actual multi-pack rotation belongs to a later coordinator.
                throw new InvalidOperationException($"Single-pack scan requires another pack: {placement}.");
            }

            count = checked(count + 1);
        }
    }
}
