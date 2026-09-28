// Package-consumer smoke for the packed ChunkShift.Patching NuGet package.
//
// The same program runs under JIT (CI package-smoke) and under NativeAOT on
// x64 and ARM64 (heavy validation), so the whole public patching surface is
// exercised from a real package consumer:
//
//   * ChunkManifest.CreateAsync over both registered hash suites;
//   * ChunkPatch.PlanAsync between a base and a target that reuse, insert and
//     edit chunks and append compressible text;
//   * ChunkPatch.CreateAsync with a base and self-contained;
//   * ChunkPatch.ApplyAsync with the base, self-contained, over a wrong base
//     and in place over the destination.
//
// Deterministic results are written as evidence lines (--evidence <path>) so
// JIT vs AOT and x64 vs ARM64 runs can be compared byte-for-byte. Patch bytes
// are a physical representation, not a contract (PATCHING-DECISIONS D7), so the
// evidence records the reconstructed content, manifest identities, counts and
// failure flags, never patch bytes.
//
// Usage:
//   ChunkShift.Patching.PackageSmoke [--evidence <path>]

using System.Security.Cryptography;
using ChunkShift;
using ChunkShift.Patching;
using ChunkShift.Primitives;

if (args.Length != 0 &&
    (args.Length != 2 || args[0] != "--evidence" || args[1].Length == 0))
{
    Console.Error.WriteLine(
        "Usage: ChunkShift.Patching.PackageSmoke [--evidence <path>]");
    return 2;
}

string? evidencePath = args.Length == 2 ? Path.GetFullPath(args[1]) : null;
var evidence = new List<string>();
DirectoryInfo work = Directory.CreateTempSubdirectory(
    "chunkshift-patching-smoke-");

try
{
    await PatchingSmoke.RunAsync(work.FullName, evidence);

    foreach (string line in evidence)
    {
        Console.WriteLine(line);
    }

    if (evidencePath is not null)
    {
        await File.WriteAllLinesAsync(evidencePath, evidence);
    }

    Console.WriteLine("patching package smoke: OK");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        $"patching package smoke failed: {exception.Message}");
    return 1;
}
finally
{
    PatchingSmoke.TryDelete(work);
}

internal static class PatchingSmoke
{
    private const int Mebibyte = 1024 * 1024;

    // The scenario: a 3 MiB base; the target inserts 4 KiB in the middle,
    // edits one byte at three offsets and appends 256 KiB of repeated text,
    // so the patch needs base-chunk dictionaries and zstd entries.
    private const int BaseLength = 3 * Mebibyte;
    private const int InsertionOffset = BaseLength / 2;
    private const int InsertionLength = 4 * 1024;
    private const int TextLength = 256 * 1024;

    private static readonly int[] EditedOffsets = [300_000, 700_000, 2_600_000];

    private static readonly byte[] TextBlock =
        "ChunkShift patching package smoke: repeated text compresses well with zstd.\n"u8.ToArray();

    internal static async Task RunAsync(string directory, List<string> evidence)
    {
        int suiteIndex = 0;

        foreach (HashSuiteId suite in new[]
        {
            HashSuiteIds.Sha256V1,
            HashSuiteIds.Blake3256V1,
        })
        {
            await RunSuiteAsync(suite, suiteIndex++, directory, evidence);
        }
    }

    private static async Task RunSuiteAsync(
        HashSuiteId suite,
        int suiteIndex,
        string directory,
        List<string> evidence)
    {
        string prefix = $"suite-{suiteIndex}";
        string basePath = Path.Combine(directory, $"{prefix}-base.bin");
        string baseManifestPath = Path.Combine(directory, $"{prefix}-base.csm");
        string targetPath = Path.Combine(directory, $"{prefix}-target.bin");
        string targetManifestPath = Path.Combine(directory, $"{prefix}-target.csm");
        string patchPath = Path.Combine(directory, $"{prefix}-patch.csp");
        string outputPath = Path.Combine(directory, $"{prefix}-out.bin");
        string selfPatchPath = Path.Combine(directory, $"{prefix}-self.csp");
        string selfOutputPath = Path.Combine(directory, $"{prefix}-self.bin");
        string wrongBasePath = Path.Combine(directory, $"{prefix}-wrong-base.bin");
        string wrongBaseManifestPath =
            Path.Combine(directory, $"{prefix}-wrong-base.csm");
        string wrongOutputPath = Path.Combine(directory, $"{prefix}-wrong.bin");
        string inPlacePath = Path.Combine(directory, $"{prefix}-inplace.bin");

        byte[] baseContent = XorShift.Create(BaseLength, 0x5EED0001u);
        byte[] targetContent = CreateTargetContent(baseContent);
        byte[] wrongBaseContent = XorShift.Create(Mebibyte, 0x5EED0003u);

        await File.WriteAllBytesAsync(basePath, baseContent);
        await File.WriteAllBytesAsync(targetPath, targetContent);
        await File.WriteAllBytesAsync(wrongBasePath, wrongBaseContent);

        ManifestInfo baseManifest = await CreateManifestAsync(
            basePath,
            baseManifestPath,
            suite);
        ManifestInfo targetManifest = await CreateManifestAsync(
            targetPath,
            targetManifestPath,
            suite);
        ManifestInfo wrongBaseManifest = await CreateManifestAsync(
            wrongBasePath,
            wrongBaseManifestPath,
            suite);

        Check(
            baseManifest.HashSuite == suite &&
            targetManifest.HashSuite == suite &&
            wrongBaseManifest.HashSuite == suite,
            "CreateAsync recorded a hash suite other than the requested one");

        string targetDigest =
            Convert.ToHexStringLower(SHA256.HashData(targetContent));

        // Reuse planning: the inserted, edited and appended target chunks must
        // be missing while the untouched base chunks stay reused.
        PatchPlan plan;

        await using (FileStream baseManifestStream = OpenRead(baseManifestPath))
        await using (FileStream targetManifestStream = OpenRead(targetManifestPath))
        {
            plan = await ChunkPatch.PlanAsync(
                baseManifestStream,
                targetManifestStream);
        }

        Check(plan.IsValid, "PlanAsync reported an integrity mismatch");
        Check(
            plan.Base.Manifest.ManifestId == baseManifest.ManifestId &&
            plan.Target.Manifest.ManifestId == targetManifest.ManifestId,
            "PlanAsync reported different manifest identities");
        Check(
            plan.ReusedChunks + plan.MissingChunks == targetManifest.ChunkCount &&
            plan.ReusedBytes + plan.MissingBytes == targetManifest.ContentLength,
            "PlanAsync counts do not add up to the target manifest");
        Check(
            plan.ReusedChunks > 0 && plan.MissingChunks > 0,
            "the scenario reused no chunk or missed no chunk");

        evidence.Add(Invariant(
            $"suite={suite} plan-reused-chunks={plan.ReusedChunks}"));
        evidence.Add(Invariant(
            $"suite={suite} plan-reused-bytes={plan.ReusedBytes}"));
        evidence.Add(Invariant(
            $"suite={suite} plan-missing-chunks={plan.MissingChunks}"));
        evidence.Add(Invariant(
            $"suite={suite} plan-missing-bytes={plan.MissingBytes}"));
        evidence.Add(Invariant(
            $"suite={suite} plan-unique-missing-chunks={plan.UniqueMissingChunks}"));
        evidence.Add(Invariant(
            $"suite={suite} plan-unique-missing-bytes={plan.UniqueMissingBytes}"));

        // A patch over the base, applied with the base.
        PatchInfo basePatch;

        await using (FileStream baseManifestStream = OpenRead(baseManifestPath))
        await using (FileStream baseContentStream = OpenRead(basePath))
        await using (FileStream targetManifestStream = OpenRead(targetManifestPath))
        await using (FileStream targetContentStream = OpenRead(targetPath))
        await using (FileStream destination = CreateFile(patchPath))
        {
            basePatch = await ChunkPatch.CreateAsync(
                baseManifestStream,
                baseContentStream,
                targetManifestStream,
                targetContentStream,
                destination);
        }

        Console.WriteLine(Invariant(
            $"patch suite={suite} entries={basePatch.PayloadEntryCount} payload-bytes={basePatch.PayloadBytes} physical-bytes={basePatch.PhysicalLength}"));

        Check(
            basePatch.TargetManifestId == targetManifest.ManifestId &&
            basePatch.BaseManifestId == baseManifest.ManifestId &&
            basePatch.HashSuite == suite,
            "CreateAsync bound the wrong identities");
        Check(
            basePatch.PayloadEntryCount > 0,
            "the patch stored no payload entry for its missing chunks");

        evidence.Add(Invariant(
            $"suite={suite} patch-target-manifest-id={basePatch.TargetManifestId}"));
        evidence.Add(Invariant(
            $"suite={suite} patch-base-manifest-id={basePatch.BaseManifestId}"));
        evidence.Add(Invariant(
            $"suite={suite} patch-hash-suite={basePatch.HashSuite}"));

        PatchApplyResult baseApplied;

        await using (FileStream patchStream = OpenRead(patchPath))
        await using (FileStream baseManifestStream = OpenRead(baseManifestPath))
        await using (FileStream baseContentStream = OpenRead(basePath))
        {
            baseApplied = await ChunkPatch.ApplyAsync(
                patchStream,
                baseManifestStream,
                baseContentStream,
                outputPath);
        }

        Check(
            baseApplied.IsApplied,
            $"applying with the base failed: {baseApplied.Failures}");
        Check(
            baseApplied.Target is not null &&
            baseApplied.Target.ManifestId == targetManifest.ManifestId,
            "ApplyAsync reported a different target manifest");
        string outputDigest = FileDigest(outputPath);
        long outputLength = new FileInfo(outputPath).Length;
        Check(
            outputDigest == targetDigest,
            "the reconstructed content hash differs from the target");
        Check(
            outputLength == targetContent.LongLength,
            "the reconstructed length differs from the target");

        evidence.Add(Invariant(
            $"suite={suite} apply-is-applied={FormatBool(baseApplied.IsApplied)}"));
        evidence.Add(Invariant(
            $"suite={suite} apply-sha256={outputDigest}"));
        evidence.Add(Invariant(
            $"suite={suite} apply-bytes={outputLength}"));
        evidence.Add(Invariant(
            $"suite={suite} apply-target-manifest-id={baseApplied.Target!.ManifestId}"));

        // The self-contained overload carries no base.
        PatchInfo selfPatch;

        await using (FileStream targetManifestStream = OpenRead(targetManifestPath))
        await using (FileStream targetContentStream = OpenRead(targetPath))
        await using (FileStream destination = CreateFile(selfPatchPath))
        {
            selfPatch = await ChunkPatch.CreateAsync(
                targetManifestStream,
                targetContentStream,
                destination);
        }

        Console.WriteLine(Invariant(
            $"self-patch suite={suite} entries={selfPatch.PayloadEntryCount} payload-bytes={selfPatch.PayloadBytes} physical-bytes={selfPatch.PhysicalLength}"));

        Check(
            selfPatch.TargetManifestId == targetManifest.ManifestId &&
            selfPatch.BaseManifestId is null &&
            selfPatch.HashSuite == suite,
            "the self-contained CreateAsync bound the wrong identities");

        PatchApplyResult selfApplied;

        await using (FileStream patchStream = OpenRead(selfPatchPath))
        {
            selfApplied = await ChunkPatch.ApplyAsync(
                patchStream,
                selfOutputPath);
        }

        Check(
            selfApplied.IsApplied,
            $"applying the self-contained patch failed: {selfApplied.Failures}");
        Check(
            selfApplied.Target is not null &&
            selfApplied.Target.ManifestId == targetManifest.ManifestId,
            "the self-contained ApplyAsync reported a different target manifest");
        string selfDigest = FileDigest(selfOutputPath);
        long selfLength = new FileInfo(selfOutputPath).Length;
        Check(
            selfDigest == targetDigest,
            "the self-contained reconstruction hash differs from the target");
        Check(
            selfLength == targetContent.LongLength,
            "the self-contained reconstruction length differs from the target");

        evidence.Add(Invariant(
            $"suite={suite} self-target-manifest-id={selfPatch.TargetManifestId}"));
        evidence.Add(Invariant($"suite={suite} self-base-manifest-id=none"));
        evidence.Add(Invariant(
            $"suite={suite} self-hash-suite={selfPatch.HashSuite}"));
        evidence.Add(Invariant(
            $"suite={suite} self-apply-is-applied={FormatBool(selfApplied.IsApplied)}"));
        evidence.Add(Invariant(
            $"suite={suite} self-apply-sha256={selfDigest}"));
        evidence.Add(Invariant(
            $"suite={suite} self-apply-bytes={selfLength}"));
        evidence.Add(Invariant(
            $"suite={suite} self-apply-target-manifest-id={selfApplied.Target!.ManifestId}"));

        // A different base is refused before anything is published.
        PatchApplyResult wrongBase;

        await using (FileStream patchStream = OpenRead(patchPath))
        await using (FileStream wrongManifestStream = OpenRead(wrongBaseManifestPath))
        await using (FileStream wrongContentStream = OpenRead(wrongBasePath))
        {
            wrongBase = await ChunkPatch.ApplyAsync(
                patchStream,
                wrongManifestStream,
                wrongContentStream,
                wrongOutputPath);
        }

        Check(
            !wrongBase.IsApplied,
            "applying over a different base unexpectedly succeeded");
        Check(
            wrongBase.Failures == PatchApplyFailure.BaseMismatch,
            $"applying over a different base reported {wrongBase.Failures}");
        Check(
            !File.Exists(wrongOutputPath),
            "the rejected apply left a destination file behind");

        evidence.Add(Invariant(
            $"suite={suite} wrong-base-failures={wrongBase.Failures}"));

        // In-place: the destination is the base file itself.
        File.Copy(basePath, inPlacePath, overwrite: true);

        PatchApplyResult inPlace;

        await using (FileStream patchStream = OpenRead(patchPath))
        await using (FileStream baseManifestStream = OpenRead(baseManifestPath))
        await using (FileStream baseContentStream = new(
            inPlacePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete))
        {
            inPlace = await ChunkPatch.ApplyAsync(
                patchStream,
                baseManifestStream,
                baseContentStream,
                inPlacePath);
        }

        Check(
            inPlace.IsApplied,
            $"the in-place update failed: {inPlace.Failures}");
        string inPlaceDigest = FileDigest(inPlacePath);
        Check(
            inPlaceDigest == targetDigest,
            "the in-place update did not produce the target");
        Check(
            new FileInfo(inPlacePath).Length == targetContent.LongLength,
            "the in-place update length differs from the target");

        evidence.Add(Invariant(
            $"suite={suite} inplace-sha256={inPlaceDigest}"));
    }

    private static byte[] CreateTargetContent(byte[] baseContent)
    {
        byte[] inserted = XorShift.Create(InsertionLength, 0x5EED0002u);
        var target = new byte[
            baseContent.Length + inserted.Length + TextLength];

        baseContent.AsSpan(0, InsertionOffset).CopyTo(target);
        inserted.CopyTo(target, InsertionOffset);
        baseContent
            .AsSpan(InsertionOffset)
            .CopyTo(target.AsSpan(InsertionOffset + inserted.Length));

        for (int offset = baseContent.Length + inserted.Length;
            offset < target.Length;)
        {
            int count = Math.Min(TextBlock.Length, target.Length - offset);
            TextBlock.AsSpan(0, count).CopyTo(target.AsSpan(offset));
            offset += count;
        }

        foreach (int offset in EditedOffsets)
        {
            target[offset] ^= 0x5A;
        }

        return target;
    }

    private static async Task<ManifestInfo> CreateManifestAsync(
        string contentPath,
        string manifestPath,
        HashSuiteId suite)
    {
        await using FileStream content = OpenRead(contentPath);
        await using FileStream destination = CreateFile(manifestPath);

        return await ChunkManifest.CreateAsync(
            content,
            destination,
            new ManifestCreationOptions { HashSuite = suite });
    }

    private static string FileDigest(string path)
    {
        using FileStream content = OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(content));
    }

    private static FileStream OpenRead(string path) => new(
        path,
        new FileStreamOptions
        {
            Access = FileAccess.Read,
            Mode = FileMode.Open,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            BufferSize = 64 * 1024,
        });

    private static FileStream CreateFile(string path) => new(
        path,
        new FileStreamOptions
        {
            Access = FileAccess.Write,
            Mode = FileMode.CreateNew,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous,
            BufferSize = 64 * 1024,
        });

    private static string FormatBool(bool value) => value ? "true" : "false";

    private static string Invariant(FormattableString value) =>
        FormattableString.Invariant(value);

    private static void Check(bool condition, string failure)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                $"ChunkShift patching package smoke failed: {failure}.");
        }
    }

    internal static void TryDelete(DirectoryInfo directory)
    {
        try
        {
            directory.Delete(recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

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
