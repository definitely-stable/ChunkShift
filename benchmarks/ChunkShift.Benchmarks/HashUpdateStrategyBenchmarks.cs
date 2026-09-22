using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using Blake3;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

[MemoryDiagnoser]
public class HashUpdateStrategyBenchmarks
{
    private byte[] _data = null!;

    [Params(64 * 1024, 256 * 1024, 1024 * 1024)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _data = new byte[Size];
        var random = new Lab.DeterministicPrng(0xD1CE_BA5E_2026UL);
        random.Fill(_data);
    }

    [Benchmark(Baseline = true)]
    public Hash256 Blake3OneShot()
    {
        Hash hash = Hasher.Hash(_data);
        return Hash256.FromBytes(hash.AsSpan());
    }

    [Benchmark]
    public Hash256 Blake3Incremental64K()
    {
        using Hasher hasher = Hasher.New();

        for (int offset = 0; offset < _data.Length; offset += 64 * 1024)
        {
            int length = Math.Min(64 * 1024, _data.Length - offset);
            hasher.Update(_data.AsSpan(offset, length));
        }

        Hash hash = hasher.Finalize();
        return Hash256.FromBytes(hash.AsSpan());
    }

    [Benchmark]
    public Hash256 Sha256OneShot()
    {
        Span<byte> digest = stackalloc byte[32];
        _ = SHA256.HashData(_data, digest);
        return Hash256.FromBytes(digest);
    }

    [Benchmark]
    public Hash256 Sha256Incremental64K()
    {
        using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        for (int offset = 0; offset < _data.Length; offset += 64 * 1024)
        {
            int length = Math.Min(64 * 1024, _data.Length - offset);
            hasher.AppendData(_data.AsSpan(offset, length));
        }

        Span<byte> digest = stackalloc byte[32];
        if (!hasher.TryGetHashAndReset(digest, out int written) || written != digest.Length)
        {
            throw new CryptographicException("Could not finalize incremental SHA-256 benchmark.");
        }

        return Hash256.FromBytes(digest);
    }
}
