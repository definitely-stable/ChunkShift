using System.Buffers.Binary;
using BenchmarkDotNet.Attributes;
using ChunkShift.Hashing;
using ChunkShift.Manifest;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

/// <summary>
/// CBLK CRC-32C cost: the bitwise definition CSM used before (baseline) versus
/// <see cref="Crc32C"/>. 147,480 bytes is one full CBLK record (4096 entries).
/// </summary>
[MemoryDiagnoser]
public class Crc32CBenchmarks
{
    private byte[] _data = null!;

    [Params(64, 4 * 1024, 147_480)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = new byte[Size];
        new Lab.DeterministicPrng(0x32C0_2026UL).Fill(_data);
    }

    [Benchmark(Baseline = true)]
    public uint Bitwise() => BitwiseCrc32C(_data);

    [Benchmark]
    public uint Current() => Crc32C.Compute(_data);

    private static uint BitwiseCrc32C(ReadOnlySpan<byte> data)
    {
        uint state = 0xFFFFFFFFu;

        foreach (byte value in data)
        {
            state ^= value;

            for (int bit = 0; bit < 8; bit++)
            {
                uint mask = unchecked((uint)-(int)(state & 1u));
                state = (state >> 1) ^ (0x82F63B78u & mask);
            }
        }

        return ~state;
    }
}

/// <summary>
/// Manifest-only verification (<c>VerifyManifestAsync</c> path) of a
/// 262,144-entry CSM (64 CBLKs, about 9.4 MB): CBLK CRCs, logical ManifestId
/// and physical FileDigest. Compare across commits to see the CRC share.
/// </summary>
[MemoryDiagnoser]
public class CsmVerifyManifestBenchmarks
{
    private byte[] _manifest = null!;

    [Params(262_144)]
    public int Entries { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        var profileId = new ChunkingProfileId("bench.csm-verify.v1");
        var fingerprint = new ProfileFingerprint(
            HashSuiteHasher.Hash(HashSuiteIds.Sha256V1, "bench.csm-verify.profile.v1"u8));

        using var encoded = new MemoryStream();
        using (CsmEncoderSession encoder = await CsmEncoderSession.CreateAsync(
            encoded,
            HashSuiteIds.Default,
            profileId,
            fingerprint,
            includeBlockIndex: true,
            CancellationToken.None))
        {
            var idBytes = new byte[CsmFormat.HashSize];

            for (int index = 0; index < Entries; index++)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(idBytes, checked((ulong)index + 1));
                await encoder.AppendAsync(
                    new ChunkId(Hash256.FromBytes(idBytes)),
                    checked((uint)((index % 97) + 1)),
                    CancellationToken.None);
            }

            _ = await encoder.CompleteAsync(CancellationToken.None);
        }

        _manifest = encoded.ToArray();
    }

    [Benchmark]
    public async Task<bool> VerifyManifest()
    {
        CsmReadResult result = await CsmReader.ReadAndVerifyAsync(
            new MemoryStream(_manifest, writable: false));
        return result.IsValid;
    }
}
