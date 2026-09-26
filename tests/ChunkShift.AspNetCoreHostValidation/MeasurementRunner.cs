using System.Net.Http.Json;
using System.Text.Json;

namespace ChunkShift.AspNetCoreHostValidation;

internal static class MeasurementRunner
{
    internal static async Task RunAsync(
        Uri baseUri,
        string outputPath,
        long bytes,
        int runs,
        CancellationToken cancellationToken)
    {
        if (runs < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(runs));
        }

        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10),
        };

        // Warm both host-only adapters without recording a sample.
        _ = await PostAsync(
            client,
            baseUri,
            "body",
            new GeneratedContent(bytes, 0xB0D10001u),
            cancellationToken).ConfigureAwait(false);
        _ = await PostAsync(
            client,
            baseUri,
            "bodyreader",
            new GeneratedContent(bytes, 0xB0D10001u),
            cancellationToken).ConfigureAwait(false);

        var samples = new List<AdapterSample>(runs * 2);

        for (int run = 0; run < runs; run++)
        {
            uint seed = checked(0xB0D11000u + (uint)run);
            string[] order =
                run % 2 == 0
                    ? new[] { "body", "bodyreader" }
                    : new[] { "bodyreader", "body" };

            for (int position = 0; position < order.Length; position++)
            {
                string adapter = order[position];
                ScanEvidence evidence = await PostAsync(
                    client,
                    baseUri,
                    adapter,
                    new GeneratedContent(bytes, seed),
                    cancellationToken).ConfigureAwait(false);

                ValidationAssert.Equal(bytes, evidence.Bytes, $"{adapter} measurement bytes");
                samples.Add(
                    new AdapterSample(
                        run + 1,
                        position + 1,
                        adapter,
                        seed,
                        evidence));
            }

            AdapterSample body = samples.Single(
                sample => sample.Run == run + 1 && sample.Adapter == "body");
            AdapterSample bodyReader = samples.Single(
                sample => sample.Run == run + 1 && sample.Adapter == "bodyreader");

            ValidationAssert.Equal(
                body.Evidence.SequenceSha256,
                bodyReader.Evidence.SequenceSha256,
                $"run {run + 1} Body/BodyReader sequence digest");
        }

        object bodySummary = Summarize(
            samples.Where(sample => sample.Adapter == "body").ToArray());
        object bodyReaderSummary = Summarize(
            samples.Where(sample => sample.Adapter == "bodyreader").ToArray());

        var paired = new List<double>(runs);
        for (int run = 1; run <= runs; run++)
        {
            AdapterSample body = samples.Single(
                sample => sample.Run == run && sample.Adapter == "body");
            AdapterSample bodyReader = samples.Single(
                sample => sample.Run == run && sample.Adapter == "bodyreader");

            double bodyThroughput =
                BytesToGiB(body.Evidence.Bytes) / body.Evidence.ElapsedSeconds;
            double readerThroughput =
                BytesToGiB(bodyReader.Evidence.Bytes) / bodyReader.Evidence.ElapsedSeconds;
            paired.Add(readerThroughput / bodyThroughput);
        }

        var report = new
        {
            schemaVersion = 1,
            purpose = "#17 Body vs BodyReader host-only adapter measurement",
            utc = DateTimeOffset.UtcNow,
            bytesPerRequest = bytes,
            runsPerAdapter = runs,
            ordering = "alternating Body/BodyReader; same deterministic bytes within each paired run",
            statisticalUnit = "one real-Kestrel HTTP request; server-side metrics returned by the endpoint",
            interpretation = "measurement only; no automatic winner and no Core PipeReader API implication",
            body = bodySummary,
            bodyReader = bodyReaderSummary,
            pairedBodyReaderOverBodyThroughput = Statistics.Describe(paired),
            samples,
        };

        await ScenarioRunner.WriteJsonAsync(
            outputPath,
            report,
            cancellationToken).ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(report, ValidationJson.Options));
    }

    private static object Summarize(IReadOnlyList<AdapterSample> samples)
    {
        double gib = BytesToGiB(samples[0].Evidence.Bytes);

        double[] throughput = samples
            .Select(sample => gib / sample.Evidence.ElapsedSeconds)
            .ToArray();
        double[] cpuPerGiB = samples
            .Select(sample => sample.Evidence.CpuSeconds / gib)
            .ToArray();
        double[] allocationsPerGiB = samples
            .Select(sample => sample.Evidence.AllocatedBytes / gib)
            .ToArray();
        double[] firstChunk = samples
            .Select(sample => sample.Evidence.FirstChunkMilliseconds)
            .ToArray();
        double[] workingSet = samples
            .Select(sample => (double)sample.Evidence.WorkingSetBytes)
            .ToArray();
        double[] peak = samples
            .Select(sample => (double)sample.Evidence.PeakWorkingSetBytes)
            .ToArray();

        return new
        {
            throughputGiBPerSecond = Statistics.Describe(throughput),
            cpuSecondsPerGiB = Statistics.Describe(cpuPerGiB),
            allocatedBytesPerGiB = Statistics.Describe(allocationsPerGiB),
            firstChunkMilliseconds = Statistics.Describe(firstChunk),
            workingSetBytesAfterRequest = Statistics.Describe(workingSet),
            processPeakWorkingSetBytes = Statistics.Describe(peak),
            note = "Peak working set is process-lifetime monotonic and is retained only as a guardrail, not as a per-request independent measurement.",
        };
    }

    private static double BytesToGiB(long bytes) =>
        bytes / (double)(1L << 30);

    private static async Task<ScanEvidence> PostAsync(
        HttpClient client,
        Uri baseUri,
        string adapter,
        HttpContent content,
        CancellationToken cancellationToken)
    {
        using (content)
        using (HttpResponseMessage response = await client.PostAsync(
            new Uri(baseUri, $"scan?adapter={adapter}"),
            content,
            cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<ScanEvidence>(
                ValidationJson.Options,
                cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("Missing ScanEvidence response.");
        }
    }

    private sealed record AdapterSample(
        int Run,
        int Position,
        string Adapter,
        uint Seed,
        ScanEvidence Evidence);
}
