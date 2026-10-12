using System.Buffers;
using System.Text.Json;

namespace ChunkShift.Benchmarks;

/// <summary>G1-I direct .NET pooled-buffer bucket size probe; no timed use.</summary>
internal static class CsmG1iPoolProbe
{
    private sealed record Bucket(
        int Cap,
        int ReturnedLength,
        long TheoreticalMaxCachedPayloadBytes);

    internal static int Run(string[] args)
    {
        if (args is not ["--output", var output] || string.IsNullOrWhiteSpace(output))
        {
            throw new ArgumentException("G1-I requires --output <json>.");
        }

        const int request = 147_500;
        var observed = new List<Bucket>(3);
        foreach (int cap in new[] { 1, 2, 4 })
        {
            ArrayPool<byte> pool = ArrayPool<byte>.Create(
                maxArrayLength: request, maxArraysPerBucket: cap);
            var live = new byte[cap][];
            for (int i = 0; i < cap; i++)
            {
                live[i] = pool.Rent(request);
            }

            int length = live[0].Length;
            foreach (byte[] rented in live)
            {
                if (rented.Length != length || length < request)
                {
                    throw new InvalidOperationException("Unexpected pool bucket capacity.");
                }

                pool.Return(rented, clearArray: true);
            }

            observed.Add(new Bucket(cap, length, checked((long)cap * length)));
        }

        string? parent = Path.GetDirectoryName(output);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var result = new
        {
            Schema = "chunkshift.core011.g1i-bucket-probe.v1",
            RequestedBytes = request,
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Buckets = observed,
            Caveat = "Measured on separate pool instances, not private reader pools; cap is per bucket.",
        };

        File.WriteAllText(output, JsonSerializer.Serialize(
            result, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) + "\n");
        return 0;
    }
}
