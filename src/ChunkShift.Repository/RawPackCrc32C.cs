namespace ChunkShift.Repository;

/// <summary>
/// Portable CRC32C (Castagnoli) correctness baseline; incremental reflected
/// state starts at all ones and the final result is complemented.
/// Hardware acceleration is a separate measured decision.
/// </summary>
internal static class RawPackCrc32C
{
    private static readonly uint[] Table = CreateTable();

    internal static uint Append(uint state, ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
        {
            state = Table[(state ^ value) & 0xff] ^ (state >> 8);
        }

        return state;
    }

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value >> 1) ^ ((value & 1) == 0 ? 0u : 0x82f63b78u);
            }

            table[i] = value;
        }

        return table;
    }
}
