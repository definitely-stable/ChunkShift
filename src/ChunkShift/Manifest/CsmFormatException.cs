namespace ChunkShift.Manifest;

internal sealed class CsmFormatException : InvalidDataException
{
    internal CsmFormatException(string message)
        : base(message)
    {
    }
}
