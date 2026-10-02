using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ChunkShift.Benchmarks.PatchLab.PatchGap;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public class PatchGapInventoryLockTests
{
    [Fact]
    public void PuffinBitExtentsMatchStructuralMemberOrderAndByteEnvelopes()
    {
        string fileSha = new('a', 64);
        var structural = new PatchGapG5Classification(
            PatchGapG5Kind.Gzip,
            2,
            new string('b', 64),
            "TEST",
            [
                new PatchGapDeflateExtent(5, 3),
                new PatchGapDeflateExtent(20, 5),
            ]);
        var locator = new PatchGapPuffinFileLocator(
            true,
            "OK",
            [
                new PatchGapBitExtent(5UL * 8UL, (3UL * 8UL) - 1UL),
                new PatchGapBitExtent(20UL * 8UL, (5UL * 8UL) - 1UL),
            ],
            fileSha,
            new string('c', 64));

        Assert.True(PatchGapG5InventoryLock.TryValidatePuffinAgreement(
            structural,
            locator,
            fileBytes: 32,
            fileSha,
            out string? reason));
        Assert.Null(reason);

        var reorderedStructural = structural with
        {
            DeflateExtents =
            [
                structural.DeflateExtents[1],
                structural.DeflateExtents[0],
            ],
        };
        Assert.False(PatchGapG5InventoryLock.TryValidatePuffinAgreement(
            reorderedStructural,
            locator,
            fileBytes: 32,
            fileSha,
            out reason));
        Assert.Equal("bit-extent-byte-envelope-mismatch", reason);

        PatchGapPuffinFileLocator wrongEnvelope = locator with
        {
            DeflateBitExtents =
            [
                new PatchGapBitExtent(5UL * 8UL, 8),
                new PatchGapBitExtent(20UL * 8UL, (5UL * 8UL) - 1UL),
            ],
        };
        Assert.False(PatchGapG5InventoryLock.TryValidatePuffinAgreement(
            structural,
            wrongEnvelope,
            32,
            fileSha,
            out reason));
        Assert.Equal("bit-extent-byte-envelope-mismatch", reason);

        PatchGapPuffinFileLocator wrongCount = locator with
        {
            DeflateBitExtents = [locator.DeflateBitExtents[0]],
        };
        Assert.False(PatchGapG5InventoryLock.TryValidatePuffinAgreement(
            structural,
            wrongCount,
            32,
            fileSha,
            out reason));
        Assert.Equal("deflate-count-mismatch", reason);
    }

    [Fact]
    public void PuffinAgreementRejectsNonCanonicalLocatorOrder()
    {
        string fileSha = new('a', 64);
        var structural = new PatchGapG5Classification(
            PatchGapG5Kind.Gzip,
            2,
            new string('b', 64),
            "TEST",
            [
                new PatchGapDeflateExtent(5, 3),
                new PatchGapDeflateExtent(20, 5),
            ]);
        var locator = new PatchGapPuffinFileLocator(
            true,
            "OK",
            [
                new PatchGapBitExtent(20UL * 8UL, (5UL * 8UL) - 1UL),
                new PatchGapBitExtent(5UL * 8UL, (3UL * 8UL) - 1UL),
            ],
            fileSha,
            new string('c', 64));

        Assert.False(PatchGapG5InventoryLock.TryValidatePuffinAgreement(
            structural,
            locator,
            fileBytes: 32,
            fileSha,
            out string? reason));
        Assert.Equal("invalid-or-noncanonical-bit-extent-order", reason);
    }

    [Fact]
    public void PuffinAgreementRejectsOverlapAndReconstructionMismatch()
    {
        string fileSha = new('a', 64);
        var structural = new PatchGapG5Classification(
            PatchGapG5Kind.ZipCompatible,
            2,
            new string('b', 64),
            "TEST",
            [
                new PatchGapDeflateExtent(1, 2),
                new PatchGapDeflateExtent(3, 2),
            ]);
        var overlap = new PatchGapPuffinFileLocator(
            true,
            "OK",
            [
                new PatchGapBitExtent(8, 16),
                new PatchGapBitExtent(16, 24),
            ],
            fileSha,
            new string('c', 64));

        Assert.False(PatchGapG5InventoryLock.TryValidatePuffinAgreement(
            structural,
            overlap,
            8,
            fileSha,
            out string? reason));
        Assert.Equal("invalid-or-overlapping-bit-extent", reason);

        PatchGapPuffinFileLocator wrongSha = overlap with
        {
            DeflateBitExtents =
            [
                new PatchGapBitExtent(8, 16),
                new PatchGapBitExtent(24, 16),
            ],
            ReconstructedSha256 = new string('d', 64),
        };
        Assert.False(PatchGapG5InventoryLock.TryValidatePuffinAgreement(
            structural,
            wrongSha,
            8,
            fileSha,
            out reason));
        Assert.Equal("puffhuff-reconstruction-mismatch", reason);
    }

    [Fact]
    public void ZipAttributionUsesRawNameAndDuplicateOrdinal()
    {
        byte[] baseZip = Zip(
            ("same.bin", "same", CompressionLevel.Optimal),
            ("recompressed.bin", "same-raw", CompressionLevel.NoCompression),
            ("changed.bin", "old", CompressionLevel.Optimal),
            ("duplicate.bin", "first", CompressionLevel.Optimal),
            ("duplicate.bin", "second", CompressionLevel.Optimal));

        byte[] targetZip = Zip(
            ("same.bin", "same", CompressionLevel.Optimal),
            ("recompressed.bin", "same-raw", CompressionLevel.Optimal),
            ("changed.bin", "new", CompressionLevel.Optimal),
            ("new.bin", "new-member", CompressionLevel.Optimal),
            ("duplicate.bin", "first", CompressionLevel.Optimal),
            ("duplicate.bin", "third", CompressionLevel.Optimal),
            ("stored.bin", "stored", CompressionLevel.NoCompression));

        PatchGapZipMemberAttribution[] rows =
            PatchGapG5InventoryLock.AttributeZipMembers(baseZip, targetZip);

        Assert.Equal(
            PatchGapZipMemberCategory.MetadataOrLayoutOnly,
            Find(rows, "same.bin", 0).Category);
        Assert.Equal(
            PatchGapZipMemberCategory.Recompressed,
            Find(rows, "recompressed.bin", 0).Category);
        Assert.Equal(
            PatchGapZipMemberCategory.ChangedCompressed,
            Find(rows, "changed.bin", 0).Category);
        Assert.Equal(
            PatchGapZipMemberCategory.NewCompressed,
            Find(rows, "new.bin", 0).Category);
        Assert.Equal(
            PatchGapZipMemberCategory.MetadataOrLayoutOnly,
            Find(rows, "duplicate.bin", 0).Category);
        Assert.Equal(
            PatchGapZipMemberCategory.ChangedCompressed,
            Find(rows, "duplicate.bin", 1).Category);
        Assert.Equal(
            PatchGapZipMemberCategory.NonDeflate,
            Find(rows, "stored.bin", 0).Category);
    }

    private static PatchGapZipMemberAttribution Find(
        IEnumerable<PatchGapZipMemberAttribution> rows,
        string rawName,
        int duplicateOrdinal)
    {
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawName));
        return Assert.Single(
            rows,
            row => row.RawNameBase64 == encoded && row.DuplicateOrdinal == duplicateOrdinal);
    }

    private static byte[] Zip(params (string Name, string Content, CompressionLevel Level)[] entries)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string content, CompressionLevel level) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(name, level);
                using Stream stream = entry.Open();
                byte[] bytes = Encoding.UTF8.GetBytes(content);
                stream.Write(bytes);
            }
        }

        return output.ToArray();
    }
}
