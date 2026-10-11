using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ChunkShift.Benchmarks.Lab;
using ChunkShift.Chunking;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

/// <summary>
/// CORE011-SCAN-001 hosted pilot. No decision-bearing A/B comparison is made.
/// Measures the unchanged public scanner over memory, short-read memory, and
/// sequential file sources with per-source read-call/byte counters.
/// </summary>
internal static class ScannerIoBaselineHarness
{
    private const string ExperimentId = "CORE011-SCAN-001";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    internal static readonly int[] NormalReads = [int.MaxValue];
    internal static readonly int[] ShortReads = [1, 7, 31, 4095, 65535, 2, 257];

    internal sealed record Sample(
        string Mode,
        string HashSuite,
        int Round,
        double WallMilliseconds,
        double ProcessCpuMilliseconds,
        long ProcessAllocatedBytes,
        long WorkingSetBytes,
        long ReadCalls,
        long ReadBytes,
        long EofReads,
        long ChunkCount,
        long ContentBytes);

    public static int Run(string[] args)
    {
        Dictionary<string, string> values = ParseArguments(args);
        int sizeMiB = GetNumber(values, "--size-mib", 16, 1, 64);
        int rounds = GetNumber(values, "--rounds", 3, 1, 12);
        if (!values.TryGetValue("--output", out string? output) ||
            string.IsNullOrWhiteSpace(output))
        {
            throw new ArgumentException("An explicit --output JSON file is required.");
        }

        string commit = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "local";
        string runId = values.GetValueOrDefault("--run-id") ??
            FormattableString.Invariant(
                $"{ExperimentId}/LOCAL-{DateTimeOffset.UtcNow:yyyyMMddTHHmmss}");
        byte[] payload = new byte[checked(sizeMiB * 1024 * 1024)];
        new DeterministicPrng(0xC485_5A11_2026UL).Fill(payload);
        string sourceSha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        string workspace = Path.Combine(
            Path.GetTempPath(), "chunkshift-core011-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        string filePath = Path.Combine(workspace, "source.bin");

        try
        {
            // Never include fixture construction or disk write in timed samples.
            File.WriteAllBytes(filePath, payload);

            HashSuiteId[] suites = [HashSuiteIds.Blake3256V1, HashSuiteIds.Sha256V1];
            string[] modes = ["memory", "fragmented-memory", "file-sequential"];
            var expected = new Dictionary<string, ChunkKernelChunk[]>();
            foreach (HashSuiteId suite in suites)
            {
                expected.Add(
                    suite.Value,
                    ChunkingReference.Chunk(
                        payload,
                        ChunkingKernelProfile.FastCdcGear(FastCdcProfile.CreateStable64K()),
                        suite));
            }

            // Warm all lanes before any timed sample, including file buffers/JIT.
            foreach (string mode in modes)
            {
                foreach (HashSuiteId suite in suites)
                {
                    _ = Scan(payload, filePath, mode, suite, expected[suite.Value], 0);
                }
            }

            var samples = new List<Sample>(rounds * modes.Length * suites.Length);
            for (int round = 1; round <= rounds; round++)
            {
                IEnumerable<string> sourceOrder =
                    (round & 1) == 0 ? modes.Reverse() : modes;
                IEnumerable<HashSuiteId> suiteOrder =
                    (round & 1) == 0 ? suites.Reverse() : suites;

                foreach (string mode in sourceOrder)
                {
                    foreach (HashSuiteId suite in suiteOrder)
                    {
                        Sample sample = Scan(
                            payload, filePath, mode, suite, expected[suite.Value], round);
                        samples.Add(sample);
                        Console.WriteLine(FormattableString.Invariant(
                            $"lane={mode} suite={suite.Value} round={round} wall-ms={sample.WallMilliseconds:F3} cpu-ms={sample.ProcessCpuMilliseconds:F3} read-calls={sample.ReadCalls} read-bytes={sample.ReadBytes} alloc-bytes={sample.ProcessAllocatedBytes}"));
                    }
                }
            }

            var artifact = new
            {
                schema = "chunkshift.core011.scan-io-pilot.v1",
                experimentId = ExperimentId,
                runId,
                gitSha = commit,
                recordedUtc = DateTimeOffset.UtcNow,
                runtime = RuntimeInformation.FrameworkDescription,
                os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                processorCount = Environment.ProcessorCount,
                cpuModel = GetCpuModel(),
                runnerImage = Environment.GetEnvironmentVariable("ImageOS"),
                corpus = new
                {
                    sourceId = "splitmix64-0xC4855A112026",
                    sourceSha256,
                    contentBytes = payload.Length,
                    stableProfileId = FastCdcProfile.Stable64KProfileId.Value,
                    inputMaterialization = "single-process-memory-and-temp-file",
                },
                conditions = new
                {
                    sizeMiB,
                    rounds,
                    suiteCount = suites.Length,
                    modeCount = modes.Length,
                    minReadFragmentation = 1,
                    sourceFileCaching = "uncontrolled",
                    isolation = "github-hosted-runner-pilot-not-decision-evidence",
                    note = "Process CPU/GC/allocation include incidental runtime activity; file lane residency is uncontrolled.",
                },
                samples,
            };

            string absoluteOutput = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(absoluteOutput)!);
            File.WriteAllText(
                absoluteOutput,
                JsonSerializer.Serialize(
                    artifact,
                    JsonOptions));

            Console.WriteLine($"CORE011-SCAN-001 recorded {samples.Count} pilot samples in {absoluteOutput}");
            return 0;
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static Sample Scan(
        byte[] payload,
        string filePath,
        string mode,
        HashSuiteId suite,
        ChunkKernelChunk[] expected,
        int round)
    {
        Stream source = mode switch
        {
            "memory" or "fragmented-memory" =>
                new MemoryStream(payload, writable: false),
            "file-sequential" => new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan),
            _ => throw new ArgumentException("Unknown scanner source mode.", nameof(mode)),
        };

        using var counted = new CountingReadStream(
            source, mode == "fragmented-memory" ? ShortReads : NormalReads);
        long nextOffset = 0;
        int nextIndex = 0;

        using Process process = Process.GetCurrentProcess();
        TimeSpan cpuStart = process.TotalProcessorTime;
        long allocStart = GC.GetTotalAllocatedBytes(precise: false);
        long ticks = Stopwatch.GetTimestamp();

        ChunkScanner.ScanAsync(
            counted,
            (chunk, content, _) =>
            {
                if (nextIndex >= expected.Length ||
                    chunk.Index != nextIndex ||
                    chunk.Offset != nextOffset ||
                    chunk.Length != content.Length ||
                    chunk.Offset != expected[nextIndex].Offset ||
                    chunk.Length != expected[nextIndex].Length ||
                    chunk.Id != expected[nextIndex].Id)
                {
                    throw new InvalidOperationException(
                        "CORE011-SCAN-001 failed exact chunk identity/offset oracle.");
                }

                nextIndex++;
                nextOffset += chunk.Length;
                return ValueTask.CompletedTask;
            },
            new ChunkScanOptions { HashSuite = suite }).GetAwaiter().GetResult();

        double elapsed = Stopwatch.GetElapsedTime(ticks).TotalMilliseconds;
        TimeSpan cpu = process.TotalProcessorTime - cpuStart;
        long allocation = GC.GetTotalAllocatedBytes(precise: false) - allocStart;

        if (nextIndex != expected.Length ||
            nextOffset != payload.LongLength ||
            counted.ReadBytes != payload.LongLength ||
            counted.EofReads != 1)
        {
            throw new InvalidOperationException(
                "CORE011-SCAN-001 scanner returned an incomplete content stream.");
        }

        return new Sample(
            mode,
            suite.Value,
            round,
            elapsed,
            cpu.TotalMilliseconds,
            Math.Max(0, allocation),
            process.WorkingSet64,
            counted.ReadCalls,
            counted.ReadBytes,
            counted.EofReads,
            nextIndex,
            nextOffset);
    }

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 == args.Length ||
                args[i] is not ("--size-mib" or "--rounds" or "--output" or "--run-id") ||
                !values.TryAdd(args[i], args[i + 1]))
            {
                throw new ArgumentException("Invalid or duplicate scanner probe argument.");
            }
        }
        return values;
    }

    private static int GetNumber(
        Dictionary<string, string> values,
        string key,
        int fallback,
        int minimum,
        int maximum)
    {
        if (!values.TryGetValue(key, out string? literal))
        {
            return fallback;
        }
        if (!int.TryParse(literal, out int value) || value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(key, literal, "Out of bounded probe range.");
        }
        return value;
    }

    private static string? GetCpuModel()
    {
        const string cpuInfo = "/proc/cpuinfo";
        if (File.Exists(cpuInfo))
        {
            foreach (string line in File.ReadLines(cpuInfo))
            {
                if (line.StartsWith("model name", StringComparison.Ordinal) ||
                    line.StartsWith("Processor", StringComparison.Ordinal))
                {
                    return line.Split(':', 2).LastOrDefault()?.Trim();
                }
            }
        }
        return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
    }

    internal sealed class CountingReadStream(Stream inner, int[] schedule) : Stream
    {
        private int _nextRead;

        public long ReadCalls { get; private set; }
        public long ReadBytes { get; private set; }
        public long EofReads { get; private set; }
        public override bool CanRead => inner.CanRead;
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

        public override int Read(Span<byte> buffer) => ReadCore(buffer);

        private int ReadCore(Span<byte> buffer)
        {
            int limit = Math.Min(buffer.Length, schedule[_nextRead++ % schedule.Length]);
            int read = inner.Read(buffer[..limit]);
            Account(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            int limit = Math.Min(buffer.Length, schedule[_nextRead++ % schedule.Length]);
            int read = await inner.ReadAsync(
                buffer[..limit], cancellationToken).ConfigureAwait(false);
            Account(read);
            return read;
        }

        private void Account(int read)
        {
            ReadCalls++;
            ReadBytes += read;
            if (read == 0)
            {
                EofReads++;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }
            base.Dispose(disposing);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
