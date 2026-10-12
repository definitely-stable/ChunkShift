using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Diagnostics.Tracing;

namespace ChunkShift.CsmTraceAnalyzer;

// Independent post-processor, not linked into ChunkShift or the benchmark
// process. GCAllocationTick is sampled; the output never calls it an exact
// object count, exact LOH allocation volume, or an object-lifetime oracle.
internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private sealed record Phase(int Code, int Round, double Ms);
    private sealed record Tick(double Ms, string Kind, string Type, long Amount);

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 6 || args[0] != "--trace" ||
                args[2] != "--scenario" || args[4] != "--output")
            {
                throw new ArgumentException(
                    "Usage: --trace <nettrace> --scenario <JSON> --output <JSON>");
            }
            Analyze(args[1], args[3], args[5]);
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("TRACE_INVALID: " + e);
            return 1;
        }
    }

    private static void Analyze(string tracePath, string scenarioPath, string output)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(scenarioPath));
        JsonElement root = doc.RootElement;
        JsonElement conditions = root.GetProperty("conditions");
        if (root.GetProperty("schema").GetString() !=
                "chunkshift.core011.csm-multiblock-metadata.v1" ||
            root.GetProperty("verdict").GetString() != "NO_VERDICT_EXPLORATORY" ||
            !conditions.GetProperty("traceEvents").GetBoolean() ||
            !root.GetProperty("fixture").GetProperty("payloadCollectedBeforeMeasurement")
                .GetBoolean() ||
            conditions.GetProperty("controlArrays").GetInt32() != 32 ||
            conditions.GetProperty("controlArrayBytes").GetInt32() != 128 * 1024)
        {
            throw new InvalidDataException("Scenario is not a trace-enabled source-free CSM workload.");
        }

        int rounds = conditions.GetProperty("rounds").GetInt32();
        var phases = new List<Phase>();
        var ticks = new List<Tick>();
        using (var source = new EventPipeEventSource(tracePath))
        {
            source.Dynamic.All += e =>
            {
                if (e.ProviderName == "ChunkShift-Core011-CSM-Trace" &&
                    e.EventName == "Phase")
                {
                    int code = Convert.ToInt32(e.PayloadByName("phase"),
                        CultureInfo.InvariantCulture);
                    int round = Convert.ToInt32(e.PayloadByName("round"),
                        CultureInfo.InvariantCulture);
                    phases.Add(new Phase(code, round, e.TimeStampRelativeMSec));
                }
            };
            source.Clr.GCAllocationTick += e =>
            {
                string kind = e.AllocationKind.ToString();
                if (kind is not ("Small" or "Large" or "Pinned"))
                {
                    throw new InvalidDataException("Unknown sampled allocation kind: " + kind);
                }
                long amount = checked((long)e.AllocationAmount64);
                if (amount < 0 || double.IsNaN(e.TimeStampRelativeMSec))
                {
                    throw new InvalidDataException("Invalid sampled GC allocation event.");
                }
                ticks.Add(new Tick(e.TimeStampRelativeMSec,
                    kind, e.TypeName ?? "", amount));
            };
            source.Process();
            if (source.EventsLost != 0)
            {
                throw new InvalidDataException("EventPipe dropped " + source.EventsLost + " events.");
            }
        }

        if (phases.Count != 2 * rounds + 3 || phases[0] is not { Code: 1, Round: 0 })
        {
            throw new InvalidDataException("Missing/duplicate trace phase markers.");
        }
        for (int round = 1; round <= rounds; round++)
        {
            int index = 1 + (round - 1) * 2;
            if (phases[index].Code != 2 || phases[index].Round != round ||
                phases[index + 1].Code != 3 || phases[index + 1].Round != round ||
                phases[index].Ms > phases[index + 1].Ms)
            {
                throw new InvalidDataException("Unbalanced or reordered metadata trace rounds.");
            }
        }
        if (phases[^2].Code != 4 || phases[^2].Round != 0 ||
            phases[^1].Code != 5 || phases[^1].Round != 0)
        {
            throw new InvalidDataException("Missing/invalid positive control markers.");
        }
        for (int i = 1; i < phases.Count; i++)
        {
            if (phases[i].Ms < phases[i - 1].Ms)
            {
                throw new InvalidDataException("Nonmonotonic phase timestamps.");
            }
        }

        static object Aggregate(List<Tick> all, Phase start, Phase end)
        {
            Tick[] inside = all.Where(t => t.Ms >= start.Ms && t.Ms <= end.Ms).ToArray();
            Tick[] large = inside.Where(t => t.Kind == "Large").ToArray();
            // TypeName describes the object that triggered an allocation tick;
            // Amount64 is the allocated-since-previous-tick sample, not the
            // individual object size or an exact by-type allocation volume.
            return new
            {
                startMs = start.Ms,
                endMs = end.Ms,
                allTicks = inside.Length,
                lohTicks = large.Length,
                observedLohTickIntervalBytes = large.Sum(t => t.Amount),
                lohTickTriggerTypes = large.GroupBy(t => t.Type)
                    .OrderByDescending(g => g.Count())
                    .Select(g => new { type = g.Key, ticks = g.Count() })
                    .ToArray(),
            };
        }

        var control = ticks.Where(t => t.Kind == "Large" &&
            t.Ms >= phases[^2].Ms && t.Ms <= phases[^1].Ms).ToArray();
        if (!control.Any(t => t.Type.Contains("Byte[]", StringComparison.Ordinal) ||
                              t.Type.Contains("Byte[", StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "LOH positive control did not produce a System.Byte[] allocation tick.");
        }

        var measured = Enumerable.Range(0, rounds).Select(i =>
            new { round = i + 1, diagnostic = Aggregate(ticks, phases[1 + 2*i], phases[2 + 2*i]) })
            .ToArray();
        var result = new
        {
            schema = "chunkshift.core011.csm-loh-eventpipe-diagnostic.v1",
            experimentId = "CORE011-CSM-001",
            runId = root.GetProperty("runId").GetString(),
            checkoutSha = root.GetProperty("checkoutSha").GetString(),
            traceSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(tracePath))).ToLowerInvariant(),
            traceBytes = new FileInfo(tracePath).Length,
            scenarioSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(scenarioPath))).ToLowerInvariant(),
            eventsLost = 0,
            phaseMarkerCount = phases.Count,
            fixtureEventsExcluded = true,
            rounds = measured,
            positiveControl = Aggregate(ticks, phases[^2], phases[^1]),
            limitations = new []
            {
                "GCAllocationTick-is-sampled-not-per-object-exact",
                "AllocationAmount64-reports-interval-allocation-not-object-size",
                "trigger-TypeName-does-not-attribute-all-interval-bytes-to-that-type",
                "byte-array-type-alone-does-not-prove-CBLK-ownership",
                "EventPipe-overhead-precludes-untraced-latency-comparison",
                "no-object-lifetime-or-per-round-peak-RSS-inference",
            },
            verdict = "NO_VERDICT_EXPLORATORY",
        };
        string destination = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, JsonSerializer.Serialize(result, JsonOptions) + "\n");
        Console.WriteLine("CORE011-CSM-001 traced rounds: " + measured.Length +
            "; positive control LOH ticks: " + control.Length);
    }
}
