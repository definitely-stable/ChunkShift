using System.Globalization;
using ChunkShift.Primitives;

namespace ChunkShift.Cli;

internal static class CliApp
{
    private const int IoBufferSize = 64 * 1024;

    internal static async Task<int> RunAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        if (args.Length == 0 ||
            args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return args.Length == 0 ? 2 : 0;
        }

        try
        {
            return args[0] switch
            {
                "create" => await RunCreateAsync(
                    args[1..],
                    cancellationToken).ConfigureAwait(false),
                "inspect" => await RunInspectAsync(
                    args[1..],
                    cancellationToken).ConfigureAwait(false),
                "verify" => await RunVerifyAsync(
                    args[1..],
                    cancellationToken).ConfigureAwait(false),
                _ => UsageError(
                    $"Unknown command '{args[0]}'."),
            };
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or NotSupportedException
                or ArgumentException)
        {
            Console.Error.WriteLine(
                $"error: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> RunCreateAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        if (args.Length < 2)
        {
            return UsageError(
                "create requires <content> and <manifest>.");
        }

        string contentPath = Path.GetFullPath(args[0]);
        string manifestPath = Path.GetFullPath(args[1]);

        // Publishing replaces <manifest>. If it named the source, a POSIX rename
        // would silently replace the content with its own manifest.
        if (PathsReferToSameFile(contentPath, manifestPath))
        {
            return UsageError(
                "create: <manifest> must not be the same file as <content>.");
        }

        bool includeBlockIndex = false;
        HashSuiteId? hashSuite = null;

        for (int index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--bidx":
                    includeBlockIndex = true;
                    break;
                case "--sha256":
                    SetHashSuite(
                        ref hashSuite,
                        HashSuiteIds.Sha256V1);
                    break;
                case "--blake3":
                    SetHashSuite(
                        ref hashSuite,
                        HashSuiteIds.Blake3256V1);
                    break;
                default:
                    return UsageError(
                        $"Unknown create option '{args[index]}'.");
            }
        }

        string? directory = Path.GetDirectoryName(manifestPath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException(
                "Manifest destination must resolve to a directory.",
                nameof(args));
        }

        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(manifestPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            ManifestInfo info;

            await using (FileStream content = OpenRead(contentPath))
            await using (FileStream destination =
                new(
                    temporaryPath,
                    new FileStreamOptions
                    {
                        Access = FileAccess.Write,
                        Mode = FileMode.CreateNew,
                        Share = FileShare.None,
                        Options =
                            FileOptions.Asynchronous
                            | FileOptions.SequentialScan,
                        BufferSize = IoBufferSize,
                    }))
            {
                info =
                    await ChunkManifest.CreateAsync(
                        content,
                        destination,
                        new ManifestCreationOptions
                        {
                            HashSuite = hashSuite,
                            IncludeBlockIndex =
                                includeBlockIndex,
                        },
                        cancellationToken).ConfigureAwait(false);

                await destination.FlushAsync(
                    cancellationToken).ConfigureAwait(false);
            }

            // Report only after the manifest is published, so a failed move is
            // never preceded by a success-looking summary.
            File.Move(
                temporaryPath,
                manifestPath,
                overwrite: true);

            PrintManifest(info);
            Console.WriteLine(
                $"written={manifestPath}");
            return 0;
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static async Task<int> RunInspectAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        if (args.Length != 1)
        {
            return UsageError(
                "inspect requires exactly <manifest>.");
        }

        await using FileStream manifest =
            OpenRead(Path.GetFullPath(args[0]));
        await using ManifestReader reader =
            await ManifestReader.OpenAsync(
                manifest,
                cancellationToken).ConfigureAwait(false);

        var entries = new ChunkEntry[512];
        while (await reader.ReadAsync(
            entries,
            cancellationToken).ConfigureAwait(false) != 0)
        {
        }

        ManifestVerificationResult result =
            reader.VerificationResult
            ?? throw new InvalidOperationException(
                "ManifestReader completed without a verification result.");

        PrintManifest(result.Manifest);
        PrintVerification(result);

        return result.IsValid ? 0 : 1;
    }

    private static async Task<int> RunVerifyAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        if (args.Length is < 1 or > 2)
        {
            return UsageError(
                "verify requires <manifest> [content].");
        }

        await using FileStream manifest =
            OpenRead(Path.GetFullPath(args[0]));

        ManifestVerificationResult result;

        if (args.Length == 1)
        {
            result = await ChunkManifest.VerifyManifestAsync(
                manifest,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await using FileStream content =
                OpenRead(Path.GetFullPath(args[1]));

            result = await ChunkManifest.VerifyAsync(
                content,
                manifest,
                cancellationToken).ConfigureAwait(false);
        }

        PrintManifest(result.Manifest);
        PrintVerification(result);

        return result.IsValid ? 0 : 1;
    }

    private static bool PathsReferToSameFile(string first, string second)
    {
        // Windows and default macOS volumes are case-insensitive. Hard links are
        // not detected; symbolic links are resolved to their final target.
        StringComparison comparison =
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        return string.Equals(
            ResolveFinalPath(first),
            ResolveFinalPath(second),
            comparison);
    }

    private static string ResolveFinalPath(string path)
    {
        try
        {
            FileSystemInfo? target = File.ResolveLinkTarget(
                path,
                returnFinalTarget: true);

            return target is null
                ? path
                : Path.GetFullPath(target.FullName);
        }
        catch (IOException)
        {
            return path;
        }
        catch (UnauthorizedAccessException)
        {
            return path;
        }
    }

    private static FileStream OpenRead(string path) =>
        new(
            path,
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Share = FileShare.Read,
                Options =
                    FileOptions.Asynchronous
                    | FileOptions.SequentialScan,
                BufferSize = IoBufferSize,
            });

    private static void PrintManifest(ManifestInfo info)
    {
        Console.WriteLine(
            $"hash-suite={info.HashSuite}");
        Console.WriteLine(
            $"profile={info.ProfileId}");
        Console.WriteLine(
            $"profile-fingerprint={info.ProfileFingerprint}");
        Console.WriteLine(
            $"manifest-id={info.ManifestId}");
        Console.WriteLine(
            $"file-digest={info.FileDigest.ToHexLower()}");
        Console.WriteLine(
            $"chunks={info.ChunkCount.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine(
            $"content-bytes={info.ContentLength.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine(
            $"physical-bytes={info.PhysicalLength.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine(
            $"cblk-count={info.ChunkBlockCount.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine(
            $"bidx={info.HasBlockIndex.ToString().ToLowerInvariant()}");
    }

    private static void PrintVerification(
        ManifestVerificationResult result)
    {
        Console.WriteLine(
            $"valid={result.IsValid.ToString().ToLowerInvariant()}");
        Console.WriteLine(
            $"failures={result.Failures}");
    }

    private static void SetHashSuite(
        ref HashSuiteId? current,
        HashSuiteId requested)
    {
        if (current.HasValue &&
            current.Value != requested)
        {
            throw new ArgumentException(
                "Choose only one of --sha256 or --blake3.");
        }

        current = requested;
    }

    private static int UsageError(string message)
    {
        Console.Error.WriteLine($"error: {message}");
        PrintUsage();
        return 2;
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            ChunkShift CSM utility

            Usage:
              chunkshift create <content> <manifest> [--bidx] [--sha256|--blake3]
              chunkshift inspect <manifest>
              chunkshift verify <manifest> [content]

            Exit codes:
              0  success / verification valid
              1  verification mismatch or operational error
              2  command-line usage error
              130 cancelled
            """);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
