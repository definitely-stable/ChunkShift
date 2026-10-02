using System.Text.Json;
using ChunkShift.Patching;

namespace ChunkShift.Benchmarks.PatchLab.PatchGap;

/// <summary>PATCH-GAP-001 foundation commands. No command runs a factor decision dataset.</summary>
internal static class PatchGapRunner
{
    internal static int Execute(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("patch-lab gap needs a mode: h0, inventory or finalize-inventory.");
            return 2;
        }

        return args[0] switch
        {
            "h0" => RunH0(args[1..]),
            "inventory" => RunInventory(args[1..]),
            "finalize-inventory" => RunFinalizeInventory(args[1..]),
            _ => Unknown(args[0]),
        };
    }

    internal static PatchGapH0AnchorResult BuildAnchor(
        PatchLabRunResult run,
        PatchGapSourceBinding sourceBinding,
        string runId,
        string commandLine)
    {
        string expectedExecution = run.Environment.ProcessorCount >= 2 ? "h2-w2" : "h0";
        var expectedPolicy = new PatchLabPolicy(19, 4, 8, 256 * 1024, "prefix", 20, 20);

        if (!string.Equals(run.Lane, "csp", StringComparison.Ordinal) ||
            run.Policy != expectedPolicy ||
            run.Workers != 1 ||
            run.ApplyRepeats != 1 ||
            !string.Equals(run.Execution, expectedExecution, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The regenerated H0 document does not use the frozen production policy/execution.");
        }

        if (!string.Equals(run.CorpusPairsSha256, PatchGapProtocol.CorpusPairsSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"H0 corpus pairs SHA-256 is {run.CorpusPairsSha256}, expected {PatchGapProtocol.CorpusPairsSha256}.");
        }

        if (run.Environment.GitCommit is not null &&
            !string.Equals(run.Environment.GitCommit, sourceBinding.SourceCommit, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"H0 run environment commit {run.Environment.GitCommit} differs from bound source {sourceBinding.SourceCommit}.");
        }

        long calibration = 0;
        long evaluation = 0;

        foreach (PatchLabFileResult file in run.Files)
        {
            if (file.PatchSha256 is null)
            {
                throw new InvalidDataException(
                    $"H0 file {file.Family}/{file.Base}->{file.Target}/{file.Path} has no physical patch SHA-256.");
            }

            if (PatchGapProtocol.DatasetRole(file.Family) == "calibration")
            {
                calibration = checked(calibration + file.PatchBytes);
            }
            else
            {
                evaluation = checked(evaluation + file.PatchBytes);
            }
        }

        long total = checked(calibration + evaluation);
        string patchSetSha256 = PatchGapProtocol.PatchSetDigest(run.Files);
        bool matches =
            run.Files.Length == PatchGapProtocol.ChangedFiles &&
            calibration == PatchGapProtocol.H0CalibrationBytes &&
            evaluation == PatchGapProtocol.H0EvaluationBytes &&
            total == PatchGapProtocol.H0TotalBytes &&
            string.Equals(
                patchSetSha256,
                PatchGapProtocol.H0PatchSetSha256,
                StringComparison.Ordinal);

        DateTimeOffset completedUtc = run.StartedUtc.AddSeconds(run.ElapsedSeconds);

        return new PatchGapH0AnchorResult(
            "chunkshift.patch-gap-h0-anchor.v1",
            PatchGapProtocol.ProtocolCommit,
            sourceBinding.SourceCommit,
            run.CorpusPairsSha256,
            run.Execution ?? "h0",
            run.Files.Length,
            calibration,
            evaluation,
            total,
            patchSetSha256,
            matches,
            PatchGapProvenance.Create(
                runId,
                sourceBinding,
                run.StartedUtc,
                completedUtc,
                commandLine,
                1,
                run.Environment));
    }

    private static int RunH0(string[] args)
    {
        if (!PatchLabArguments.TryValue(args, "--corpus", out string corpusRoot) ||
            !PatchLabArguments.TryValue(args, "--output", out string output) ||
            !PatchLabArguments.TryValue(args, "--source-commit", out string sourceCommit) ||
            !PatchLabArguments.TryValue(args, "--run-id", out string runId))
        {
            throw new PatchLabUsageException(
                "patch-lab gap h0 requires --corpus, --output, --source-commit and --run-id.");
        }

        PatchGapSourceBinding binding = PatchGapSourceBindingProbe.Capture(sourceCommit);
        PatchLabCorpus corpus = LoadFrozenCorpus(corpusRoot, args);

        string temporary = Path.Combine(
            Path.GetTempPath(),
            $"chunkshift-gap-h0-{Guid.NewGuid():N}.json");
        string execution = Environment.ProcessorCount >= 2 ? "h2-w2" : "h0";

        try
        {
            var runArgs = new List<string>
            {
                "--corpus", corpusRoot,
                "--lane", "csp",
                "--output", temporary,
                "--workers", "1",
                "--apply-repeats", "1",
                "--run-id", runId,
                "--execution", execution,
            };

            string? work = PatchLabArguments.Value(args, "--work");
            if (work is not null)
            {
                runArgs.Add("--work");
                runArgs.Add(work);
            }

            int exit = PatchLabRun.Execute([.. runArgs]);
            if (exit != 0)
            {
                return exit;
            }

            PatchLabRunResult run = JsonSerializer.Deserialize<PatchLabRunResult>(
                File.ReadAllBytes(temporary),
                PatchLabRunner.JsonOptions)
                ?? throw new InvalidDataException("Could not parse the regenerated H0 document.");

            PatchGapH0AnchorResult anchor = BuildAnchor(
                run,
                binding,
                runId,
                "patch-lab gap h0 " + string.Join(' ', args));
            _ = PatchGapEvidence.WriteCanonical(output, anchor);
            return anchor.MatchesFrozenAnchor ? 0 : 1;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static int RunInventory(string[] args)
    {
        if (!PatchLabArguments.TryValue(args, "--corpus", out string corpusRoot) ||
            !PatchLabArguments.TryValue(args, "--g4-output", out string g4Output) ||
            !PatchLabArguments.TryValue(args, "--g5-structural-output", out string g5StructuralOutput) ||
            !PatchLabArguments.TryValue(args, "--run-output", out string runOutput) ||
            !PatchLabArguments.TryValue(args, "--source-commit", out string sourceCommit) ||
            !PatchLabArguments.TryValue(args, "--run-id", out string runId))
        {
            throw new PatchLabUsageException(
                "patch-lab gap inventory requires --corpus, --g4-output, --g5-structural-output, "
                + "--run-output, --source-commit and --run-id.");
        }

        DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
        PatchGapSourceBinding binding = PatchGapSourceBindingProbe.Capture(sourceCommit);
        PatchLabCorpus corpus = LoadFrozenCorpus(corpusRoot, args);

        (PatchGapG4InventoryRow[] g4Rows, PatchGapG5InventoryRow[] g5Rows) =
            BuildInventoryAsync(corpus, CancellationToken.None).GetAwaiter().GetResult();

        PatchGapSubsetManifest<PatchGapG4InventoryRow> g4 = PatchGapSubsetManifest.Create(
            "chunkshift.patch-gap-g4-inventory.v1",
            binding.SourceCommit,
            g4Rows,
            static row => row.DatasetRole,
            static row => row.Key);

        // This is deliberately not the frozen final G5 inventory schema.
        // Puffin locator extents and ZIP member attribution must be joined and
        // validated before Stage-A membership can be locked.
        PatchGapSubsetManifest<PatchGapG5InventoryRow> g5Structural = PatchGapSubsetManifest.Create(
            "chunkshift.patch-gap-g5-structural-prepass.v1",
            binding.SourceCommit,
            g5Rows,
            static row => row.DatasetRole,
            static row => row.Key);

        string g4Sha = PatchGapEvidence.WriteCanonical(g4Output, g4);
        string g5Sha = PatchGapEvidence.WriteCanonical(g5StructuralOutput, g5Structural);
        DateTimeOffset completedUtc = DateTimeOffset.UtcNow;
        string commandLine = "patch-lab gap inventory " + string.Join(' ', args);

        var run = new PatchGapInventoryRun(
            "chunkshift.patch-gap-inventory-run.v1",
            PatchGapProtocol.ExperimentId,
            PatchGapProvenance.Create(
                runId,
                binding,
                startedUtc,
                completedUtc,
                commandLine,
                g5Rows.Length),
            completedUtc,
            g4Sha,
            g5Sha,
            g4Rows.Length,
            g5Rows.Length);

        _ = PatchGapEvidence.WriteCanonical(runOutput, run);
        return 0;
    }

    private static int RunFinalizeInventory(string[] args)
    {
        if (!PatchLabArguments.TryValue(args, "--corpus", out string corpusRoot) ||
            !PatchLabArguments.TryValue(args, "--g4", out string g4Path) ||
            !PatchLabArguments.TryValue(args, "--g5-structural", out string structuralPath) ||
            !PatchLabArguments.TryValue(args, "--puffin-locator", out string locatorPath) ||
            !PatchLabArguments.TryValue(args, "--evidence-dir", out string evidenceDirectory) ||
            !PatchLabArguments.TryValue(args, "--source-commit", out string sourceCommit) ||
            !PatchLabArguments.TryValue(args, "--run-id", out string runId))
        {
            throw new PatchLabUsageException(
                "patch-lab gap finalize-inventory requires --corpus, --g4, --g5-structural, "
                + "--puffin-locator, --evidence-dir, --source-commit and --run-id.");
        }

        DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
        PatchGapSourceBinding binding = PatchGapSourceBindingProbe.Capture(sourceCommit);
        PatchLabCorpus corpus = LoadFrozenCorpus(corpusRoot, args);

        PatchGapSubsetManifest<PatchGapG4InventoryRow> g4 =
            DeserializeRequired<PatchGapSubsetManifest<PatchGapG4InventoryRow>>(g4Path);
        PatchGapSubsetManifest<PatchGapG5InventoryRow> structural =
            DeserializeRequired<PatchGapSubsetManifest<PatchGapG5InventoryRow>>(structuralPath);
        PatchGapPuffinLocatorDocument locator =
            DeserializeRequired<PatchGapPuffinLocatorDocument>(locatorPath);

        ValidateSubset(
            g4,
            "chunkshift.patch-gap-g4-inventory.v1",
            binding.SourceCommit,
            static row => row.DatasetRole,
            static row => row.Key);
        ValidateSubset(
            structural,
            PatchGapG5InventoryLock.StructuralSchema,
            binding.SourceCommit,
            static row => row.DatasetRole,
            static row => row.Key);
        ValidateInventoryAgainstCorpus(g4, structural, corpus, binding.SourceCommit);

        PatchGapSubsetManifest<PatchGapG5LockedRow> g5 =
            PatchGapG5InventoryLock.Finalize(structural, locator, corpus);

        string fullEvidenceDirectory = Path.GetFullPath(evidenceDirectory);
        string subsets = Path.Combine(fullEvidenceDirectory, "subsets");
        string evidenceInputs = Path.Combine(fullEvidenceDirectory, "evidence-input");
        string toolProvenance = Path.Combine(fullEvidenceDirectory, "tool-provenance");
        Directory.CreateDirectory(subsets);
        Directory.CreateDirectory(evidenceInputs);
        Directory.CreateDirectory(toolProvenance);

        string g4InputCopy = Path.Combine(evidenceInputs, "g4.json");
        string structuralInputCopy = Path.Combine(evidenceInputs, "g5-structural.json");
        string locatorInputCopy = Path.Combine(evidenceInputs, "puffin-locator.json");
        CopyExactInput(g4Path, g4InputCopy);
        CopyExactInput(structuralPath, structuralInputCopy);
        CopyExactInput(locatorPath, locatorInputCopy);

        byte[] buildProvenance = Convert.FromBase64String(locator.BuildProvenanceBase64);
        string buildProvenanceOutput = Path.Combine(
            toolProvenance, "puffin-build-provenance.bin");
        File.WriteAllBytes(buildProvenanceOutput, buildProvenance);
        string durableBuildProvenanceSha = PatchGapEvidence.FileSha256(buildProvenanceOutput);
        if (!string.Equals(
                durableBuildProvenanceSha,
                locator.BuildProvenanceSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Durable Puffin build provenance differs from the locator record.");
        }

        string g4Output = Path.Combine(subsets, "g4.json");
        string g5Output = Path.Combine(subsets, "g5.json");
        string g4Sha = PatchGapEvidence.WriteCanonical(g4Output, g4);
        string g5Sha = PatchGapEvidence.WriteCanonical(g5Output, g5);

        var inputs = new PatchGapInputsDocument(
            "chunkshift.patch-gap-inputs.v1",
            PatchGapProtocol.ProtocolCommit,
            binding.SourceCommit,
            PatchGapProtocol.CorpusPairsSha256,
            PatchGapProtocol.CorpusManifestSha256,
            PatchGapProtocol.SourceAssetsSha256,
            BuildMaterializedInputDigests(
                corpus,
                g4InputCopy,
                structuralInputCopy,
                locatorInputCopy,
                buildProvenanceOutput));
        PatchGapEvidenceBundle.WriteInputs(fullEvidenceDirectory, inputs);

        PatchGapExternalToolSpec puffin = PatchGapExternalTools.Frozen
            .Single(static tool => tool.Id == "puffin");
        var tool = new PatchGapToolFile(
            puffin.Id,
            puffin.Version,
            puffin.UpstreamCommit,
            locator.ToolVersion,
            locator.ToolCommit,
            puffin.CreateCommandTemplate,
            puffin.ApplyCommandTemplate,
            locator.LogicalArtifact,
            locator.BuildCommand,
            locator.HelpOutput,
            locator.SourceArchiveSha256,
            locator.ExecutableSha256,
            locator.ExecutableSha256,
            locator.ExecutableBytes);
        PatchGapEvidenceBundle.WriteTools(
            fullEvidenceDirectory,
            new PatchGapToolsDocument(
                "chunkshift.patch-gap-tools.v1",
                PatchGapProtocol.ProtocolCommit,
                [tool]));

        DateTimeOffset completedUtc = DateTimeOffset.UtcNow;
        var run = new PatchGapInventoryLockRun(
            "chunkshift.patch-gap-inventory-lock-run.v1",
            PatchGapProtocol.ExperimentId,
            PatchGapProvenance.Create(
                runId,
                binding,
                startedUtc,
                completedUtc,
                "patch-lab gap finalize-inventory " + string.Join(' ', args),
                g5.Rows.Length),
            completedUtc,
            g4Sha,
            PatchGapEvidence.FileSha256(structuralPath),
            PatchGapEvidence.FileSha256(locatorPath),
            g5Sha,
            g5.Rows.Length,
            g5.Rows.Count(static row => row.PuffinSupported));

        _ = PatchGapEvidence.WriteCanonical(
            Path.Combine(fullEvidenceDirectory, "inventory-lock-run.json"),
            run);

        return 0;
    }

    private static void ValidateInventoryAgainstCorpus(
        PatchGapSubsetManifest<PatchGapG4InventoryRow> g4,
        PatchGapSubsetManifest<PatchGapG5InventoryRow> structural,
        PatchLabCorpus corpus,
        string sourceCommit)
    {
        (PatchGapG4InventoryRow[] expectedG4Rows, PatchGapG5InventoryRow[] expectedG5Rows) =
            BuildInventoryAsync(corpus, CancellationToken.None).GetAwaiter().GetResult();

        PatchGapSubsetManifest<PatchGapG4InventoryRow> expectedG4 =
            PatchGapSubsetManifest.Create(
                "chunkshift.patch-gap-g4-inventory.v1",
                sourceCommit,
                expectedG4Rows,
                static row => row.DatasetRole,
                static row => row.Key);
        PatchGapSubsetManifest<PatchGapG5InventoryRow> expectedStructural =
            PatchGapSubsetManifest.Create(
                PatchGapG5InventoryLock.StructuralSchema,
                sourceCommit,
                expectedG5Rows,
                static row => row.DatasetRole,
                static row => row.Key);

        if (!PatchGapEvidence.CanonicalBytes(expectedG4)
                .AsSpan()
                .SequenceEqual(PatchGapEvidence.CanonicalBytes(g4)) ||
            !PatchGapEvidence.CanonicalBytes(expectedStructural)
                .AsSpan()
                .SequenceEqual(PatchGapEvidence.CanonicalBytes(structural)))
        {
            throw new InvalidDataException(
                "PATCH-GAP inventory inputs do not recompute exactly from the frozen materialized corpus.");
        }
    }

    private static void CopyExactInput(string source, string destination)
    {
        string fullSource = Path.GetFullPath(source);
        string fullDestination = Path.GetFullPath(destination);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!string.Equals(fullSource, fullDestination, comparison))
        {
            File.Copy(fullSource, fullDestination, overwrite: true);
        }
    }

    private static T DeserializeRequired<T>(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return JsonSerializer.Deserialize<T>(bytes, PatchLabRunner.JsonOptions)
            ?? throw new InvalidDataException($"Could not deserialize required PATCH-GAP evidence '{path}'.");
    }

    private static void ValidateSubset<TRow>(
        PatchGapSubsetManifest<TRow> document,
        string schema,
        string sourceCommit,
        Func<TRow, string> role,
        Func<TRow, string> key)
    {
        if (!string.Equals(document.Schema, schema, StringComparison.Ordinal) ||
            !string.Equals(document.ProtocolCommit, PatchGapProtocol.ProtocolCommit, StringComparison.Ordinal) ||
            !string.Equals(document.SourceCommit, sourceCommit, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(document.CorpusPairsSha256, PatchGapProtocol.CorpusPairsSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{schema} has foreign protocol/source/corpus identity.");
        }

        PatchGapSubsetManifest<TRow> recomputed = PatchGapSubsetManifest.Create(
            schema,
            sourceCommit,
            document.Rows,
            role,
            key);

        if (!string.Equals(
                recomputed.CalibrationSha256,
                document.CalibrationSha256,
                StringComparison.Ordinal) ||
            !string.Equals(
                recomputed.EvaluationSha256,
                document.EvaluationSha256,
                StringComparison.Ordinal) ||
            !PatchGapEvidence.CanonicalBytes(recomputed.Rows)
                .AsSpan()
                .SequenceEqual(PatchGapEvidence.CanonicalBytes(document.Rows)))
        {
            throw new InvalidDataException($"{schema} split/order fingerprint does not recompute.");
        }
    }

    private static SortedDictionary<string, string> BuildMaterializedInputDigests(
        PatchLabCorpus corpus,
        string g4Path,
        string structuralPath,
        string locatorPath,
        string buildProvenancePath)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["pairs.json"] = PatchGapEvidence.FileSha256(Path.Combine(corpus.Root, "pairs.json")),
            ["evidence-input/g4.json"] = PatchGapEvidence.FileSha256(g4Path),
            ["evidence-input/g5-structural.json"] = PatchGapEvidence.FileSha256(structuralPath),
            ["evidence-input/puffin-locator.json"] = PatchGapEvidence.FileSha256(locatorPath),
            ["tool-provenance/puffin-build-provenance.bin"] =
                PatchGapEvidence.FileSha256(buildProvenancePath),
        };

        foreach (PatchLabPair pair in corpus.Pairs)
        {
            foreach (PatchLabChangedFile file in pair.Changed)
            {
                AddMaterialized(
                    result,
                    corpus.ContentPath(pair, pair.Base, file.Path),
                    $"tree/{pair.Family}/{pair.Base}/{file.Path}",
                    file.BaseSha256);
                AddMaterialized(
                    result,
                    corpus.ContentPath(pair, pair.Target, file.Path),
                    $"tree/{pair.Family}/{pair.Target}/{file.Path}",
                    file.TargetSha256);
            }
        }

        return result;
    }

    private static void AddMaterialized(
        SortedDictionary<string, string> result,
        string physicalPath,
        string logicalPath,
        string expectedSha256)
    {
        string actual = PatchGapEvidence.FileSha256(physicalPath);
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Materialized PATCH-GAP input '{logicalPath}' hashes to {actual}, expected {expectedSha256}.");
        }

        string normalized = logicalPath.Replace('\\', '/');
        if (result.TryGetValue(normalized, out string? existing) &&
            !string.Equals(existing, actual, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Materialized PATCH-GAP input '{normalized}' has conflicting identities.");
        }

        result[normalized] = actual;
    }

    internal static async Task<(PatchGapG4InventoryRow[] G4, PatchGapG5InventoryRow[] G5)> BuildInventoryAsync(
        PatchLabCorpus corpus,
        CancellationToken cancellationToken)
    {
        var g4 = new List<PatchGapG4InventoryRow>();
        var g5 = new List<PatchGapG5InventoryRow>();

        foreach (PatchLabPair pair in corpus.Pairs)
        {
            foreach (PatchLabChangedFile file in pair.Changed)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string basePath = corpus.ContentPath(pair, pair.Base, file.Path);
                string targetPath = corpus.ContentPath(pair, pair.Target, file.Path);
                long uniqueMissingBytes = await UniqueMissingBytesAsync(
                    corpus,
                    pair,
                    file,
                    basePath,
                    targetPath,
                    cancellationToken).ConfigureAwait(false);

                byte[] baseBytes = await File.ReadAllBytesAsync(basePath, cancellationToken).ConfigureAwait(false);
                byte[] targetBytes = await File.ReadAllBytesAsync(targetPath, cancellationToken).ConfigureAwait(false);

                if (pair.Family is "dotnet-aspnetcore-win-x64" or
                    "dotnet-runtime-linux-arm64" or
                    "node-win-x64" or
                    "node-linux-x64")
                {
                    g4.Add(PatchGapG4Classifier.Pair(
                        pair.Family,
                        pair.Base,
                        pair.Target,
                        file.Path,
                        file.TargetSize,
                        uniqueMissingBytes,
                        baseBytes,
                        targetBytes));
                }

                g5.Add(PatchGapG5Classifier.Pair(
                    pair.Family,
                    pair.Base,
                    pair.Target,
                    file.Path,
                    file.TargetSize,
                    uniqueMissingBytes,
                    baseBytes,
                    targetBytes));
            }
        }

        return ([.. g4], [.. g5]);
    }

    private static PatchLabCorpus LoadFrozenCorpus(string corpusRoot, string[] args)
    {
        PatchLabCorpus corpus = PatchLabCorpus.Load(
            corpusRoot,
            families: null,
            PatchLabArguments.Value(args, "--work"));

        if (!string.Equals(corpus.PairsSha256, PatchGapProtocol.CorpusPairsSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"PATCH-GAP-001 requires frozen pairs SHA-256 {PatchGapProtocol.CorpusPairsSha256}; got {corpus.PairsSha256}.");
        }

        return corpus;
    }

    private static async Task<long> UniqueMissingBytesAsync(
        PatchLabCorpus corpus,
        PatchLabPair pair,
        PatchLabChangedFile file,
        string basePath,
        string targetPath,
        CancellationToken cancellationToken)
    {
        string baseManifestPath = await PatchLabManifests
            .EnsureAsync(basePath, corpus.WorkDirectory, file.BaseSha256, cancellationToken)
            .ConfigureAwait(false);
        string targetManifestPath = await PatchLabManifests
            .EnsureAsync(targetPath, corpus.WorkDirectory, file.TargetSha256, cancellationToken)
            .ConfigureAwait(false);

        await using FileStream baseManifest = PatchLabFiles.OpenRead(baseManifestPath);
        await using FileStream targetManifest = PatchLabFiles.OpenRead(targetManifestPath);
        PatchPlan plan = await ChunkPatch
            .PlanAsync(baseManifest, targetManifest, cancellationToken)
            .ConfigureAwait(false);

        if (!plan.IsValid)
        {
            throw new InvalidDataException(
                $"{pair.Family}/{pair.Base}->{pair.Target}/{file.Path}: manifests do not verify.");
        }

        return plan.UniqueMissingBytes;
    }

    private static int Unknown(string mode)
    {
        Console.Error.WriteLine($"Unknown patch-lab gap mode '{mode}'; expected h0, inventory or finalize-inventory.");
        return 2;
    }
}
