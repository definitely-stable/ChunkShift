// Package-consumer smoke for the packed ChunkShift NuGet package.
//
// The same program runs under JIT (CI package-smoke), under NativeAOT on x64
// and ARM64 (heavy validation) and in the release workflow, so every lane
// exercises the real public paths instead of a primitive-only placeholder:
//
//   * ChunkScanner over both HashSuites;
//   * ChunkManifest.CreateAsync / VerifyManifestAsync / VerifyAsync and
//     ManifestReader, with and without BIDX, including a content mismatch;
//   * a 32 MiB generated non-seekable source through the scanner and the
//     manifest create/verify path, each under a managed-allocation budget.
//
// Deterministic results are written as evidence lines (--evidence <path>) so
// JIT vs AOT and x64 vs ARM64 runs can be compared byte-for-byte. Allocation
// figures are printed to stdout only, because they are not deterministic.

using System.Globalization;
using System.Security.Cryptography;
using ChunkShift;
using ChunkShift.Primitives;

const long GeneratedLength = 32L * 1024 * 1024;
const uint GeneratedSeed = 0x51A6E55u;

// A full-source materialization alone would allocate at least 32 MiB. Leave
// substantial runtime headroom while still detecting that class of regression.
const long AllocationBudget = 16L * 1024 * 1024;

string? evidencePath = ParseEvidencePath(args);
var evidence = new List<string>();

byte[] bytes = XorShift.Create(768 * 1024, 0xC0FFEE42u);
byte[] tampered = (byte[])bytes.Clone();
tampered[tampered.Length / 2] ^= 0x80;

foreach (HashSuiteId suite in new[] { HashSuiteIds.Blake3256V1, HashSuiteIds.Sha256V1 })
{
    ScanResult scan = await ScanAsync(
        new MemoryStream(bytes, writable: false),
        suite);

    Check(
        scan.Covered == bytes.LongLength && scan.Ids.Count > 0,
        "scanner did not cover the complete source");

    evidence.Add(Invariant(
        $"scan suite={suite} chunks={scan.Ids.Count} bytes={scan.Covered} ids-sha256={scan.IdsDigest}"));

    foreach (bool includeBlockIndex in new[] { false, true })
    {
        using var manifest = new MemoryStream();

        ManifestInfo created = await ChunkManifest.CreateAsync(
            new MemoryStream(bytes, writable: false),
            manifest,
            new ManifestCreationOptions
            {
                HashSuite = suite,
                IncludeBlockIndex = includeBlockIndex,
            });

        Check(
            created.HashSuite == suite &&
            created.HasBlockIndex == includeBlockIndex &&
            created.ChunkCount == (ulong)scan.Ids.Count &&
            created.ContentLength == (ulong)bytes.LongLength &&
            created.PhysicalLength == (ulong)manifest.Length,
            "CreateAsync summary disagrees with the scanner or the written bytes");

        manifest.Position = 0;
        ManifestVerificationResult structural =
            await ChunkManifest.VerifyManifestAsync(manifest);

        Check(
            structural.IsValid &&
            structural.Manifest.ManifestId == created.ManifestId &&
            structural.Manifest.FileDigest == created.FileDigest,
            "VerifyManifestAsync rejected or misreported a fresh manifest");

        manifest.Position = 0;
        ManifestVerificationResult withContent =
            await ChunkManifest.VerifyAsync(
                new MemoryStream(bytes, writable: false),
                manifest);

        Check(withContent.IsValid, "VerifyAsync rejected the original content");

        manifest.Position = 0;
        await CheckReaderMatchesScanAsync(manifest, scan);

        manifest.Position = 0;
        ManifestVerificationResult mismatch =
            await ChunkManifest.VerifyAsync(
                new MemoryStream(tampered, writable: false),
                manifest);

        Check(
            !mismatch.IsValid &&
            mismatch.Failures == ManifestVerificationFailure.Content,
            "VerifyAsync did not report changed content as a Content mismatch");

        evidence.Add(Invariant(
            $"manifest suite={suite} bidx={includeBlockIndex} manifest-id={created.ManifestId} file-digest={created.FileDigest.ToHexLower()} physical-bytes={created.PhysicalLength} cblk={created.ChunkBlockCount}"));
    }
}

// Large forward-only source: scanner.
{
    using var generated = new GeneratedStream(GeneratedLength, GeneratedSeed);
    long before = GC.GetTotalAllocatedBytes(precise: true);
    ScanResult scan = await ScanAsync(generated, HashSuiteIds.Default);
    long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

    Check(
        scan.Covered == GeneratedLength &&
        generated.BytesRead == GeneratedLength &&
        generated.MaxRequestedReadLength is > 0 and <= 1024 * 1024,
        "scanner did not preserve bounded generated-stream semantics");
    CheckBudget("generated scan", allocated);

    evidence.Add(Invariant(
        $"generated-scan chunks={scan.Ids.Count} bytes={scan.Covered} ids-sha256={scan.IdsDigest}"));
    Console.WriteLine(Invariant($"generated-scan allocated={allocated}"));
}

// Large forward-only source: manifest create and content verification.
{
    using var manifest = new MemoryStream();
    ManifestInfo created;
    long allocated;

    using (var generated = new GeneratedStream(GeneratedLength, GeneratedSeed))
    {
        long before = GC.GetTotalAllocatedBytes(precise: true);
        created = await ChunkManifest.CreateAsync(
            generated,
            new ForwardOnlyWriteStream(manifest),
            new ManifestCreationOptions { IncludeBlockIndex = true });
        allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        Check(
            created.ContentLength == (ulong)GeneratedLength &&
            generated.BytesRead == GeneratedLength,
            "CreateAsync did not consume the complete generated stream");
    }

    CheckBudget("generated create", allocated);
    Console.WriteLine(Invariant($"generated-create allocated={allocated}"));

    using (var generated = new GeneratedStream(GeneratedLength, GeneratedSeed))
    {
        manifest.Position = 0;
        long before = GC.GetTotalAllocatedBytes(precise: true);
        ManifestVerificationResult verified =
            await ChunkManifest.VerifyAsync(generated, manifest);
        allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        Check(
            verified.IsValid && verified.Manifest.ManifestId == created.ManifestId,
            "VerifyAsync rejected the generated content");
    }

    CheckBudget("generated verify", allocated);
    Console.WriteLine(Invariant($"generated-verify allocated={allocated}"));

    evidence.Add(Invariant(
        $"generated-manifest suite={created.HashSuite} manifest-id={created.ManifestId} file-digest={created.FileDigest.ToHexLower()} chunks={created.ChunkCount} physical-bytes={created.PhysicalLength}"));
}

foreach (string line in evidence)
{
    Console.WriteLine(line);
}

if (evidencePath is not null)
{
    await File.WriteAllLinesAsync(evidencePath, evidence);
}

Console.WriteLine("package smoke: OK");
return 0;

static string? ParseEvidencePath(string[] arguments)
{
    if (arguments.Length == 0)
    {
        return null;
    }

    if (arguments.Length == 2 && arguments[0] == "--evidence")
    {
        return Path.GetFullPath(arguments[1]);
    }

    throw new ArgumentException("Usage: ChunkShift.PackageSmoke [--evidence <path>]");
}

static string Invariant(FormattableString value) =>
    FormattableString.Invariant(value);

static void Check(bool condition, string failure)
{
    if (!condition)
    {
        throw new InvalidOperationException($"ChunkShift package smoke failed: {failure}.");
    }
}

static void CheckBudget(string operation, long allocated)
{
    Check(
        allocated < AllocationBudget,
        string.Create(
            CultureInfo.InvariantCulture,
            $"{operation} allocated {allocated} bytes; bounded streaming regressed"));
}

static async Task<ScanResult> ScanAsync(Stream source, HashSuiteId suite)
{
    var ids = new List<ChunkId>();
    var lengths = new List<int>();
    long covered = 0;

    await ChunkScanner.ScanAsync(
        source,
        (chunk, content, _) =>
        {
            if (chunk.Index != ids.Count ||
                chunk.Offset != covered ||
                chunk.Length != content.Length ||
                chunk.Length <= 0)
            {
                throw new InvalidOperationException(
                    "ChunkShift package smoke failed: scanner chunk invariant.");
            }

            ids.Add(chunk.Id);
            lengths.Add(chunk.Length);
            covered += chunk.Length;
            return ValueTask.CompletedTask;
        },
        new ChunkScanOptions { HashSuite = suite });

    return new ScanResult(ids, lengths, covered, DigestIds(ids));
}

static async Task CheckReaderMatchesScanAsync(Stream manifest, ScanResult scan)
{
    await using ManifestReader reader = await ManifestReader.OpenAsync(manifest);
    var batch = new ChunkEntry[5];
    int index = 0;
    ulong offset = 0;
    int read;

    while ((read = await reader.ReadAsync(batch)) != 0)
    {
        for (int item = 0; item < read; item++, index++)
        {
            ChunkEntry entry = batch[item];
            Check(
                index < scan.Ids.Count &&
                entry.Index == (ulong)index &&
                entry.Offset == offset &&
                entry.Length == (uint)scan.Lengths[index] &&
                entry.Id == scan.Ids[index],
                "ManifestReader entries disagree with the scanner");
            offset += entry.Length;
        }
    }

    Check(
        index == scan.Ids.Count &&
        reader.IsCompleted &&
        reader.VerificationResult is { IsValid: true },
        "ManifestReader did not complete as valid");
}

static string DigestIds(List<ChunkId> ids)
{
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    Span<byte> buffer = stackalloc byte[32];

    foreach (ChunkId id in ids)
    {
        id.Value.CopyTo(buffer);
        hash.AppendData(buffer);
    }

    return Convert.ToHexStringLower(hash.GetHashAndReset());
}

internal sealed record ScanResult(
    List<ChunkId> Ids,
    List<int> Lengths,
    long Covered,
    string IdsDigest);

internal static class XorShift
{
    internal static byte[] Create(int length, uint seed)
    {
        var bytes = new byte[length];
        uint state = seed;

        for (int index = 0; index < bytes.Length; index++)
        {
            state = Next(state);
            bytes[index] = (byte)state;
        }

        return bytes;
    }

    internal static uint Next(uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }
}

// Non-seekable, generated-on-demand source that never materializes its payload.
internal sealed class GeneratedStream : Stream
{
    private long _remaining;
    private uint _state;

    internal GeneratedStream(long length, uint seed)
    {
        _remaining = length;
        _state = seed;
    }

    internal long BytesRead { get; private set; }

    internal int MaxRequestedReadLength { get; private set; }

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
        MaxRequestedReadLength = Math.Max(MaxRequestedReadLength, destination.Length);

        int count = (int)Math.Min(destination.Length, _remaining);

        for (int index = 0; index < count; index++)
        {
            _state = XorShift.Next(_state);
            destination[index] = (byte)_state;
        }

        _remaining -= count;
        BytesRead += count;
        return count;
    }

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

// Write-only, non-seekable view of a caller-owned stream: CreateAsync must
// produce the CSM strictly forward.
internal sealed class ForwardOnlyWriteStream(Stream inner) : Stream
{
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) =>
        inner.Write(buffer, offset, count);

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        inner.WriteAsync(buffer, cancellationToken);

    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) =>
        inner.FlushAsync(cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
