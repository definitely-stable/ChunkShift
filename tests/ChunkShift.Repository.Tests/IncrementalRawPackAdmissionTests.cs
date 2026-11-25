using System.Buffers.Binary;
using ChunkShift.Primitives;

namespace ChunkShift.Repository.Tests;

public sealed class IncrementalRawPackAdmissionTests
{
    private static HashSuiteId Sha => HashSuiteIds.Sha256V1;
    private static HashSuiteId Blake => HashSuiteIds.Blake3256V1;
    private static readonly RawPackReadLimits Limits = new(1000, 8, 544, 764);

    [Fact]
    public void ChargesFullColdOverheadAndRetainsCompletedPackReservations()
    {
        var ledger = new IncrementalRawPackAdmission(400, Limits, 1278, 3, 502, 4);
        Assert.Equal(new IncrementalPackReservation(true, RawPackPlacement.Append, 0, 0, 237, 237),
            ledger.ReserveUnique(Key(1, Sha), 1));
        Assert.Equal(new IncrementalPackReservation(false, RawPackPlacement.Append, 0, 1, 305, 68),
            ledger.ReserveUnique(Key(2, Sha), 0));
        Assert.Equal(new IncrementalPackReservation(true, RawPackPlacement.RotateTarget, 1, 0, 736, 736),
            ledger.ReserveUnique(Key(3, Sha), 500));
        Assert.Equal(new IncrementalPackReservation(true, RawPackPlacement.RotateTarget, 2, 0, 237, 237),
            ledger.ReserveUnique(Key(4, Sha), 1));
        Assert.Equal(new IncrementalPackBudget(1278, 3, 4, 502), ledger.Budget);
    }

    [Fact]
    public void DuplicateAcrossRotationsIsRejectedWithoutTrustingClaimedKey()
    {
        var ledger = new IncrementalRawPackAdmission(300, Limits, 1000, 4, 100, 4);
        ChunkObjectKey key = Key(1, Sha);
        ledger.ReserveUnique(key, 1);
        ledger.ReserveUnique(Key(2, Sha), 1);
        IncrementalPackBudget before = ledger.Budget;
        Reject(ledger, key, 1, IncrementalPackRejection.DuplicateKey);
        Assert.Equal(before, ledger.Budget);
        // Equal digest value in another suite is a different logical key.
        IncrementalPackReservation other = ledger.ReserveUnique(Key(1, Blake), 1);
        Assert.True(other.StartsNewPack);
        Assert.Equal(RawPackPlacement.RotateHashSuite, other.Decision);
        Assert.Equal(3, ledger.Budget.UniqueChunks);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ResourceRejectionPreservesAllReservations(int variant)
    {
        var ledger = new IncrementalRawPackAdmission(300,
            variant == 4 ? new RawPackReadLimits(1000, 8, 544, 1) : Limits,
            variant == 0 ? 473 : 1000,
            variant == 1 ? 1 : 4,
            variant == 2 ? 2 : 100,
            variant == 3 ? 1 : 8);
        ledger.ReserveUnique(Key(1, Sha), 1);
        IncrementalPackBudget before = ledger.Budget;
        IncrementalPackRejection reason = variant switch
        {
            0 => IncrementalPackRejection.TotalPhysicalBytes,
            1 => IncrementalPackRejection.PackCount,
            2 => IncrementalPackRejection.LogicalBytes,
            3 => IncrementalPackRejection.UniqueChunks,
            _ => IncrementalPackRejection.FrameBytes,
        };
        Reject(ledger, Key(2, Sha), variant == 0 || variant == 1 || variant == 3 ? 1 : 2, reason);
        Assert.Equal(before, ledger.Budget);
    }

    [Fact]
    public void ImpossibleChunkAndZeroLengthMetadataAreBounded()
    {
        var ledger = new IncrementalRawPackAdmission(236,
            new RawPackReadLimits(236, 8, 544, 100), 236, 1, 0, 8);
        // Logical ceiling is checked before the impossible physical chunk.
        Reject(ledger, Key(1, Sha), 1, IncrementalPackRejection.LogicalBytes);
        Assert.Equal(default, ledger.Budget);
        ledger.ReserveUnique(Key(2, Sha), 0);
        Assert.Equal(new IncrementalPackBudget(236, 1, 1, 0), ledger.Budget);
        Reject(ledger, Key(3, Sha), 0, IncrementalPackRejection.PackCount);

        var physical = new IncrementalRawPackAdmission(236,
            new RawPackReadLimits(236, 8, 544, 100), 1000, 4, 100, 8);
        Reject(physical, Key(1, Sha), 1, IncrementalPackRejection.ImpossibleChunk);
        Assert.Equal(default, physical.Budget);
    }

    [Fact]
    public void LongBoundaryDoesNotWrapOrReleasePriorPacks()
    {
        var ledger = new IncrementalRawPackAdmission(long.MaxValue,
            new RawPackReadLimits(long.MaxValue, 8, 544, long.MaxValue),
            long.MaxValue, 2, long.MaxValue, 2);
        ledger.ReserveUnique(Key(1, Sha), long.MaxValue - 236);
        Assert.Equal(long.MaxValue, ledger.Budget.PhysicalBytes);
        Reject(ledger, Key(2, Sha), 0, IncrementalPackRejection.TotalPhysicalBytes);
        Assert.Equal(long.MaxValue, ledger.Budget.PhysicalBytes);
    }

    [Fact]
    public void SeededUniqueTraceMatchesIndependentAggregateFormula()
    {
        for (int seed = 0; seed < 16; seed++)
        {
            var random = new Random(seed);
            var ledger = new IncrementalRawPackAdmission(700,
                new RawPackReadLimits(1000, 5, 272, 764), 200000, 128, 100000, 128);
            long totalPayload = 0;
            int packs = 0;
            long currentPayload = 0;
            int currentCount = 0;
            for (int i = 0; i < 128; i++)
            {
                int size = random.Next(765);
                var reservation = ledger.ReserveUnique(Key(i, random.Next(4) == 0 ? Blake : Sha), size);
                if (reservation.StartsNewPack)
                {
                    packs++;
                    currentPayload = 0;
                    currentCount = 0;
                }

                currentPayload += size;
                currentCount++;
                totalPayload += size;
                Assert.Equal(packs - 1, reservation.PackOrdinal);
                Assert.Equal(currentCount - 1, reservation.FrameOrdinal);
                Assert.Equal(168 + currentPayload + 68L * currentCount, reservation.ProjectedPackBytes);
                Assert.InRange(reservation.ProjectedPackBytes, 236, 1000);
                Assert.True(reservation.ProjectedPackBytes <= 700 || currentCount == 1);
                Assert.InRange(currentCount, 1, 4);
                Assert.Equal(new IncrementalPackBudget(totalPayload + 68L * (i + 1) + 168L * packs,
                    packs, i + 1, totalPayload), ledger.Budget);
            }
        }
    }

    private static void Reject(IncrementalRawPackAdmission ledger, ChunkObjectKey key,
        long length, IncrementalPackRejection expected)
    {
        IncrementalPackBudget before = ledger.Budget;
        var error = Assert.Throws<IncrementalPackAdmissionException>(() => ledger.ReserveUnique(key, length));
        Assert.Equal(expected, error.Reason);
        Assert.Equal(before, ledger.Budget);
    }

    // Synthetic identity for a pure reservation ledger; not a verified chunk.
    private static ChunkObjectKey Key(int value, HashSuiteId suite)
    {
        Span<byte> bytes = stackalloc byte[32];
        bytes.Clear();
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return new ChunkObjectKey(suite, new ChunkId(Hash256.FromBytes(bytes)));
    }
}
