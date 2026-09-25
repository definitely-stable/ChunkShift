using System.Globalization;
using System.Text;
using ChunkShift.Hashing;
using ChunkShift.Primitives;
using ChunkShift.Profiles;

namespace ChunkShift.Benchmarks.Lab.Prefreeze;

/// <summary>
/// Returns the chunk length at the start of <paramref name="source"/>. Looks at
/// no more than <see cref="PrefreezeCandidate.Maximum"/> bytes, so a window of
/// at least that many bytes gives the same answer as the whole remainder.
/// </summary>
internal delegate int CutFinder(ReadOnlySpan<byte> source);

/// <summary>
/// One (semantic candidate, nominal target) cell of the #99 comparison.
/// </summary>
internal sealed class PrefreezeCandidate
{
    internal const string Current = "current";
    internal const string WarmedPrefix = "warmed-prefix";
    internal const string Fixed = "fixed";

    internal static readonly string[] Names = [Current, WarmedPrefix, Fixed];

    private readonly CutFinder _findCut;

    private PrefreezeCandidate(
        string name,
        string algorithmId,
        string profileId,
        string profileFingerprint,
        int minimum,
        int target,
        int maximum,
        int transientPositions,
        CutFinder findCut)
    {
        Name = name;
        AlgorithmId = algorithmId;
        ProfileId = profileId;
        ProfileFingerprint = profileFingerprint;
        Minimum = minimum;
        Target = target;
        Maximum = maximum;
        TransientPositions = transientPositions;
        _findCut = findCut;
    }

    internal string Name { get; }

    internal string AlgorithmId { get; }

    internal string ProfileId { get; }

    internal string ProfileFingerprint { get; }

    internal int Minimum { get; }

    internal int Target { get; }

    internal int Maximum { get; }

    /// <summary>
    /// Tested positions after Minimum whose predicate is not a window function
    /// (see <see cref="GearCandidate.TransientPositions"/>); zero for fixed.
    /// </summary>
    internal int TransientPositions { get; }

    internal static PrefreezeCandidate Create(string name, int target)
    {
        switch (name)
        {
            case Current:
            case WarmedPrefix:
                GearCandidate gear = name == Current ? GearCandidate.Current(target) : GearCandidate.Warmed(target);
                return new PrefreezeCandidate(
                    name,
                    gear.AlgorithmId,
                    gear.ProfileId,
                    gear.ComputeFingerprint(),
                    gear.Minimum,
                    gear.Target,
                    gear.Maximum,
                    gear.TransientPositions,
                    source => GearCutters.FindCut(source, gear));

            case Fixed:
                // Same identity as the lab's fixed.reference.v1 control.
                string artifact = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{{\"semantics\":{{\"algorithm\":\"fixed\",\"version\":1,\"size\":{target}}}}}");
                return new PrefreezeCandidate(
                    name,
                    LabChunker.FixedAlgorithm,
                    string.Create(CultureInfo.InvariantCulture, $"fixed.v1.{target / 1024}k"),
                    ProfileFingerprintComputer.Compute(Encoding.UTF8.GetBytes(artifact)).ToString(),
                    target,
                    target,
                    target,
                    0,
                    source => Math.Min(source.Length, target));

            default:
                throw new InvalidOperationException($"Unknown pre-freeze candidate '{name}'.");
        }
    }

    internal int FindCut(ReadOnlySpan<byte> source) => _findCut(source);

    internal ChunkRecord[] Chunk(ReadOnlySpan<byte> data, HashSuiteId hashSuite)
    {
        var chunks = new List<ChunkRecord>(Math.Max(1, data.Length / Target + 2));
        int offset = 0;

        while (offset < data.Length)
        {
            ReadOnlySpan<byte> remaining = data[offset..];
            int length = FindCut(remaining);
            chunks.Add(new ChunkRecord(offset, length, HashSuiteHasher.Hash(hashSuite, remaining[..length])));
            offset += length;
        }

        return [.. chunks];
    }

    /// <summary>
    /// Chunks a stream with a bounded buffer of about two maxima: a cut is only
    /// looked for once a full maximum is buffered or the stream has ended, so
    /// the result equals <see cref="Chunk(ReadOnlySpan{byte}, HashSuiteId)"/>
    /// over the whole content for any read segmentation.
    /// </summary>
    internal ChunkRecord[] Chunk(Stream source, HashSuiteId hashSuite)
    {
        byte[] buffer = new byte[Math.Max(2 * Maximum, 1024 * 1024)];
        var chunks = new List<ChunkRecord>();
        int start = 0;
        int filled = 0;
        long offset = 0;
        bool ended = false;

        while (true)
        {
            while (!ended && filled - start < Maximum)
            {
                if (filled == buffer.Length)
                {
                    buffer.AsSpan(start, filled - start).CopyTo(buffer);
                    filled -= start;
                    start = 0;
                }

                int read = source.Read(buffer, filled, buffer.Length - filled);
                if (read == 0)
                {
                    ended = true;
                }
                else
                {
                    filled += read;
                }
            }

            if (start == filled)
            {
                return [.. chunks];
            }

            ReadOnlySpan<byte> window = buffer.AsSpan(start, filled - start);
            int length = FindCut(window);
            chunks.Add(new ChunkRecord(offset, length, HashSuiteHasher.Hash(hashSuite, window[..length])));
            start += length;
            offset += length;
        }
    }
}
