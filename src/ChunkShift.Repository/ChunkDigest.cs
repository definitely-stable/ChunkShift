using System.Security.Cryptography;
using Blake3;
using ChunkShift.Primitives;

namespace ChunkShift.Repository;

/// <summary>
/// Repository-local incremental hash dispatch. Uses the same stable hash suite meanings as Core.
/// Do not change providers or package versions without existing Core conformance evidence.
/// </summary>
internal sealed class ChunkDigest : IDisposable
{
    private readonly Hasher? _blake3;
    private readonly IncrementalHash? _sha256;
    private bool _finalized;

    internal ChunkDigest(HashSuiteId hashSuite)
    {
        ArgumentNullException.ThrowIfNull(hashSuite);

        if (hashSuite == HashSuiteIds.Blake3256V1)
        {
            _blake3 = Hasher.New();
        }
        else if (hashSuite == HashSuiteIds.Sha256V1)
        {
            _sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        }
        else
        {
            throw new NotSupportedException($"Unsupported HashSuiteId '{hashSuite}'.");
        }
    }

    internal void Append(ReadOnlySpan<byte> bytes)
    {
        if (_finalized)
        {
            throw new InvalidOperationException("Chunk digest has already been finalized.");
        }

        if (_blake3 is not null)
        {
            _blake3.Update(bytes);
        }
        else
        {
            _sha256!.AppendData(bytes);
        }
    }

    internal ChunkId Finish()
    {
        if (_finalized)
        {
            throw new InvalidOperationException("Chunk digest has already been finalized.");
        }

        _finalized = true;
        if (_blake3 is not null)
        {
            Blake3.Hash result = _blake3.Finalize();
            return new ChunkId(Hash256.FromBytes(result.AsSpan()));
        }

        Span<byte> digest = stackalloc byte[32];
        if (!_sha256!.TryGetHashAndReset(digest, out int written) ||
            written != digest.Length)
        {
            throw new CryptographicException("SHA-256 incremental hash finalization failed.");
        }

        return new ChunkId(Hash256.FromBytes(digest));
    }

    public void Dispose()
    {
        _blake3?.Dispose();
        _sha256?.Dispose();
    }
}
