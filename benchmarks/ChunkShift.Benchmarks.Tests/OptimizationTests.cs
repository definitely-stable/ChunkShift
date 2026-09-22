using System.Diagnostics;
using System.Reflection;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.Tests;

public class OptimizationTests
{
    [Fact]
    public void ProductionAssembly_HasJitOptimizerEnabled()
    {
        Assembly production = typeof(Hash256).Assembly;
        DebuggableAttribute? debug = production.GetCustomAttribute<DebuggableAttribute>();

        Assert.False(
            debug?.IsJITOptimizerDisabled ?? false,
            $"ChunkShift Release assembly disables JIT optimization. DebuggingFlags={debug?.DebuggingFlags.ToString() ?? "<none>"}.");
    }
}
