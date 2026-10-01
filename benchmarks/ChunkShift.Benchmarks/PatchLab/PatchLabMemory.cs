using System.Diagnostics;
using System.Globalization;
using static System.FormattableString;

namespace ChunkShift.Benchmarks.PatchLab;

/// <summary>
/// <c>patch-lab memory</c>: peak working set of create and of apply over the
/// corpus files of at least <c>--min-bytes</c>, each in a process of its own,
/// plus an idle-process baseline
/// (docs/benchmarks/PATCH-PREFREEZE-PROTOCOL.md section 2).
/// </summary>
internal static class PatchLabMemory
{
    internal const string Schema = "chunkshift.patch-lab-memory.v1";

    private const int IdleRuns = 3;

    internal static int Execute(string[] args)
    {
        if (!PatchLabMemoryOptions.TryParse(args, out PatchLabMemoryOptions options, out string? error))
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        return RunAsync(options).GetAwaiter().GetResult();
    }

    private static async Task<int> RunAsync(PatchLabMemoryOptions options)
    {
        PatchLabCorpus corpus = PatchLabCorpus.Load(options.Corpus, options.Families, options.Work);
        string directory = Directory.CreateTempSubdirectory("chunkshift-patch-lab-memory-").FullName;

        try
        {
            var idlePeaks = new long[IdleRuns];

            for (int run = 0; run < idlePeaks.Length; run++)
            {
                idlePeaks[run] = RunChild(["one", "idle"], "idle");
            }

            long idleBaselineBytes = PatchLabRunner.Median(idlePeaks);
            var files = new List<PatchLabMemoryFile>();
            int index = 0;

            foreach (PatchLabPair pair in corpus.Pairs)
            {
                foreach (PatchLabChangedFile file in pair.Changed)
                {
                    if (!IsInMemoryPopulation(file, options.MinBytes))
                    {
                        continue;
                    }

                    string baseContentPath = corpus.ContentPath(pair, pair.Base, file.Path);
                    string targetContentPath = corpus.ContentPath(pair, pair.Target, file.Path);
                    _ = await PatchLabManifests
                        .EnsureAsync(baseContentPath, corpus.WorkDirectory, file.BaseSha256, CancellationToken.None)
                        .ConfigureAwait(false);
                    _ = await PatchLabManifests
                        .EnsureAsync(targetContentPath, corpus.WorkDirectory, file.TargetSha256, CancellationToken.None)
                        .ConfigureAwait(false);

                    string[] selection =
                    [
                        "one", "create",
                        "--corpus", corpus.Root,
                        "--family", pair.Family,
                        "--base", pair.Base,
                        "--target", pair.Target,
                        "--path", file.Path,
                        "--work", corpus.WorkDirectory,
                        "--lane", options.Lane,
                    ];

                    if (options.Execution is not null)
                    {
                        selection = [.. selection, "--execution", options.Execution];
                    }

                    string patchPath = Path.Combine(directory, Invariant($"patch-{index}.csp"));
                    long createPeakBytes = RunChild([.. selection, "--patch", patchPath], $"create of {file.Path}");
                    string outputPath = Path.Combine(directory, Invariant($"output-{index}"));
                    long applyPeakBytes = RunChild(
                        [
                            "one", "apply",
                            "--corpus", corpus.Root,
                            "--family", pair.Family,
                            "--base", pair.Base,
                            "--target", pair.Target,
                            "--path", file.Path,
                            "--work", corpus.WorkDirectory,
                            "--patch", patchPath,
                            "--output", outputPath,
                        ],
                        $"apply of {file.Path}");

                    files.Add(new PatchLabMemoryFile(
                        pair.Family,
                        pair.Base,
                        pair.Target,
                        file.Path,
                        file.BaseSize,
                        file.TargetSize,
                        createPeakBytes,
                        applyPeakBytes));

                    Console.Error.WriteLine(Invariant(
                        $"patch-lab memory {pair.Family} {pair.Base}->{pair.Target} {file.Path}: create {createPeakBytes} B, apply {applyPeakBytes} B"));
                    index++;
                }
            }

            PatchLabRunner.WriteJson(options.Output, new PatchLabMemoryResult(
                Schema,
                options.RunId,
                options.Lane,
                PatchLabLane.Describe(PatchLabLane.Parse(options.Lane)),
                corpus.PairsSha256,
                PatchLabRunner.Snapshot(),
                MemoryEnvironment(),
                idleBaselineBytes,
                [.. files],
                options.Execution));

            return 0;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    internal static bool IsInMemoryPopulation(PatchLabChangedFile file, long minimumBytes) =>
        Math.Max(file.BaseSize, file.TargetSize) >= minimumBytes;

    /// <summary>
    /// Returns the allocator and GC variables the children inherit, sorted by
    /// name, so a run records the memory configuration it measured.
    /// </summary>
    internal static SortedDictionary<string, string> MemoryEnvironment()
    {
        var variables = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            string name = (string)entry.Key;

            if (name.StartsWith("MALLOC_", StringComparison.Ordinal) ||
                name.StartsWith("DOTNET_GC", StringComparison.OrdinalIgnoreCase))
            {
                variables[name] = (string?)entry.Value ?? string.Empty;
            }
        }

        return variables;
    }

    /// <summary>
    /// Runs one <c>patch-lab one</c> child of this same executable and returns
    /// the peak working set the child reported as its last stdout line.
    /// </summary>
    internal static long RunChild(string[] arguments, string description)
    {
        string host = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot locate the running executable for a patch-lab child.");
        var start = new ProcessStartInfo(host)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        // Under "dotnet ChunkShift.Benchmarks.dll" the host is dotnet itself.
        if (string.Equals(
            Path.GetFileNameWithoutExtension(host),
            "dotnet",
            StringComparison.OrdinalIgnoreCase))
        {
            start.ArgumentList.Add(typeof(PatchLabRunner).Assembly.Location);
        }

        start.ArgumentList.Add("patch-lab");

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process child = Process.Start(start)
            ?? throw new InvalidOperationException($"Could not start the {description} child process.");
        Task<string> output = child.StandardOutput.ReadToEndAsync();
        Task<string> error = child.StandardError.ReadToEndAsync();
        child.WaitForExit();
        string stdout = output.GetAwaiter().GetResult();
        string stderr = error.GetAwaiter().GetResult();

        if (child.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"The {description} child exited with code {child.ExitCode}: {stderr.Trim()}");
        }

        string? peak = stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(static line => line.StartsWith("peak=", StringComparison.Ordinal));

        if (peak is null ||
            !long.TryParse(
                peak["peak=".Length..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long bytes))
        {
            throw new InvalidOperationException(
                $"The {description} child did not report a peak working set: {stdout.Trim()}");
        }

        return bytes;
    }
}
