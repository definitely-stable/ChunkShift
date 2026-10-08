using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ChunkShift.Benchmarks.PatchLab;
using ChunkShift.Benchmarks.PatchLab.PatchGap;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public class PatchGapFoundationTests
{
    [Fact]
    public void G1FrozenEnvelopesMatchProtocolAccounting()
    {
        Assert.Collection(
            PatchGapG1Model.Envelopes,
            h0 => AssertEnvelope(h0, "G1-H0", 4, 1, 1, 2_359_424, 67_108_864),
            b1 => AssertEnvelope(b1, "G1-B1-R64", 64, 1, 1, 2_361_344, 67_110_784),
            b4 => AssertEnvelope(b4, "G1-B4-R256", 256, 4, 8, 12_853_248, 77_602_688),
            b8 => AssertEnvelope(b8, "G1-B8-R512", 512, 8, 16, 25_444_352, 90_193_792),
            b32 => AssertEnvelope(b32, "G1-B32-R2048", 2048, 32, 64, 100_990_976, 165_740_416));

        Assert.Equal(
            ["G1-B1-R64", "G1-B4-R256", "G1-B8-R512", "G1-B32-R2048"],
            PatchGapG1Model.Nested("G1-B32-R2048").Select(static item => item.Id));
        Assert.Equal(8L * 32 * 1024 * 1024, PatchGapG1Model.MaximumBaseBytesRead("G1-B32-R2048"));
    }

    [Fact]
    public void G1MaximalPrefixObeysBudgetAndReferenceProperties()
    {
        var random = new Random(0x501);
        PatchGapG1BaseRecord[] records =
        [
            .. Enumerable.Range(0, 3000)
                .Select(index => new PatchGapG1BaseRecord(
                    index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture),
                    random.Next(16 * 1024, (256 * 1024) + 1))),
        ];

        foreach (PatchGapG1Envelope envelope in PatchGapG1Model.ResearchEnvelopes)
        {
            for (int iteration = 0; iteration < 200; iteration++)
            {
                int start = random.Next(records.Length);
                PatchGapG1Prefix prefix = PatchGapG1Model.BuildMaximalPrefix(records, start, envelope);

                Assert.InRange(prefix.DictionaryBytes, 1, envelope.DictionaryBudgetBytes);
                Assert.InRange(prefix.DictionaryReferences, 1, envelope.MaximumReferences);
                Assert.Equal(
                    records.Skip(start).Take(prefix.DictionaryReferences).Select(static item => item.ChunkIdentity),
                    prefix.ChunkIdentities);

                int next = start + prefix.DictionaryReferences;
                if (next < records.Length && prefix.DictionaryReferences < envelope.MaximumReferences)
                {
                    Assert.True(
                        records[next].Length > envelope.DictionaryBudgetBytes - prefix.DictionaryBytes,
                        $"{envelope.Id} stopped before the maximal byte prefix.");
                }
            }
        }
    }

    [Fact]
    public void G1DedupKeepsDifferentWindowsDistinctAndTiesKeepH0()
    {
        PatchGapG1BaseRecord[] records =
        [
            new("a", 16 * 1024),
            new("b", 16 * 1024),
            new("c", 16 * 1024),
            new("d", 16 * 1024),
        ];

        PatchGapG1Trial[] trials = PatchGapG1Model.BuildTrials(records, [0, 0], "G1-B4-R256");
        Assert.Contains(trials, static trial => trial.Deduplicated);
        Assert.NotEqual(
            trials.First(static trial => trial.EnvelopeId == "G1-B1-R64").Identity.Key,
            trials.First(static trial => trial.EnvelopeId == "G1-B4-R256").Identity.Key);

        PatchGapG1Trial first = trials.First(static trial => !trial.Deduplicated);
        PatchGapG1Winner tie = PatchGapG1Model.ChooseWinner(
            1000,
            0,
            [new PatchGapG1TrialCost(first, 1000 - first.Prefix.DictionaryReferences * 32L)]);

        Assert.Equal("H0", tie.StoredForm);
        Assert.Throws<InvalidDataException>(() =>
            PatchGapG1Model.PhysicalPatchBytes(1000, [(100, 101)]));
    }

    [Fact]
    public void G3RejectsUnorderedEntriesAndUnexpectedFrameResults()
    {
        byte[] a = "aaaa"u8.ToArray();
        byte[] b = "bbbb"u8.ToArray();

        Assert.Throws<InvalidDataException>(() => PatchGapG3Model.Group(
            [Entry(1, 4, b, 4, []), Entry(0, 0, a, 4, [])],
            PatchGapG3Kind.File));

        PatchGapG3Group[] groups = PatchGapG3Model.Group(
            [Entry(0, 0, a, 4, []), Entry(1, 4, b, 4, [])],
            PatchGapG3Kind.File);

        Assert.Throws<InvalidDataException>(() =>
            PatchGapG3Model.PhysicalPatchBytes(
                100,
                groups,
                new Dictionary<int, long> { [0] = 5, [99] = 1 }));
    }

    [Fact]
    public async Task G3FullTargetOracleInterleavesGroupedAndOrdinaryRecords()
    {
        byte[] a = "AAAA"u8.ToArray();
        byte[] reused = "BASE"u8.ToArray();
        byte[] b = "BBBB"u8.ToArray();
        byte[] replay = "AAAA"u8.ToArray();

        PatchGapG3Group group = Assert.Single(PatchGapG3Model.Group(
            [Entry(0, 0, a, 9, []), Entry(2, 8, b, 8, [])],
            PatchGapG3Kind.File));

        PatchGapG3TargetRecord[] target =
        [
            new(0, a.Length, Sha256(a)),
            new(1, reused.Length, Sha256(reused)),
            new(2, b.Length, Sha256(b)),
            new(3, replay.Length, Sha256(replay)),
        ];

        byte[] groupedBytes = [.. a, .. b];
        var groups = new Dictionary<int, Stream>
        {
            [group.GroupId] = new ShortReadMemoryStream(groupedBytes, maximumRead: 2),
        };

        byte[] full = [.. a, .. reused, .. b, .. replay];
        await PatchGapG3ReconstructionOracle.VerifyFullTargetAsync(
            target,
            [group],
            groups,
            (record, destination, _) =>
            {
                ReadOnlySpan<byte> source = record.TargetIndex switch
                {
                    1 => reused,
                    3 => replay,
                    _ => throw new InvalidOperationException("Grouped record was resolved through ordinary path."),
                };
                source.CopyTo(destination.Span);
                return ValueTask.CompletedTask;
            },
            static bytes => Sha256(bytes.Span),
            Sha256(full),
            CancellationToken.None);
    }

    [Fact]
    public async Task G3GroupOracleRejectsShortExtraCorruptAndCancellation()
    {
        byte[] a = "aaaa"u8.ToArray();
        byte[] b = "bbbb"u8.ToArray();
        PatchGapG3Group group = Assert.Single(PatchGapG3Model.Group(
            [Entry(0, 0, a, 4, []), Entry(1, 4, b, 4, [])],
            PatchGapG3Kind.File));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PatchGapG3ReconstructionOracle.VerifyDecodedGroupAsync(
                group,
                new MemoryStream([.. a, .. b[..3]]),
                static bytes => Sha256(bytes.Span),
                null,
                CancellationToken.None));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PatchGapG3ReconstructionOracle.VerifyDecodedGroupAsync(
                group,
                new MemoryStream([.. a, .. b, 0xff]),
                static bytes => Sha256(bytes.Span),
                null,
                CancellationToken.None));

        byte[] corrupt = [.. a, .. b];
        corrupt[5] ^= 1;
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PatchGapG3ReconstructionOracle.VerifyDecodedGroupAsync(
                group,
                new MemoryStream(corrupt),
                static bytes => Sha256(bytes.Span),
                null,
                CancellationToken.None));

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PatchGapG3ReconstructionOracle.VerifyDecodedGroupAsync(
                group,
                new MemoryStream([.. a, .. b]),
                static bytes => Sha256(bytes.Span),
                null,
                cancelled.Token));
    }

    [Fact]
    public async Task G3StreamingCodecRoundTripsRawPrefixAcrossMemberBoundaries()
    {
        byte[] prefix = Enumerable.Range(0, 64 * 1024)
            .Select(static index => (byte)(index * 17))
            .ToArray();
        byte[] first = new byte[96 * 1024];
        byte[] second = new byte[80 * 1024];

        for (int index = 0; index < first.Length; index++)
        {
            first[index] = prefix[(index + 113) % prefix.Length];
        }

        for (int index = 0; index < second.Length; index++)
        {
            second[index] = prefix[(index + 997) % prefix.Length];
        }

        byte[] expected = [.. first, .. second];
        byte[] frame;
        using (var encoded = new MemoryStream())
        {
            using var encoder = new PatchGapG3Codec.StreamingEncoder(expected.Length, prefix);
            encoder.Write(first, encoded);
            encoder.Write(second, encoded);
            encoder.Finish(encoded);
            frame = encoded.ToArray();
        }

        using var scope = new PatchLabRunTests.TempDirectory();
        string framePath = Path.Combine(scope.Path, "group.zst");
        File.WriteAllBytes(framePath, frame);
        PatchGapG3FrameEnvelope.Validate(framePath, expected.Length);

        using var source = new MemoryStream(frame);
        await using var decoder = new PatchGapG3Codec.StreamingDecoder(source, prefix);
        using var decoded = new MemoryStream();
        await decoder.CopyToAsync(decoded);

        Assert.Equal(expected, decoded.ToArray());

        File.WriteAllBytes(framePath, [.. frame, 0x42]);
        Assert.Throws<InvalidDataException>(() =>
            PatchGapG3FrameEnvelope.Validate(framePath, expected.Length));
    }

    [Fact]
    public void G4ClassifierRequiresCompleteElfHeaderAndCompatibleClass()
    {
        PatchGapExecutableClassification arm64 = PatchGapG4Classifier.Classify(Elf(2, 183));
        Assert.Equal(PatchGapExecutableKind.Elf, arm64.Kind);
        Assert.Equal(PatchGapExecutableArchitecture.Arm64, arm64.Architecture);

        byte[] truncated = Elf(2, 183)[..20];
        Assert.Equal(PatchGapExecutableKind.Malformed, PatchGapG4Classifier.Classify(truncated).Kind);

        byte[] wrongClass = Elf(1, 183);
        Assert.Equal(PatchGapExecutableKind.Malformed, PatchGapG4Classifier.Classify(wrongClass).Kind);

        byte[] wrongVersion = Elf(2, 62);
        wrongVersion[6] = 0;
        Assert.Equal(PatchGapExecutableKind.Malformed, PatchGapG4Classifier.Classify(wrongVersion).Kind);

        byte[] wrongHeaderSize = Elf(2, 62);
        BinaryPrimitives.WriteUInt16LittleEndian(wrongHeaderSize.AsSpan(52, 2), 52);
        Assert.Equal(PatchGapExecutableKind.Malformed, PatchGapG4Classifier.Classify(wrongHeaderSize).Kind);
    }

    [Fact]
    public void G4ClassifierValidatesNativePeSectionTableAndDirectoryArray()
    {
        byte[] native = Pe(managed: false, r2r: false);
        Assert.Equal(PatchGapExecutableKind.PeNative, PatchGapG4Classifier.Classify(native).Kind);

        byte[] truncatedSections = Pe(managed: false, r2r: false);
        WritePeSectionCount(truncatedSections, 100);
        Assert.Equal(PatchGapExecutableKind.Malformed, PatchGapG4Classifier.Classify(truncatedSections).Kind);

        byte[] truncatedDirectories = Pe(managed: false, r2r: false);
        int optional = PeOptionalOffset();
        BinaryPrimitives.WriteUInt32LittleEndian(truncatedDirectories.AsSpan(optional + 108, 4), 17);
        Assert.Equal(PatchGapExecutableKind.Malformed, PatchGapG4Classifier.Classify(truncatedDirectories).Kind);
    }

    [Fact]
    public void G4ClassifierRejectsClrHeaderInVirtualOnlyTail()
    {
        byte[] pe = Pe(managed: true, r2r: false);
        int section = PeSectionOffset();
        BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(section + 8, 4), 0x400);
        BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(section + 16, 4), 0x20);
        Assert.Equal(PatchGapExecutableKind.Malformed, PatchGapG4Classifier.Classify(pe).Kind);
    }

    [Fact]
    public void G4PairRequiresSameSupportedFamilyAndArchitecture()
    {
        PatchGapG4InventoryRow native = PatchGapG4Classifier.Pair(
            "node-win-x64", "1", "2", "node", 10, 5, Pe(false, false), Pe(false, false));
        Assert.True(native.SameExecutableFamilyAndArchitecture);
        Assert.True(native.GateEligible);

        PatchGapG4InventoryRow mixed = PatchGapG4Classifier.Pair(
            "node-win-x64", "1", "2", "node", 10, 5, Pe(false, false), Elf(2, 62));
        Assert.False(mixed.SameExecutableFamilyAndArchitecture);
        Assert.False(mixed.GateEligible);
    }

    [Fact]
    public void G4BcjPositionPlanPinsX86AndArm64Offsets()
    {
        Assert.Equal(
            (0, 0xfffffffEu, 17),
            PatchGapG4Bcj.Plan(0xfffffffEL, 17, PatchGapExecutableArchitecture.X64));

        Assert.Equal(
            (3, 4u, 14),
            PatchGapG4Bcj.Plan(1, 17, PatchGapExecutableArchitecture.Arm64));

        Assert.Equal(
            (2, 4u, 0),
            PatchGapG4Bcj.Plan(2, 2, PatchGapExecutableArchitecture.Arm64));

        Assert.Throws<InvalidDataException>(() =>
            PatchGapG4Bcj.Plan(0, 1, PatchGapExecutableArchitecture.Unknown));
    }

    [Fact]
    public void G4CanonicalDictionaryUsesEarliestWholeSequence()
    {
        PatchGapG4BaseRecord[] records =
        [
            // Canonicalization is defined by the ordered ChunkId sequence only;
            // lengths/offsets belong to the chosen physical occurrence.
            new("a", 0, 7),
            new("b", 7, 9),
            new("c", 16, 12),
            new("a", 28, 10),
            new("b", 38, 11),
            new("d", 54, 13),
            new("c", 67, 12),
        ];

        Assert.Equal(0, PatchGapG4Model.CanonicalSequenceStart(records, 3, 2));
        Assert.Equal(2, PatchGapG4Model.CanonicalSequenceStart(records, 2, 1));
        Assert.Equal(5, PatchGapG4Model.CanonicalSequenceStart(records, 5, 2));
    }

    [Fact]
    public void G4WinnerKeepsH0OnTieAndEarlierTrialOnLaterTie()
    {
        PatchGapG4Winner tiedH0 = PatchGapG4Model.ChooseWinner(
            h0Encoding: PatchGapG4Model.H0EncodingZstd,
            h0StoredBytes: 100,
            h0DictionaryReferences: 0,
            [
                new("bcj-zstd", PatchGapG4Model.EncodingX86, -1, -1, -1, 100, 0),
            ]);
        Assert.Equal("H0", tiedH0.StoredForm);
        Assert.Equal(PatchGapG4Model.H0EncodingZstd, tiedH0.Encoding);

        PatchGapG4Winner trial = PatchGapG4Model.ChooseWinner(
            h0Encoding: PatchGapG4Model.H0EncodingRaw,
            h0StoredBytes: 100,
            h0DictionaryReferences: 0,
            [
                new("bcj-zstd", PatchGapG4Model.EncodingX86, -1, -1, -1, 90, 0),
                new("bcj-zstd-dictionary", PatchGapG4Model.EncodingX86, 0, 4, 1, 58, 1),
                new("bcj-zstd-dictionary", PatchGapG4Model.EncodingX86, 1, 8, 8, 58, 1),
            ]);

        Assert.Equal("bcj-zstd", trial.StoredForm);
        Assert.Equal(90, trial.CostBytes);
        Assert.Equal(
            990,
            PatchGapG4Model.PhysicalPatchBytes(1000, [(100L, trial.CostBytes)]));
    }

    [Fact]
    public void G5ParsesConcatenatedGzipAndRejectsTrailingOrCorruptMembers()
    {
        byte[] first = GzipBytes("first");
        byte[] second = GzipBytes("second");
        byte[] concatenated = [.. first, .. second];

        PatchGapG5Classification valid = PatchGapG5Classifier.Classify(concatenated);
        Assert.Equal(PatchGapG5Kind.Gzip, valid.Kind);
        Assert.Equal(2, valid.DeflateMembers);
        Assert.Equal(2, valid.DeflateExtents.Length);

        Assert.Equal(
            PatchGapG5Kind.Malformed,
            PatchGapG5Classifier.Classify([.. concatenated, 0x42]).Kind);

        byte[] corrupt = concatenated.ToArray();
        corrupt[^8] ^= 1;
        Assert.Equal(PatchGapG5Kind.Malformed, PatchGapG5Classifier.Classify(corrupt).Kind);

        Assert.Equal(
            PatchGapG5Kind.Malformed,
            PatchGapG5Classifier.Classify(concatenated.AsSpan(0, concatenated.Length - 3)).Kind);
    }

    [Fact]
    public void G5ZlibRequiresExactFooterAndAdler()
    {
        byte[] valid = ZlibBytes("payload");
        PatchGapG5Classification result = PatchGapG5Classifier.Classify(valid);
        Assert.Equal(PatchGapG5Kind.Zlib, result.Kind);
        Assert.Single(result.DeflateExtents);

        Assert.Equal(
            PatchGapG5Kind.Malformed,
            PatchGapG5Classifier.Classify([.. valid, 0]).Kind);

        byte[] corrupt = valid.ToArray();
        corrupt[^1] ^= 1;
        Assert.Equal(PatchGapG5Kind.Malformed, PatchGapG5Classifier.Classify(corrupt).Kind);
    }

    [Fact]
    public void G5ZipEocdSearchIgnoresSignatureInsideComment()
    {
        byte[] zip = ZipBytes();
        int eocd = FindEocdForTest(zip);
        byte[] comment = [0x11, 0x50, 0x4b, 0x05, 0x06, 0x22, 0x33, 0x44, 0x55];
        byte[] withComment = new byte[zip.Length + comment.Length];
        zip.CopyTo(withComment, 0);
        comment.CopyTo(withComment, zip.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(
            withComment.AsSpan(eocd + 20, 2),
            checked((ushort)comment.Length));

        Assert.Equal(
            PatchGapG5Kind.ZipCompatible,
            PatchGapG5Classifier.Classify(withComment).Kind);
    }

    [Fact]
    public void G5ZipMalformedLocalOffsetDoesNotThrow()
    {
        byte[] zip = ZipBytes();
        int central = FindCentralForTest(zip);
        BinaryPrimitives.WriteUInt32LittleEndian(zip.AsSpan(central + 42, 4), 0xfffffffe);

        PatchGapG5Classification classification = PatchGapG5Classifier.Classify(zip);
        Assert.Equal(PatchGapG5Kind.Malformed, classification.Kind);
    }

    [Fact]
    public void G5ZipRejectsLocalCrcMismatchWithoutDescriptor()
    {
        byte[] zip = ZipBytes();
        int central = FindCentralForTest(zip);
        uint localOffset = BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(central + 42, 4));
        int local = checked((int)localOffset);
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(local + 6, 2));
        Assert.Equal(0, flags & 0x0008);

        zip[local + 14] ^= 1;
        Assert.Equal(PatchGapG5Kind.Malformed, PatchGapG5Classifier.Classify(zip).Kind);
    }

    [Fact]
    public void G5ZipRejectsCorruptDataDescriptor()
    {
        byte[] valid = ZipWithDescriptorBytes(corruptDescriptor: false);
        Assert.Equal(PatchGapG5Kind.ZipCompatible, PatchGapG5Classifier.Classify(valid).Kind);

        byte[] corrupt = ZipWithDescriptorBytes(corruptDescriptor: true);
        Assert.Equal(PatchGapG5Kind.Malformed, PatchGapG5Classifier.Classify(corrupt).Kind);
    }

    [Fact]
    public void G5DoesNotGuessRawDeflateAndMarksPresetZlibUnsupported()
    {
        Assert.Equal(
            PatchGapG5Kind.NotCompressedOrUnknown,
            PatchGapG5Classifier.Classify([0x03, 0x00]).Kind);

        byte[] preset = [0x78, 0x20, 0, 0, 0, 0];
        Assert.Equal(
            PatchGapG5Kind.UnsupportedPresetDictionary,
            PatchGapG5Classifier.Classify(preset).Kind);
    }

    [Fact]
    public void CanonicalSubsetManifestIsOrderIndependentAndSplitLocked()
    {
        PatchGapG5InventoryRow a = PatchGapG5Classifier.Pair(
            "node-linux-x64", "1", "2", "b", 1, 1, GzipBytes("a"), GzipBytes("a"));
        PatchGapG5InventoryRow b = PatchGapG5Classifier.Pair(
            "dotnet-runtime-linux-arm64", "1", "2", "a", 1, 1, GzipBytes("b"), GzipBytes("b"));

        string sourceCommit = new string('a', 40);
        PatchGapSubsetManifest<PatchGapG5InventoryRow> left = PatchGapSubsetManifest.Create(
            "test", sourceCommit, [a, b], static row => row.DatasetRole, static row => row.Key);
        PatchGapSubsetManifest<PatchGapG5InventoryRow> right = PatchGapSubsetManifest.Create(
            "test", sourceCommit, [b, a], static row => row.DatasetRole, static row => row.Key);

        Assert.Equal(PatchGapEvidence.CanonicalSha256(left), PatchGapEvidence.CanonicalSha256(right));
        Assert.Equal(
            ["dotnet-runtime-linux-arm64", "node-linux-x64"],
            left.Rows.Select(static row => row.Family));
        Assert.NotEqual(left.CalibrationSha256, left.EvaluationSha256);
    }

    [Fact]
    public void WriteCanonicalDigestEqualsExactFileBytes()
    {
        using var scope = new PatchLabRunTests.TempDirectory();
        string path = Path.Combine(scope.Path, "canonical.json");
        string returned = PatchGapEvidence.WriteCanonical(path, new { z = 1, a = "x" });

        Assert.Equal(Sha256(File.ReadAllBytes(path)), returned);
        Assert.False(File.ReadAllText(path).EndsWith('\n'));
    }

    [Fact]
    public void ArtifactManifestRequiresSizesAndCanonicalUniquePaths()
    {
        using var scope = new PatchLabRunTests.TempDirectory();
        string sha = new('a', 64);
        var document = new PatchGapArtifactsDocument(
            "chunkshift.patch-gap-artifacts.v1",
            PatchGapProtocol.ProtocolCommit,
            [
                new PatchGapArtifactRecord(
                    1,
                    2,
                    "artifact",
                    sha,
                    100,
                    [
                        new("b.txt", sha, 2),
                        new("a.txt", sha, 1),
                    ],
                    90),
            ]);

        PatchGapEvidenceBundle.WriteArtifacts(scope.Path, document);
        using JsonDocument parsed = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(scope.Path, "artifacts.json")));
        JsonElement files = parsed.RootElement.GetProperty("artifacts").EnumerateArray().First()
            .GetProperty("containedFiles");
        JsonElement firstFile = files.EnumerateArray().First();
        Assert.Equal("a.txt", firstFile.GetProperty("path").GetString());
        Assert.Equal(1, firstFile.GetProperty("bytes").GetInt64());

        var duplicate = document with
        {
            Artifacts =
            [
                document.Artifacts[0] with
                {
                    ContainedFiles =
                    [
                        new("same", sha, 1),
                        new("same", sha, 2),
                    ],
                },
            ],
        };
        Assert.Throws<InvalidDataException>(() => PatchGapEvidenceBundle.WriteArtifacts(scope.Path, duplicate));
    }

    [Fact]
    public void FrozenToolsCarryExactCreateApplyAndFullProvenanceShape()
    {
        var expected = new Dictionary<string, (string Create, string Apply)>(StringComparer.Ordinal)
        {
            ["zstd-patch-from"] = (
                "zstd -19 --long=31 -q --patch-from=OLD NEW -o PATCH",
                "zstd -d --long=31 -q --patch-from=OLD PATCH -o NEW"),
            ["bsdiff4"] = (
                "bsdiff4.diff(base,target)",
                "bsdiff4.patch(base,patch)"),
            ["aosp-bsdiff"] = (
                "bsdiff --format bsdf2 --type bz2:brotli --brotli_quality 11 OLD NEW PATCH",
                "bspatch OLD NEW PATCH"),
            ["xdelta3"] = (
                "xdelta3 -a -A -D -S none -e -9 -f -s OLD NEW PATCH",
                "xdelta3 -D -R -d -f -s OLD PATCH NEW"),
            ["xdelta3-legacy"] = (
                "xdelta3 -A -D -S none -e -9 -f -s OLD NEW PATCH",
                "xdelta3 -D -R -d -f -s OLD PATCH NEW"),
            ["hdiffpatch-memory"] = (
                "hdiffz -m-4 -SD -d -f -p-1 -c-zstd-21-24 OLD NEW PATCH",
                "hpatchz -s-8m -f OLD PATCH NEW"),
            ["hdiffpatch-stream"] = (
                "hdiffz -s-64 -SD -d -f -p-1 -c-zstd-21-24 OLD NEW PATCH",
                "hpatchz -s-8m -f OLD PATCH NEW"),
            ["zucchini"] = (
                "zucchini -gen OLD NEW PATCH",
                "zucchini -apply OLD PATCH NEW"),
            ["xz-bcj"] = (
                "liblzma raw BCJ encode (x86/arm64; no .xz container)",
                "liblzma raw BCJ decode (x86/arm64; no .xz container)"),
            ["puffin"] = (
                "puffin --operation=puffdiff --src_file=OLD --dst_file=NEW --patch_file=PATCH --src_file_type=TYPE --dst_file_type=TYPE --patch_algorithm=0",
                "puffin --operation=puffpatch --src_file=OLD --dst_file=RECON --patch_file=PATCH --cache_size=5242880"),
        };

        Assert.Equal(expected.Count, PatchGapExternalTools.Frozen.Count);
        foreach (PatchGapExternalToolSpec tool in PatchGapExternalTools.Frozen)
        {
            Assert.True(expected.TryGetValue(tool.Id, out var commands), tool.Id);
            Assert.Equal(commands.Create, tool.CreateCommandTemplate);
            Assert.Equal(commands.Apply, tool.ApplyCommandTemplate);
        }
    }

    [Fact]
    public void ToolProbeRejectsObservedIdentityMismatch()
    {
        using var scope = new PatchLabRunTests.TempDirectory();
        string path = Path.Combine(scope.Path, "tool.bin");
        File.WriteAllBytes(path, [1, 2, 3]);
        PatchGapExternalToolSpec spec =
            Assert.Single(PatchGapExternalTools.Frozen, static tool => tool.Id == "zstd-patch-from");
        string sha = Sha256(File.ReadAllBytes(path));

        var input = new PatchGapToolProbeInput(
            "tools/zstd",
            "build command",
            "wrong-version",
            spec.UpstreamCommit,
            "version output",
            new string('a', 64),
            sha);

        Assert.Throws<InvalidDataException>(() => PatchGapExternalTools.Probe(spec, path, input));
    }

    [Fact]
    public void CandidateTraceConsumerValidatesMandatoryHeaderAndRows()
    {
        using var scope = new PatchLabRunTests.TempDirectory();
        string good = Path.Combine(scope.Path, "trace.json");
        File.WriteAllText(good, JsonSerializer.Serialize(ValidTrace()));

        PatchGapConsumedG2Evidence consumed = PatchGapG2Consumer.Read(good);
        Assert.Equal(PatchGapProtocol.CandidateTraceSchema, JsonDocument.Parse(File.ReadAllBytes(good)).RootElement.GetProperty("schema").GetString());
        Assert.Equal("H4-L1-R2", consumed.Lane);
        Assert.Equal(1, consumed.RowCount);
        Assert.Equal(PatchGapProtocol.CorpusPairsSha256, consumed.DatasetSha256);

        string missing = Path.Combine(scope.Path, "missing.json");
        var missingPlatform = ToDictionary(ValidTrace());
        missingPlatform.Remove("platform");
        File.WriteAllText(missing, JsonSerializer.Serialize(missingPlatform));
        Assert.Throws<InvalidDataException>(() => PatchGapG2Consumer.Read(missing));

        string badDataset = Path.Combine(scope.Path, "bad-dataset.json");
        var wrong = ToDictionary(ValidTrace());
        wrong["datasetSha256"] = new string('b', 64);
        File.WriteAllText(badDataset, JsonSerializer.Serialize(wrong));
        Assert.Throws<InvalidDataException>(() => PatchGapG2Consumer.Read(badDataset));
    }

    [Fact]
    public void CandidateTraceProducerRoundTripsThroughStrictConsumer()
    {
        using var scope = new PatchLabRunTests.TempDirectory();
        _ = PatchLabRunTests.WriteCorpus(scope.Path);

        const string frozenFamily = "dotnet-runtime-linux-arm64";
        string syntheticFamily = Path.Combine(scope.Path, "tree", "synthetic");
        string frozenFamilyPath = Path.Combine(scope.Path, "tree", frozenFamily);
        Directory.Move(syntheticFamily, frozenFamilyPath);

        string pairsPath = Path.Combine(scope.Path, "pairs.json");
        JsonNode pairs = JsonNode.Parse(File.ReadAllText(pairsPath))
            ?? throw new InvalidDataException("Synthetic pairs.json did not parse.");
        JsonObject pair = pairs["pairs"]?[0]?.AsObject()
            ?? throw new InvalidDataException("Synthetic pairs.json has no first pair.");
        pair["family"] = frozenFamily;
        File.WriteAllText(pairsPath, pairs.ToJsonString());

        string datasetSha256 = Sha256(File.ReadAllBytes(pairsPath));
        string output = Path.Combine(scope.Path, "run.json");
        string traces = Path.Combine(scope.Path, "traces");
        const string sourceCommit = "2222222222222222222222222222222222222222";
        string runId = $"PATCH-ENC-005/RUN-20261002-001-{sourceCommit}-linux-x64";

        string? previousSha = Environment.GetEnvironmentVariable("GITHUB_SHA");
        string? previousRunNumber = Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER");
        string? previousRunAttempt = Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT");

        int exit;
        try
        {
            Environment.SetEnvironmentVariable("GITHUB_SHA", sourceCommit);
            Environment.SetEnvironmentVariable("GITHUB_RUN_NUMBER", "1");
            Environment.SetEnvironmentVariable("GITHUB_RUN_ATTEMPT", "1");

            exit = PatchLabRunner.Run(
            [
                "run",
                "--corpus", scope.Path,
                "--lane", "H4-L1-R2",
                "--output", output,
                "--workers", "1",
                "--execution", "h2-w2",
                "--no-apply",
                "--run-id", runId,
                "--trace-dir", traces,
                "--protocol-commit", PatchGapProtocol.PatchEnc005ProtocolCommit,
                "--source-commit", sourceCommit,
                "--platform", "linux-x64",
                "--dataset-role", "calibration",
            ]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_SHA", previousSha);
            Environment.SetEnvironmentVariable("GITHUB_RUN_NUMBER", previousRunNumber);
            Environment.SetEnvironmentVariable("GITHUB_RUN_ATTEMPT", previousRunAttempt);
        }

        Assert.Equal(0, exit);
        string tracePath = Assert.Single(Directory.GetFiles(traces, "*.json"));

        // Decision consumption remains bound to the frozen development corpus.
        Assert.Throws<InvalidDataException>(() => PatchGapG2Consumer.Read(tracePath));

        PatchGapConsumedG2Evidence consumed =
            PatchGapG2Consumer.ReadContract(tracePath, datasetSha256);
        Assert.Equal(PatchGapProtocol.PatchEnc005ExperimentId, consumed.ExperimentId);
        Assert.Equal("H4-L1-R2", consumed.Lane);
        Assert.Equal("calibration", consumed.DatasetRole);
        Assert.Equal(datasetSha256, consumed.DatasetSha256);
        Assert.True(consumed.RowCount > 0);

        JsonNode mutated = JsonNode.Parse(File.ReadAllText(tracePath))
            ?? throw new InvalidDataException("Producer trace did not parse.");
        Assert.True(mutated.AsObject().Remove("platform"));
        string badTrace = Path.Combine(scope.Path, "trace-missing-platform.json");
        File.WriteAllText(badTrace, mutated.ToJsonString());

        Assert.Throws<InvalidDataException>(() =>
            PatchGapG2Consumer.ReadContract(badTrace, datasetSha256));
    }

    [Fact]
    public void G2OracleConsumerRequiresRowsAndCostEquations()
    {
        using var scope = new PatchLabRunTests.TempDirectory();
        string good = Path.Combine(scope.Path, "oracle.json");
        File.WriteAllText(good, JsonSerializer.Serialize(ValidOracle()));

        string sampleSha256 = new string('f', 64);
        PatchGapConsumedG2Evidence consumed = PatchGapG2Consumer.Read(good, sampleSha256);
        Assert.Equal(1, consumed.RowCount);
        Assert.Equal(sampleSha256, consumed.OracleSampleSha256);
        Assert.Throws<InvalidDataException>(() => PatchGapG2Consumer.Read(good));
        Assert.Throws<InvalidDataException>(() =>
            PatchGapG2Consumer.Read(good, new string('e', 64)));

        string rowless = Path.Combine(scope.Path, "rowless.json");
        var noRows = ToDictionary(ValidOracle());
        noRows["rows"] = Array.Empty<object>();
        File.WriteAllText(rowless, JsonSerializer.Serialize(noRows));
        Assert.Throws<InvalidDataException>(() => PatchGapG2Consumer.Read(rowless, sampleSha256));

        string badCost = Path.Combine(scope.Path, "bad-cost.json");
        JsonElement oracle = JsonSerializer.SerializeToElement(ValidOracle());
        var bad = ToDictionary(ValidOracle());
        JsonElement row = oracle.GetProperty("rows")[0];
        Dictionary<string, object?> rowMap = JsonSerializer.Deserialize<Dictionary<string, object?>>(row.GetRawText())!;
        rowMap["savedBytes"] = 7;
        bad["rows"] = new object[] { rowMap };
        File.WriteAllText(badCost, JsonSerializer.Serialize(bad));
        Assert.Throws<InvalidDataException>(() => PatchGapG2Consumer.Read(badCost, sampleSha256));
    }

    [Fact]
    public void DecisionEvaluatorUsesExactFifteenPercentGate()
    {
        Assert.Equal(850, PatchGapDecisionEvaluator.WholeSplitFactorBytes(1000, 300, 150));
        Assert.Equal(0.15, PatchGapDecisionEvaluator.ReductionVsCsp(1000, 850), 12);
        Assert.True(PatchGapDecisionEvaluator.MeetsRfcSizeGate(1000, 850));
        Assert.False(PatchGapDecisionEvaluator.MeetsRfcSizeGate(1000, 851));

        long csp = long.MaxValue - 100;
        long exactSaved = checked((long)(((Int128)csp * 3) / 20));
        while ((Int128)20 * exactSaved < (Int128)3 * csp)
        {
            exactSaved++;
        }

        Assert.True(PatchGapDecisionEvaluator.MeetsRfcSizeGate(csp, csp - exactSaved));
        Assert.False(PatchGapDecisionEvaluator.MeetsRfcSizeGate(csp, csp - exactSaved + 1));
    }

    [Fact]
    public void H0PatchSetDigestIsCanonicalAndFrozenConstantsAreExact()
    {
        PatchLabFileResult a = FileResult("b", new string('2', 64));
        PatchLabFileResult b = FileResult("a", new string('1', 64));

        Assert.Equal(
            PatchGapProtocol.PatchSetDigest([a, b]),
            PatchGapProtocol.PatchSetDigest([b, a]));
        Assert.Equal(38_223_638, PatchGapProtocol.H0TotalBytes);
        Assert.Equal(
            PatchGapProtocol.H0CalibrationBytes + PatchGapProtocol.H0EvaluationBytes,
            PatchGapProtocol.H0TotalBytes);
    }

    private static object ValidTrace() => new
    {
        schema = PatchGapProtocol.CandidateTraceSchema,
        experimentId = PatchGapProtocol.PatchEnc005ExperimentId,
        runId = "PATCH-ENC-005/test",
        protocolCommit = PatchGapProtocol.PatchEnc005ProtocolCommit,
        sourceCommit = new string('a', 40),
        platform = "linux-x64",
        lane = "H4-L1-R2",
        datasetRole = "evaluation",
        datasetSha256 = PatchGapProtocol.CorpusPairsSha256,
        family = "node-linux-x64",
        baseVersion = "1",
        targetVersion = "2",
        path = "bin/node",
        baseManifestId = new string('b', 64),
        targetManifestId = new string('c', 64),
        finalLevel = 19,
        entries = new[]
        {
            new
            {
                targetIndex = 0L,
                targetChunkId = new string('d', 64),
                targetOffset = 0L,
                targetLength = 100,
                candidateCount = 1,
                cheapTrialCount = 1,
                expensiveTrialCount = 1,
                totalCompressionTrialCount = 3,
                level19TrialCount = 2,
                noDictionaryFrameBytes = 80,
                l19NoDictionaryFrameBytes = (int?)80,
                baselineCostBytes = 80,
                selectedEncoding = "zstd-dictionary",
                selectedCandidate = (int?)0,
                storedBytes = 50,
                dictionaryRefs = 1,
                candidates = new[]
                {
                    new
                    {
                        ordinal = 0,
                        startIndex = 0,
                        startOffset = 0L,
                        recordCount = 1,
                        firstChunkId = new string('e', 64),
                        source = "offset",
                        cheapLevel = (int?)1,
                        cheapFrameBytes = (int?)60,
                        cheapCostBytes = (int?)92,
                        finalFrameBytes = (int?)50,
                        finalCostBytes = (int?)82,
                        l19FrameBytes = (int?)50,
                        l19CostBytes = (int?)82,
                        selected = true,
                    },
                },
            },
        },
    };

    private static object ValidOracle() => new
    {
        schema = PatchGapProtocol.G2OracleSchema,
        experimentId = PatchGapProtocol.PatchEnc005ExperimentId,
        runId = "PATCH-ENC-005/oracle",
        protocolCommit = PatchGapProtocol.PatchEnc005ProtocolCommit,
        sourceCommit = new string('a', 40),
        datasetRole = "calibration",
        datasetSha256 = PatchGapProtocol.CorpusPairsSha256,
        oracleSampleSha256 = new string('f', 64),
        policy = PatchGapProtocol.G2OraclePolicy,
        candidateOrder = PatchGapProtocol.G2OracleCandidateOrder,
        rows = new[]
        {
            new
            {
                family = "dotnet-runtime-linux-arm64",
                baseVersion = "1",
                targetVersion = "2",
                path = "file.bin",
                targetIndex = 0L,
                targetChunkId = new string('1', 64),
                targetOffset = 0L,
                targetLength = 100,
                candidateStartsEnumerated = 10L,
                validCandidateCount = 8L,
                h0Encoding = "zstd",
                h0StoredBytes = 80,
                h0DictionaryRefs = 0,
                h0CostBytes = 80L,
                h0StartIndex = (int?)null,
                h0StartOffset = (long?)null,
                h0RecordCount = (int?)null,
                h0FirstChunkId = (string?)null,
                oracleEncoding = "zstd-dictionary",
                oracleStoredBytes = 40,
                oracleDictionaryRefs = 1,
                oracleCostBytes = 72L,
                oracleStartIndex = (int?)3,
                oracleStartOffset = (long?)200,
                oracleRecordCount = (int?)1,
                oracleFirstChunkId = new string('2', 64),
                oracleStartDistanceBytes = (long?)200,
                savedBytes = 8L,
            },
        },
    };

    private static Dictionary<string, object?> ToDictionary(object value) =>
        JsonSerializer.Deserialize<Dictionary<string, object?>>(
            JsonSerializer.Serialize(value))!;

    private static void AssertEnvelope(
        PatchGapG1Envelope envelope,
        string id,
        int refs,
        int dictionaryMiB,
        int windowMiB,
        long decoder,
        long apply)
    {
        Assert.Equal(id, envelope.Id);
        Assert.Equal(refs, envelope.MaximumReferences);
        Assert.Equal(dictionaryMiB * 1024 * 1024, envelope.DictionaryBudgetBytes);
        Assert.Equal(windowMiB * 1024 * 1024, envelope.WindowBytes);
        Assert.Equal(decoder, envelope.DecoderDataEnvelopeBytes);
        Assert.Equal(apply, envelope.ApplyRssLimitBytes);
    }

    private static PatchGapG3Entry Entry(
        long index,
        long offset,
        byte[] bytes,
        long stored,
        string[] dictionary) =>
        new(index, offset, bytes.Length, Sha256(bytes), stored, dictionary.Length, dictionary);

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static byte[] Pe(bool managed, bool r2r)
    {
        byte[] bytes = new byte[1024];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x3c, 4), 0x80);
        "PE\0\0"u8.CopyTo(bytes.AsSpan(0x80, 4));

        int coff = 0x84;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(coff, 2), 0x8664);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(coff + 2, 2), managed ? (ushort)1 : (ushort)0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(coff + 16, 2), 240);

        int optional = PeOptionalOffset();
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(optional, 2), 0x20b);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(optional + 108, 4), 16);

        if (!managed)
        {
            return bytes;
        }

        int clrDirectory = optional + 112 + (14 * 8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(clrDirectory, 4), 0x2000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(clrDirectory + 4, 4), 72);

        int section = PeSectionOffset();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(section + 8, 4), 0x200);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(section + 12, 4), 0x2000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(section + 16, 4), 0x200);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(section + 20, 4), 0x200);

        int clr = 0x200;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(clr, 4), 72);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(clr + 16, 4), 1);
        if (r2r)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(clr + 64, 4), 0x3000);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(clr + 68, 4), 128);
        }

        return bytes;
    }

    private static int PeOptionalOffset() => 0x84 + 20;
    private static int PeSectionOffset() => PeOptionalOffset() + 240;

    private static void WritePeSectionCount(byte[] bytes, ushort count) =>
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x84 + 2, 2), count);

    private static byte[] Elf(byte elfClass, ushort machine)
    {
        int header = elfClass == 1 ? 52 : 64;
        byte[] bytes = new byte[header];
        bytes[0] = 0x7f;
        bytes[1] = (byte)'E';
        bytes[2] = (byte)'L';
        bytes[3] = (byte)'F';
        bytes[4] = elfClass;
        bytes[5] = 1;
        bytes[6] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(18, 2), machine);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20, 4), 1);
        if (elfClass == 1)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(40, 2), 52);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(52, 2), 64);
        }

        return bytes;
    }

    private static byte[] ZipBytes()
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry entry = zip.CreateEntry("payload.bin", CompressionLevel.Optimal);
            using Stream stream = entry.Open();
            stream.Write(Encoding.UTF8.GetBytes(new string('x', 2048)));
        }

        return output.ToArray();
    }

    private static int FindEocdForTest(byte[] zip)
    {
        for (int i = zip.Length - 22; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(i, 4)) == 0x06054b50)
            {
                return i;
            }
        }

        throw new InvalidDataException("Test ZIP has no EOCD.");
    }

    private static int FindCentralForTest(byte[] zip)
    {
        int eocd = FindEocdForTest(zip);
        return checked((int)BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(eocd + 16, 4)));
    }

    private static byte[] ZipWithDescriptorBytes(bool corruptDescriptor)
    {
        byte[] source = ZipBytes();
        int oldEocd = FindEocdForTest(source);
        int oldCentral = FindCentralForTest(source);
        uint localOffset = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(oldCentral + 42, 4));
        int local = checked((int)localOffset);
        uint crc32 = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(oldCentral + 16, 4));
        uint compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(oldCentral + 20, 4));
        uint uncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(oldCentral + 24, 4));
        ushort localNameLength = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(local + 26, 2));
        ushort localExtraLength = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(local + 28, 2));
        int dataOffset = checked(local + 30 + localNameLength + localExtraLength);
        int dataEnd = checked(dataOffset + (int)compressedSize);
        Assert.Equal(oldCentral, dataEnd);

        byte[] result = new byte[source.Length + 16];
        source.AsSpan(0, oldCentral).CopyTo(result);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(oldCentral, 4), 0x08074b50);
        BinaryPrimitives.WriteUInt32LittleEndian(
            result.AsSpan(oldCentral + 4, 4),
            corruptDescriptor ? crc32 ^ 1u : crc32);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(oldCentral + 8, 4), compressedSize);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(oldCentral + 12, 4), uncompressedSize);
        source.AsSpan(oldCentral).CopyTo(result.AsSpan(oldCentral + 16));

        int central = oldCentral + 16;
        int eocd = oldEocd + 16;
        ushort centralFlags = BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(central + 8, 2));
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(central + 8, 2), (ushort)(centralFlags | 0x0008));
        ushort localFlags = BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(local + 6, 2));
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(local + 6, 2), (ushort)(localFlags | 0x0008));
        result.AsSpan(local + 14, 12).Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(eocd + 16, 4), checked((uint)central));
        return result;
    }

    private static byte[] GzipBytes(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(text));
        }

        return output.ToArray();
    }

    private static byte[] ZlibBytes(string text)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(Encoding.UTF8.GetBytes(text));
        }

        return output.ToArray();
    }

    private static PatchLabFileResult FileResult(string path, string patchSha) =>
        new(
            "node-linux-x64",
            "1",
            "2",
            path,
            1,
            1,
            1,
            1,
            1,
            1,
            0,
            0,
            1,
            0,
            1,
            0,
            0,
            0,
            patchSha);

    private sealed class ShortReadMemoryStream(byte[] bytes, int maximumRead) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, maximumRead)], cancellationToken);
    }
}
