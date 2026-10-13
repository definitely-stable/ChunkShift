using System.Buffers.Binary;
using System.Text;
using BenchmarkDotNet.Attributes;
using ChunkShift.Chunking;
using ChunkShift.Hashing;
using ChunkShift.Manifest;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

/// <summary>
/// CORE011-CSM-002/G2-B: deliberately NOT wired into production.
/// Collects the exact canonical 36-byte logical records into a bounded
/// per-operation buffer, then forwards the identical byte stream to the
/// existing hash-suite backend.
/// </summary>
internal sealed class CsmManifestIdStagingProbe : IDisposable
{
    private const int RecordBytes = CsmFormat.HashSize + sizeof(uint);
    private readonly HashSuiteIncrementalHasher _hasher;
    private readonly byte[] _staging;
    private int _used;
    private ulong _chunkCount;
    private ulong _contentLength;
    private int _entryUpdates;
    private bool _completed;
    private bool _disposed;

    internal CsmManifestIdStagingProbe(
        HashSuiteId hashSuite,
        ChunkingProfileId profileId,
        ProfileFingerprint fingerprint,
        int batchEntries)
    {
        if (batchEntries is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(batchEntries));
        }

        ArgumentNullException.ThrowIfNull(hashSuite);
        ArgumentNullException.ThrowIfNull(profileId);
        _staging = new byte[checked(batchEntries * RecordBytes)];
        _hasher = HashSuiteIncrementalHasher.Create(hashSuite);
        _hasher.Append("chunkshift.manifest-id.v1\0"u8);
        AppendIdentifier(hashSuite.Value);
        AppendIdentifier(profileId.Value);

        Span<byte> fingerprintBytes = stackalloc byte[CsmFormat.HashSize];
        fingerprint.Value.CopyTo(fingerprintBytes);
        _hasher.Append(fingerprintBytes);
    }

    internal int EntryUpdateCalls => _entryUpdates;
    internal int StagingCapacityBytes => _staging.Length;
    internal ulong ChunkCount => _chunkCount;
    internal ulong ContentLength => _contentLength;

    internal void Append(ChunkId id, uint length)
    {
        ThrowIfUnavailable();
        ArgumentOutOfRangeException.ThrowIfZero(length);

        // Apply integer-validation before buffering this new record.
        // This avoids a partially staged record on a rejected call.
        ulong newChunkCount = checked(_chunkCount + 1);
        ulong newContentLength = checked(_contentLength + length);

        Span<byte> entry = _staging.AsSpan(_used, RecordBytes);
        id.Value.CopyTo(entry);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[CsmFormat.HashSize..], length);
        _used += RecordBytes;

        _chunkCount = newChunkCount;
        _contentLength = newContentLength;

        if (_used == _staging.Length)
        {
            Flush();
        }
    }

    internal ManifestId Complete()
    {
        ThrowIfUnavailable();
        Flush();

        Span<byte> totals = stackalloc byte[2 * sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(totals, _chunkCount);
        BinaryPrimitives.WriteUInt64LittleEndian(totals[sizeof(ulong)..], _contentLength);
        _hasher.Append(totals);
        _completed = true;
        return new ManifestId(_hasher.FinalizeHash());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hasher.Dispose();
    }

    private void Flush()
    {
        if (_used == 0)
        {
            return;
        }

        _hasher.Append(_staging.AsSpan(0, _used));
        _entryUpdates++;
        _used = 0;
    }

    private void AppendIdentifier(string value)
    {
        int bytes = Encoding.UTF8.GetByteCount(value);
        if (bytes is <= 0 or > CsmFormat.MaximumIdentifierBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        Span<byte> prefix = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(prefix, checked((ushort)bytes));
        _hasher.Append(prefix);

        Span<byte> utf8 = stackalloc byte[bytes];
        if (Encoding.UTF8.GetBytes(value, utf8) != bytes)
        {
            throw new InvalidOperationException("UTF-8 identifier length changed.");
        }

        _hasher.Append(utf8);
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed)
        {
            throw new InvalidOperationException("Manifest identity has already been completed.");
        }
    }
}

/// <summary>
/// Micro-screen for record staging; no product-path CPU/LOH verdict follows.
/// </summary>
[MemoryDiagnoser]
public class Core011CsmBatchBenchmarks
{
    private ChunkId[] _ids = [];
    private uint[] _lengths = [];
    private HashSuiteId _suite = null!;
    private ChunkingProfileId _profileId = null!;
    private ProfileFingerprint _fingerprint;
    private ManifestId _expected;

    [Params(64, 4096, 8193)]
    public int Entries { get; set; }

    [Params("blake3", "sha256")]
    public string SuiteName { get; set; } = "blake3";

    internal ManifestId Expected => _expected;
    internal HashSuiteId Suite => _suite;
    internal ChunkingProfileId Profile => _profileId;
    internal ProfileFingerprint Fingerprint => _fingerprint;

    [GlobalSetup]
    public void Setup()
    {
        if (Entries is < 0 or > 8193)
        {
            throw new ArgumentOutOfRangeException(nameof(Entries));
        }

        _suite = SuiteName switch
        {
            "blake3" => HashSuiteIds.Blake3256V1,
            "sha256" => HashSuiteIds.Sha256V1,
            _ => throw new ArgumentException("Unknown hash suite.", nameof(SuiteName)),
        };
        ChunkScanConfiguration.ProfileRegistration profile =
            ChunkScanConfiguration.ResolveProfileRegistration(null);
        _profileId = profile.Id;
        _fingerprint = profile.Fingerprint;

        _ids = new ChunkId[Entries];
        _lengths = new uint[Entries];
        Span<byte> hashBytes = stackalloc byte[CsmFormat.HashSize];

        for (int index = 0; index < Entries; index++)
        {
            for (int j = 0; j < CsmFormat.HashSize; j++)
            {
                hashBytes[j] = unchecked((byte)(j * 23 + index * 19));
            }

            BinaryPrimitives.WriteInt32LittleEndian(hashBytes, index);
            _ids[index] = new ChunkId(Hash256.FromBytes(hashBytes));
            _lengths[index] = (uint)(16_384 + index % 251);
        }

        // The two intentionally independent baselines agree:
        // 1. existing production incremental accumulator
        // 2. one-shot hash of exact canonical bytes.
        _expected = ProductionPerEntry();
        if (_expected != IndependentCanonicalHash())
        {
            throw new InvalidOperationException("Independent canonical-byte ManifestId mismatch.");
        }

        foreach (int size in new[] { 1, 16, 64, 256 })
        {
            if (ComputeStaged(size, out int calls) != _expected ||
                calls != (Entries + size - 1) / size)
            {
                throw new InvalidOperationException("Staged ManifestId differs from exact canonical bytes.");
            }
        }
    }

    [Benchmark(Baseline = true)]
    public ManifestId ProductionPerEntry()
    {
        using var accumulator = new ManifestIdAccumulator(_suite, _profileId, _fingerprint);
        for (int index = 0; index < _ids.Length; index++)
        {
            accumulator.Append(_ids[index], _lengths[index]);
        }

        return accumulator.Complete();
    }

    [Benchmark]
    public ManifestId Stage16() => ComputeStaged(16, out _);

    [Benchmark]
    public ManifestId Stage64() => ComputeStaged(64, out _);

    [Benchmark]
    public ManifestId Stage256() => ComputeStaged(256, out _);

    internal ManifestId ComputeStaged(int batchEntries, out int entryUpdateCalls)
    {
        using var accumulator = new CsmManifestIdStagingProbe(
            _suite, _profileId, _fingerprint, batchEntries);
        for (int index = 0; index < _ids.Length; index++)
        {
            accumulator.Append(_ids[index], _lengths[index]);
        }

        ManifestId result = accumulator.Complete();
        entryUpdateCalls = accumulator.EntryUpdateCalls;
        return result;
    }

    internal ManifestId IndependentCanonicalHash()
    {
        using var output = new MemoryStream();
        output.Write("chunkshift.manifest-id.v1\0"u8);
        WriteIdentifier(output, _suite.Value);
        WriteIdentifier(output, _profileId.Value);
        Span<byte> hashBytes = stackalloc byte[CsmFormat.HashSize];
        _fingerprint.Value.CopyTo(hashBytes);
        output.Write(hashBytes);

        ulong contentLength = 0;
        Span<byte> lengthBytes = stackalloc byte[sizeof(uint)];
        for (int i = 0; i < _ids.Length; i++)
        {
            _ids[i].Value.CopyTo(hashBytes);
            output.Write(hashBytes);
            BinaryPrimitives.WriteUInt32LittleEndian(lengthBytes, _lengths[i]);
            output.Write(lengthBytes);
            contentLength = checked(contentLength + _lengths[i]);
        }

        Span<byte> totals = stackalloc byte[2 * sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(totals, checked((ulong)_ids.Length));
        BinaryPrimitives.WriteUInt64LittleEndian(totals[sizeof(ulong)..], contentLength);
        output.Write(totals);
        return new ManifestId(HashSuiteHasher.Hash(_suite, output.ToArray()));
    }

    private static void WriteIdentifier(Stream destination, string value)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        if (utf8.Length is <= 0 or > CsmFormat.MaximumIdentifierBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        Span<byte> prefix = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(prefix, checked((ushort)utf8.Length));
        destination.Write(prefix);
        destination.Write(utf8);
    }
}
