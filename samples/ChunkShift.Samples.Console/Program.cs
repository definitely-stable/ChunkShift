// Minimal console use of the ChunkShift Core package.
//
//   scan   <content>              print every chunk: offset, length and ChunkId
//   create <content> <manifest>   write a binary CSM manifest for <content>
//   verify <content> <manifest>   check <content> against a CSM manifest
//
// Exit codes: 0 success, 1 verification failed, 2 usage or input error.

using ChunkShift;
using static System.FormattableString;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

try
{
    return args switch
    {
        ["scan", string content] => await ScanAsync(content, cancellation.Token),
        ["create", string content, string manifest] => await CreateAsync(content, manifest, cancellation.Token),
        ["verify", string content, string manifest] => await VerifyAsync(content, manifest, cancellation.Token),
        _ => Usage(),
    };
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.Error.WriteLine("canceled");
    return 130;
}

static async Task<int> ScanAsync(string contentPath, CancellationToken cancellationToken)
{
    await using FileStream content = OpenRead(contentPath);

    // The handler runs once per chunk, in order. Its second argument is the
    // chunk's bytes, borrowed: valid only until the returned ValueTask
    // completes, so copy them if you need to keep them (for example, before
    // handing them to a background upload).
    await ChunkScanner.ScanAsync(
        content,
        (chunk, _, _) =>
        {
            Console.WriteLine(Invariant($"{chunk.Index,6} {chunk.Offset,12} {chunk.Length,8} {chunk.Id}"));
            return ValueTask.CompletedTask;
        },
        cancellationToken: cancellationToken);

    return 0;
}

static async Task<int> CreateAsync(string contentPath, string manifestPath, CancellationToken cancellationToken)
{
    // The manifest is written forward while the content is read. A failed or
    // cancelled run leaves an incomplete CSM behind, so write to a temporary
    // file next to the target and move it into place only after success.
    string temporaryPath = manifestPath + ".tmp";
    ManifestInfo info;

    await using (FileStream content = OpenRead(contentPath))
    {
        // CreateNew: never overwrite a file this run did not create.
        FileStream manifest = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);

        try
        {
            await using (manifest)
            {
                info = await ChunkManifest.CreateAsync(
                    content,
                    manifest,
                    new ManifestCreationOptions { IncludeBlockIndex = true },
                    cancellationToken);
            }

            File.Move(temporaryPath, manifestPath, overwrite: false);
        }
        catch
        {
            File.Delete(temporaryPath);
            throw;
        }
    }

    Console.WriteLine(Invariant($"manifest-id={info.ManifestId}"));
    Console.WriteLine(Invariant($"profile={info.ProfileId} hash-suite={info.HashSuite}"));
    Console.WriteLine(Invariant($"content-bytes={info.ContentLength} chunks={info.ChunkCount}"));
    return 0;
}

static async Task<int> VerifyAsync(string contentPath, string manifestPath, CancellationToken cancellationToken)
{
    await using FileStream content = OpenRead(contentPath);
    await using FileStream manifest = OpenRead(manifestPath);

    ManifestVerificationResult result =
        await ChunkManifest.VerifyAsync(content, manifest, cancellationToken);

    if (!result.IsValid)
    {
        Console.WriteLine(Invariant($"valid=false failures={result.Failures}"));
        return 1;
    }

    Console.WriteLine(Invariant($"valid=true manifest-id={result.Manifest.ManifestId}"));
    return 0;
}

// ChunkShift reads its source forward-only, so sequential-scan buffering fits.
static FileStream OpenRead(string path) =>
    new(path, new FileStreamOptions
    {
        Mode = FileMode.Open,
        Access = FileAccess.Read,
        Share = FileShare.Read,
        Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
    });

static int Usage()
{
    Console.Error.WriteLine("usage: scan <content> | create <content> <manifest> | verify <content> <manifest>");
    return 2;
}
