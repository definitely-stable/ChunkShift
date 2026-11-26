using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

internal static class FlushProbe
{
    internal static void Run(string path)
    {
        byte[] bytes = new byte[64 * 1024];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(i % 251);
        }

        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            file.Write(bytes);
            file.Flush(flushToDisk: true);
        }

        int collisionHResult;
        try
        {
            using var unexpected = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            throw new InvalidOperationException("CreateNew accepted existing path.");
        }
        catch (IOException error)
        {
            collisionHResult = error.HResult;
        }

        byte[] actual = File.ReadAllBytes(path);
        if (!actual.AsSpan().SequenceEqual(bytes))
        {
            throw new InvalidDataException("Managed flush fixture mismatch.");
        }

        string root = Path.GetPathRoot(Path.GetFullPath(path))
            ?? throw new InvalidOperationException("Missing volume root.");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            kind = "managed-flush", bytes = bytes.Length,
            sha256 = Convert.ToHexStringLower(SHA256.HashData(actual)),
            flush_to_disk = true, create_collision_hresult = collisionHResult,
            runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            arch = RuntimeInformation.ProcessArchitecture.ToString(),
            filesystem = new DriveInfo(root).DriveFormat
        }));
    }
}
