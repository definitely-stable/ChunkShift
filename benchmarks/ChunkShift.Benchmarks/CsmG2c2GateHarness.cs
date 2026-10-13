using System.Reflection;
using ChunkShift.Chunking;
using ChunkShift.Manifest;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks;

/// <summary>
/// G2-C2 non-timing oracle executed against each genuine compiled Core variant.
/// Checks the lazy owned staging array is absent through chunk 256 and appears
/// exactly on the 257th successful record, without changing production API.
/// </summary>
internal static class CsmG2c2GateHarness
{
    internal static int Run(string[] args)
    {
        if (args is not [string variant] ||
            variant is not ("baseline" or "gate256-stage64" or "gate256-stage256"))
        {
            throw new ArgumentException("Expected one preregistered G2-C2 build variant.");
        }

        FieldInfo? field = typeof(ManifestIdAccumulator).GetField(
            "_g2c2Buffer", BindingFlags.Instance | BindingFlags.NonPublic);
        int expectedBytes = variant switch
        {
            "baseline" => 0,
            "gate256-stage64" => 64 * 36,
            "gate256-stage256" => 256 * 36,
            _ => throw new InvalidOperationException("Unknown variant."),
        };
        if ((field is null) != (expectedBytes == 0))
        {
            throw new InvalidOperationException("Compiled staging field does not match source variant.");
        }
        ChunkScanConfiguration.ProfileRegistration profile =
            ChunkScanConfiguration.ResolveProfileRegistration(null);

        foreach (HashSuiteId suite in new[] { HashSuiteIds.Blake3256V1, HashSuiteIds.Sha256V1 })
        {
            byte[] bytes = new byte[32];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i * 7 + 3);
            var id = new ChunkId(Hash256.FromBytes(bytes));
            using var accumulator = new ManifestIdAccumulator(suite, profile.Id, profile.Fingerprint);
            AssertLength(0);
            for (int i = 1; i <= 256; i++)
            {
                accumulator.Append(id, (uint)(i + 1));
                if (i is 1 or 64 or 255 or 256)
                {
                    AssertLength(0);
                }
            }
            try
            {
                accumulator.Append(id, 0u);
                throw new InvalidOperationException("Invalid zero chunk length accepted.");
            }
            catch (ArgumentOutOfRangeException)
            {
                AssertLength(0);
            }
            accumulator.Append(id, 258u); // 257th valid record, first eligible staged record
            AssertLength(expectedBytes);
            for (int i = 258; i <= 5052; i++)
            {
                accumulator.Append(id, 1u);
            }
            if (accumulator.ChunkCount != 5052UL || accumulator.ContentLength != 5052UL + (257UL * 258UL / 2) + 1UL)
            {
                // Lengths of first 256 records are 2..257; 257th is 258;
                // remaining 4795 records have length 1.
                long correct = Enumerable.Range(1, 256).Sum(x => x + 1L)
                    + 258 + (5052 - 257);
                if (accumulator.ContentLength != (ulong)correct)
                {
                    throw new InvalidOperationException("G2-C2 valid logical totals mismatch.");
                }
            }
            _ = accumulator.Complete();
            AssertLength(expectedBytes);
            Console.WriteLine("G2-C2 lazy owned array verified " + suite.Value
                + " " + variant + " bytes=" + expectedBytes);

            void AssertLength(int expected)
            {
                if (field is null)
                {
                    if (expected != 0) throw new InvalidOperationException("Unexpected baseline stage length.");
                    return;
                }
                byte[]? array = (byte[]?)field.GetValue(accumulator);
                if ((array?.Length ?? 0) != expected)
                {
                    throw new InvalidOperationException("G2-C2 stage buffer allocated before gate or wrong capacity.");
                }
            }
        }
        return 0;
    }
}
