namespace ChunkShift.Benchmarks.Tests;

public class AmdahlHarnessTests
{
    [Fact]
    public void StreamingComponentsHashTheSameChunksAsTheHashComponents()
    {
        BoundaryScanKernels.Case @case = BoundaryScanKernels.CreateCase(64 * 1024);

        Assert.Equal(
            AmdahlHarness.RunComponent("hash-blake3", @case),
            AmdahlHarness.RunComponent("stream-blake3", @case));
        Assert.Equal(
            AmdahlHarness.RunComponent("hash-sha256", @case),
            AmdahlHarness.RunComponent("stream-sha256", @case));

        foreach (string component in AmdahlHarness.Components)
        {
            Assert.Equal(AmdahlHarness.RunComponent(component, @case), AmdahlHarness.RunComponent(component, @case));
        }

        Assert.Throws<ArgumentException>(() => AmdahlHarness.RunComponent("unknown", @case));
    }
}
