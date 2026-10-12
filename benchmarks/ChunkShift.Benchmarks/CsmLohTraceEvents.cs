using System.Diagnostics.Tracing;

namespace ChunkShift.Benchmarks;

/// <summary>
/// Trace-only phase boundaries for CORE011-CSM-001. Not part of the product API.
/// Phase values: 1=ready, 2=round-start, 3=round-end,
/// 4=positive-control-start, 5=positive-control-end.
/// </summary>
[EventSource(Name = "ChunkShift-Core011-CSM-Trace")]
internal sealed class CsmLohTraceEvents : EventSource
{
    internal static readonly CsmLohTraceEvents Log = new();

    private CsmLohTraceEvents() { }

    [Event(1, Level = EventLevel.Informational)]
    public void Phase(int phase, int round)
    {
        if (IsEnabled())
        {
            WriteEvent(1, phase, round);
        }
    }
}
