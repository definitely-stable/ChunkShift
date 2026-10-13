using System.Buffers.Binary;
using BenchmarkDotNet.Attributes;
using ChunkShift.Chunking;
using ChunkShift.Hashing;
using ChunkShift.Manifest;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

/// <summary>
/// CORE011-CSM-001 / G2-A: non-decision component screening on a real,
/// writer-generated CSM. Logical ChunkIds are deterministic synthetic inputs;
/// the manifest is valid for metadata verification, not content verification.
/// Times for overlapping kernels MUST NOT be added as Amdahl percentages.
/// </summary>
[MemoryDiagnoser]
public sealed class Core011CsmComponentBenchmarks
{
    private byte[] _manifest = [];
    private byte[] _blockRecord = [];
    private ChunkId[] _ids = [];
    private uint[] _lengths = [];
    private HashSuiteId _suite = null!;
    private ChunkingProfileId _profileId = null!;
    private ProfileFingerprint _fingerprint;
    private ManifestId _expectedManifestId;

    [Params(64, 4096)]
    public int Entries { get; set; }

    [Params("blake3", "sha256")]
    public string SuiteName { get; set; } = "blake3";

    [Params(false, true)]
    public bool IncludeBlockIndex { get; set; }

    internal ManifestId ExpectedManifestId => _expectedManifestId;

    internal uint StoredBlockCrc =>
        BinaryPrimitives.ReadUInt32LittleEndian(_blockRecord.AsSpan(_blockRecord.Length - sizeof(uint)));

    internal int EncodedBlockBytes => _blockRecord.Length;

    [GlobalSetup]
    public void Setup()
    {
        if (Entries is < 1 or > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(Entries));
        }

        _suite = SuiteName switch
        {
            "blake3" => HashSuiteIds.Blake3256V1,
            "sha256" => HashSuiteIds.Sha256V1,
            _ => throw new ArgumentException("Unsupported hash suite.", nameof(SuiteName)),
        };

        ChunkScanConfiguration.ProfileRegistration registration =
            ChunkScanConfiguration.ResolveProfileRegistration(null);
        _profileId = registration.Id;
        _fingerprint = registration.Fingerprint;

        _ids = new ChunkId[Entries];
        _lengths = new uint[Entries];
        Span<byte> digest = stackalloc byte[CsmFormat.HashSize];

        for (int i = 0; i < Entries; i++)
        {
            for (int j = 0; j < digest.Length; j++)
            {
                digest[j] = unchecked((byte)(i * 37 + j * 13));
            }

            BinaryPrimitives.WriteInt32LittleEndian(digest, i);
            _ids[i] = new ChunkId(Hash256.FromBytes(digest));
            _lengths[i] = checked((uint)(16_384 + (i % 251)));
        }

        // Produce actual CSM v1 physical bytes with the existing production
        // encoder. This avoids benchmarking an invented/incorrect block layout.
        using var output = new MemoryStream();
        using (CsmEncoderSession session = CsmEncoderSession.CreateAsync(
            output, _suite, _profileId, _fingerprint, IncludeBlockIndex,
            CancellationToken.None).GetAwaiter().GetResult())
        {
            for (int i = 0; i < Entries; i++)
            {
                session.AppendAsync(_ids[i], _lengths[i], CancellationToken.None)
                    .GetAwaiter().GetResult();
            }

            _expectedManifestId = session.CompleteAsync(CancellationToken.None)
                .GetAwaiter().GetResult().ManifestId;
        }

        _manifest = output.ToArray();
        _blockRecord = ExtractFirstChunkBlock(_manifest);
        int expectedRecordBytes = checked(
            CsmFormat.SectionHeaderSize + CsmFormat.CblkPrefixSize
            + Entries * (CsmFormat.HashSize + sizeof(uint)) + sizeof(uint));
        if (_blockRecord.Length != expectedRecordBytes ||
            ComputeBlockCrc() != StoredBlockCrc ||
            HashFromLogicalEntries() != _expectedManifestId ||
            DecodeAndHashBlock() != _expectedManifestId)
        {
            throw new InvalidOperationException("G2-A component source is not a consistent physical CSM.");
        }

        using var verifyStream = new MemoryStream(_manifest, writable: false);
        CsmReadResult verified = CsmReader.ReadAndVerifyAsync(
            verifyStream).GetAwaiter().GetResult();
        if (!verified.IsValid || verified.ComputedManifestId != _expectedManifestId ||
            verified.ChunkCount != checked((ulong)Entries) ||
            verified.HasBlockIndex != IncludeBlockIndex)
        {
            throw new InvalidOperationException("Production CSM metadata verifier rejected the G2-A fixture.");
        }
    }

    // True metadata-only product-path baseline, including reader construction,
    // CBLK CRC, entry validation, identity accumulation, and trailer/BIDX.
    [Benchmark]
    public async Task<ManifestId> VerifyManifestAsync()
    {
        using var stream = new MemoryStream(_manifest, writable: false);
        CsmReadResult result = await CsmReader.ReadAndVerifyAsync(stream);
        if (!result.IsValid || result.ComputedManifestId != _expectedManifestId)
        {
            throw new InvalidOperationException("CSM verification identity changed during measurement.");
        }

        return result.ComputedManifestId;
    }

    // Individual diagnostic kernels overlap; their timings are NOT additive.
    [Benchmark]
    public uint ComputeBlockCrc() =>
        Crc32C.Compute(_blockRecord.AsSpan(0, _blockRecord.Length - sizeof(uint)));

    [Benchmark]
    public ManifestId HashFromLogicalEntries()
    {
        using var accumulator = new ManifestIdAccumulator(_suite, _profileId, _fingerprint);
        for (int i = 0; i < _ids.Length; i++)
        {
            accumulator.Append(_ids[i], _lengths[i]);
        }

        return accumulator.Complete();
    }

    [Benchmark]
    public ManifestId DecodeAndHashBlock()
    {
        using var accumulator = new ManifestIdAccumulator(_suite, _profileId, _fingerprint);
        int idsOffset = CsmFormat.SectionHeaderSize + CsmFormat.CblkPrefixSize;
        int lengthsOffset = idsOffset + Entries * CsmFormat.HashSize;

        for (int i = 0; i < Entries; i++)
        {
            ChunkId id = new(Hash256.FromBytes(
                _blockRecord.AsSpan(idsOffset + i * CsmFormat.HashSize, CsmFormat.HashSize)));
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(
                _blockRecord.AsSpan(lengthsOffset + i * sizeof(uint), sizeof(uint)));
            if (length is 0 or > int.MaxValue)
            {
                throw new InvalidDataException("Invalid G2-A encoded chunk length.");
            }

            accumulator.Append(id, length);
        }

        return accumulator.Complete();
    }

    [Benchmark]
    public ulong DecodeBlockOnly()
    {
        int idsOffset = CsmFormat.SectionHeaderSize + CsmFormat.CblkPrefixSize;
        int lengthsOffset = idsOffset + Entries * CsmFormat.HashSize;
        ulong checksum = 0;
        Span<byte> decoded = stackalloc byte[CsmFormat.HashSize];

        for (int i = 0; i < Entries; i++)
        {
            ChunkId id = new(Hash256.FromBytes(
                _blockRecord.AsSpan(idsOffset + i * CsmFormat.HashSize, CsmFormat.HashSize)));
            id.Value.CopyTo(decoded);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(
                _blockRecord.AsSpan(lengthsOffset + i * sizeof(uint), sizeof(uint)));
            if (length is 0 or > int.MaxValue)
            {
                throw new InvalidDataException("Invalid G2-A encoded chunk length.");
            }

            checksum = unchecked(checksum +
                BinaryPrimitives.ReadUInt64LittleEndian(decoded) + length);
        }

        return checksum;
    }

    private static byte[] ExtractFirstChunkBlock(byte[] manifest)
    {
        int position = CsmFormat.PreambleSize;

        while (position <= manifest.Length - CsmFormat.SectionHeaderSize)
        {
            ReadOnlySpan<byte> header =
                manifest.AsSpan(position, CsmFormat.SectionHeaderSize);
            uint sectionType = BinaryPrimitives.ReadUInt32LittleEndian(header);
            ulong payloadBytes = BinaryPrimitives.ReadUInt64LittleEndian(header[8..]);
            if (payloadBytes > int.MaxValue ||
                payloadBytes > (ulong)(manifest.Length - position - CsmFormat.SectionHeaderSize))
            {
                throw new InvalidDataException("Malformed G2-A CSM section length.");
            }

            int recordBytes = checked(CsmFormat.SectionHeaderSize + (int)payloadBytes);
            if (sectionType == CsmFormat.ChunkBlock)
            {
                return manifest.AsSpan(position, recordBytes).ToArray();
            }

            position = checked(position + recordBytes);
        }

        throw new InvalidDataException("G2-A physical CSM does not contain a CBLK.");
    }
}
