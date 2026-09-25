using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ChunkShift.Primitives;
using static System.FormattableString;

namespace ChunkShift.Benchmarks.Lab.Prefreeze;

/// <summary>
/// <c>prefreeze</c> mode (#99): chunk-quality scorecard for the current Gear
/// candidate, the lab-only warmed-prefix candidate and the fixed-size control,
/// over the checked-in synthetic plan and, optionally, a local real-corpus
/// manifest. Deterministic: the same plan and payloads give the same JSON
/// apart from the timestamp and environment.
/// <code>
/// prefreeze --plan &lt;plan.json&gt; --output &lt;result.json&gt; [--markdown &lt;summary.md&gt;]
///           [--lane &lt;name&gt;] [--real &lt;real-corpus.json&gt;] [--no-synthetic]
/// </code>
/// </summary>
internal static class PrefreezeRunner
{
    internal const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static int Run(string[] args)
    {
        string? planPath = Argument(args, "--plan");
        string? outputPath = Argument(args, "--output");

        if (planPath is null || outputPath is null)
        {
            Console.Error.WriteLine("prefreeze requires --plan and --output.");
            return 2;
        }

        PrefreezePlan plan = Load<PrefreezePlan>(planPath);
        string? laneFilter = Argument(args, "--lane");
        string? realPath = Argument(args, "--real");
        bool synthetic = !args.Contains("--no-synthetic", StringComparer.Ordinal);

        PrefreezeRun run = Execute(plan, laneFilter, synthetic, realPath, Console.Out);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(outputPath, JsonSerializer.Serialize(run, JsonOptions));

        string? markdownPath = Argument(args, "--markdown");
        if (markdownPath is not null)
        {
            File.WriteAllText(markdownPath, Summarize(run));
        }

        return 0;
    }

    internal static PrefreezeRun Execute(
        PrefreezePlan plan,
        string? laneFilter,
        bool synthetic,
        string? realManifestPath,
        TextWriter log)
    {
        ValidatePlan(plan);
        HashSuiteId hashSuite = ParseHashSuite(plan.HashSuite);
        PrefreezeLane[] lanes = plan.Lanes
            .Where(lane => laneFilter is null || lane.Name == laneFilter)
            .ToArray();

        if (lanes.Length == 0)
        {
            throw new InvalidOperationException($"No lane named '{laneFilter}'.");
        }

        var rows = new List<PrefreezeRow>();
        var divergence = new List<SemanticDivergence>();

        if (synthetic)
        {
            foreach (PrefreezeLane lane in lanes)
            {
                RunSyntheticLane(plan, lane, hashSuite, rows, divergence, log);
            }
        }

        RealCorpusSummary? realSummary = null;
        if (realManifestPath is not null)
        {
            RealCorpusManifest manifest = Load<RealCorpusManifest>(realManifestPath);
            string baseDirectory = Path.GetDirectoryName(Path.GetFullPath(realManifestPath))!;
            realSummary = RealCorpus.Validate(manifest, baseDirectory);
            int[] targets = lanes.SelectMany(static lane => lane.Targets).Distinct().Order().ToArray();
            RunReal(plan, manifest, baseDirectory, targets, hashSuite, rows, divergence, log);
        }

        PrefreezeFamilyAggregate[] families = PrefreezeAggregation.ByFamily(rows, realSummary);

        return new PrefreezeRun(
            SchemaVersion,
            DateTimeOffset.UtcNow,
            realManifestPath is null ? "synthetic" : synthetic ? "synthetic+real" : "real",
            new EnvironmentSnapshot(
                RuntimeInformation.OSDescription,
                RuntimeInformation.OSArchitecture.ToString(),
                RuntimeInformation.ProcessArchitecture.ToString(),
                RuntimeInformation.FrameworkDescription,
                Environment.ProcessorCount,
                Environment.GetEnvironmentVariable("GITHUB_SHA")),
            plan,
            realSummary,
            [.. divergence],
            [.. rows],
            families,
            PrefreezeAggregation.AcrossFamilies(families));
    }

    private static void RunSyntheticLane(
        PrefreezePlan plan,
        PrefreezeLane lane,
        HashSuiteId hashSuite,
        List<PrefreezeRow> rows,
        List<SemanticDivergence> divergence,
        TextWriter log)
    {
        foreach (CorpusEntry corpus in lane.Corpus)
        {
            byte[] source = CorpusGenerator.Generate(corpus);

            foreach (int target in lane.Targets)
            {
                PrefreezeCandidate[] candidates = plan.Candidates
                    .Select(name => PrefreezeCandidate.Create(name, target))
                    .ToArray();
                ChunkRecord[][] sourceChunks = candidates
                    .Select(candidate => candidate.Chunk(source, hashSuite))
                    .ToArray();

                AddDivergence(lane.Name, corpus.Id, "synthetic", candidates, sourceChunks, divergence);

                // Anchors for boundary-edit mutations come from the current
                // candidate, so every candidate sees the same edited bytes.
                ChunkRecord[] anchorChunks = sourceChunks[AnchorIndex(candidates)];
                PrefreezeCandidate anchorCandidate = candidates[AnchorIndex(candidates)];

                for (int c = 0; c < candidates.Length; c++)
                {
                    rows.Add(PrefreezeScorecard.CreateRow(
                        lane.Name, lane.Exploratory, corpus, "synthetic", candidates[c], "identity", "identity",
                        sourceChunks[c], sourceChunks[c], MutationGenerator.Identity(source), source.Length, source.Length));
                }

                foreach (PrefreezeMutation definition in plan.Mutations)
                {
                    MutationResult? mutation = Mutate(source, definition, anchorChunks, anchorCandidate.Minimum, anchorCandidate.Maximum);
                    if (mutation is null)
                    {
                        continue;
                    }

                    for (int c = 0; c < candidates.Length; c++)
                    {
                        ChunkRecord[] targetChunks = candidates[c].Chunk(mutation.Target, hashSuite);
                        rows.Add(PrefreezeScorecard.CreateRow(
                            lane.Name, lane.Exploratory, corpus, "synthetic", candidates[c], definition.Id, definition.Kind,
                            sourceChunks[c], targetChunks, mutation, source.Length, mutation.Target.Length));
                    }
                }

                log.WriteLine(Invariant($"prefreeze {lane.Name} {corpus.Id} target={target}: done"));
            }
        }
    }

    private static void RunReal(
        PrefreezePlan plan,
        RealCorpusManifest manifest,
        string baseDirectory,
        int[] targets,
        HashSuiteId hashSuite,
        List<PrefreezeRow> rows,
        List<SemanticDivergence> divergence,
        TextWriter log)
    {
        foreach (RealCorpusFamily family in manifest.Families)
        {
            var category = new CorpusEntry(family.Id, family.Category, "real", 0, 0, family.Provenance);

            foreach (int target in targets)
            {
                PrefreezeCandidate[] candidates = plan.Candidates
                    .Select(name => PrefreezeCandidate.Create(name, target))
                    .ToArray();

                // Each version is chunked once per candidate, streaming with a
                // bounded buffer; transitions reuse the chunk lists.
                ChunkRecord[][][] chunks = family.Versions
                    .Select(version => candidates
                        .Select(candidate =>
                        {
                            using FileStream stream = File.OpenRead(RealCorpus.Resolve(baseDirectory, version.Path));
                            return candidate.Chunk(stream, hashSuite);
                        })
                        .ToArray())
                    .ToArray();

                for (int v = 0; v < family.Versions.Length; v++)
                {
                    AddDivergence("real", $"{family.Id}:{family.Versions[v].Version}", family.Split, candidates, chunks[v], divergence);
                }

                foreach (RealCorpus.Transition transition in RealCorpus.Transitions(family))
                {
                    string kind = transition.Stride == 1 ? "version-adjacent" : $"version-skip-{transition.Stride}";
                    RealCorpusVersion from = family.Versions[transition.From];
                    RealCorpusVersion to = family.Versions[transition.To];

                    // The changed byte count of a real transition is unknown, so
                    // Change Amplification and resync are not applicable; reuse,
                    // missing bytes and survival are.
                    MutationResult unknownChange = new(Array.Empty<byte>(), 0, 0, 0, ChangedBytesBases.None);

                    for (int c = 0; c < candidates.Length; c++)
                    {
                        rows.Add(PrefreezeScorecard.CreateRow(
                            "real", false, category, family.Split, candidates[c], transition.Id, kind,
                            chunks[transition.From][c], chunks[transition.To][c], unknownChange, from.SizeBytes, to.SizeBytes));
                    }
                }

                log.WriteLine(Invariant($"prefreeze real {family.Id} target={target}: done"));
            }
        }
    }

    private static void AddDivergence(
        string lane,
        string corpusId,
        string split,
        PrefreezeCandidate[] candidates,
        ChunkRecord[][] chunks,
        List<SemanticDivergence> divergence)
    {
        int current = Array.FindIndex(candidates, static candidate => candidate.Name == PrefreezeCandidate.Current);
        int warmed = Array.FindIndex(candidates, static candidate => candidate.Name == PrefreezeCandidate.WarmedPrefix);

        if (current >= 0 && warmed >= 0)
        {
            divergence.Add(PrefreezeScorecard.Diverge(lane, corpusId, split, candidates[current], chunks[current], chunks[warmed]));
        }
    }

    private static int AnchorIndex(PrefreezeCandidate[] candidates)
    {
        int current = Array.FindIndex(candidates, static candidate => candidate.Name == PrefreezeCandidate.Current);
        return current >= 0 ? current : 0;
    }

    internal static MutationResult? Mutate(
        byte[] source,
        PrefreezeMutation definition,
        ChunkRecord[] anchorChunks,
        int anchorMinimum,
        int anchorMaximum)
    {
        if (definition.Kind != "boundary-edit")
        {
            return MutationGenerator.Apply(source, new MutationDefinition(definition.Kind, definition.SizeBytes, definition.Seed));
        }

        // The start of the middle chunk that follows a content-defined cut; no
        // such chunk (all cuts forced or too few chunks) means no anchor.
        int middle = anchorChunks.Length / 2;
        int anchorChunk = -1;

        for (int index = middle; index < anchorChunks.Length && anchorChunk < 0; index++)
        {
            if (index > 0 && anchorChunks[index - 1].Length < anchorMaximum)
            {
                anchorChunk = index;
            }
        }

        if (anchorChunk < 0)
        {
            return null;
        }

        long anchor = anchorChunks[anchorChunk].Offset + definition.Anchor switch
        {
            "boundary" => 0,
            "minimum" => anchorMinimum,
            _ => throw new InvalidOperationException($"Unknown boundary-edit anchor '{definition.Anchor}'."),
        };

        long position = anchor + definition.RelativeOffset;
        if (position < 0 || position >= source.Length)
        {
            return null;
        }

        byte[] target = (byte[])source.Clone();
        target[position] ^= 0x5A;
        return new MutationResult(target, (int)position, (int)position + 1, 1, ChangedBytesBases.Differing);
    }

    private static void ValidatePlan(PrefreezePlan plan)
    {
        if (plan.SchemaVersion != 1)
        {
            throw new InvalidOperationException("Unsupported pre-freeze plan schema.");
        }

        foreach (string name in plan.Candidates)
        {
            if (!PrefreezeCandidate.Names.Contains(name))
            {
                throw new InvalidOperationException($"Unknown pre-freeze candidate '{name}'.");
            }
        }

        if (plan.Mutations.DistinctBy(static mutation => mutation.Id).Count() != plan.Mutations.Length)
        {
            throw new InvalidOperationException("Pre-freeze mutation ids must be unique.");
        }

        if (plan.Lanes.DistinctBy(static lane => lane.Name).Count() != plan.Lanes.Length)
        {
            throw new InvalidOperationException("Pre-freeze lane names must be unique.");
        }
    }

    internal static string Summarize(PrefreezeRun run)
    {
        var text = new StringBuilder();
        text.AppendLine("# Pre-freeze CDC scorecard (#99)");
        text.AppendLine();
        text.AppendLine(Invariant($"Scope: {run.Scope}; {run.Environment.ProcessArchitecture}; {run.Environment.FrameworkDescription}; commit {run.Environment.GitCommit ?? "local"}."));
        text.AppendLine("Quality metrics only; no throughput. Means and Change Amplification are averaged over corpora/mutations of the group.");
        text.AppendLine();

        if (run.RealCorpus is { } real)
        {
            text.AppendLine(Invariant($"Real corpus: {real.Families} families ({real.CalibrationFamilies} calibration, {real.HoldoutFamilies} holdout)."));
            foreach (string warning in real.Warnings)
            {
                text.AppendLine("- warning: " + warning);
            }

            text.AppendLine();
        }

        text.AppendLine("## Current vs warmed-prefix boundaries (unmutated inputs)");
        text.AppendLine();
        text.AppendLine("| lane | corpus | target | boundaries cur/warm | shared | agreement | cur cuts in transient | warm cuts in transient |");
        text.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
        foreach (SemanticDivergence d in run.Divergence)
        {
            text.AppendLine(Invariant(
                $"| {d.Lane} | {d.CorpusId} | {Size(d.Target)} | {d.CurrentBoundaries}/{d.WarmedBoundaries} | {d.SharedBoundaries} | {d.BoundaryAgreement:P2} | {d.CurrentCutsInTransient} | {d.WarmedCutsInTransient} |"));
        }

        text.AppendLine();
        text.AppendLine("## Boundary quality (unmutated inputs)");
        text.AppendLine();
        text.AppendLine("| lane | corpus | target | candidate | actual mean | mean/target | CV | p99 | forced-max | near-max |");
        text.AppendLine("|---|---|---:|---|---:|---:|---:|---:|---:|---:|");
        foreach (PrefreezeRow row in run.Rows.Where(static row => row.MutationKind == "identity" || row.MutationKind == "version-adjacent"))
        {
            text.AppendLine(Invariant(
                $"| {row.Lane} | {row.CorpusId} | {Size(row.NominalTarget)} | {row.Candidate} | {row.ActualMeanBytes / 1024:F1} KiB | {row.MeanToTargetRatio:F3} | {row.CoefficientOfVariation:F3} | {row.P99ChunkBytes / 1024.0:F1} KiB | {row.ForcedMaximumRate:P2} | {row.NearMaximumRate:P2} |"));
        }

        text.AppendLine();
        text.AppendLine("## Mutation response by group");
        text.AppendLine();
        text.AppendLine("| lane | split | target | candidate | kind | n | mean actual | reuse | Change Amp. | resync coverage | resync p50 | resync p95 | survival |");
        text.AppendLine("|---|---|---:|---|---|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var group in run.Rows
            .Where(static row => row.MutationKind != "identity")
            .GroupBy(static row => (row.Lane, row.Split, row.NominalTarget, row.Candidate, row.MutationKind)))
        {
            PrefreezeRow[] members = [.. group];
            PrefreezeRow[] applicable = members.Where(static row => row.ResynchronizationStatus != ResynchronizationStatuses.NotApplicable).ToArray();
            DistributionSummary? resync = DistributionCalculator.Summarize(applicable.Select(static row => row.ResynchronizationDistanceBytes));
            string coverage = applicable.Length == 0
                ? "n/a"
                : Invariant($"{applicable.Count(static row => row.ResynchronizationStatus == ResynchronizationStatuses.Resynchronized) / (double)applicable.Length:P0}");

            text.AppendLine(Invariant(
                $"| {group.Key.Lane} | {group.Key.Split} | {Size(group.Key.NominalTarget)} | {group.Key.Candidate} | {group.Key.MutationKind} | {members.Length} | {members.Average(static row => row.ActualMeanBytes) / 1024:F1} KiB | {members.Average(static row => row.ReuseRatio):P2} | {members.Average(static row => row.ChangeAmplification):F2} | {coverage} | {Bytes(resync?.P50)} | {Bytes(resync?.P95)} | {members.Average(static row => row.BoundarySurvival):P2} |"));
        }

        text.AppendLine();
        text.AppendLine("## Family-level results");
        text.AppendLine();
        text.AppendLine("One row per family: its transitions are aggregated first, so every family has equal weight below. Missing bytes are a chunk-level lower bound, not a patch size.");
        text.AppendLine();
        text.AppendLine("| lane | family | split | history | target | candidate | scope | n | actual mean | chunks/GiB | reuse | missing bytes | missing/target | survival | resync p50 / p95 / p99 / max | forced-max | manifest/GiB |");
        text.AppendLine("|---|---|---|---|---:|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (PrefreezeFamilyAggregate f in run.Families)
        {
            DistributionSummary? r = f.ResynchronizationDistance;
            string resync = r is null ? "n/a" : $"{Bytes(r.P50)} / {Bytes(r.P95)} / {Bytes(r.P99)} / {Bytes(r.Max)}";
            text.AppendLine(Invariant(
                $"| {f.Lane} | {f.FamilyId} | {f.Split} | {f.History} | {Size(f.NominalTarget)} | {f.Candidate} | {f.Scope} | {f.Transitions} | {f.ActualMeanBytes / 1024:F1} KiB | {f.ChunksPerGiB:F0} | {f.ReuseRatio:P2} | {f.UniqueMissingPayloadBytes / 1024.0:F1} KiB | {f.UniqueMissingPayloadRatio:P2} | {f.BoundarySurvival:P2} | {resync} | {f.ForcedMaximumRate:P2} | {f.ManifestBytesPerSourceGiB / 1024:F1} KiB |"));
        }

        text.AppendLine();
        text.AppendLine("## Across families (equal weight per family)");
        text.AppendLine();
        text.AppendLine("| lane | split | target | candidate | scope | families | selection-eligible | mean actual | reuse mean / worst | missing/target mean / worst | survival mean | forced-max worst |");
        text.AppendLine("|---|---|---:|---|---|---:|---:|---:|---:|---:|---:|---:|");
        foreach (PrefreezeFamilyComparison c in run.FamilyComparison)
        {
            text.AppendLine(Invariant(
                $"| {c.Lane} | {c.Split} | {Size(c.NominalTarget)} | {c.Candidate} | {c.Scope} | {c.Families} | {c.SelectionEligibleFamilies} | {c.MeanActualMeanBytes / 1024:F1} KiB | {c.MeanReuseRatio:P2} / {c.WorstReuseRatio:P2} | {c.MeanUniqueMissingPayloadRatio:P2} / {c.WorstUniqueMissingPayloadRatio:P2} | {c.MeanBoundarySurvival:P2} | {c.WorstForcedMaximumRate:P2} |"));
        }

        text.AppendLine();
        text.AppendLine("## Distribution projection (E4; projection, not a Repository measurement)");
        text.AppendLine();
        text.AppendLine("| lane | split | target | candidate | chunks / 64 MiB pack | missing chunks | ranges gap 0 / 64K / 1M | downloaded vs missing, gap 1M |");
        text.AppendLine("|---|---|---:|---|---:|---:|---:|---:|");
        foreach (var group in run.Rows
            .Where(static row => row.MutationKind != "identity")
            .GroupBy(static row => (row.Lane, row.Split, row.NominalTarget, row.Candidate)))
        {
            PrefreezeRow[] members = [.. group];
            double pack = members.Average(static row => row.Distribution.Packs.Single(static pack => pack.PackMiB == 64).ChunksPerPack);
            double missing = members.Average(static row => row.Distribution.MissingChunks);
            double Ranges(int gap) => members.Average(row => row.Distribution.Ranges.Single(range => range.CoalescingGapBytes == gap).Ranges);
            long downloaded = members.Sum(static row => row.Distribution.Ranges.Single(static range => range.CoalescingGapBytes == 1024 * 1024).DownloadedBytes);
            long unique = members.Sum(static row => row.UniqueMissingPayloadBytes);

            text.AppendLine(Invariant(
                $"| {group.Key.Lane} | {group.Key.Split} | {Size(group.Key.NominalTarget)} | {group.Key.Candidate} | {pack:F0} | {missing:F1} | {Ranges(0):F1} / {Ranges(64 * 1024):F1} / {Ranges(1024 * 1024):F1} | {(unique == 0 ? 1 : downloaded / (double)unique):F2}x |"));
        }

        return text.ToString();
    }

    private static string Size(int bytes) => bytes >= 1024 * 1024
        ? Invariant($"{bytes / (1024 * 1024)} MiB")
        : Invariant($"{bytes / 1024} KiB");

    private static string Bytes(long? bytes) => bytes is null ? "n/a" : Invariant($"{bytes.Value / 1024.0:F1} KiB");

    private static HashSuiteId ParseHashSuite(string value)
    {
        if (string.Equals(value, HashSuiteIds.Blake3256V1.Value, StringComparison.Ordinal))
        {
            return HashSuiteIds.Blake3256V1;
        }

        if (string.Equals(value, HashSuiteIds.Sha256V1.Value, StringComparison.Ordinal))
        {
            return HashSuiteIds.Sha256V1;
        }

        throw new InvalidOperationException($"Unsupported HashSuiteId '{value}'.");
    }

    private static T Load<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidOperationException($"Could not parse '{path}'.");

    private static string? Argument(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
