using System.Globalization;
using ChunkShift.Patching.Creation;

namespace ChunkShift.Benchmarks.PatchLab;

/// <summary>
/// The CSP lanes of the patching pre-freeze protocol
/// (docs/benchmarks/PATCH-PREFREEZE-PROTOCOL.md section 2): the three named
/// policies and the 12 sweep settings.
/// </summary>
internal static class PatchLabLane
{
    private const int CandidateRadius = 256 * 1024;
    private const int WideRadius = 1024 * 1024;

    /// <summary>Gets every lane name, in protocol order.</summary>
    internal static IReadOnlyList<string> Names { get; } = CreateNames();

    internal static bool TryParse(string name, out CspEncoderPolicy policy)
    {
        switch (name)
        {
            case "csp":
                policy = CspEncoderPolicy.Default;
                return true;
            case "csp-zstd":
                policy = CspEncoderPolicy.Default with { DictionaryChunks = 0 };
                return true;
            case "csp-raw":
                policy = CspEncoderPolicy.Default with { Level = 0 };
                return true;
        }

        string[] parts = name.Split('-');

        if (parts.Length == 4 &&
            string.Equals(parts[0], "sweep", StringComparison.Ordinal) &&
            TryLevel(parts[1], out int level) &&
            TryDictionaryChunks(parts[2], out int dictionaryChunks) &&
            TryCandidates(parts[3], out int maxCandidates, out int searchRadius))
        {
            policy = new CspEncoderPolicy(level, dictionaryChunks, maxCandidates, searchRadius);
            return true;
        }

        policy = CspEncoderPolicy.Default;
        return false;
    }

    internal static CspEncoderPolicy Parse(string name) =>
        TryParse(name, out CspEncoderPolicy policy)
            ? policy
            : throw new PatchLabUsageException(
                $"Unknown lane '{name}'; expected one of: {string.Join(", ", Names)}.");

    /// <summary>
    /// Returns the effective settings of a lane as the lab records them. This
    /// is the seam callers outside the Patching package use, because
    /// <see cref="CspEncoderPolicy"/> is internal to that package.
    /// </summary>
    internal static bool TryDescribe(string name, out PatchLabPolicy policy)
    {
        if (!TryParse(name, out CspEncoderPolicy parsed))
        {
            policy = new PatchLabPolicy(0, 0, 0, 0);
            return false;
        }

        policy = Describe(parsed);
        return true;
    }

    /// <summary>Records the effective settings of one policy.</summary>
    internal static PatchLabPolicy Describe(CspEncoderPolicy policy) =>
        new(policy.Level, policy.DictionaryChunks, policy.MaxCandidates, policy.SearchRadius);

    private static string[] CreateNames()
    {
        var names = new List<string> { "csp", "csp-zstd", "csp-raw" };

        foreach (int level in (int[])[9, 19])
        {
            foreach (int chunks in (int[])[1, 2, 4])
            {
                names.Add($"sweep-L{level}-K{chunks}-C8");
                names.Add($"sweep-L{level}-K{chunks}-C16");
            }
        }

        return [.. names];
    }

    private static bool TryLevel(string part, out int level)
    {
        if (part is "L9" or "L19")
        {
            level = int.Parse(part.AsSpan(1), CultureInfo.InvariantCulture);
            return true;
        }

        level = 0;
        return false;
    }

    private static bool TryDictionaryChunks(string part, out int chunks)
    {
        if (part is "K1" or "K2" or "K4")
        {
            chunks = int.Parse(part.AsSpan(1), CultureInfo.InvariantCulture);
            return true;
        }

        chunks = 0;
        return false;
    }

    private static bool TryCandidates(string part, out int maxCandidates, out int searchRadius)
    {
        switch (part)
        {
            case "C8":
                maxCandidates = 8;
                searchRadius = CandidateRadius;
                return true;
            case "C16":
                maxCandidates = 16;
                searchRadius = WideRadius;
                return true;
            default:
                maxCandidates = 0;
                searchRadius = 0;
                return false;
        }
    }
}
