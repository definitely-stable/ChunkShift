using ChunkShift.Primitives;

namespace ChunkShift.Repository.IngestLab;

internal static class RangeSelfTest
{
    internal static async Task RunAsync()
    {
        string root = Directory.CreateTempSubdirectory("chunkshift-range-test-").FullName;
        string path = Path.Combine(root, "source.bin");
        string stage = Path.Combine(root, "stage");
        try
        {
            byte[] original = [10, 20, 30, 40, 50, 60];
            await File.WriteAllBytesAsync(path, original).ConfigureAwait(false);
            using (var owner = new FileStream(path, FileMode.Open, FileAccess.ReadWrite,
                FileShare.None, 4096, FileOptions.Asynchronous))
            {
                var handle = owner.SafeFileHandle;
                owner.Position = 1;
                using var a = new SourceRangeStream(handle, 6, 1, 3);
                using var b = new SourceRangeStream(handle, 6, 3, 3);
                using var empty = new SourceRangeStream(handle, 6, 6, 0);
                byte[] buffer = new byte[8];
                Check(await a.ReadAsync(buffer.AsMemory(0, 1)).ConfigureAwait(false) == 1 &&
                      buffer[0] == 20, "first range read");
                Check(b.Read(buffer.AsSpan()) == 3 && buffer.AsSpan(0, 3).SequenceEqual(original.AsSpan(3)),
                      "independent second range and clipping");
                Check(await a.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None).ConfigureAwait(false) == 2 &&
                      buffer.AsSpan(0, 2).SequenceEqual(original.AsSpan(2, 2)), "first range cursor");
                Check(a.ReadByte() == -1 && b.ReadByte() == -1 && empty.ReadByte() == -1 &&
                      a.ConsumedBytes == 3 && b.ConsumedBytes == 3 && owner.Position == 1, "bounded EOF and owner cursor");
                using var cancel = new CancellationTokenSource();
                cancel.Cancel();
                await ThrowsAsync<OperationCanceledException>(async () =>
                    { _ = await empty.ReadAsync(buffer.AsMemory(), cancel.Token).ConfigureAwait(false); }).ConfigureAwait(false);
                using var retry = new SourceRangeStream(handle, 6, 0, 2);
                await ThrowsAsync<OperationCanceledException>(async () =>
                    { _ = await retry.ReadAsync(buffer.AsMemory(), cancel.Token).ConfigureAwait(false); }).ConfigureAwait(false);
                Check(retry.ConsumedBytes == 0 && retry.ReadByte() == 10, "cancel before read and retry");
                retry.Dispose();
                Check(!retry.CanRead && !handle.IsClosed, "non-owning disposal");
                await ThrowsAsync<ObjectDisposedException>(() => Task.FromResult(retry.ReadByte())).ConfigureAwait(false);
                await ThrowsAsync<ArgumentOutOfRangeException>(() =>
                {
                    using var bad = new SourceRangeStream(handle, 6, long.MaxValue, 1);
                    return Task.CompletedTask;
                }).ConfigureAwait(false);

                // Bind expected digest to original bytes, then deliberately violate
                // stable-source contract. Staging must reject changed payload.
                HashSuiteId suite = HashSuiteIds.Sha256V1;
                using var digest = new ChunkDigest(suite);
                digest.Append(original.AsSpan(0, 2));
                var key = new ChunkObjectKey(suite, digest.Finish());
                RandomAccess.Write(handle, new byte[] { 99 }, 0);
                using (var session = new IngestSession(stage, 6, 6, 1, 1))
                using (var changed = new SourceRangeStream(handle, 6, 0, 2))
                {
                    await ThrowsAsync<InvalidDataException>(() =>
                        session.IngestAsync([new IngestRequest(changed, key, 2)])).ConfigureAwait(false);
                    Check(session.Budget == default && !Directory.EnumerateFileSystemEntries(stage).Any(),
                        "digest mismatch cleanup");
                }

                using var truncated = new SourceRangeStream(handle, 6, 4, 2);
                RandomAccess.SetLength(handle, 4);
                await ThrowsAsync<EndOfStreamException>(async () =>
                    { _ = await truncated.ReadAsync(buffer.AsMemory()).ConfigureAwait(false); }).ConfigureAwait(false);
            }

            File.Delete(path);
            Directory.Delete(stage, recursive: false);
            Directory.Delete(root, recursive: false);
            Console.WriteLine("Source-range self-test PASS: clipping, cursors, EOF, cancellation, ownership, bounds, digest mismatch cleanup, truncation.");
        }
        catch
        {
            // Preserve failed fixture for diagnostics.
            Console.Error.WriteLine($"Failed source-range fixture: {root}");
            throw;
        }
    }

    private static void Check(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidDataException(message);
        }
    }

    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (T)
        {
            return;
        }

        throw new InvalidDataException($"Expected {typeof(T).Name}.");
    }
}
