using System.Diagnostics;
using ChunkShift.Patching.Application;
using ChunkShift.Patching.Creation;
using static System.FormattableString;

namespace ChunkShift.Benchmarks.PatchLab;

/// <summary>
/// The <c>patch-lab one</c> child of the memory mode: one operation per
/// process, whose peak working set is printed as the last stdout line
/// (<c>peak=&lt;bytes&gt;</c>), so the parent can attribute it to that
/// operation alone.
/// </summary>
internal static class PatchLabOne
{
    internal static int Execute(string[] args)
    {
        if (args.Length == 0)
        {
            throw new PatchLabUsageException("patch-lab one needs an action: idle, create or apply.");
        }

        return args[0] switch
        {
            "idle" => Idle(),
            "create" => CreateAsync(args[1..]).GetAwaiter().GetResult(),
            "apply" => ApplyAsync(args[1..]).GetAwaiter().GetResult(),
            _ => throw new PatchLabUsageException($"Unknown patch-lab one action '{args[0]}'; expected idle, create or apply."),
        };
    }

    /// <summary>
    /// Starts, touches the types the measured operations use (the policy record
    /// and an empty <see cref="MemoryStream"/>) and reports its peak.
    /// </summary>
    private static int Idle()
    {
        _ = CspEncoderPolicy.Default;
        using var empty = new MemoryStream();
        PrintPeak();
        return 0;
    }

    private static async Task<int> CreateAsync(string[] args)
    {
        if (!PatchLabArguments.TryValue(args, "--corpus", out string corpusRoot) ||
            !PatchLabArguments.TryValue(args, "--family", out string family) ||
            !PatchLabArguments.TryValue(args, "--base", out string baseVersion) ||
            !PatchLabArguments.TryValue(args, "--target", out string targetVersion) ||
            !PatchLabArguments.TryValue(args, "--path", out string path) ||
            !PatchLabArguments.TryValue(args, "--patch", out string patchPath))
        {
            throw new PatchLabUsageException(
                "patch-lab one create requires --corpus, --family, --base, --target, --path and --patch.");
        }

        CspEncoderPolicy policy = PatchLabLane.Parse(PatchLabArguments.Value(args, "--lane") ?? "csp");

        string workDirectory = PatchLabCorpus.ResolveWorkDirectory(
            corpusRoot,
            PatchLabArguments.Value(args, "--work"));
        string baseManifest = await EnsureManifestAsync(
            corpusRoot,
            family,
            baseVersion,
            path,
            workDirectory).ConfigureAwait(false);
        string targetManifest = await EnsureManifestAsync(
            corpusRoot,
            family,
            targetVersion,
            path,
            workDirectory).ConfigureAwait(false);

        await using (FileStream baseManifestStream = PatchLabFiles.OpenRead(baseManifest))
        await using (FileStream baseContent = PatchLabFiles.OpenRead(
            PatchLabCorpus.ContentPath(corpusRoot, family, baseVersion, path)))
        await using (FileStream targetManifestStream = PatchLabFiles.OpenRead(targetManifest))
        await using (FileStream targetContent = PatchLabFiles.OpenRead(
            PatchLabCorpus.ContentPath(corpusRoot, family, targetVersion, path)))
        await using (FileStream destination = PatchLabFiles.Create(patchPath))
        {
            _ = await CspPatchBuilder
                .CreateAsync(
                    baseManifestStream,
                    baseContent,
                    targetManifestStream,
                    targetContent,
                    destination,
                    policy,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        PrintPeak();
        return 0;
    }

    /// <remarks>
    /// <c>--patch</c> names an existing patch; <c>--check</c> selects the
    /// re-chunk check (<c>off</c>, <c>seq</c>, <c>overlap</c> or <c>boundary</c>, default
    /// <c>seq</c>).
    /// </remarks>
    private static async Task<int> ApplyAsync(string[] args)
    {
        if (!PatchLabArguments.TryValue(args, "--corpus", out string corpusRoot) ||
            !PatchLabArguments.TryValue(args, "--family", out string family) ||
            !PatchLabArguments.TryValue(args, "--base", out string baseVersion) ||
            !PatchLabArguments.TryValue(args, "--target", out string targetVersion) ||
            !PatchLabArguments.TryValue(args, "--path", out string path) ||
            !PatchLabArguments.TryValue(args, "--patch", out string patchPath) ||
            !PatchLabArguments.TryValue(args, "--output", out string outputPath))
        {
            throw new PatchLabUsageException(
                "patch-lab one apply requires --corpus, --family, --base, --target, --path, --patch and --output.");
        }

        string lane = PatchLabArguments.Value(args, "--check") ?? "seq";

        if (!PatchLabApplyCheck.TryParseLane(lane, out ChunkingCheck check))
        {
            throw new PatchLabUsageException($"Unknown --check '{lane}'; expected off, seq, overlap or boundary.");
        }

        string workDirectory = PatchLabCorpus.ResolveWorkDirectory(
            corpusRoot,
            PatchLabArguments.Value(args, "--work"));
        string baseManifest = await EnsureManifestAsync(
            corpusRoot,
            family,
            baseVersion,
            path,
            workDirectory).ConfigureAwait(false);

        _ = await PatchLabRun
            .ApplyOnceAsync(
                patchPath,
                baseManifest,
                PatchLabCorpus.ContentPath(corpusRoot, family, baseVersion, path),
                outputPath,
                check,
                CancellationToken.None)
            .ConfigureAwait(false);

        PrintPeak();
        return 0;
    }

    private static Task<string> EnsureManifestAsync(
        string corpusRoot,
        string family,
        string version,
        string path,
        string workDirectory) =>
        PatchLabManifests.EnsureAsync(
            PatchLabCorpus.ContentPath(corpusRoot, family, version, path),
            workDirectory,
            expectedSha256: null,
            CancellationToken.None);

    private static void PrintPeak()
    {
        using Process process = Process.GetCurrentProcess();
        process.Refresh();
        Console.Out.WriteLine(Invariant($"peak={process.PeakWorkingSet64}"));
    }
}
