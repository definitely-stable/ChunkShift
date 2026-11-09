using System.Security.Cryptography;
using System.Text;
using ChunkShift.Patching.Encoding;
using ChunkShift.Patching.Format;

namespace ChunkShift.Benchmarks.PatchLab.Dictionary;

/// <summary>
/// D1-A lab-only physical PAYL-entry/PIDX cost screen. This is neither a full
/// CSP artifact nor a real-corpus performance/adoption result.
/// </summary>
internal static class PatchDictD1a
{
    internal const int MaxBaseChunks = 64;
    internal const int MaxChunkBytes = 256 * 1024;
    internal const int MaxSourceBytes = 8 * 1024 * 1024;
    internal const int MaxDictionaryBytes = 1024 * 1024;
    internal const int MaxCandidateRuns = 8;
    internal const int MaxReferences = 4;
    internal const int PaylEntryHeaderBytes = 40;
    internal const int PidxEntryBytes = 24;
    internal const int ReferenceBytes = 32;

    internal readonly record struct Run(int Start, int Count)
    {
        internal int End => checked(Start + Count);
    }

    internal sealed record Observation(
        string Kind,
        string RunOrdinals,
        int RunCount,
        int ReferenceCount,
        int DictionaryBytes,
        int StoredBytes,
        long EntryAndPidxBytes,
        string FrameSha256);

    internal sealed record Result(
        string ExperimentId,
        string EvidenceClass,
        string TargetSha256,
        int TargetBytes,
        int DictionaryBudgetBytes,
        int EligibleCandidatePlans,
        int SkippedBudgetPlans,
        int SkippedUnstorableFrames,
        Observation Raw,
        Observation PlainZstd,
        Observation BestContiguous,
        Observation? BestComposite,
        Observation BestAnyEntry,
        Observation[] DictionaryObservations);

    internal static Result Evaluate(
        IReadOnlyList<byte[]> baseChunks,
        IReadOnlyList<string> verifiedBaseSha256,
        byte[] target,
        IReadOnlyList<Run> proposedRuns,
        int dictionaryBudgetBytes = MaxDictionaryBytes)
    {
        ArgumentNullException.ThrowIfNull(baseChunks);
        ArgumentNullException.ThrowIfNull(verifiedBaseSha256);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(proposedRuns);

        if (baseChunks.Count is < 1 or > MaxBaseChunks ||
            verifiedBaseSha256.Count != baseChunks.Count)
        {
            throw new ArgumentException("D1-A requires 1..64 bound base occurrences.");
        }

        if (target.Length is < 1 or > MaxChunkBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }

        if (dictionaryBudgetBytes is < 1 or > MaxDictionaryBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(dictionaryBudgetBytes));
        }

        if (proposedRuns.Count is < 1 or > MaxCandidateRuns)
        {
            throw new ArgumentException("D1-A candidate pool must contain 1..8 runs.");
        }

        // An immutable snapshot of the authoritative single-file byte sequence:
        // no physical base offsets or unverified advisory features enter the trial.
        byte[][] data = new byte[baseChunks.Count][];
        long sourceBytes = 0;
        for (int i = 0; i < baseChunks.Count; i++)
        {
            byte[]? chunk = baseChunks[i];
            if (chunk is null || chunk.Length is < 1 or > MaxChunkBytes)
            {
                throw new InvalidDataException("Invalid base chunk length.");
            }

            sourceBytes = checked(sourceBytes + chunk.Length);
            if (sourceBytes > MaxSourceBytes)
            {
                throw new InvalidDataException("D1-A source exceeds 8 MiB.");
            }

            if (!string.Equals(Sha256(chunk), verifiedBaseSha256[i], StringComparison.Ordinal))
            {
                throw new InvalidDataException("Verified base occurrence SHA-256 mismatch.");
            }

            data[i] = (byte[])chunk.Clone();
        }

        Run[] pool = [.. proposedRuns.OrderBy(x => x.Start).ThenBy(x => x.Count)];
        for (int i = 0; i < pool.Length; i++)
        {
            Run run = pool[i];
            if (run.Start < 0 || run.Count < 1 ||
                run.Count > MaxReferences || run.Start > data.Length - run.Count)
            {
                throw new InvalidDataException("Invalid candidate run.");
            }

            if (i > 0 && pool[i - 1] == run)
            {
                throw new InvalidDataException("Duplicate candidate run.");
            }
        }

        string targetHash = Sha256(target);
        var candidates = new List<Observation>();
        int budgetSkipped = 0;
        int unstorable = 0;

        using var encoder = new CspPayloadEncoder(
            19, CspDictionaryLoad.Prefix, dictionaryHashLog: 20, dictionaryChainLog: 20);
        using var decoder = new CspPayloadDecoder();

        Observation raw = new(
            "raw", "", 0, 0, 0, target.Length,
            checked(PaylEntryHeaderBytes + PidxEntryBytes + (long)target.Length), "");
        byte[] noDictionaryFrame = encoder.EncodeZstd(target, []).ToArray();
        VerifyFrame(decoder, noDictionaryFrame, [], target, targetHash);
        Observation plain = new(
            "zstd-no-dictionary", "", 0, 0, 0, noDictionaryFrame.Length,
            checked(PaylEntryHeaderBytes + PidxEntryBytes + (long)noDictionaryFrame.Length),
            Sha256(noDictionaryFrame));

        // <=8 proposals => <=255 subsets, before overlap/ref/budget gates.
        for (int mask = 1; mask < (1 << pool.Length); mask++)
        {
            var selected = new List<Run>();
            int references = 0;
            bool canonical = true;
            for (int i = 0; i < pool.Length; i++)
            {
                if ((mask & (1 << i)) == 0)
                {
                    continue;
                }

                Run run = pool[i];
                if (selected.Count != 0 && selected[^1].End >= run.Start)
                {
                    canonical = false;
                    break; // adjacent run pairs must be merged into one run
                }

                references += run.Count;
                if (references > MaxReferences)
                {
                    canonical = false;
                    break;
                }

                selected.Add(run);
            }

            if (!canonical)
            {
                continue;
            }

            int dictionaryBytes = 0;
            foreach (Run run in selected)
            {
                for (int j = run.Start; j < run.End; j++)
                {
                    dictionaryBytes = checked(dictionaryBytes + data[j].Length);
                }
            }

            if (dictionaryBytes > dictionaryBudgetBytes)
            {
                budgetSkipped++;
                continue;
            }

            byte[] dictionary = new byte[dictionaryBytes];
            int at = 0;
            foreach (Run run in selected)
            {
                for (int j = run.Start; j < run.End; j++)
                {
                    // Explicit re-verification at the read/materialization boundary.
                    if (Sha256(data[j]) != verifiedBaseSha256[j])
                    {
                        throw new InvalidDataException("Dictionary base SHA-256 mismatch.");
                    }

                    data[j].CopyTo(dictionary, at);
                    at += data[j].Length;
                }
            }

            if (dictionary.AsSpan().StartsWith(CspFormat.ZstdDictionaryMagic))
            {
                budgetSkipped++;
                continue;
            }

            byte[] frame = encoder.EncodeZstd(target, dictionary).ToArray();
            VerifyFrame(decoder, frame, dictionary, target, targetHash);
            // CSP-V1-CANDIDATE: StoredLength cannot exceed target chunk length.
            if (frame.Length > target.Length)
            {
                unstorable++;
                continue;
            }
            string canonicalRuns = string.Join(";", selected.Select(r => $"{r.Start:D2}+{r.Count:D2}"));
            candidates.Add(new Observation(
                "zstd-verified-dictionary",
                canonicalRuns,
                selected.Count,
                references,
                dictionaryBytes,
                frame.Length,
                checked(PaylEntryHeaderBytes + PidxEntryBytes +
                        (long)ReferenceBytes * references + frame.Length),
                Sha256(frame)));
        }

        Observation? contiguous = Rank(candidates.Where(c => c.RunCount == 1)).FirstOrDefault();
        if (contiguous is null)
        {
            throw new InvalidDataException("No eligible same-budget single-run comparator.");
        }

        Observation? composite = Rank(candidates.Where(c => c.RunCount > 1)).FirstOrDefault();
        Observation best = Rank(candidates.Append(raw).Append(plain)).First();

        return new Result(
            "PATCH-DICT-001",
            "D1A_SYNTHETIC_EXACT_FRAME_AND_ENTRY_COST_ONLY_NOT_FULL_CSP",
            targetHash,
            target.Length,
            dictionaryBudgetBytes,
            candidates.Count,
            budgetSkipped,
            unstorable,
            raw,
            plain,
            contiguous,
            composite,
            best,
            [.. candidates]);
    }

    private static IOrderedEnumerable<Observation> Rank(IEnumerable<Observation> values) =>
        values.OrderBy(o => o.EntryAndPidxBytes)
            .ThenBy(o => o.ReferenceCount)
            .ThenBy(o => o.RunCount)
            .ThenBy(o => o.RunOrdinals, StringComparer.Ordinal);

    /// <summary>Lab-only test seam; no production library friend-assembly expansion.</summary>
    internal static byte[] EncodeFrameForTests(byte[] target, byte[] dictionary)
    {
        using var encoder = new CspPayloadEncoder(
            19, CspDictionaryLoad.Prefix, dictionaryHashLog: 20, dictionaryChainLog: 20);
        return encoder.EncodeZstd(target, dictionary).ToArray();
    }

    internal static void VerifyFrameForTests(byte[] frame, byte[] dictionary, byte[] target)
    {
        using var decoder = new CspPayloadDecoder();
        VerifyFrame(decoder, frame, dictionary, target, Sha256(target));
    }

    private static void VerifyFrame(
        CspPayloadDecoder decoder,
        byte[] frame,
        byte[] dictionary,
        byte[] target,
        string targetSha256)
    {
        byte[] decoded = new byte[target.Length];
        decoder.Decode(CspFormat.EncodingZstd, frame, dictionary, decoded);
        if (Sha256(decoded) != targetSha256 ||
            !CryptographicOperations.FixedTimeEquals(decoded, target))
        {
            throw new InvalidDataException("D1-A frame did not reproduce exact target.");
        }
    }

    internal static string Sha256(ReadOnlySpan<byte> data) =>
        Convert.ToHexStringLower(SHA256.HashData(data));

    internal static (byte[][] Base, string[] Ids, byte[] Target, Run[] Pool, int Budget)
        SyntheticFixture()
    {
        byte[][] chunks = new byte[6][];
        for (int i = 0; i < chunks.Length; i++)
        {
            chunks[i] = new byte[8192];
            for (int j = 0; j < 8192 / 32; j++)
            {
                byte[] block = SHA256.HashData(
                    Encoding.ASCII.GetBytes($"PATCH-DICT-001/D1A/{i}/{j}"));
                block.CopyTo(chunks[i], j * 32);
            }
        }

        byte[] target = new byte[8192];
        chunks[0].AsSpan(0, 4096).CopyTo(target);
        chunks[3].AsSpan(4096, 4096).CopyTo(target.AsSpan(4096));
        Run[] pool =
        [
            new(0, 1), new(1, 1), new(3, 1), new(5, 1),
            new(0, 2), new(3, 2),
        ];

        return (chunks, [.. chunks.Select(chunk => Sha256(chunk))], target, pool, 16 * 1024);
    }

    internal static int Execute(string[] args)
    {
        if (args.Length != 2 || args[0] != "--output" || string.IsNullOrWhiteSpace(args[1]))
        {
            throw new PatchLabUsageException("patch-lab dict-d1a requires --output <json>.");
        }

        var f = SyntheticFixture();
        Result result = Evaluate(f.Base, f.Ids, f.Target, f.Pool, f.Budget);
        PatchLabRunner.WriteJson(args[1], result);
        return 0;
    }
}
