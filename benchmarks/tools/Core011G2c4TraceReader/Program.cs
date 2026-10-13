using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.EventPipe;

if (args.Length != 4 || args[0] != "--trace" || args[2] != "--output")
{
    Console.Error.WriteLine("Usage: reader --trace input.nettrace --output analysis.json");
    return 2;
}

string input = Path.GetFullPath(args[1]);
string output = Path.GetFullPath(args[3]);
if (!File.Exists(input) || new FileInfo(input).Length == 0 || File.Exists(output))
{
    Console.Error.WriteLine("Missing trace, empty trace, or existing output");
    return 2;
}

var methodEvents = new List<object>();
var markers = new List<object>();
int jitStarted = 0, methodLoaded = 0;
string Value(TraceEvent e, string field)
{
    try { return e.PayloadByName(field)?.ToString() ?? ""; }
    catch (ArgumentException) { return ""; }
    catch (IndexOutOfRangeException) { return ""; }
}

using (var source = new EventPipeEventSource(input))
{
    source.Clr.MethodJittingStarted += data =>
    {
        jitStarted++;
        methodEvents.Add(new
        {
            Event = "MethodJittingStarted",
            Tms = data.TimeStampRelativeMSec,
            Namespace = Value(data, "MethodNamespace"),
            Name = Value(data, "MethodName"),
            MethodId = Value(data, "MethodID"),
            MethodFlags = Value(data, "MethodFlags"),
            ModuleId = Value(data, "ModuleID"),
        });
    };
    source.Clr.MethodLoadVerbose += data =>
    {
        methodLoaded++;
        methodEvents.Add(new
        {
            Event = "MethodLoadVerbose",
            Tms = data.TimeStampRelativeMSec,
            Namespace = Value(data, "MethodNamespace"),
            Name = Value(data, "MethodName"),
            MethodId = Value(data, "MethodID"),
            MethodFlags = Value(data, "MethodFlags"),
            ModuleId = Value(data, "ModuleID"),
        });
    };
    source.Dynamic.All += data =>
    {
        if (!string.Equals(data.ProviderName, "ChunkShift-G2C4", StringComparison.Ordinal))
            return;
        var payload = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string key in data.PayloadNames)
            payload[key] = Value(data, key);
        markers.Add(new
        {
            Event = data.EventName, Id = data.ID, Tms = data.TimeStampRelativeMSec,
            Payload = payload,
        });
    };
    source.Process();
}

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
var target = new
{
    Schema = "chunkshift.core011-csm-g2c4-trace-reader.v1",
    SourceTraceSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(input))),
    Reader = "Microsoft.Diagnostics.Tracing.TraceEvent/3.2.8",
    JittingStartedCount = jitStarted,
    MethodLoadVerboseCount = methodLoaded,
    MarkerCount = markers.Count,
    MethodEvents = methodEvents,
    Markers = markers,
    CausalityEstablished = false,
    Verdict = "NO_CAUSAL_JIT_PROOF",
};
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(target, new JsonSerializerOptions
{
    WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
}) + "\n");
Console.WriteLine($"G2-C4 trace reader: jitting={jitStarted}, loads={methodLoaded}, markers={markers.Count}, output={output}");
if (jitStarted == 0 && methodLoaded == 0)
{
    Console.Error.WriteLine("No CLR JIT/MethodLoadVerbose events: invalid diagnostic trace.");
    return 1;
}
if (markers.Count == 0)
{
    Console.Error.WriteLine("No ChunkShift-G2C4 markers: trace and calls cannot be aligned.");
    return 1;
}
return 0;
