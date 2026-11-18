namespace ChunkShift.Cli.Tests;

internal sealed class CliWorkspace : IDisposable
{
    internal CliWorkspace()
    {
        Root = Path.Combine(
            Path.GetTempPath(),
            $"chunkshift-cli-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
    }

    internal string Root { get; }

    internal string PathFor(string fileName) => Path.Combine(Root, fileName);

    internal async Task<string> WriteContentAsync(
        string fileName,
        int seed = 0x5eed,
        int length = 1024 * 1024)
    {
        string path = PathFor(fileName);
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }

    internal async Task<string> WriteEditedContentAsync(
        string basePath,
        string fileName)
    {
        byte[] original = await File.ReadAllBytesAsync(basePath);
        var edited = new byte[original.Length + 4096];
        const int insertionOffset = 300_000;
        Buffer.BlockCopy(original, 0, edited, 0, insertionOffset);
        Array.Fill(edited, (byte)0x5a, insertionOffset, 4096);
        Buffer.BlockCopy(
            original,
            insertionOffset,
            edited,
            insertionOffset + 4096,
            original.Length - insertionOffset);

        string path = PathFor(fileName);
        await File.WriteAllBytesAsync(path, edited);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Failing tests should retain the original assertion, not a cleanup exception.
        }
        catch (UnauthorizedAccessException)
        {
            // Windows may temporarily retain an antivirus/indexer handle.
        }
    }
}
