namespace ChunkShift.Benchmarks.PatchLab;

/// <summary>File access shared by the patch-lab modes.</summary>
internal static class PatchLabFiles
{
    internal static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);

    internal static FileStream Create(string path) =>
        new(path, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024);

    internal static FileStream CreateNew(string path) =>
        new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024);
}
