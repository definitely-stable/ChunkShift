using System.Globalization;
using ChunkShift.Patching.Creation;
using ChunkShift.Patching.Encoding;

namespace ChunkShift.Benchmarks.PatchLab;

/// <summary>
/// The CSP lanes of the patching pre-freeze protocol
/// (docs/benchmarks/PATCH-PREFREEZE-PROTOCOL.md section 2): the three named
/// policies and the 12 sweep settings; and the dictionary-memory lanes of
/// PATCH-ENC-003, <c>enc-L{level}-K{chunks}-C{8|16}-{copy|attach|prefix}</c>
/// with an optional <c>-H{hashLog}C{chainLog}</c> cap for dictionary entries.
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
            case "H4-L1-R2":
                policy = CspEncoderPolicy.Default with
                {
                    CandidateSelection = CspCandidateSelection.RankLevel1Top2,
                };
                return true;
            case "H7-L1-R2-E75":
                policy = CspEncoderPolicy.Default with
                {
                    CandidateSelection = CspCandidateSelection.RankLevel1Top2EarlyExit75,
                };
                return true;
            case "H9-L9-K4-C16-R1M":
                policy = PhaseAH9(9);
                return true;
            case "H9-L12-K4-C16-R1M":
                policy = PhaseAH9(12);
                return true;
            case "H9-L15-K4-C16-R1M":
                policy = PhaseAH9(15);
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

        if (parts.Length is 5 or 6 &&
            string.Equals(parts[0], "enc", StringComparison.Ordinal) &&
            TryLevel(parts[1], out level) &&
            TryDictionaryChunks(parts[2], out dictionaryChunks) &&
            TryCandidates(parts[3], out maxCandidates, out searchRadius) &&
            TryLoad(parts[4], out CspDictionaryLoad load))
        {
            int hashLog = 0;
            int chainLog = 0;

            if (parts.Length == 6 && !TryTableLogs(parts[5], out hashLog, out chainLog))
            {
                policy = CspEncoderPolicy.Default;
                return false;
            }

            policy = new CspEncoderPolicy(level, dictionaryChunks, maxCandidates, searchRadius)
            {
                DictionaryLoad = load,
                DictionaryHashLog = hashLog,
                DictionaryChainLog = chainLog,
            };
            return true;
        }

        policy = CspEncoderPolicy.Default;
        return false;
    }

    internal static CspEncoderPolicy Parse(string name) =>
        TryParse(name, out CspEncoderPolicy policy)
            ? policy
            : throw new PatchLabUsageException(
                $"Unknown lane '{name}'; expected one of: {string.Join(", ", Names)}, or enc-L{{9|19}}-K{{1|2|4}}-C{{8|16}}-{{copy|attach|prefix}}[-H{{n}}C{{n}}].");

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
        new(
            policy.Level,
            policy.DictionaryChunks,
            policy.MaxCandidates,
            policy.SearchRadius,
            LoadName(policy.DictionaryLoad),
            policy.DictionaryHashLog,
            policy.DictionaryChainLog,
            SelectionName(policy.CandidateSelection));

    private static string LoadName(CspDictionaryLoad load) => load switch
    {
        CspDictionaryLoad.Attach => "attach",
        CspDictionaryLoad.Prefix => "prefix",
        _ => "copy",
    };

    private static string SelectionName(CspCandidateSelection selection) => selection switch
    {
        CspCandidateSelection.RankLevel1Top2 => "l1-r2",
        CspCandidateSelection.RankLevel1Top2EarlyExit75 => "l1-r2-e75",
        _ => "exhaustive",
    };

    private static CspEncoderPolicy PhaseAH9(int level) =>
        new(level, DictionaryChunks: 4, MaxCandidates: 16, SearchRadius: WideRadius)
        {
            DictionaryLoad = CspDictionaryLoad.Prefix,
            DictionaryHashLog = 20,
            DictionaryChainLog = 20,
        };

    private static bool TryLoad(string part, out CspDictionaryLoad load)
    {
        switch (part)
        {
            case "copy":
                load = CspDictionaryLoad.Copy;
                return true;
            case "attach":
                load = CspDictionaryLoad.Attach;
                return true;
            case "prefix":
                load = CspDictionaryLoad.Prefix;
                return true;
            default:
                load = CspDictionaryLoad.Copy;
                return false;
        }
    }

    // H{hashLog}C{chainLog}, each 6..30.
    private static bool TryTableLogs(string part, out int hashLog, out int chainLog)
    {
        hashLog = 0;
        chainLog = 0;
        int chain = part.IndexOf('C', StringComparison.Ordinal);

        return part.StartsWith('H') &&
            chain > 1 &&
            int.TryParse(part.AsSpan(1, chain - 1), NumberStyles.None, CultureInfo.InvariantCulture, out hashLog) &&
            int.TryParse(part.AsSpan(chain + 1), NumberStyles.None, CultureInfo.InvariantCulture, out chainLog) &&
            hashLog is >= 6 and <= 30 &&
            chainLog is >= 6 and <= 30;
    }

    private static string[] CreateNames()
    {
        var names = new List<string>
        {
            "csp",
            "csp-zstd",
            "csp-raw",
            "H4-L1-R2",
            "H7-L1-R2-E75",
            "H9-L9-K4-C16-R1M",
            "H9-L12-K4-C16-R1M",
            "H9-L15-K4-C16-R1M",
        };

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
