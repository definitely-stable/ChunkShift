using System.Buffers.Binary;
using System.Globalization;
using ChunkShift.Benchmarks.PatchLab;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public class PatchEnc005FeatureTests
{
    [Fact]
    public void FrozenRabinVectorsMatch()
    {
        Assert.Equal(0x0000000000000000UL, PatchEnc005Features.Rabin48(new byte[48]));
        Assert.Equal(
            0xa37764d925385c09UL,
            PatchEnc005Features.Rabin48(Enumerable.Range(0, 48).Select(static value => (byte)value).ToArray()));
        Assert.Equal(
            0xf028667cb9141fe0UL,
            PatchEnc005Features.Rabin48(Enumerable.Repeat((byte)0xff, 48).ToArray()));
    }

    [Fact]
    public void OptimizedRabinMatchesReferenceOnOverlappingWindows()
    {
        var random = new Random(181005);
        var bytes = new byte[4096];
        random.NextBytes(bytes);

        for (int offset = 0; offset <= bytes.Length - 48; offset++)
        {
            ReadOnlySpan<byte> window = bytes.AsSpan(offset, 48);
            Assert.Equal(
                PatchEnc005Features.Rabin48Reference(window),
                PatchEnc005Features.Rabin48(window));
        }
    }

    [Fact]
    public void FrozenTransformVectorMatches()
    {
        PatchEnc005Features.TransformPair transform = PatchEnc005Features.Transform(0);

        Assert.Equal(
            "26d817cc25bd7deefff034120e80e42332fde80917014280d8273df9b807daaa",
            transform.DigestHex);
        Assert.Equal(0xcc17d827U, transform.Multiplier);
        Assert.Equal(0xee7dbd25U, transform.Addend);
    }

    [Theory]
    [InlineData("PATCH-ENC-005/H5F/SF", 0, true, "2653b8064dced72c")]
    [InlineData("PATCH-ENC-005/H6O/SF3", 2, false, "fb14428b9f29aa0e")]
    public void FrozenFourValueKeyVectorsMatch(
        string domain,
        byte prefix,
        bool use64,
        string expected)
    {
        byte[] payload = use64 ? new byte[1 + (4 * sizeof(ulong))] : new byte[1 + (4 * sizeof(uint))];
        payload[0] = prefix;

        for (int index = 0; index < 4; index++)
        {
            if (use64)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(
                    payload.AsSpan(1 + (index * sizeof(ulong)), sizeof(ulong)),
                    (ulong)index + 1);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    payload.AsSpan(1 + (index * sizeof(uint)), sizeof(uint)),
                    (uint)index + 1);
            }
        }

        Assert.Equal(
            expected,
            PatchEnc005Features.Key64(domain, payload).ToString("x16", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void FrozenH6PTierKeyVectorMatches()
    {
        byte[] payload = new byte[2 + (4 * sizeof(uint))];
        payload[0] = 1;
        payload[1] = 0;

        for (int index = 0; index < 4; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                payload.AsSpan(2 + (index * sizeof(uint)), sizeof(uint)),
                (uint)index + 1);
        }

        Assert.Equal(
            "4ebbf4b747872713",
            PatchEnc005Features.Key64("PATCH-ENC-005/H6P/TIER", payload)
                .ToString("x16", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void FrozenStrideKeyVectorMatches()
    {
        Assert.Equal(
            "33ef21b43b1b962b",
            PatchEnc005Features.Key64("PATCH-ENC-005/INDEX-STRIDE", new byte[32])
                .ToString("x16", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void PrivateGearCopyMatchesFrozenIdentity()
    {
        Assert.Equal(PatchEnc005Features.ExpectedGearSha256, PatchEnc005Features.GearSha256());
        Assert.Equal("chunkshift.fastcdc.gear.v1", PatchEnc005Features.GearId);
    }
}
