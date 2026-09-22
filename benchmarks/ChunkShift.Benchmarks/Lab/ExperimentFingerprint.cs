using System.Security.Cryptography;
using System.Text;

namespace ChunkShift.Benchmarks.Lab;

public static class ExperimentFingerprint
{
    public static string Compute(ExperimentDefinition definition)
    {
        MutationDefinition? mutation = definition.Mutation;
        string canonical = string.Join(
            "\n",
            "chunkshift.lab.experiment.v1",
            definition.Id,
            definition.CorpusId,
            definition.ChunkSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            definition.HashSuite,
            mutation?.Kind ?? "none",
            mutation?.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0",
            mutation?.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0");

        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(digest);
    }
}
