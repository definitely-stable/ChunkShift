using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ChunkShift.Benchmarks.Lab;

public static class ExperimentFingerprint
{
    public static string Compute(ExperimentDefinition definition, CorpusEntry corpus)
    {
        MutationDefinition? mutation = definition.Mutation;
        string canonical = string.Join(
            "\n",
            "chunkshift.lab.experiment.v2",
            definition.Id,
            definition.CorpusId,
            corpus.Generator,
            corpus.GeneratorVersion.ToString(CultureInfo.InvariantCulture),
            corpus.SizeBytes.ToString(CultureInfo.InvariantCulture),
            corpus.Seed.ToString(CultureInfo.InvariantCulture),
            definition.ChunkSize.ToString(CultureInfo.InvariantCulture),
            definition.HashSuite,
            mutation?.Kind ?? "none",
            mutation?.SizeBytes.ToString(CultureInfo.InvariantCulture) ?? "0",
            mutation?.Seed.ToString(CultureInfo.InvariantCulture) ?? "0");

        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(digest);
    }
}
