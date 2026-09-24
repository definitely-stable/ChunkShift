using BenchmarkDotNet.Attributes;

namespace ChunkShift.Benchmarks;

/// <summary>
/// A1-F08: the production boundary loop against its dependency-chain floor and
/// against variants without field state or per-byte branches. See
/// <see cref="BoundaryScanKernels"/> for what each variant isolates; the
/// benchmark-lab <c>boundary-scan-f08</c> job runs this class and the
/// <c>f08</c> hardware-counter harness.
/// </summary>
[DisassemblyDiagnoser(maxDepth: 2)]
public class BoundaryScanBenchmarks
{
    private BoundaryScanKernels.Case _case = null!;

    [Params(64 * 1024, 256 * 1024)]
    public int TargetSize { get; set; }

    [GlobalSetup]
    public void Setup() => _case = BoundaryScanKernels.CreateCase(TargetSize);

    [Benchmark(Baseline = true)]
    public long Scan() => BoundaryScanKernels.Scan(_case.Data, _case.KernelProfile, null);

    [Benchmark]
    public long ScanLocals() => BoundaryScanKernels.ScanLocals(_case.Data, _case.KernelProfile, null);

    [Benchmark]
    public long ScalarLocals() => BoundaryScanKernels.ScalarLocals(_case.Data, _case.Profile, null);

    [Benchmark]
    public ulong ChainFloor() => BoundaryScanKernels.ChainFloor(_case.Data, _case.Cuts, _case.Profile);

    [Benchmark]
    public long NoChain() => BoundaryScanKernels.NoChain(_case.Data, _case.Cuts, _case.Profile);
}
