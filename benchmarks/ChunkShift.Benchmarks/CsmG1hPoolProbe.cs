using System.Buffers;
using System.Text.Json;

namespace ChunkShift.Benchmarks;

/// <summary>G1-H lab-only direct .NET bucket-capacity probe (never in timed loops).</summary>
internal static class CsmG1hPoolProbe
{
    internal static int Run(string[] args)
    {
        if (args is not ["--output", var output] || string.IsNullOrWhiteSpace(output))
        {
            throw new ArgumentException("G1-H requires --output <json>.");
        }

        const int requestBytes = 147_500;
        const int retainedPerBucket = 4;
        const int smallThresholdBytes = 80 * 1024;
        ArrayPool<byte> pool = ArrayPool<byte>.Create(
            maxArrayLength: requestBytes, maxArraysPerBucket: retainedPerBucket);

        byte[][] rented = new byte[retainedPerBucket][];
        for (int i = 0; i < rented.Length; i++)
        {
            rented[i] = pool.Rent(requestBytes);
        }

        int bucket = rented[0].Length;
        if (bucket < requestBytes)
        {
            throw new InvalidOperationException("ArrayPool returned a buffer smaller than requested.");
        }

        foreach (byte[] array in rented)
        {
            if (array.Length != bucket)
            {
                throw new InvalidOperationException("Unexpected inconsistent large pool bucket.");
            }

            pool.Return(array, clearArray: true);
        }

        string? parent = Path.GetDirectoryName(output);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var data = new
        {
            schema = "chunkshift.core011.g1h-pool-probe.v1",
            requestedBytes = requestBytes,
            rentedArrayLength = bucket,
            retentionPerBucket = retainedPerBucket,
            smallThresholdBytes,
            maximumRetainedPayloadBytes = (long)retainedPerBucket * bucket,
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            isDirectRentCapacityMeasurement = true,
            caveat = "Independent pool instance; actual reader-owned retention is measured separately after GC.",
        };

        File.WriteAllText(output, JsonSerializer.Serialize(data) + "\n");
        return 0;
    }
}
