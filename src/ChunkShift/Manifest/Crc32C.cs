namespace ChunkShift.Manifest;

internal static class Crc32C
{
    // Reflected Castagnoli polynomial corresponding to normal-form 0x1EDC6F41.
    private const uint ReflectedPolynomial = 0x82F63B78u;
    private const uint InitialState = 0xFFFFFFFFu;

    internal static uint Compute(ReadOnlySpan<byte> data)
    {
        uint state = Append(InitialState, data);
        return Finalize(state);
    }

    internal static uint Start() => InitialState;

    internal static uint Append(uint state, ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
        {
            state ^= value;

            for (int bit = 0; bit < 8; bit++)
            {
                uint mask = unchecked((uint)-(int)(state & 1u));
                state = (state >> 1) ^ (ReflectedPolynomial & mask);
            }
        }

        return state;
    }

    internal static uint Finalize(uint state) => ~state;
}
