namespace ChunkShift.Benchmarks.Tests;

public class BoundaryScanKernelsTests
{
    [Theory]
    [InlineData(64 * 1024)]
    [InlineData(256 * 1024)]
    public void CuttingVariantsAgreeAndReferenceVariantsAreDeterministic(int target)
    {
        // CreateCase throws unless Scan, ScanLocals and ScalarLocals produce
        // the same cut sequence covering the data.
        BoundaryScanKernels.Case first = BoundaryScanKernels.CreateCase(target);
        BoundaryScanKernels.Case second = BoundaryScanKernels.CreateCase(target);

        Assert.Equal(first.Cuts, second.Cuts);
        Assert.True(first.Cuts.Length > 1);

        long scan = BoundaryScanKernels.Run("Scan", first);
        Assert.Equal(scan, BoundaryScanKernels.Run("ScanLocals", first));
        Assert.Equal(scan, BoundaryScanKernels.Run("ScalarLocals", first));

        foreach (string variant in BoundaryScanKernels.Variants)
        {
            Assert.Equal(BoundaryScanKernels.Run(variant, first), BoundaryScanKernels.Run(variant, second));
        }
    }

    [Fact]
    public void UnknownVariantIsRejected()
    {
        BoundaryScanKernels.Case @case = BoundaryScanKernels.CreateCase(64 * 1024);

        Assert.Throws<ArgumentException>(() => BoundaryScanKernels.Run("Unknown", @case));
    }
}
