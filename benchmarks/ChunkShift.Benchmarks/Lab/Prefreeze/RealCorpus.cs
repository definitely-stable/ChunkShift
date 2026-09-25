using System.Security.Cryptography;

namespace ChunkShift.Benchmarks.Lab.Prefreeze;

/// <summary>
/// Validation and transition enumeration for a local <see cref="RealCorpusManifest"/>.
/// </summary>
internal static class RealCorpus
{
    internal const string Calibration = "calibration";
    internal const string Holdout = "holdout";

    // #99 C: prefer at least five consecutive versions per family.
    internal const int PreferredVersions = 5;

    // Skipped transitions vN -> vN+2 and vN -> vN+3 where the history allows.
    internal const int MaximumStride = 3;

    internal readonly record struct Transition(RealCorpusFamily Family, int From, int To)
    {
        internal int Stride => To - From;

        internal string Id => $"{Family.Id}:{Family.Versions[From].Version}->{Family.Versions[To].Version}";
    }

    /// <summary>
    /// Rejects a manifest whose split, provenance or digests are incomplete,
    /// then verifies every payload's size and SHA-256 against the manifest.
    /// </summary>
    internal static RealCorpusSummary Validate(RealCorpusManifest manifest, string baseDirectory)
    {
        if (manifest.SchemaVersion != 1)
        {
            throw new InvalidOperationException("Unsupported real-corpus manifest schema.");
        }

        if (manifest.Families.Length == 0)
        {
            throw new InvalidOperationException("Real-corpus manifest lists no families.");
        }

        if (manifest.Families.Any(static family => string.IsNullOrWhiteSpace(family.Id)))
        {
            throw new InvalidOperationException("Every real-corpus family needs a non-empty id.");
        }

        if (manifest.Families.DistinctBy(static family => family.Id).Count() != manifest.Families.Length)
        {
            throw new InvalidOperationException("Real-corpus family ids must be unique.");
        }

        foreach (RealCorpusFamily family in manifest.Families)
        {
            if (family.Split is not (Calibration or Holdout))
            {
                throw new InvalidOperationException(
                    $"Family '{family.Id}' has split '{family.Split}'; assign '{Calibration}' or '{Holdout}' before running.");
            }

            if (string.IsNullOrWhiteSpace(family.Provenance) ||
                string.IsNullOrWhiteSpace(family.License) ||
                string.IsNullOrWhiteSpace(family.RetrievedUtc) ||
                string.IsNullOrWhiteSpace(family.Category))
            {
                throw new InvalidOperationException(
                    $"Family '{family.Id}' must record category, provenance, license and retrieval date.");
            }

            if (family.Versions.Length < 2)
            {
                throw new InvalidOperationException($"Family '{family.Id}' needs at least two versions.");
            }

            // Version labels form the transition and evidence ids, so they must
            // be unambiguous; digests must be canonical before any comparison.
            if (family.Versions.Any(static version => string.IsNullOrWhiteSpace(version.Version)) ||
                family.Versions.DistinctBy(static version => version.Version).Count() != family.Versions.Length)
            {
                throw new InvalidOperationException($"Family '{family.Id}' needs non-empty, unique version labels.");
            }

            foreach (RealCorpusVersion version in family.Versions)
            {
                if (string.IsNullOrWhiteSpace(version.Path))
                {
                    throw new InvalidOperationException($"Family '{family.Id}' version '{version.Version}' has no path.");
                }

                if (!IsCanonicalSha256(version.Sha256))
                {
                    throw new InvalidOperationException(
                        $"Family '{family.Id}' version '{version.Version}': SHA-256 must be 64 lowercase hex characters.");
                }

                string path = Resolve(baseDirectory, version.Path);
                var info = new FileInfo(path);

                if (!info.Exists || info.Length != version.SizeBytes)
                {
                    throw new InvalidOperationException(
                        $"Family '{family.Id}' version '{version.Version}': '{path}' is missing or not {version.SizeBytes} bytes.");
                }

                using FileStream stream = info.OpenRead();
                string actual = Convert.ToHexStringLower(SHA256.HashData(stream));

                if (!string.Equals(actual, version.Sha256, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Family '{family.Id}' version '{version.Version}': SHA-256 {actual} does not match the manifest.");
                }
            }
        }

        int holdout = manifest.Families.Count(static family => family.Split == Holdout);
        int eligibleCalibration = manifest.Families.Count(static family => family.Split == Calibration && IsSelectionEligible(family));
        int eligibleHoldout = manifest.Families.Count(static family => family.Split == Holdout && IsSelectionEligible(family));
        var warnings = new List<string>();

        if (holdout == 0)
        {
            warnings.Add("no holdout family: these results can calibrate candidates but cannot select the stable profile");
        }
        else if (eligibleHoldout == 0)
        {
            warnings.Add("no selection-eligible holdout family (all holdout families are pair-only): the stable profile cannot be selected from this run");
        }

        if (holdout == manifest.Families.Length)
        {
            warnings.Add("no calibration family: calibrate candidates on a separate family before reading the holdout");
        }
        else if (eligibleCalibration == 0)
        {
            warnings.Add("no selection-eligible calibration family (all calibration families are pair-only)");
        }

        string[] pairOnly = manifest.Families.Where(static family => family.Versions.Length == 2).Select(static family => family.Id).ToArray();
        string[] shortHistory = manifest.Families
            .Where(static family => family.Versions.Length is > 2 and < PreferredVersions)
            .Select(static family => family.Id)
            .ToArray();

        if (pairOnly.Length > 0)
        {
            warnings.Add("pair-only families are labelled and must not alone select the stable profile: " + string.Join(", ", pairOnly));
        }

        return new RealCorpusSummary(
            manifest.Families.Length,
            manifest.Families.Length - holdout,
            holdout,
            eligibleCalibration,
            eligibleHoldout,
            eligibleHoldout > 0,
            pairOnly,
            shortHistory,
            [.. warnings]);
    }

    /// <summary>Adjacent transitions, then skipped ones up to <see cref="MaximumStride"/>.</summary>
    internal static IEnumerable<Transition> Transitions(RealCorpusFamily family)
    {
        for (int stride = 1; stride <= MaximumStride; stride++)
        {
            for (int from = 0; from + stride < family.Versions.Length; from++)
            {
                yield return new Transition(family, from, from + stride);
            }
        }
    }

    /// <summary>A pair-only family can support or contradict, never select (protocol §4.3).</summary>
    internal static bool IsSelectionEligible(RealCorpusFamily family) => family.Versions.Length > 2;

    private static bool IsCanonicalSha256(string? value) =>
        value is { Length: 64 } && value.All(static c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    internal static string Resolve(string baseDirectory, string path) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(baseDirectory, path));
}
