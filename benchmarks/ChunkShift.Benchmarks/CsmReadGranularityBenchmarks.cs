using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using ChunkShift.Hashing;
using ChunkShift.Manifest;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

/// <summary>
/// A2-F11: cost of CSM verification as a function of how many source reads the
/// reader issues. The source is forward-only and either free per call or
/// charges a fixed busy-wait per call, as a request body or pipe might.
/// </summary>
/// <remarks>
/// Entry counts approximate the manifests of 1 MiB, 64 MiB and 1 GiB sources at
/// a 64 KiB target (about 80 KiB mean chunk). Read calls per verification are
/// printed once per case in the setup, outside the measurement.
/// </remarks>
[MemoryDiagnoser]
public class CsmReadGranularityBenchmarks
{
    private byte[] _manifest = null!;

    [Params(16, 800, 13_000)]
    public int Entries { get; set; }

    [Params(0, 20)]
    public int MicrosecondsPerRead { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _manifest = CreateManifest(Entries);

        var counting = new CostedReadStream(_manifest, 0);
        ManifestVerificationResult result =
            ChunkManifest.VerifyManifestAsync(counting).GetAwaiter().GetResult();

        if (!result.IsValid)
        {
            throw new InvalidOperationException("Benchmark manifest did not verify.");
        }

        Console.WriteLine(FormattableString.Invariant(
            $"// csm-read-granularity entries={Entries} bytes={_manifest.Length} reads={counting.Reads}"));
    }

    [Benchmark]
    public async Task<bool> VerifyManifest()
    {
        ManifestVerificationResult result = await ChunkManifest.VerifyManifestAsync(
            new CostedReadStream(_manifest, MicrosecondsPerRead));
        return result.IsValid;
    }

    internal static byte[] CreateManifest(int entries)
    {
        using var encoded = new MemoryStream();
        using (CsmEncoderSession encoder = CsmEncoderSession.CreateAsync(
            encoded,
            HashSuiteIds.Default,
            new ChunkingProfileId("bench.csm-read-granularity.v1"),
            new ProfileFingerprint(HashSuiteHasher.Hash(HashSuiteIds.Sha256V1, "bench.csm-read-granularity.v1"u8)),
            includeBlockIndex: true,
            CancellationToken.None).GetAwaiter().GetResult())
        {
            byte[] id = new byte[32];

            for (int index = 0; index < entries; index++)
            {
                BitConverter.TryWriteBytes(id, (ulong)index + 1);
                encoder.AppendAsync(
                    new ChunkId(Hash256.FromBytes(id)),
                    (uint)(64 * 1024 + (index % 97)),
                    CancellationToken.None).AsTask().GetAwaiter().GetResult();
            }

            _ = encoder.CompleteAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        return encoded.ToArray();
    }

    /// <summary>Forward-only source that busy-waits a fixed time per read call.</summary>
    internal sealed class CostedReadStream(byte[] data, int microsecondsPerRead) : Stream
    {
        private int _position;

        internal int Reads { get; private set; }

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

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ReadCore(buffer.Span));

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int ReadCore(Span<byte> destination)
        {
            Reads++;

            if (microsecondsPerRead > 0)
            {
                long start = Stopwatch.GetTimestamp();
                while (Stopwatch.GetElapsedTime(start).TotalMicroseconds < microsecondsPerRead)
                {
                }
            }

            int count = Math.Min(destination.Length, data.Length - _position);
            data.AsSpan(_position, count).CopyTo(destination);
            _position += count;
            return count;
        }
    }
}
