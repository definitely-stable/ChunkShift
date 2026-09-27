using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ZstdSharp;
using ZstdSharp.Unsafe;

// ZstdSharp side of benchmarks/zstd-backend/zstd_backend_probe.py.
//
//   ZstdBackendProbe --samples <samples.tsv> --work <dir> --label <name>
//                    --levels 3,9,19 [--compress-repeat N] [--decode-repeat N]
//
// Every sample line is target<TAB>offset<TAB>length<TAB>base<TAB>dictOffset<TAB>dictLength.
// For each level and variant ("none": no dictionary, "k2": the sample's raw-content
// base-chunk dictionary) the probe compresses every sample, writes the frames to
// frames-<label>-<variant>-L<level>.bin (UInt32 LE length prefix per frame),
// decodes them into an exact-length buffer, verifies the bytes, and decodes the
// libzstd frames of the same configuration when the orchestrator has written them.
// Timings go to timings-<label>.json. The orchestrator compares the frames.

string samplesPath = Arg("--samples");
string work = Arg("--work");
string label = Arg("--label");
int[] levels = Arg("--levels").Split(',').Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();
int compressRepeat = int.Parse(Arg("--compress-repeat", "1"), CultureInfo.InvariantCulture);
int decodeRepeat = int.Parse(Arg("--decode-repeat", "5"), CultureInfo.InvariantCulture);

var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
var samples = new List<Sample>();

foreach (string line in File.ReadLines(samplesPath))
{
    if (line.Length == 0)
    {
        continue;
    }

    string[] parts = line.Split('\t');
    samples.Add(new Sample(
        Load(parts[0]),
        int.Parse(parts[1], CultureInfo.InvariantCulture),
        int.Parse(parts[2], CultureInfo.InvariantCulture),
        Load(parts[3]),
        int.Parse(parts[4], CultureInfo.InvariantCulture),
        int.Parse(parts[5], CultureInfo.InvariantCulture)));
}

var results = new List<Dictionary<string, object>>();

foreach (int level in levels)
{
    foreach (string variant in new[] { "none", "k2" })
    {
        bool useDictionary = variant == "k2";
        var frames = new byte[samples.Count][];
        var compressSeconds = new List<double>();

        for (int run = 0; run < compressRepeat; run++)
        {
            using var compressor = new Compressor(level);
            var stopwatch = Stopwatch.StartNew();

            for (int index = 0; index < samples.Count; index++)
            {
                Sample sample = samples[index];

                if (useDictionary)
                {
                    compressor.LoadDictionary(sample.Dictionary);
                }

                frames[index] = compressor.Wrap(sample.Data).ToArray();
            }

            compressSeconds.Add(stopwatch.Elapsed.TotalSeconds);
        }

        var decodeSeconds = new List<double>();
        var output = new byte[samples.Max(sample => sample.Length)];

        for (int run = 0; run < decodeRepeat; run++)
        {
            decodeSeconds.Add(Decode(frames, useDictionary, output, verify: run == 0));
        }

        string framesPath = Path.Combine(work, $"frames-{label}-{variant}-L{level}.bin");
        WriteFrames(framesPath, frames);

        string otherPath = Path.Combine(work, $"frames-libzstd-{variant}-L{level}.bin");
        int crossDecoded = -1;
        if (File.Exists(otherPath))
        {
            byte[][] other = ReadFrames(otherPath);
            if (other.Length != samples.Count)
            {
                throw new InvalidDataException($"{otherPath}: expected {samples.Count} frames, found {other.Length}.");
            }

            Decode(other, useDictionary, output, verify: true);
            crossDecoded = other.Length;
        }

        results.Add(new Dictionary<string, object>
        {
            ["level"] = level,
            ["variant"] = variant,
            ["samples"] = samples.Count,
            ["input_bytes"] = samples.Sum(sample => (long)sample.Length),
            ["frame_bytes"] = frames.Sum(frame => (long)frame.Length),
            ["compress_seconds"] = compressSeconds,
            ["decode_seconds"] = decodeSeconds,
            ["cross_decoded_libzstd_frames"] = crossDecoded,
        });

        Console.WriteLine(
            $"{label} L{level} {variant}: frames {frames.Sum(frame => (long)frame.Length)} B, " +
            $"compress {Median(compressSeconds).ToString("F3", CultureInfo.InvariantCulture)} s, " +
            $"decode {Median(decodeSeconds).ToString("F4", CultureInfo.InvariantCulture)} s");
    }
}

var report = new Dictionary<string, object>
{
    ["label"] = label,
    ["zstd_version"] = Methods.ZSTD_versionNumber(),
    ["runtime"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    ["native_aot"] = !System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported,
    ["results"] = results,
};

File.WriteAllText(
    Path.Combine(work, $"timings-{label}.json"),
    JsonSerializer.Serialize(report, ProbeJson.Default.DictionaryStringObject));

return 0;

double Decode(byte[][] frames, bool useDictionary, byte[] output, bool verify)
{
    using var decompressor = new Decompressor();
    decompressor.SetParameter(ZSTD_dParameter.ZSTD_d_windowLogMax, 20);
    var stopwatch = Stopwatch.StartNew();

    for (int index = 0; index < frames.Length; index++)
    {
        Sample sample = samples[index];
        if (useDictionary)
        {
            decompressor.LoadDictionary(sample.Dictionary);
        }

        Span<byte> destination = output.AsSpan(0, sample.Length);
        int written = decompressor.Unwrap(frames[index], destination);

        if (written != sample.Length || (verify && !destination.SequenceEqual(sample.Data)))
        {
            throw new InvalidDataException($"sample {index}: frame does not round-trip.");
        }
    }

    return stopwatch.Elapsed.TotalSeconds;
}

byte[] Load(string path)
{
    if (!files.TryGetValue(path, out byte[]? bytes))
    {
        bytes = File.ReadAllBytes(path);
        files[path] = bytes;
    }

    return bytes;
}

string Arg(string name, string? fallback = null)
{
    int index = Array.IndexOf(args, name);
    if (index >= 0 && index + 1 < args.Length)
    {
        return args[index + 1];
    }

    return fallback ?? throw new ArgumentException($"missing {name}");
}

static void WriteFrames(string path, byte[][] frames)
{
    using var stream = File.Create(path);
    Span<byte> prefix = stackalloc byte[4];

    foreach (byte[] frame in frames)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(prefix, (uint)frame.Length);
        stream.Write(prefix);
        stream.Write(frame);
    }
}

static byte[][] ReadFrames(string path)
{
    byte[] bytes = File.ReadAllBytes(path);
    var frames = new List<byte[]>();
    int position = 0;

    while (position < bytes.Length)
    {
        int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position, 4));
        frames.Add(bytes.AsSpan(position + 4, length).ToArray());
        position += 4 + length;
    }

    return frames.ToArray();
}

static double Median(List<double> values)
{
    double[] sorted = values.Order().ToArray();
    return sorted[sorted.Length / 2];
}

internal sealed record Sample(byte[] Target, int Offset, int Length, byte[] Base, int DictionaryOffset, int DictionaryLength)
{
    public ReadOnlySpan<byte> Data => Target.AsSpan(Offset, Length);

    public ReadOnlySpan<byte> Dictionary => Base.AsSpan(DictionaryOffset, DictionaryLength);
}

[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, object>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(List<Dictionary<string, object>>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(List<double>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(int))]
[System.Text.Json.Serialization.JsonSerializable(typeof(long))]
[System.Text.Json.Serialization.JsonSerializable(typeof(uint))]
[System.Text.Json.Serialization.JsonSerializable(typeof(bool))]
[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal sealed partial class ProbeJson : System.Text.Json.Serialization.JsonSerializerContext
{
}
