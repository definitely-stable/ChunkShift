using ChunkShift.Patching.Creation;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.PatchLab.PatchGap;

internal sealed record PatchGapG4TrialCost(
    string StoredForm,
    byte Encoding,
    int CandidateOrdinal,
    int CandidateStartIndex,
    int CanonicalStartIndex,
    long StoredBytes,
    int DictionaryReferences)
{
    internal long CostBytes =>
        checked(StoredBytes + ((long)DictionaryReferences * PatchGapG1Model.DictionaryReferenceBytes));
}

internal sealed record PatchGapG4Winner(
    string StoredForm,
    byte Encoding,
    int CandidateOrdinal,
    int CandidateStartIndex,
    int CanonicalStartIndex,
    long StoredBytes,
    int DictionaryReferences,
    long CostBytes);

internal static class PatchGapG4Model
{
    internal const byte EncodingX86 = 2;
    internal const byte EncodingArm64 = 3;

    internal static int CanonicalSequenceStart(
        IReadOnlyList<CspPatchBuilder.BaseRecord> records,
        int candidateStart,
        int recordCount)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (candidateStart < 0 ||
            recordCount <= 0 ||
            candidateStart > records.Count - recordCount)
        {
            throw new InvalidDataException("PATCH-GAP G4 H0 dictionary range is invalid.");
        }

        for (int start = 0; start <= records.Count - recordCount; start++)
        {
            bool equal = true;
            for (int offset = 0; offset < recordCount; offset++)
            {
                CspPatchBuilder.BaseRecord expected = records[candidateStart + offset];
                CspPatchBuilder.BaseRecord actual = records[start + offset];
                if (actual.ChunkId != expected.ChunkId || actual.Length != expected.Length)
                {
                    equal = false;
                    break;
                }
            }

            if (equal)
            {
                return start;
            }
        }

        throw new InvalidDataException(
            "PATCH-GAP G4 could not find the H0 dictionary sequence in the base manifest.");
    }

    internal static ChunkId[] DictionaryIds(
        IReadOnlyList<CspPatchBuilder.BaseRecord> records,
        int start,
        int count)
    {
        if (start < 0 || count <= 0 || start > records.Count - count)
        {
            throw new InvalidDataException("PATCH-GAP G4 dictionary range is invalid.");
        }

        return [.. records.Skip(start).Take(count).Select(static record => record.ChunkId)];
    }

    internal static PatchGapG4Winner ChooseWinner(
        long h0StoredBytes,
        int h0DictionaryReferences,
        IEnumerable<PatchGapG4TrialCost> trials)
    {
        if (h0StoredBytes <= 0 || h0DictionaryReferences < 0)
        {
            throw new InvalidDataException("PATCH-GAP G4 preserved H0 cost is invalid.");
        }

        long h0Cost = checked(
            h0StoredBytes +
            ((long)h0DictionaryReferences * PatchGapG1Model.DictionaryReferenceBytes));

        var winner = new PatchGapG4Winner(
            "H0",
            encoding: 0,
            CandidateOrdinal: -1,
            CandidateStartIndex: -1,
            CanonicalStartIndex: -1,
            h0StoredBytes,
            h0DictionaryReferences,
            h0Cost);

        foreach (PatchGapG4TrialCost trial in trials)
        {
            if (trial.StoredBytes <= 0 ||
                trial.DictionaryReferences < 0 ||
                trial.CostBytes <= 0)
            {
                throw new InvalidDataException("PATCH-GAP G4 trial cost is invalid.");
            }

            if (trial.CostBytes < winner.CostBytes)
            {
                winner = new PatchGapG4Winner(
                    trial.StoredForm,
                    trial.Encoding,
                    trial.CandidateOrdinal,
                    trial.CandidateStartIndex,
                    trial.CanonicalStartIndex,
                    trial.StoredBytes,
                    trial.DictionaryReferences,
                    trial.CostBytes);
            }
        }

        return winner;
    }

    internal static long PhysicalPatchBytes(
        long h0PatchBytes,
        IEnumerable<(long H0CostBytes, long WinnerCostBytes)> entries)
    {
        long removed = 0;
        long added = 0;

        foreach ((long h0, long winner) in entries)
        {
            if (h0 <= 0 || winner <= 0 || winner > h0)
            {
                throw new InvalidDataException(
                    "PATCH-GAP G4 violates the mandatory additive non-regression oracle.");
            }

            removed = checked(removed + h0);
            added = checked(added + winner);
        }

        return checked(h0PatchBytes - removed + added);
    }

    internal static byte EncodingFor(PatchGapExecutableArchitecture architecture) =>
        architecture switch
        {
            PatchGapExecutableArchitecture.X86 or PatchGapExecutableArchitecture.X64 => EncodingX86,
            PatchGapExecutableArchitecture.Arm64 => EncodingArm64,
            _ => throw new InvalidDataException(
                $"PATCH-GAP G4 has no synthetic encoding for {architecture}."),
        };
}
