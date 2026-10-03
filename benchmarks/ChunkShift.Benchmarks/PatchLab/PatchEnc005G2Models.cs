using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChunkShift.Patching.Creation;

namespace ChunkShift.Benchmarks.PatchLab;

internal static class PatchEnc005G2Protocol
{
    internal const string ExperimentId = "PATCH-ENC-005";
    internal const string ProtocolCommit = "96fd9b296d6998cac397e61041f22df51e6dd43c";
    internal const string DatasetSha256 = "8b3b92a9d0fba4bee80602aeafbdd443e5c612ff94889621537b8fb910fd22dd";
    internal const string DatasetRole = "calibration";
    internal const string SampleSchema = "chunkshift.patch-g2-sample.v1";
    internal const string OracleSchema = "chunkshift.patch-g2-oracle.v1";
    internal const string Policy = "L19-K4-ALL-PREFIX-H20C20-REF32";
    internal const string CandidateOrder = "abs-offset-then-lower-index";
    internal const int SamplePerPair = 64;
    internal const int DictionaryChunks = 4;

    internal static bool IsCalibrationFamily(string family) =>
        family is "dotnet-aspnetcore-win-x64" or "dotnet-runtime-linux-arm64";

    internal static string SampleKeySha256(
        string family,
        string baseVersion,
        string targetVersion,
        string normalizedPath,
        string targetChunkIdHex,
        long firstTargetIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstTargetIndex);

        if (targetChunkIdHex.Length != 64 ||
            targetChunkIdHex.Any(static ch =>
                ch is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException(
                "targetChunkIdHex must be exactly 64 lowercase hexadecimal characters.",
                nameof(targetChunkIdHex));
        }

        string[] fields =
        [
            family,
            baseVersion,
            targetVersion,
            normalizedPath,
            targetChunkIdHex,
            firstTargetIndex.ToString(CultureInfo.InvariantCulture),
        ];

        byte[][] encoded = fields.Select(Encoding.UTF8.GetBytes).ToArray();
        int length = encoded.Sum(static bytes => bytes.Length) + encoded.Length - 1;
        byte[] preimage = new byte[length];
        int offset = 0;

        for (int index = 0; index < encoded.Length; index++)
        {
            if (index != 0)
            {
                preimage[offset++] = 0;
            }

            encoded[index].CopyTo(preimage, offset);
            offset += encoded[index].Length;
        }

        return Convert.ToHexStringLower(SHA256.HashData(preimage));
    }

    internal static IEnumerable<int> WholeBaseCandidateStarts(
        IReadOnlyList<CspPatchBuilder.BaseRecord> records,
        long targetOffset)
    {
        int right = FirstAtOrAbove(records, targetOffset);
        int left = right - 1;

        while (left >= 0 || right < records.Count)
        {
            bool takeLeft = left >= 0 &&
                (right >= records.Count ||
                 Distance(records[left].Offset, targetOffset) <=
                 Distance(records[right].Offset, targetOffset));

            yield return takeLeft ? left-- : right++;
        }
    }

    internal static byte[] CanonicalBytes<T>(T value) =>
        JsonSerializer.SerializeToUtf8Bytes(
            value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = false,
            });

    internal static string RowsSha256(PatchEnc005G2SampleRow[] rows) =>
        Convert.ToHexStringLower(SHA256.HashData(CanonicalBytes(rows)));

    private static int FirstAtOrAbove(
        IReadOnlyList<CspPatchBuilder.BaseRecord> records,
        long value)
    {
        int low = 0;
        int high = records.Count;

        while (low < high)
        {
            int middle = (low + high) >>> 1;

            if (records[middle].Offset < value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static long Distance(long left, long right) =>
        left >= right ? left - right : right - left;
}

internal sealed record PatchEnc005G2SampleDocument(
    string Schema,
    string ExperimentId,
    string ProtocolCommit,
    string SourceCommit,
    string DatasetRole,
    string DatasetSha256,
    string OracleSampleSha256,
    PatchEnc005G2SampleRow[] Rows);

internal sealed record PatchEnc005G2SampleRow(
    string Family,
    string BaseVersion,
    string TargetVersion,
    string Path,
    long TargetIndex,
    string TargetChunkId,
    long TargetOffset,
    int TargetLength,
    string SampleKeySha256);

internal sealed record PatchEnc005G2OracleDocument(
    string Schema,
    string ExperimentId,
    string RunId,
    string ProtocolCommit,
    string SourceCommit,
    string DatasetRole,
    string DatasetSha256,
    string OracleSampleSha256,
    string Policy,
    string CandidateOrder,
    PatchEnc005G2OracleRow[] Rows);

internal sealed record PatchEnc005G2OracleRow(
    string Family,
    string BaseVersion,
    string TargetVersion,
    string Path,
    long TargetIndex,
    string TargetChunkId,
    long TargetOffset,
    int TargetLength,
    int CandidateStartsEnumerated,
    int ValidCandidateCount,
    string H0Encoding,
    int H0StoredBytes,
    int H0DictionaryRefs,
    int H0CostBytes,
    int? H0StartIndex,
    long? H0StartOffset,
    int? H0RecordCount,
    string? H0FirstChunkId,
    string OracleEncoding,
    int OracleStoredBytes,
    int OracleDictionaryRefs,
    int OracleCostBytes,
    int? OracleStartIndex,
    long? OracleStartOffset,
    int? OracleRecordCount,
    string? OracleFirstChunkId,
    long? OracleStartDistanceBytes,
    int SavedBytes);

internal readonly record struct PatchEnc005G2Choice(
    string Encoding,
    int StoredBytes,
    int DictionaryRefs,
    int CostBytes,
    int? StartIndex,
    long? StartOffset,
    int? RecordCount,
    string? FirstChunkId);
