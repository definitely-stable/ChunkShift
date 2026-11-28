using System.Text.Json;
using ChunkShift.Repository;

internal static class NativeReadEioProbe
{
    private static readonly RawPackReadLimits Limits = new(1024 * 1024, 8, 544, 512 * 1024);

    internal static async Task RunAsync(string pinned, string faulted)
    {
        if (!OperatingSystem.IsLinux() || !NativeDirectoryBarrier.IsSupportedPlatform)
        {
            throw new PlatformNotSupportedException("Native read EIO probe requires Linux x64/ARM64.");
        }

        using var source = File.OpenRead(pinned);
        RawPackDescriptor expected = (await RawPackReader.VerifyAsync(source, Limits).ConfigureAwait(false)).Descriptor;
        using var reader = new FileStream(faulted, FileMode.Open, FileAccess.Read, FileShare.Read,
            16 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);
        if (reader.Length != expected.PhysicalBytes)
        {
            throw new InvalidDataException("Faulted candidate extent differs from pinned source.");
        }

        try
        {
            _ = await RawPackReader.VerifyAsync(reader, Limits, expected).ConfigureAwait(false);
        }
        catch (IOException error) when (error.HResult == 5)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                kind = "native-read-eio", hresult = error.HResult,
                type = error.GetType().Name, accepted = false,
                bytes = reader.Length, expected_sha256 = expected.PackDigest.Value.ToHexLower(),
            }));
            return;
        }

        throw new InvalidOperationException("Faulted Pack unexpectedly passed full verification.");
    }
}
