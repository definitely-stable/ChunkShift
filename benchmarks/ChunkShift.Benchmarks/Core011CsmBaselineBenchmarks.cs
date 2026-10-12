using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using ChunkShift.Benchmarks.Lab;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

/// <summary>
/// CORE011-CSM-001: public-API end-to-end CSM baseline, with identical content,
/// hash suite and BIDX choices across all operation lanes. These benchmarks
/// compare workloads; they do not make an adoption decision on their own.
/// </summary>
[MemoryDiagnoser]
public sealed class Core011CsmBaselineBenchmarks
{
    private byte[] _content = null!;
    private byte[] _manifest = null!;
    private HashSuiteId _suite = null!;
    private ManifestCreationOptions _options = null!;
    private long _expectedChunks;
    private ManifestInfo _expected = null!;
    private string _contentSha256 = null!;
    internal long ExpectedChunks => _expectedChunks;

    internal string CorpusSha256 => _contentSha256;

    internal string ManifestSha256 =>
        Convert.ToHexString(SHA256.HashData(_manifest)).ToLowerInvariant();

    internal int ManifestBytes => _manifest.Length;


    // The large lane crosses a 4096-record CBLK boundary at the frozen profile's
    // measured mean (~80.69 KiB). Small CI tests override this parameter to 1.
    [Params(16, 384)]
    public int ContentMiB { get; set; }

    [Params("blake3", "sha256")]
    public string SuiteName { get; set; } = "blake3";

    [Params(false, true)]
    public bool IncludeBlockIndex { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        if (ContentMiB < 1 || ContentMiB > 384)
        {
            throw new ArgumentOutOfRangeException(nameof(ContentMiB));
        }

        _suite = SuiteName switch
        {
            "blake3" => HashSuiteIds.Blake3256V1,
            "sha256" => HashSuiteIds.Sha256V1,
            _ => throw new ArgumentException("Unsupported benchmark suite.", nameof(SuiteName)),
        };

        _options = new ManifestCreationOptions
        {
            HashSuite = _suite,
            IncludeBlockIndex = IncludeBlockIndex,
        };

        _content = new byte[checked(ContentMiB * 1024 * 1024)];
        var random = new DeterministicPrng(0xC485_5A11_2026UL);
        random.Fill(_content);
        _contentSha256 = Convert.ToHexString(SHA256.HashData(_content)).ToLowerInvariant();

        using var source = new MemoryStream(_content, writable: false);
        using var destination = new MemoryStream();

        _expected = ChunkManifest.CreateAsync(source, destination, _options)
            .GetAwaiter().GetResult();
        _expectedChunks = _expected.ChunkCount;
        _manifest = destination.ToArray();

        if (_manifest.LongLength != _expected.PhysicalLength ||
            _expected.ContentLength != _content.LongLength ||
            _expected.HasBlockIndex != IncludeBlockIndex)
        {
            throw new InvalidOperationException("Baseline manifest fixture is inconsistent.");
        }

        // Fail fast before a benchmark run if the generated fixture is invalid.
        using var verify = new MemoryStream(_manifest, writable: false);
        ManifestVerificationResult result = ChunkManifest.VerifyManifestAsync(verify)
            .GetAwaiter().GetResult();
        Validate(result);
    }

    // The metadata-only multi-CBLK lab must not keep a 384-MiB source in LOH.
    // The stored manifest and the reference identity remain fully verified.
    internal WeakReference ReleaseContentForMetadataOnly()
    {
        if (_expected is null || _content.LongLength != _expected.ContentLength)
        {
            throw new InvalidOperationException("No retained content fixture to release.");
        }

        var weak = new WeakReference(_content);
        _content = [];
        return weak;
    }

    private void RequireContent()
    {
        if (_expected is null || _content.LongLength != _expected.ContentLength)
        {
            throw new InvalidOperationException("Content was released for a metadata-only workload.");
        }
    }

    [Benchmark]
    public async Task<long> ScanAsync()
    {
        RequireContent();
        using var source = new MemoryStream(_content, writable: false);
        long count = 0;
        long offset = 0;

        await ChunkScanner.ScanAsync(
            source,
            (chunk, content, _) =>
            {
                if (chunk.Index != count || chunk.Offset != offset ||
                    content.Length != chunk.Length)
                {
                    throw new InvalidOperationException("Scanner lost chunk contiguity.");
                }

                offset += chunk.Length;
                count++;
                return ValueTask.CompletedTask;
            },
            new ChunkScanOptions { HashSuite = _suite });

        if (count != _expectedChunks || offset != _content.LongLength)
        {
            throw new InvalidOperationException("Scanner and manifest disagree.");
        }

        return count;
    }

    [Benchmark]
    public async Task<long> CreateAsync()
    {
        RequireContent();
        using var content = new MemoryStream(_content, writable: false);
        // Reusable consumer contract: destination is preallocated to the
        // known manifest length; includes normal instance allocation cost.
        using var destination = new MemoryStream(checked(_manifest.Length + 256));
        ManifestInfo created = await ChunkManifest.CreateAsync(
            content, destination, _options);

        if (created.ChunkCount != _expectedChunks ||
            created.ManifestId != _expected.ManifestId ||
            created.FileDigest != _expected.FileDigest ||
            destination.Length != _manifest.Length ||
            !destination.GetBuffer().AsSpan(0, _manifest.Length).SequenceEqual(_manifest))
        {
            throw new InvalidOperationException("Create changed frozen CSM bytes/identities.");
        }

        return created.ChunkCount;
    }

    [Benchmark]
    public async Task<long> VerifyManifestAsync()
    {
        using var source = new MemoryStream(_manifest, writable: false);
        ManifestVerificationResult verified =
            await ChunkManifest.VerifyManifestAsync(source);
        Validate(verified);
        return verified.Manifest.ChunkCount;
    }

    [Benchmark]
    public async Task<long> EnumerateAsync()
    {
        using var source = new MemoryStream(_manifest, writable: false);
        await using ManifestReader reader = await ManifestReader.OpenAsync(source);

        var entries = new ChunkInfo[256];
        long count = 0;
        long offset = 0;
        int read;

        while ((read = await reader.ReadAsync(entries)) != 0)
        {
            for (int i = 0; i < read; i++)
            {
                ChunkInfo entry = entries[i];
                if (entry.Index != count || entry.Offset != offset ||
                    entry.Length <= 0)
                {
                    throw new InvalidOperationException("Enumerated CSM entries are discontinuous.");
                }

                count++;
                offset += entry.Length;
            }
        }

        if (count != _expectedChunks || offset != _expected.ContentLength ||
            reader.VerificationResult is not { } verified)
        {
            throw new InvalidOperationException("CSM enumeration was incomplete.");
        }

        Validate(verified);
        return count;
    }

    [Benchmark]
    public async Task<long> VerifyContentAsync()
    {
        RequireContent();
        using var content = new MemoryStream(_content, writable: false);
        using var manifest = new MemoryStream(_manifest, writable: false);

        ManifestVerificationResult verified =
            await ChunkManifest.VerifyAsync(content, manifest);
        Validate(verified);
        return verified.Manifest.ChunkCount;
    }

    private void Validate(ManifestVerificationResult result)
    {
        if (!result.IsValid ||
            result.Manifest.ChunkCount != _expectedChunks ||
            result.Manifest.ManifestId != _expected.ManifestId ||
            result.Manifest.FileDigest != _expected.FileDigest ||
            result.Manifest.ContentLength != _expected.ContentLength)
        {
            throw new InvalidOperationException("Baseline CSM oracle mismatch.");
        }
    }
}
