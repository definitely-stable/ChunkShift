using System.Buffers.Binary;
using System.IO.Compression;

namespace ChunkShift.Benchmarks.Lab;

public static class CorpusGenerator
{
    public static byte[] Generate(CorpusEntry entry)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entry.SizeBytes);

        return entry.Generator switch
        {
            "zero" => new byte[entry.SizeBytes],
            "random" => GenerateRandom(entry.SizeBytes, entry.Seed),
            "random-like" => GenerateRandom(entry.SizeBytes, entry.Seed),
            "repeated" => GenerateRepeated(entry.SizeBytes, entry.Seed),
            "low-entropy" => GenerateLowEntropy(entry.SizeBytes, entry.Seed),
            "game-pak-like" => GenerateGamePak(entry.SizeBytes, entry.Seed),
            "executable-app-like" => GenerateExecutable(entry.SizeBytes, entry.Seed),
            "db-vm-data-like" => GenerateDbVm(entry.SizeBytes, entry.Seed),
            "compressed-like" => GenerateCompressed(entry.SizeBytes, entry.Seed),
            "single-byte" => GenerateSingleByte(entry.SizeBytes, entry.Seed),
            "short-pattern" => GenerateShortPattern(entry.SizeBytes, entry.Seed),
            "alternating-entropy" => GenerateAlternatingEntropy(entry.SizeBytes, entry.Seed),
            "low-entropy-runs" => GenerateLowEntropyRuns(entry.SizeBytes, entry.Seed),
            _ => throw new InvalidOperationException($"Unknown corpus generator '{entry.Generator}'."),
        };
    }

    private static byte[] GenerateRandom(int size, ulong seed)
    {
        var bytes = new byte[size];
        new DeterministicPrng(seed).Fill(bytes);
        return bytes;
    }

    private static byte[] GenerateRepeated(int size, ulong seed)
    {
        var bytes = new byte[size];
        var pattern = new byte[Math.Min(4096, size)];
        new DeterministicPrng(seed).Fill(pattern);

        for (int offset = 0; offset < bytes.Length; offset += pattern.Length)
        {
            pattern.AsSpan(0, Math.Min(pattern.Length, bytes.Length - offset)).CopyTo(bytes.AsSpan(offset));
        }

        return bytes;
    }

    private static byte[] GenerateLowEntropy(int size, ulong seed)
    {
        var bytes = new byte[size];
        var random = new DeterministicPrng(seed);

        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)random.NextInt32(16);
        }

        return bytes;
    }

    private static byte[] GenerateGamePak(int size, ulong seed)
    {
        var bytes = new byte[size];
        const int assetSize = 64 * 1024;
        var random = new DeterministicPrng(seed);

        for (int offset = 0, asset = 0; offset < bytes.Length; offset += assetSize, asset++)
        {
            int length = Math.Min(assetSize, bytes.Length - offset);
            Span<byte> block = bytes.AsSpan(offset, length);
            random.Fill(block);

            if (length >= 16)
            {
                BinaryPrimitives.WriteInt32LittleEndian(block, asset);
                BinaryPrimitives.WriteInt32LittleEndian(block[4..], length);
                BinaryPrimitives.WriteUInt64LittleEndian(block[8..], seed ^ (uint)asset);
            }

            if (asset > 0 && length > 4096 && offset >= 4096)
            {
                bytes.AsSpan(offset - 4096, 4096).CopyTo(block[^4096..]);
            }
        }

        return bytes;
    }

    private static byte[] GenerateExecutable(int size, ulong seed)
    {
        var bytes = new byte[size];
        const int pageSize = 4096;
        var random = new DeterministicPrng(seed);

        for (int offset = 0, page = 0; offset < bytes.Length; offset += pageSize, page++)
        {
            Span<byte> pageData = bytes.AsSpan(offset, Math.Min(pageSize, bytes.Length - offset));

            if (page % 8 == 0)
            {
                pageData.Clear();
            }
            else if (page % 4 == 0)
            {
                pageData.Fill(0xCC);
            }
            else
            {
                random.Fill(pageData);
            }
        }

        if (bytes.Length >= 2)
        {
            bytes[0] = (byte)'M';
            bytes[1] = (byte)'Z';
        }

        return bytes;
    }

    private static byte[] GenerateDbVm(int size, ulong seed)
    {
        var bytes = new byte[size];
        const int pageSize = 4096;
        var random = new DeterministicPrng(seed);

        for (int offset = 0, page = 0; offset < bytes.Length; offset += pageSize, page++)
        {
            Span<byte> pageData = bytes.AsSpan(offset, Math.Min(pageSize, bytes.Length - offset));

            if (pageData.Length >= 16)
            {
                BinaryPrimitives.WriteInt32LittleEndian(pageData, page);
                BinaryPrimitives.WriteUInt64LittleEndian(pageData[4..], seed);
                BinaryPrimitives.WriteInt32LittleEndian(pageData[12..], pageData.Length);
            }

            int recordBytes = Math.Min(Math.Max(0, pageData.Length - 64), 512);
            if (recordBytes > 0)
            {
                random.Fill(pageData.Slice(64, recordBytes));
            }
        }

        return bytes;
    }

    private static byte[] GenerateCompressed(int size, ulong seed)
    {
        using var output = new MemoryStream(size + 4096);
        var random = new DeterministicPrng(seed);
        var source = new byte[256 * 1024];

        while (output.Length < size)
        {
            for (int i = 0; i < source.Length; i++)
            {
                source[i] = (byte)random.NextInt32(32);
            }

            using var frame = new MemoryStream();
            using (var compressor = new ZLibStream(frame, CompressionLevel.Fastest, leaveOpen: true))
            {
                compressor.Write(source);
            }

            byte[] compressed = frame.ToArray();
            int remaining = size - checked((int)output.Length);
            output.Write(compressed, 0, Math.Min(remaining, compressed.Length));
        }

        return output.ToArray();
    }

    // One non-zero byte value repeated: like all-zero, but with a Gear entry
    // other than GEAR[0].
    private static byte[] GenerateSingleByte(int size, ulong seed)
    {
        var bytes = new byte[size];
        bytes.AsSpan().Fill((byte)(1 + new DeterministicPrng(seed).NextInt32(255)));
        return bytes;
    }

    // A 7-byte period: shorter than any Gear predicate window.
    private static byte[] GenerateShortPattern(int size, ulong seed)
    {
        var bytes = new byte[size];
        Span<byte> pattern = stackalloc byte[7];
        new DeterministicPrng(seed).Fill(pattern);

        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = pattern[i % pattern.Length];
        }

        return bytes;
    }

    // 192 KiB random blocks alternating with 192 KiB blocks over a 4-symbol alphabet.
    private static byte[] GenerateAlternatingEntropy(int size, ulong seed)
    {
        const int blockSize = 192 * 1024;
        var bytes = new byte[size];
        var random = new DeterministicPrng(seed);

        for (int offset = 0, block = 0; offset < bytes.Length; offset += blockSize, block++)
        {
            Span<byte> span = bytes.AsSpan(offset, Math.Min(blockSize, bytes.Length - offset));

            if (block % 2 == 0)
            {
                random.Fill(span);
            }
            else
            {
                for (int i = 0; i < span.Length; i++)
                {
                    span[i] = (byte)random.NextInt32(4);
                }
            }
        }

        return bytes;
    }

    // Random data with a zero run of 64 KiB to 1 MiB starting in every 2 MiB
    // region: long low-entropy runs inside high-entropy data.
    private static byte[] GenerateLowEntropyRuns(int size, ulong seed)
    {
        const int regionSize = 2 * 1024 * 1024;
        var bytes = new byte[size];
        var random = new DeterministicPrng(seed);
        random.Fill(bytes);

        for (int region = 0; region < bytes.Length; region += regionSize)
        {
            int start = region + random.NextInt32(regionSize / 2);
            int length = 64 * 1024 + random.NextInt32(960 * 1024);

            if (start < bytes.Length)
            {
                bytes.AsSpan(start, Math.Min(length, bytes.Length - start)).Clear();
            }
        }

        return bytes;
    }
}
