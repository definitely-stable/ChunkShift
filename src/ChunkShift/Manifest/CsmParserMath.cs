namespace ChunkShift.Manifest;

internal static class CsmParserMath
{
    internal static ulong Add(ulong left, ulong right, string field)
    {
        if (ulong.MaxValue - left < right)
        {
            throw new InvalidDataException(
                $"CSM {field} overflows UInt64.");
        }

        return left + right;
    }

    internal static int ToInt32(ulong value, string field)
    {
        if (value > int.MaxValue)
        {
            throw new InvalidDataException(
                $"CSM {field} exceeds the .NET Int32 implementation boundary.");
        }

        return (int)value;
    }
}
