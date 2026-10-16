using System.Diagnostics;
using System.Text.Json;
using ChunkShift.Patching;
using ChunkShift.Patching.Creation;

namespace ChunkShift.Benchmarks.PatchLab;

/// <summary>
/// PATCH-META-001/G1A: measures existing create stages without reading a derived
/// feature cache or changing the encoder/persisted patch semantics.
/// Lab-only; observations are not proof of a 30% metadata-cache opportunity.
/// </summary>
internal static class PatchMetaG1AAttribution
{
    internal const string Schema = "chunkshift.patch-meta-g1a-stage.v1";

    internal static async Task ExecuteAsync(
        string corpusRoot, string family, string baseVersion,
        string targetVersion, string path, string workDirectory,
        string patchPath, string observationPath,
        CspEncoderPolicy policy, CspCreateExecution execution)
    {
        Stopwatch total = Stopwatch.StartNew();
        var baseProbe = new PatchLabManifestProbe();
        var targetProbe = new PatchLabManifestProbe();
        string baseContentPath = PatchLabCorpus.ContentPath(corpusRoot, family, baseVersion, path);
        string targetContentPath = PatchLabCorpus.ContentPath(corpusRoot, family, targetVersion, path);

        string baseManifestPath = await PatchLabManifests.EnsureAsync(
            baseContentPath, workDirectory, expectedSha256: null,
            CancellationToken.None, baseProbe).ConfigureAwait(false);
        string targetManifestPath = await PatchLabManifests.EnsureAsync(
            targetContentPath, workDirectory, expectedSha256: null,
            CancellationToken.None, targetProbe).ConfigureAwait(false);

        var stats = new CspCreateStatistics();
        long baseBytes = 0, targetBytes = 0, baseManifestBytes = 0, targetManifestBytes = 0;
        long baseReads = 0, targetReads = 0, baseSeeks = 0, targetSeeks = 0;
        double builderWallSeconds, builderCpuSeconds;
        long builderAllocatedBytes;
        PatchInfo patchInfo;

        await using (FileStream baseManifest = PatchLabFiles.OpenRead(baseManifestPath))
        await using (FileStream baseContent = PatchLabFiles.OpenRead(baseContentPath))
        await using (FileStream targetManifest = PatchLabFiles.OpenRead(targetManifestPath))
        await using (FileStream targetContent = PatchLabFiles.OpenRead(targetContentPath))
        await using (FileStream destination = PatchLabFiles.Create(patchPath))
        {
            var countedBaseManifest = new PatchLabCountingStream(baseManifest);
            var countedTargetManifest = new PatchLabCountingStream(targetManifest);
            var countedBase = new PatchLabCountingStream(baseContent);
            var countedTarget = new PatchLabCountingStream(targetContent);
            using Process process = Process.GetCurrentProcess();
            process.Refresh();
            TimeSpan cpuBefore = process.TotalProcessorTime;
            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            Stopwatch buildClock = Stopwatch.StartNew();

            patchInfo = await CspPatchBuilder.CreateAsync(
                countedBaseManifest, countedBase, countedTargetManifest,
                countedTarget, destination, policy,
                execution with { Statistics = stats },
                CancellationToken.None).ConfigureAwait(false);

            builderWallSeconds = buildClock.Elapsed.TotalSeconds;
            builderAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            process.Refresh();
            builderCpuSeconds = (process.TotalProcessorTime - cpuBefore).TotalSeconds;

            baseBytes = countedBase.BytesRead;
            targetBytes = countedTarget.BytesRead;
            baseReads = countedBase.Reads;
            targetReads = countedTarget.Reads;
            baseSeeks = countedBase.Seeks;
            targetSeeks = countedTarget.Seeks;
            baseManifestBytes = countedBaseManifest.BytesRead;
            targetManifestBytes = countedTargetManifest.BytesRead;
        }

        Stopwatch hashClock = Stopwatch.StartNew();
        string patchSha256 = await PatchLabManifests.DigestAsync(
            patchPath, CancellationToken.None).ConfigureAwait(false);
        double patchHashSeconds = hashClock.Elapsed.TotalSeconds;
        double totalSeconds = total.Elapsed.TotalSeconds;

        using Process finalProcess = Process.GetCurrentProcess();
        finalProcess.Refresh();
        var observation = new
        {
            schema = Schema,
            status = "STAGE_CALIBRATION_ONLY_NO_PRODUCT_VERDICT",
            lane = "csp",
            execution = "h2-w2",
            family,
            baseVersion,
            targetVersion,
            path,
            environment = new
            {
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                runtimeIdentifier = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
                cpuCount = Environment.ProcessorCount,
                processPeakWorkingSetBytes = finalProcess.PeakWorkingSet64,
            },
            baseSourceSha256 = baseProbe.SourceSha256,
            targetSourceSha256 = targetProbe.SourceSha256,
            baseSourceBytes = new FileInfo(baseContentPath).Length,
            targetSourceBytes = new FileInfo(targetContentPath).Length,
            baseManifestCacheHit = baseProbe.CacheHit,
            targetManifestCacheHit = targetProbe.CacheHit,
            baseDigestWallSeconds = baseProbe.DigestSeconds,
            targetDigestWallSeconds = targetProbe.DigestSeconds,
            baseManifestBuildWallSeconds = baseProbe.ManifestBuildSeconds,
            targetManifestBuildWallSeconds = targetProbe.ManifestBuildSeconds,
            builderWallSeconds,
            builderCpuSeconds,
            builderAllocatedBytes,
            baseContentReadBytes = baseBytes,
            targetContentReadBytes = targetBytes,
            baseContentReadCalls = baseReads,
            targetContentReadCalls = targetReads,
            baseContentSeeks = baseSeeks,
            targetContentSeeks = targetSeeks,
            baseManifestReadBytes = baseManifestBytes,
            targetManifestReadBytes = targetManifestBytes,
            baseCandidateCacheLoads = stats.CacheLoads,
            baseCandidateCacheHits = stats.CacheHits,
            outputPatchBytes = new FileInfo(patchPath).Length,
            patchPhysicalBytes = patchInfo.PhysicalLength,
            patchSha256,
            baseManifestId = patchInfo.BaseManifestId?.ToString(),
            targetManifestId = patchInfo.TargetManifestId.ToString(),
            postBuilderPatchShaWallSeconds = patchHashSeconds,
            totalWallSeconds = totalSeconds,
        };
        // Write the observation only after successful CSP creation and hashing.
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(observationPath))!);
        File.WriteAllText(observationPath, JsonSerializer.Serialize(
            observation, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
}
