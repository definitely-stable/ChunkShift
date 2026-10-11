using ChunkShift.Chunking;
using ChunkShift.Hashing;
using ChunkShift.Primitives;

namespace ChunkShift.Tests.Fuzzing;

/// <summary>
/// CORE011-FUZZ-001: bounded, deterministic CI differential smoke for the frozen Core profile.
/// This is not an independent cryptographic oracle or a throughput benchmark.
/// </summary>
public sealed class ScannerPropertyMatrixTests
{
    private const int Minimum = 16 * 1024;
    private const int Maximum = 256 * 1024;

    private static readonly (int Length, int Pattern)[] Inputs =
    [
        (0, 0), (1, 0), (Minimum - 1, 1), (Minimum, 2),
        (Minimum + 1, 3), (64 * 1024 - 1, 0), (64 * 1024, 2),
        (64 * 1024 + 1, 3), (Maximum - 1, 1), (Maximum, 0),
        (Maximum + 1, 2), (1024 * 1024, 1), (1024 * 1024 + 17, 0),
        (1024 * 1024 + 31, 2), (1024 * 1024 + 47, 3),
    ];

    private static readonly int[][] ReadSchedules =
    [
        [64 * 1024],
        [1, 7, 31, 4095, 65535, 2, 257],
        [Minimum - 1, 1, 1, 64 * 1024, Maximum - 1, 3],
    ];

    [Fact]
    public async Task GeneratedInputs_AreExactlyEqualToReferenceAcrossReadSchedulesAndSuites()
    {
        ChunkingKernelProfile profile =
            ChunkingKernelProfile.FastCdcGear(FastCdcProfile.CreateStable64K());

        foreach (HashSuiteId suite in new[] { HashSuiteIds.Blake3256V1, HashSuiteIds.Sha256V1 })
        {
            foreach ((int length, int pattern) in Inputs)
            {
                byte[] input = CreateBytes(length, pattern);
                ChunkKernelChunk[] reference = ChunkingReference.Chunk(input, profile, suite);

                foreach (int[] readSchedule in ReadSchedules)
                {
                    await AssertEquivalentAsync(input, suite, reference, readSchedule);
                }
            }
        }
    }

    [Fact]
    public async Task EditsAroundFrozenLimits_MatchFullReferenceRechunking()
    {
        const int length = 2 * Maximum + 37;
        byte[] original = CreateBytes(length, pattern: 0);
        ChunkingKernelProfile profile =
            ChunkingKernelProfile.FastCdcGear(FastCdcProfile.CreateStable64K());

        foreach (int offset in new[] { Minimum - 1, Minimum, Minimum + 1,
                     64 * 1024 - 1, 64 * 1024, 64 * 1024 + 1,
                     Maximum - 1, Maximum, Maximum + 1 })
        {
            foreach (int edit in new[] { 0, 1, 2 })
            {
                byte[] changed = ApplyEdit(original, offset, edit);
                ChunkKernelChunk[] reference = ChunkingReference.Chunk(
                    changed, profile, HashSuiteIds.Blake3256V1);
                await AssertEquivalentAsync(
                    changed, HashSuiteIds.Blake3256V1, reference, ReadSchedules[2]);
            }
        }
    }

    private static async Task AssertEquivalentAsync(
        byte[] input,
        HashSuiteId suite,
        ChunkKernelChunk[] reference,
        int[] readSchedule)
    {
        using var stream = new ScheduledReadStream(input, readSchedule);
        using var rebuilt = new MemoryStream();
        int index = 0;
        long nextOffset = 0;

        await ChunkScanner.ScanAsync(
            stream,
            (chunk, content, _) =>
            {
                Assert.True(index < reference.Length, "Scanner emitted an unexpected extra chunk.");
                Assert.Equal(index, chunk.Index);
                Assert.Equal(nextOffset, chunk.Offset);
                Assert.Equal(reference[index].Offset, chunk.Offset);
                Assert.Equal(reference[index].Length, chunk.Length);
                Assert.Equal(reference[index].Id, chunk.Id);
                Assert.Equal(chunk.Length, content.Length);
                Assert.InRange(chunk.Length, 1, Maximum);

                // The only chunk permitted below Minimum is the final EOF remainder.
                Assert.True(chunk.Length >= Minimum ||
                    chunk.Offset + chunk.Length == input.LongLength);

                // Verify identities from actual borrowed content, not only from the oracle.
                Assert.Equal(
                    new ChunkId(HashSuiteHasher.Hash(suite, content.Span)),
                    chunk.Id);

                rebuilt.Write(content.Span);
                nextOffset += chunk.Length;
                index++;
                return ValueTask.CompletedTask;
            },
            new ChunkScanOptions { HashSuite = suite });

        Assert.Equal(reference.Length, index);
        Assert.Equal(input.LongLength, nextOffset);
        Assert.Equal(input, rebuilt.ToArray());
    }

    // 0: deterministic high entropy; 1: zeros; 2: short period; 3: alternating.
    private static byte[] CreateBytes(int length, int pattern)
    {
        byte[] bytes = new byte[length];
        uint state = 0xC011CAFEu;
        for (int i = 0; i < bytes.Length; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            bytes[i] = pattern switch
            {
                0 => (byte)state,
                1 => 0,
                2 => (byte)(i % 13),
                3 => (byte)((i & 1) == 0 ? 0 : 255),
                _ => throw new ArgumentOutOfRangeException(nameof(pattern)),
            };
        }
        return bytes;
    }

    // 0: replace, 1: insert, 2: remove. No assumptions about convergence.
    private static byte[] ApplyEdit(byte[] source, int offset, int edit)
    {
        if (edit == 0)
        {
            byte[] replaced = (byte[])source.Clone();
            replaced[offset] ^= 0xA5;
            return replaced;
        }
        if (edit == 1)
        {
            byte[] inserted = new byte[source.Length + 1];
            source.AsSpan(0, offset).CopyTo(inserted);
            inserted[offset] = 0xA5;
            source.AsSpan(offset).CopyTo(inserted.AsSpan(offset + 1));
            return inserted;
        }
        byte[] removed = new byte[source.Length - 1];
        source.AsSpan(0, offset).CopyTo(removed);
        source.AsSpan(offset + 1).CopyTo(removed.AsSpan(offset));
        return removed;
    }

    private sealed class ScheduledReadStream(byte[] data, int[] schedule) : Stream
    {
        private int _position;
        private int _nextRead;

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
            if (destination.IsEmpty || _position == data.Length)
            {
                return 0;
            }
            int budget = schedule[_nextRead++ % schedule.Length];
            int count = Math.Min(Math.Min(budget, destination.Length), data.Length - _position);
            data.AsSpan(_position, count).CopyTo(destination);
            _position += count;
            return count;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
