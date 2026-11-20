using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

const long MiB = 1024L * 1024;
const long SmallBytes = 8 * MiB;
const long LargeBytes = 384 * MiB;
const long PatchBytes = 64 * MiB;
const long HeapCap = 256 * MiB;
const long RssCap = 640 * MiB;
const long GrowthCap = 128 * MiB;
const int MaximumOutputBytes = 4096;
const string HeapHardLimitHex = "10000000";

if (args is not [string cliAssembly, string evidencePath] ||
    !File.Exists(cliAssembly))
{
    Console.Error.WriteLine(
        "Usage: CliResourceProbe <built ChunkShift.Cli.dll> <evidence.json>");
    return 2;
}

string workspace = Path.Combine(Path.GetTempPath(),
    "chunkshift-cli140-b2c-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
Directory.CreateDirectory(workspace);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(evidencePath))!);

var results = new List<ProbeMeasurement>();
string state = "failed";
string? error = null;

try
{
    string smallContent = Path.Combine(workspace, "small.bin");
    string largeContent = Path.Combine(workspace, "large.bin");
    string patchContent = Path.Combine(workspace, "patch-target.bin");

    string smallHash = await GenerateAsync(smallContent, SmallBytes, 0x5111A5E5);
    string largeHash = await GenerateAsync(largeContent, LargeBytes, 0x5111A5E5);
    string patchHash = await GenerateAsync(patchContent, PatchBytes, 0xF00D2189);

    Require(new FileInfo(smallContent).Length == SmallBytes, "small fixture size");
    Require(new FileInfo(largeContent).Length == LargeBytes, "large fixture size");
    Require(new FileInfo(patchContent).Length == PatchBytes, "patch fixture size");

    var probe = new CliRunner(cliAssembly, workspace,
        HeapHardLimitHex, RssCap, MaximumOutputBytes, results);

    string smallManifest = Path.Combine(workspace, "small.csm");
    string largeManifest = Path.Combine(workspace, "large.csm");
    string patchManifest = Path.Combine(workspace, "patch-target.csm");

    using JsonDocument createSmall = await probe.RunAsync("small-create", "manifest.create",
        SmallBytes, "manifest", "create", smallContent, "-o", smallManifest,
        "--sha256", "--json");
    using JsonDocument createLarge = await probe.RunAsync("large-create", "manifest.create",
        LargeBytes, "manifest", "create", largeContent, "-o", largeManifest,
        "--sha256", "--json");

    JsonElement smallData = createSmall.RootElement.GetProperty("manifest");
    JsonElement largeData = createLarge.RootElement.GetProperty("manifest");
    long smallChunks = smallData.GetProperty("chunks").GetInt64();
    long largeChunks = largeData.GetProperty("chunks").GetInt64();
    Require(smallData.GetProperty("contentBytes").GetInt64() == SmallBytes,
        "small CSM has incorrect byte count");
    Require(largeData.GetProperty("contentBytes").GetInt64() == LargeBytes,
        "large CSM has incorrect byte count");
    Require(largeChunks > 256 && largeChunks >= 8 * smallChunks,
        "large CSM did not exercise enough chunks");
    string? smallId = smallData.GetProperty("manifestId").GetString();
    string? largeId = largeData.GetProperty("manifestId").GetString();

    using JsonDocument inspectSmall = await probe.RunAsync("small-inspect", "manifest.inspect",
        SmallBytes, "manifest", "inspect", smallManifest, "--json");
    using JsonDocument inspectLarge = await probe.RunAsync("large-inspect", "manifest.inspect",
        LargeBytes, "manifest", "inspect", largeManifest, "--json");
    AssertInspect(inspectSmall, smallId, smallChunks);
    AssertInspect(inspectLarge, largeId, largeChunks);

    using JsonDocument verifySmall = await probe.RunAsync("small-verify", "manifest.verify",
        SmallBytes, "manifest", "verify", smallManifest, "--content", smallContent, "--json");
    using JsonDocument verifyLarge = await probe.RunAsync("large-verify", "manifest.verify",
        LargeBytes, "manifest", "verify", largeManifest, "--content", largeContent, "--json");
    AssertVerified(verifySmall);
    AssertVerified(verifyLarge);

    CheckResidentGrowth(results, "small-inspect", "large-inspect", GrowthCap);
    CheckResidentGrowth(results, "small-verify", "large-verify", GrowthCap);

    using JsonDocument plan = await probe.RunAsync("large-plan", "patch.plan",
        LargeBytes, "patch", "plan",
        "--base-manifest", largeManifest, "--target-manifest", largeManifest, "--json");
    JsonElement planData = plan.RootElement.GetProperty("plan");
    Require(planData.GetProperty("valid").GetBoolean(), "large self-plan invalid");
    Require(planData.GetProperty("missingBytes").GetInt64() == 0,
        "large self-plan reports missing bytes");
    Require(planData.GetProperty("reusedBytes").GetInt64() == LargeBytes,
        "large self-plan reuse does not cover the input");
    Require(planData.GetProperty("targetChunks").GetInt64() == largeChunks,
        "large self-plan chunk count disagrees with CSM");

    using JsonDocument createPatchManifest = await probe.RunAsync(
        "patch-target-create", "manifest.create", PatchBytes,
        "manifest", "create", patchContent, "-o", patchManifest, "--sha256", "--json");
    Require(createPatchManifest.RootElement.GetProperty("manifest")
        .GetProperty("contentBytes").GetInt64() == PatchBytes,
        "patch-target manifest byte count");

    string patchPath = Path.Combine(workspace, "self-contained.csp");
    using JsonDocument patchResult = await probe.RunAsync("patch-create", "patch.create",
        PatchBytes, "patch", "create", "--target-manifest", patchManifest,
        "--target", patchContent, "-o", patchPath, "--json");
    Require(patchResult.RootElement.GetProperty("patch")
        .GetProperty("payloadEntries").GetInt64() > 0,
        "self-contained patch has no payload entries");
    Require(File.Exists(patchPath), "CSP was not committed");

    string reconstructed = Path.Combine(workspace, "reconstructed.bin");
    using JsonDocument apply = await probe.RunAsync("patch-apply", "patch.apply",
        PatchBytes, "patch", "apply", patchPath, "-o", reconstructed, "--json");
    JsonElement applyData = apply.RootElement.GetProperty("apply");
    Require(applyData.GetProperty("applied").GetBoolean(), "CSP application failed");
    Require(applyData.GetProperty("outputBytes").GetInt64() == PatchBytes,
        "reconstructed output size mismatch");
    Require(new FileInfo(reconstructed).Length == PatchBytes, "published output size mismatch");

    string reconstructedHash = await HashFileAsync(reconstructed);
    Require(reconstructedHash == patchHash, "patch output differs from target SHA-256");
    Require(await HashFileAsync(smallContent) == smallHash,
        "small source modified unexpectedly");
    Require(await HashFileAsync(largeContent) == largeHash,
        "large source modified unexpectedly");
    Require(Directory.GetFiles(workspace, "*.tmp", SearchOption.AllDirectories).Length == 0,
        "orphan .tmp after CLI commands");
    Require(Directory.GetFiles(workspace, "*.cli-stage", SearchOption.AllDirectories).Length == 0,
        "orphan .cli-stage after CLI commands");

    state = "accepted";
    Console.WriteLine(FormattableString.Invariant(
        $"CLI140-B2C ACCEPT os={Environment.OSVersion.Platform} arch={System.Runtime.InteropServices.RuntimeInformation.OSArchitecture} large={LargeBytes} chunks={largeChunks} cases={results.Count}"));
}
catch (Exception exception)
{
    error = exception.ToString();
    Console.Error.WriteLine("CLI140-B2C REJECT: " + error);
}
finally
{
    var report = new
    {
        schemaVersion = 1,
        status = state,
        error,
        gitHead = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "local",
        os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
        limits = new
        {
            smallBytes = SmallBytes,
            largeBytes = LargeBytes,
            patchBytes = PatchBytes,
            gcHeapHardLimitBytes = HeapCap,
            rssCapBytes = RssCap,
            residentGrowthCapBytes = GrowthCap,
            stdoutCapBytes = MaximumOutputBytes,
            childTimeoutSeconds = 240,
        },
        measurements = results,
    };
    await File.WriteAllTextAsync(evidencePath, JsonSerializer.Serialize(
        report, new JsonSerializerOptions { WriteIndented = true }));
    try
    {
        Directory.Delete(workspace, recursive: true);
    }
    catch (IOException) { }
    catch (UnauthorizedAccessException) { }
}

return state == "accepted" ? 0 : 1;

static void AssertInspect(JsonDocument doc, string? expectedId, long expectedChunks)
{
    JsonElement root = doc.RootElement;
    Require(root.GetProperty("verification").GetProperty("valid").GetBoolean(),
        "manifest inspect reported invalid");
    JsonElement manifest = root.GetProperty("manifest");
    Require(manifest.GetProperty("manifestId").GetString() == expectedId,
        "manifest inspect identity mismatch");
    Require(manifest.GetProperty("chunks").GetInt64() == expectedChunks,
        "manifest inspect chunk mismatch");
}

static void AssertVerified(JsonDocument doc) =>
    Require(doc.RootElement.GetProperty("verification").GetProperty("valid").GetBoolean(),
        "manifest verify reported invalid");

static void CheckResidentGrowth(
    List<ProbeMeasurement> records, string lowLabel, string highLabel, long limit)
{
    long low = records.Single(x => x.Label == lowLabel).PeakResidentBytes;
    long high = records.Single(x => x.Label == highLabel).PeakResidentBytes;
    long delta = high - low;
    Require(delta <= limit,
        FormattableString.Invariant(
            $"{highLabel} resident growth {delta} > {limit} (low={low}, high={high})"));
}

static async Task<string> GenerateAsync(string path, long bytes, uint seed)
{
    var block = new byte[64 * 1024];
    uint state = seed;
    using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    await using var stream = new FileStream(
        path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
        bufferSize: 64 * 1024, FileOptions.Asynchronous);
    for (long offset = 0; offset < bytes;)
    {
        int count = (int)Math.Min(block.Length, bytes - offset);
        for (int i = 0; i < count; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            block[i] = (byte)state;
        }
        sha.AppendData(block, 0, count);
        await stream.WriteAsync(block.AsMemory(0, count));
        offset += count;
    }

    await stream.FlushAsync();
    return Convert.ToHexString(sha.GetHashAndReset());
}

static async Task<string> HashFileAsync(string path)
{
    await using var file = new FileStream(
        path, FileMode.Open, FileAccess.Read, FileShare.Read,
        bufferSize: 64 * 1024, FileOptions.Asynchronous);
    return Convert.ToHexString(await SHA256.HashDataAsync(file));
}

static void Require(bool valid, string message)
{
    if (!valid)
    {
        throw new InvalidOperationException("B2C resource gate: " + message);
    }
}

internal sealed record ProbeMeasurement(
    string Label, string Operation, long InputBytes,
    int ExitCode, int StdoutBytes, long PeakResidentBytes);

internal sealed class CliRunner(
    string cliAssembly, string workingDirectory, string heapLimit,
    long residentLimitBytes, int outputLimit,
    List<ProbeMeasurement> measurements)
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(240);

    internal async Task<JsonDocument> RunAsync(
        string label, string operation, long inputBytes, params string[] command)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        info.Environment["DOTNET_GCHeapHardLimit"] = heapLimit;
        info.Environment["DOTNET_NOLOGO"] = "1";
        info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        info.ArgumentList.Add(cliAssembly);
        foreach (string argument in command)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = info };
        Require(process.Start(), "Could not start the product CLI for " + label);
        process.StandardInput.Close();
        using var deadline = new CancellationTokenSource(CommandTimeout);

        byte[] stdout;
        byte[] stderr;
        long peak;
        try
        {
            Task<byte[]> outTask = ReadBoundedAsync(
                process.StandardOutput.BaseStream, outputLimit, deadline.Token);
            Task<byte[]> errTask = ReadBoundedAsync(
                process.StandardError.BaseStream, outputLimit, deadline.Token);
            Task finished = process.WaitForExitAsync(deadline.Token);
            Task<long> sampled = ObservePeakAsync(process, deadline.Token);
            Task[] pending = [outTask, errTask, finished, sampled];
            while (pending.Length > 0)
            {
                Task done = await Task.WhenAny(pending).WaitAsync(deadline.Token);
                await done;
                pending = pending.Where(x => !ReferenceEquals(done, x)).ToArray();
            }

            stdout = await outTask;
            stderr = await errTask;
            peak = await sampled;
            try
            {
                peak = Math.Max(peak, process.PeakWorkingSet64);
            }
            catch (InvalidOperationException)
            {
                // Sampled live peak remains a valid lower bound; never record 0.
            }
            catch (Win32Exception)
            {
                // OS high-water counter unavailable; live sampling was recorded.
            }
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            throw;
        }

        Require(peak > 0, label + " had no observable RSS samples");
        measurements.Add(new ProbeMeasurement(label, operation, inputBytes,
            process.ExitCode, stdout.Length, peak));
        Console.WriteLine(FormattableString.Invariant(
            $"{label} exit={process.ExitCode} rss={peak} stdout={stdout.Length}"));

        Require(peak <= residentLimitBytes,
            FormattableString.Invariant(
                $"{label} RSS {peak} exceeds {residentLimitBytes}"));
        Require(process.ExitCode == 0,
            FormattableString.Invariant(
                $"{label} CLI exit={process.ExitCode} stderr={Encoding.UTF8.GetString(stderr)}"));
        Require(stderr.Length == 0,
            label + " wrote to stderr: " + Encoding.UTF8.GetString(stderr));
        Require(stdout.Length > 1 && stdout[^1] == (byte)'\n',
            label + " result is not a newline-terminated JSON object");
        string message = new UTF8Encoding(false, true).GetString(stdout);
        JsonDocument document = JsonDocument.Parse(message);
        try
        {
            JsonElement root = document.RootElement;
            Require(root.ValueKind == JsonValueKind.Object,
                label + " emitted a non-object JSON result");
            Require(root.GetProperty("schemaVersion").GetInt32() == 1,
                label + " unexpected schemaVersion");
            Require(root.GetProperty("operation").GetString() == operation,
                label + " unexpected operation discriminator");
            Require(!root.TryGetProperty("error", out _),
                label + " success has an error envelope");
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream, int maximumBytes, CancellationToken cancellationToken)
    {
        using var sink = new MemoryStream();
        var buffer = new byte[1024];
        while (true)
        {
            int n = await stream.ReadAsync(buffer, cancellationToken);
            if (n == 0)
            {
                return sink.ToArray();
            }
            if (sink.Length + n > maximumBytes)
            {
                throw new InvalidDataException(
                    "CLI stdout/stderr exceeded the preregistered 4096-byte budget.");
            }
            sink.Write(buffer, 0, n);
        }
    }

    private static async Task<long> ObservePeakAsync(
        Process process, CancellationToken cancellationToken)
    {
        long max = 0;
        while (!process.HasExited)
        {
            try
            {
                process.Refresh();
                max = Math.Max(max, process.WorkingSet64);
                max = Math.Max(max, process.PeakWorkingSet64);
            }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
            if (process.HasExited)
            {
                break;
            }
            await Task.Delay(25, cancellationToken);
        }
        return max;
    }

    private static void Require(bool valid, string message)
    {
        if (!valid)
        {
            throw new InvalidOperationException("B2C resource gate: " + message);
        }
    }
}
