using System;
using System.Text;
using ChunkShift.Primitives;
using ChunkShift.Profiles;

namespace ChunkShift.Tests.Profiles;

public class ProfileFingerprintComputerTests
{
    [Fact]
    public void CosmeticArtifactChanges_DoNotChangeFingerprint()
    {
        const string first = """
            {
              "id": "fastcdc.example",
              "description": "first prose",
              "semantics": {
                "algorithm": "fastcdc.gear",
                "version": 1,
                "sizes": { "min": 32768, "target": 65536, "max": 262144 },
                "masks": [ 65535, 131071 ],
                "overflow": "uint64-wrap"
              }
            }
            """;

        const string second = """{"documentation":"different prose","semantics":{"overflow":"uint64-wrap","masks":[65535,131071],"sizes":{"max":262144,"target":65536,"min":32768},"version":1.0,"algorithm":"fastcdc.gear"},"id":"renamed-authoring-file"}""";

        ProfileFingerprint a = Compute(first);
        ProfileFingerprint b = Compute(second);

        Assert.Equal(a, b);
    }

    [Fact]
    public void SemanticParameterChange_ChangesFingerprint()
    {
        const string first = """{"semantics":{"algorithm":"fastcdc.gear","version":1,"target":65536}}""";
        const string second = """{"semantics":{"algorithm":"fastcdc.gear","version":1,"target":131072}}""";

        Assert.NotEqual(Compute(first), Compute(second));
    }

    [Fact]
    public void ArrayOrder_IsSemantic()
    {
        const string first = """{"semantics":{"thresholds":[1,2,3]}}""";
        const string second = """{"semantics":{"thresholds":[3,2,1]}}""";

        Assert.NotEqual(Compute(first), Compute(second));
    }

    [Fact]
    public void EquivalentIntegerSpellings_AreCanonical()
    {
        const string integer = """{"semantics":{"version":1}}""";
        const string decimalInteger = """{"semantics":{"version":1.0}}""";

        Assert.Equal(Compute(integer), Compute(decimalInteger));
    }

    [Fact]
    public void FractionalSemanticNumbers_AreRejected()
    {
        const string artifact = """{"semantics":{"threshold":1.5}}""";

        Assert.Throws<FormatException>(() => Compute(artifact));
    }

    [Fact]
    public void DuplicateSemanticProperties_AreRejected()
    {
        const string artifact = """{"semantics":{"target":65536,"target":131072}}""";

        Assert.Throws<FormatException>(() => Compute(artifact));
    }

    [Fact]
    public void MissingSemanticsObject_IsRejected()
    {
        const string artifact = """{"description":"not a semantic profile"}""";

        Assert.Throws<FormatException>(() => Compute(artifact));
    }

    [Fact]
    public void HashSuiteSelection_IsOrthogonalAndExplicit()
    {
        const string artifact = """{"semantics":{"algorithm":"fixed","version":1,"size":65536}}""";
        byte[] utf8 = Encoding.UTF8.GetBytes(artifact);

        ProfileFingerprint blake3 = ProfileFingerprintComputer.Compute(HashSuiteIds.Blake3256V1, utf8);
        ProfileFingerprint sha256 = ProfileFingerprintComputer.Compute(HashSuiteIds.Sha256V1, utf8);

        Assert.NotEqual(blake3, sha256);
    }

    private static ProfileFingerprint Compute(string artifact)
    {
        return ProfileFingerprintComputer.Compute(
            HashSuiteIds.Blake3256V1,
            Encoding.UTF8.GetBytes(artifact));
    }
}
