namespace ChunkShift.Benchmarks.Lab;

/// <summary>
/// SplitMix64-based deterministic generator. Its algorithm is part of the lab definition.
/// </summary>
public sealed class DeterministicPrng
{
    private ulong _state;

    public DeterministicPrng(ulong seed)
    {
        _state = seed;
    }

    public ulong NextUInt64()
    {
        ulong z = _state += 0x9E37_79B9_7F4A_7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58_476D_1CE4_E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D0_49BB_1331_11EBUL;
        return z ^ (z >> 31);
    }

    public int NextInt32(int exclusiveMax)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveMax);
        return (int)(NextUInt64() % (uint)exclusiveMax);
    }

    public void Fill(Span<byte> destination)
    {
        int offset = 0;

        while (offset < destination.Length)
        {
            ulong value = NextUInt64();

            for (int i = 0; i < sizeof(ulong) && offset < destination.Length; i++)
            {
                destination[offset++] = (byte)value;
                value >>= 8;
            }
        }
    }
}
