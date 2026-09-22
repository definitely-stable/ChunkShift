using System.Diagnostics;
using System.Reflection;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.Tests;

public class OptimizationTests
{
    [Fact]
    public void ProductionAssembly_HasJitOptimizerEnabled()
    {
        DebuggableAttribute? debug = typeof(Hash256)
            .Assembly
            .GetCustomAttribute<DebuggableAttribute>();

        Assert.False(
            debug?.IsJITOptimizerDisabled ?? false,
            $"ChunkShift benchmark dependency disables JIT optimization. Flags={debug?.DebuggingFlags.ToString() ?? "<none>"}.");
    }
}
