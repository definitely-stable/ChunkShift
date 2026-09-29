using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ChunkShift.Benchmarks.Tests.Prefreeze;
using ChunkShift.Benchmarks.VerifyLab;
using Microsoft.Win32.SafeHandles;

namespace ChunkShift.Benchmarks.Tests.VerifyLab;

/// <summary>
/// The execution side of docs/benchmarks/CORE-VERIFY-003-PROTOCOL.md: the
/// frozen digests against the protocol text, the calibration ladder (section
/// 4.3), the uncached-read checks (section 4.1), the Windows residency and
/// positive control (section 4.2), the environment (section 3.2) and the
/// RunId and oracle binding (sections 3.2, 9).
/// </summary>
public sealed class VerifyLabExecutionTests
{
    private const string Protocol = "docs/benchmarks/CORE-VERIFY-003-PROTOCOL.md";

    [Fact]
    public void FrozenDigestsAreTheTableOfTheProtocol()
    {
        string text = File.ReadAllText(Path.Combine(PrefreezeRunnerTests.RepositoryRoot(), Protocol));
        int start = text.IndexOf("**Rung digests (frozen).**", StringComparison.Ordinal);
        Assert.True(start >= 0, "the protocol has no rung digest table");

        var rows = new List<VerifyLabFrozenContent>();

        foreach (string line in text[start..].Split('\n').SkipWhile(static line => !line.StartsWith('|')).TakeWhile(static line => line.StartsWith('|')))
        {
            string[] cells = [.. line.Trim().Trim('|').Split('|').Select(static cell => cell.Trim())];
            Match content = Regex.Match(cells[0], "^`(S1|SL)`");

            if (!content.Success)
            {
                continue;
            }

            rows.Add(new VerifyLabFrozenContent(
                content.Groups[1].Value,
                ulong.Parse(cells[1].Trim('`')[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                long.Parse(cells[2].Replace(",", string.Empty, StringComparison.Ordinal), NumberStyles.None, CultureInfo.InvariantCulture),
                cells[3].Trim('`'),
                cells[4].Trim('`')));
        }

        Assert.Equal(5, rows.Count);
        Assert.Equal(rows, VerifyLabCalibration.FrozenContents);
    }

    [Fact]
    public void FrozenTreeAndS1AreCoreVerify002s()
    {
        string root = PrefreezeRunnerTests.RepositoryRoot();
        string text = File.ReadAllText(Path.Combine(root, Protocol));
        JsonArray runs = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "docs/research/results/data/CORE-VERIFY-002-20260929/runs.json")))!.AsArray();
        JsonNode[] workloads = [.. runs.SelectMany(static run => run!["workloads"]!.AsArray()).Select(static w => w!)];

        // Section 2 names the prefixes and the tree's size; the CORE-VERIFY-002 dataset holds the full values.
        Assert.Contains($"`T` `{VerifyLabCalibration.FrozenTree.ContentDigest[..8]}…` over 1,892 files and 855,440,301 bytes", text, StringComparison.Ordinal);
        Assert.Contains($"`S1` `{VerifyLabCalibration.Frozen("S1", 1L << 30)!.ContentDigest[..8]}…`", text, StringComparison.Ordinal);
        Assert.All(workloads.Where(static w => (string?)w["id"] == "T"), static w =>
        {
            Assert.Equal(VerifyLabCalibration.FrozenTree.ContentDigest, (string?)w["contentDigest"]);
            Assert.Equal(VerifyLabCalibration.FrozenTree.Bytes, (long)w["bytes"]!);
            Assert.Equal(VerifyLabCalibration.FrozenTree.Files, (int)w["files"]!);
        });
        Assert.All(workloads.Where(static w => (string?)w["id"] is "S1" or "SL"), static w =>
            Assert.Equal(VerifyLabCalibration.Frozen((string)w["id"]!, (long)w["bytes"]!)!.ContentDigest, (string?)w["contentDigest"]));
    }

    [Fact]
    public void ContentDigestIsTheWorkloadDigestOfTheRunDocument()
    {
        // Section 4.3: SHA-256 of "<SHA-256 hex> <bytes>\n" of the one file.
        foreach (VerifyLabFrozenContent row in VerifyLabCalibration.FrozenContents)
        {
            VerifyLabWorkloadSummary summary = VerifyLabRun.Summarize(new VerifyLabWorkload(
                row.Id,
                "d",
                row.Bytes,
                [new VerifyLabWorkloadFile("f", row.Bytes, row.Sha256, [])]));

            Assert.Equal(row.ContentDigest, summary.ContentDigest);
            Assert.Equal(row.Sha256, summary.Sha256);
        }
    }

    [Theory]
    [InlineData(16L << 30, new long[] { 6, 4, 3, 2 })]
    [InlineData(15_600_000_000L, new long[] { 6, 4, 3, 2 })]
    [InlineData(64L << 30, new long[] { 10, 4, 3, 2 })]
    [InlineData(8L << 30, new long[] { 3, 2 })]
    [InlineData(10L << 30, new long[] { 4, 3, 2 })]
    [InlineData(4L << 30, new long[] { 2 })]
    [InlineData(1L << 30, new long[] { 1 })]
    public void RungsFollowTheMemory(long memory, long[] gib)
    {
        Assert.Equal(gib.Select(static g => g << 30), VerifyLabCalibration.Rungs(memory, smoke: false));
        Assert.Equal(gib.Select(static g => g * VerifyLabCalibration.SmokeBytesPerGiB), VerifyLabCalibration.Rungs(memory, smoke: true));
    }

    [Fact]
    public void CalibrationTakesTheFirstRungWithThreeResidentTrials()
    {
        long[] rungs = VerifyLabCalibration.Rungs(16L << 30, smoke: false);
        VerifyLabCalibrationTrial Trial(int rung, int trial, string residency) => new(rung, rungs[rung], trial, residency, null, null, null, null);
        const string R = VerifyLabResidency.Resident;
        const string N = VerifyLabResidency.NotResident;

        VerifyLabCalibrationTrial[] firstPasses = [Trial(0, 0, R), Trial(0, 1, R), Trial(0, 2, R)];
        Assert.Equal((6L << 30, true), VerifyLabCalibration.Choose(rungs, firstPasses, smoke: false));
        Assert.True(VerifyLabCalibration.Stops(rungs, 0, firstPasses));

        VerifyLabCalibrationTrial[] secondPasses = [Trial(0, 0, R), Trial(0, 1, N), Trial(0, 2, R), Trial(1, 0, R), Trial(1, 1, R), Trial(1, 2, R)];
        Assert.False(VerifyLabCalibration.Stops(rungs, 0, secondPasses));
        Assert.True(VerifyLabCalibration.Stops(rungs, 1, secondPasses));
        Assert.Equal((4L << 30, true), VerifyLabCalibration.Choose(rungs, secondPasses, smoke: false));

        VerifyLabCalibrationTrial[] none =
        [
            .. Enumerable.Range(0, 4).SelectMany(rung => new[] { Trial(rung, 0, R), Trial(rung, 1, VerifyLabResidency.Unverified), Trial(rung, 2, R) }),
        ];
        Assert.True(VerifyLabCalibration.Stops(rungs, 3, none));
        Assert.Equal((2L << 30, false), VerifyLabCalibration.Choose(rungs, none, smoke: false));
        Assert.Equal((16L << 20, false), VerifyLabCalibration.Choose(VerifyLabCalibration.Rungs(16L << 30, smoke: true), none, smoke: true));
    }

    [Theory]
    [InlineData(512, 4096, 0x1FF, 6L << 30, 4096, 512, 4096, null)]
    [InlineData(512, 512, 0xFFF, 6L << 30, 512, 4096, 4096, null)]
    [InlineData(4096, 4096, 0x1FFF, 2L << 30, 4096, 8192, 8192, null)]
    [InlineData(512, 0, 0, 1L << 30, 512, 1, 4096, null)]
    [InlineData(3000, 512, 0x1FF, 6L << 30, 3000, 512, 4096, "1 MiB is not a multiple of A")]
    [InlineData(2 << 20, 512, 0x1FF, 6L << 30, 2 << 20, 512, 2 << 20, "1 MiB is not a multiple of A")]
    [InlineData(4096, 4096, 0x1FF, (6L << 30) + 512, 4096, 512, 4096, "is not a multiple of A")]
    [InlineData(512, 512, 0x2FF, 6L << 30, 512, 768, 4096, "is not a power of two")]
    [InlineData(512, 512, 0x1FFFFF, 6L << 30, 512, 2 << 20, 2 << 20, "is not a power of two no larger than 1 MiB")]
    [InlineData(0, 0, 0x1FF, 6L << 30, 0, 512, 4096, "is not positive")]
    public void UncachedReadChecksTheAlignment(
        long logical,
        long physical,
        long alignmentRequirement,
        long length,
        long sector,
        long device,
        long buffer,
        string? error)
    {
        VerifyLabAlignment checks = VerifyLabUncached.CheckAlignment(logical, physical, alignmentRequirement, length);

        Assert.Equal(sector, checks.SectorAlignment);
        Assert.Equal(device, checks.DeviceAlignment);
        Assert.Equal(buffer, checks.BufferAlignment);

        if (error is null)
        {
            Assert.Null(checks.Error);
        }
        else
        {
            Assert.Contains(error, checks.Error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void UncachedReadLoopReadsWholeMiBAndRejectsAShortRead()
    {
        string directory = Directory.CreateTempSubdirectory("chunkshift-verify-lab-uncached-").FullName;

        try
        {
            string whole = Path.Combine(directory, "whole.bin");
            string tail = Path.Combine(directory, "tail.bin");
            File.WriteAllBytes(whole, new byte[3 << 20]);
            File.WriteAllBytes(tail, new byte[(3 << 20) + 512]);
            byte[] buffer = new byte[VerifyLabUncached.ReadBytes];

            using (SafeFileHandle handle = File.OpenHandle(whole))
            {
                (long bytes, long ticks, string? error) = VerifyLabUncached.ReadAll(handle, buffer, 3 << 20);
                Assert.Equal(3 << 20, bytes);
                Assert.True(ticks >= 0);
                Assert.Null(error);
            }

            using (SafeFileHandle handle = File.OpenHandle(tail))
            {
                (long bytes, _, string? error) = VerifyLabUncached.ReadAll(handle, buffer, (3 << 20) + 512);
                Assert.Equal(3 << 20, bytes);
                Assert.Equal($"a read at offset {3 << 20} returned 512 bytes instead of 1 MiB", error);
            }

            using (SafeFileHandle handle = File.OpenHandle(whole))
            {
                // A file shorter than the length the read expects ends in a read of 0 bytes.
                (_, _, string? error) = VerifyLabUncached.ReadAll(handle, buffer, 4 << 20);
                Assert.Equal($"a read at offset {3 << 20} returned 0 bytes instead of 1 MiB", error);
                Assert.Throws<ArgumentException>(() => VerifyLabUncached.ReadAll(handle, new byte[4096], 3 << 20));
            }

            if (!OperatingSystem.IsWindows())
            {
                VerifyLabUncachedRead read = VerifyLabUncached.Read(whole, block: 1, read: 0);
                Assert.False(read.Successful);
                Assert.Null(read.GiBPerSecond);
                Assert.NotNull(read.Reason);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UIsTheFastestSuccessfulRead()
    {
        VerifyLabUncachedRead[] reads =
        [
            VerifyLabExecutionFixture.Read(1, 0, 1L << 30, 0.4),
            VerifyLabExecutionFixture.Read(1, 1, 1L << 30, 0.5) with { Successful = false, Reason = "short read", GiBPerSecond = 9 },
            VerifyLabExecutionFixture.Read(2, 0, 1L << 30, 0.45),
        ];

        Assert.Equal(0.45, VerifyLabUncached.Maximum(reads));
        Assert.Null(VerifyLabUncached.Maximum(reads.Where(static read => !read.Successful)));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0.38, 1.5)]
    [InlineData(0.5, 1.5)]
    [InlineData(1.0, 3.0)]
    [InlineData(2.5, 7.5)]
    public void WindowsThresholdIsTheFloorOrThreeTimesU(double? u, double? threshold) =>
        Assert.Equal(threshold, VerifyLabResidency.WindowsThreshold(u));

    [Theory]
    [InlineData(3.0, 3.0, true, "resident")]
    [InlineData(2.999, 3.0, true, "not-resident")]
    [InlineData(null, 3.0, true, "unverified")]
    [InlineData(9.0, null, true, "unverified")]
    [InlineData(9.0, 3.0, false, "unverified")]
    public void WindowsVerdictUsesTheThresholdAndThePositiveControls(double? probe, double? threshold, bool controls, string verdict) =>
        Assert.Equal(verdict, VerifyLabResidency.WindowsVerdict(probe, threshold, controls));

    [Fact]
    public void PositiveControlsAreJudgedAgainstTheThresholdOfTheirTime()
    {
        // Block 1: U1 = 0.6 (threshold 1.8); block 2 raises U to 2.0 (threshold 6.0).
        VerifyLabUncachedRead[] reads =
        [
            .. Enumerable.Range(0, 3).Select(static read => VerifyLabExecutionFixture.Read(1, read, 1L << 30, 0.6)),
            .. Enumerable.Range(0, 3).Select(static read => VerifyLabExecutionFixture.Read(2, read, 1L << 30, read == 1 ? 2.0 : 0.6)),
        ];
        (int, int, double?, string?)[] probes =
        [
            (1, 0, 5.0, null), (1, 1, 5.5, null), (1, 2, 6.5, null),
            (2, 0, 6.0, null), (2, 1, 7.0, null), (2, 2, 8.0, null),
        ];

        VerifyLabReference reference = VerifyLabResidency.Evaluate(reads, probes);

        Assert.Equal(0.6, reference.BlockOneMaximum);
        Assert.Equal(1.8, reference.BlockOneThreshold!.Value, 12);
        Assert.Equal(2.0, reference.Maximum);
        Assert.Equal(6.0, reference.Threshold);
        Assert.True(reference.ControlsPassed, reference.UnverifiedReason);
        Assert.Null(reference.UnverifiedReason);
        Assert.Equal([1.8, 1.8, 1.8, 6.0, 6.0, 6.0], reference.Controls.Select(static trial => Math.Round(trial.Threshold!.Value, 12)));

        // The diagnostic: control 1 would not all pass the final threshold.
        Assert.Equal([false, false, true, true, true, true], reference.Controls.Select(static trial => trial.PassesFinal));

        // One trial below its threshold makes every pre-read verdict unverified.
        VerifyLabReference failed = VerifyLabResidency.Evaluate(reads, [.. probes[..5], (2, 2, 5.9, null)]);
        Assert.False(failed.ControlsPassed);
        Assert.Contains("control 2 trial 2", failed.UnverifiedReason, StringComparison.Ordinal);

        VerifyLabSample sample = new(
            "SL", "blake3", "warm", VerifyLabOne.GatedPool, "V1", 1, 0, 0,
            new VerifyLabMeasurement(1L << 30, 1, 1, 1, 0, 0, Valid: true, [], Residency: VerifyLabResidency.Pending, ProbeGiBPerSecond: 7.0),
            null);
        Assert.Equal(VerifyLabResidency.Resident, VerifyLabRun.FinalizeWindows(sample, reference).Measurement!.Residency);
        Assert.Equal(VerifyLabResidency.Unverified, VerifyLabRun.FinalizeWindows(sample, failed).Measurement!.Residency);
        Assert.Equal(VerifyLabResidency.NotResident, VerifyLabRun.FinalizeWindows(sample with { Measurement = sample.Measurement! with { ProbeGiBPerSecond = 5.0 } }, reference).Measurement!.Residency);
        VerifyLabSample cold = sample with { Mode = "cold" };
        Assert.Same(cold, VerifyLabRun.FinalizeWindows(cold, reference));

        // A missing trial, a failed probe or no successful read fails the controls.
        Assert.False(VerifyLabResidency.Evaluate(reads, probes[..5]).ControlsPassed);
        Assert.False(VerifyLabResidency.Evaluate(reads, [.. probes[..5], (2, 2, null, "exit 1")]).ControlsPassed);
        VerifyLabReference noRead = VerifyLabResidency.Evaluate([.. reads.Select(static read => read with { Successful = false, Reason = "CreateFileW failed" })], probes);
        Assert.Null(noRead.Threshold);
        Assert.False(noRead.ControlsPassed);
        Assert.Equal("no uncached read succeeded", noRead.UnverifiedReason);
    }

    [Fact]
    public void CpuModelAndCoresComeFromProcCpuinfo()
    {
        const string X64 = "processor\t: 0\nmodel name\t: AMD EPYC 7763 64-Core Processor\nphysical id\t: 0\ncore id\t\t: 0\n\n" +
            "processor\t: 1\nmodel name\t: AMD EPYC 7763 64-Core Processor\nphysical id\t: 0\ncore id\t\t: 0\n\n" +
            "processor\t: 2\nmodel name\t: AMD EPYC 7763 64-Core Processor\nphysical id\t: 0\ncore id\t\t: 1\n\n";
        const string Arm = "processor\t: 0\nBogoMIPS\t: 50.00\nCPU implementer\t: 0x41\nCPU architecture: 8\nCPU variant\t: 0x3\nCPU part\t: 0xd0c\nCPU revision\t: 1\n\n" +
            "processor\t: 1\nCPU implementer\t: 0x41\nCPU part\t: 0xd0c\n";

        SortedDictionary<string, VerifyLabFact> x64 = VerifyLabEnvironment.ParseCpuModel(X64);
        Assert.Equal(["model name"], x64.Keys);
        Assert.Equal("AMD EPYC 7763 64-Core Processor", x64["model name"].Value);
        Assert.Equal("2", VerifyLabEnvironment.ParsePhysicalCores(X64).Value);

        SortedDictionary<string, VerifyLabFact> arm = VerifyLabEnvironment.ParseCpuModel(Arm);
        Assert.False(arm["model name"].Available);
        Assert.Equal(VerifyLabFact.UnavailableValue, arm["model name"].Value);
        Assert.Equal("0x41", arm["CPU implementer"].Value);
        Assert.Equal("0xd0c", arm["CPU part"].Value);
        Assert.Equal("0x3", arm["CPU variant"].Value);
        Assert.Equal("1", arm["CPU revision"].Value);

        VerifyLabFact cores = VerifyLabEnvironment.ParsePhysicalCores(Arm);
        Assert.False(cores.Available);
        Assert.NotNull(cores.Reason);
    }

    [Fact]
    public void ActionsIdentityRecordsEveryVariableOrWhyItIsMissing()
    {
        SortedDictionary<string, VerifyLabFact> actions = VerifyLabEnvironment.Actions(static name => name == "GITHUB_RUN_ID" ? "42" : null);

        Assert.Equal(VerifyLabEnvironment.ActionsVariables.Order(StringComparer.Ordinal), actions.Keys);
        Assert.Equal("42", actions["GITHUB_RUN_ID"].Value);
        Assert.Equal(VerifyLabFact.UnavailableValue, actions["ImageVersion"].Value);
        Assert.Equal("ImageVersion is not set", actions["ImageVersion"].Reason);
        Assert.Contains(VerifyLabEnvironment.CheckRunIdVariable, actions.Keys);
    }

    [Fact]
    public void EnvironmentIsCapturedWithoutFailing()
    {
        VerifyLabHost host = VerifyLabEnvironment.Capture(Path.GetTempPath());

        Assert.Equal(Environment.ProcessorCount, host.LogicalProcessors);
        Assert.NotEmpty(host.Cpu);
        Assert.NotEmpty(host.Storage);
        Assert.All(host.Storage.Values.Concat(host.Cpu.Values), static fact => Assert.True(fact.Available || fact.Reason is not null));
    }

    [Fact]
    public void RunIdIsComposedFromTheStartDateRunNumberCommitAndPlatform()
    {
        var started = new DateTimeOffset(2026, 9, 30, 1, 30, 0, TimeSpan.FromHours(3));

        Assert.Equal(
            "CORE-VERIFY-003/RUN-20260929-46-0123456-win-x64",
            VerifyLabRun.ComposeRunId(smoke: false, started, "46", VerifyLabExecutionFixture.Commit, "win-x64"));
        Assert.Equal(
            "CORE-VERIFY-003/SMOKE-20260929-46-0123456-linux-x64",
            VerifyLabRun.ComposeRunId(smoke: true, started, "46", VerifyLabExecutionFixture.Commit, "linux-x64"));
        Assert.Null(VerifyLabRun.ComposeRunId(smoke: false, started, null, VerifyLabExecutionFixture.Commit, "win-x64"));
        Assert.Null(VerifyLabRun.ComposeRunId(smoke: false, started, "46", null, "win-x64"));
    }

    [Fact]
    public void TheOracleReportIsBoundToOneExecution()
    {
        string path = Path.Combine(Directory.CreateTempSubdirectory("chunkshift-verify-lab-bind-").FullName, "oracle-win-x64.json");

        try
        {
            VerifyLabRunner.WriteJson(path, new VerifyLabOracleReport(VerifyLabOracle.Schema, quick: false) { Cases = 1_208, Vectors = 62 });

            VerifyLabRun.BindOracle(path, "CORE-VERIFY-003/RUN-20260930-46-0123456-win-x64", "e1", VerifyLabExecutionFixture.Commit, "win-x64");
            VerifyLabOracleSummary bound = VerifyLabRunner.ReadJson<VerifyLabOracleSummary>(path);

            Assert.Equal(("CORE-VERIFY-003/RUN-20260930-46-0123456-win-x64", "e1", VerifyLabExecutionFixture.Commit, "win-x64"), (bound.RunId, bound.ExecutionId, bound.Commit, bound.Platform));
            Assert.Equal((VerifyLabOracle.Schema, VerifyLabRun.ExperimentId, 1_208, 62, true), (bound.Schema, bound.ExperimentId, bound.Cases, bound.Vectors, bound.Passed));
            Assert.Throws<VerifyLabUsageException>(() => VerifyLabRun.BindOracle(path, "other", "e2", VerifyLabExecutionFixture.Commit, "win-x64"));

            File.WriteAllText(path, File.ReadAllText(path).Replace(VerifyLabOracle.Schema, "chunkshift.verify-lab-oracle.v1", StringComparison.Ordinal));
            Assert.Throws<VerifyLabUsageException>(() => VerifyLabRun.BindOracle(path, "other", "e3", VerifyLabExecutionFixture.Commit, "win-x64"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void PlanOfAPlatformFollowsSectionFive()
    {
        VerifyLabGroup[] linux = VerifyLabRun.Plan("linux-arm64", samples: null);
        VerifyLabGroup[] windows = VerifyLabRun.Plan("win-x64", samples: null);

        Assert.Contains(linux, static group => group.Mode == "cold");
        Assert.DoesNotContain(windows, static group => group.Mode == "cold");
        Assert.Equal([VerifyLabRun.SkippedCold], VerifyLabRun.ExpectedSkipped("win-x64"));
        Assert.Empty(VerifyLabRun.ExpectedSkipped("linux-x64"));

        VerifyLabGroup defaultTree = windows[^1];
        Assert.Equal(("T", "warm", VerifyLabOne.DefaultPool, 10), (defaultTree.Workload, defaultTree.Mode, defaultTree.Pool, defaultTree.Samples));
        Assert.Equal(
            ["V1 x1", "V2-W2 x1", "V1 x2", "V2-W2 x2", "V1 x4", "V2-W2 x4", "V1 x8", "V2-W2 x8"],
            defaultTree.Configurations.Select(static c => $"{c.Lane} x{c.Concurrency}"));

        VerifyLabWorkloadSummary[] workloads = [new("SL", "d", 1, 1, "x")];
        Assert.NotEqual(
            VerifyLabRun.Fingerprint(windows, workloads, 6L << 30),
            VerifyLabRun.Fingerprint(windows, workloads, 4L << 30));
    }
}
