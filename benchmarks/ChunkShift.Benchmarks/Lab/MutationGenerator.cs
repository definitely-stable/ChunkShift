namespace ChunkShift.Benchmarks.Lab;

public static class MutationGenerator
{
    public static MutationResult Apply(byte[] source, MutationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(definition.SizeBytes);

        return definition.Kind switch
        {
            "insert" => Insert(source, definition, prepend: false, append: false),
            "prepend" => Insert(source, definition, prepend: true, append: false),
            "append" => Insert(source, definition, prepend: false, append: true),
            "delete" => Delete(source, definition),
            "overwrite" => Overwrite(source, definition, localized: false),
            "localized-rewrite" => Overwrite(source, definition, localized: true),
            "random-rewrite" => RandomRewrite(source, definition),
            "move" => Move(source, definition),
            "reorder" => Reorder(source, definition),
            _ => throw new InvalidOperationException($"Unknown mutation kind '{definition.Kind}'."),
        };
    }

    public static MutationResult Identity(byte[] source)
    {
        return new MutationResult(source, 0, 0, 0);
    }

    private static MutationResult Insert(byte[] source, MutationDefinition definition, bool prepend, bool append)
    {
        int offset = prepend ? 0 : append ? source.Length : ResolveOffset(source.Length + 1, definition.Seed);
        var target = new byte[checked(source.Length + definition.SizeBytes)];

        source.AsSpan(0, offset).CopyTo(target);
        var random = new DeterministicPrng(definition.Seed);
        random.Fill(target.AsSpan(offset, definition.SizeBytes));
        source.AsSpan(offset).CopyTo(target.AsSpan(offset + definition.SizeBytes));

        return new MutationResult(
            target,
            offset,
            checked(offset + definition.SizeBytes),
            definition.SizeBytes);
    }

    private static MutationResult Delete(byte[] source, MutationDefinition definition)
    {
        int size = Math.Min(definition.SizeBytes, source.Length);
        if (size == source.Length)
        {
            return new MutationResult(Array.Empty<byte>(), 0, 0, size);
        }

        int offset = ResolveOffset(source.Length - size + 1, definition.Seed);
        var target = new byte[source.Length - size];

        source.AsSpan(0, offset).CopyTo(target);
        source.AsSpan(offset + size).CopyTo(target.AsSpan(offset));

        return new MutationResult(target, offset, offset, size);
    }

    private static MutationResult Overwrite(byte[] source, MutationDefinition definition, bool localized)
    {
        int size = Math.Min(definition.SizeBytes, source.Length);
        var target = source.ToArray();
        int offset = ResolveOffset(source.Length - size + 1, definition.Seed);
        var random = new DeterministicPrng(definition.Seed ^ (localized ? 0x10CA1UL : 0x0A11UL));
        random.Fill(target.AsSpan(offset, size));

        return new MutationResult(target, offset, checked(offset + size), size);
    }

    private static MutationResult RandomRewrite(byte[] source, MutationDefinition definition)
    {
        if (source.Length == 0)
        {
            return new MutationResult(Array.Empty<byte>(), 0, 0, 0);
        }

        var target = source.ToArray();
        var random = new DeterministicPrng(definition.Seed);
        int changes = Math.Min(definition.SizeBytes, source.Length);
        int min = source.Length;
        int max = 0;

        for (int i = 0; i < changes; i++)
        {
            int offset = random.NextInt32(source.Length);
            target[offset] ^= (byte)(1 + random.NextInt32(255));
            min = Math.Min(min, offset);
            max = Math.Max(max, offset + 1);
        }

        int actualChanges = 0;
        min = source.Length;
        max = 0;

        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == target[i])
            {
                continue;
            }

            actualChanges++;
            min = Math.Min(min, i);
            max = Math.Max(max, i + 1);
        }

        if (actualChanges == 0)
        {
            min = 0;
            max = 0;
        }

        return new MutationResult(target, min, max, actualChanges);
    }

    private static MutationResult Move(byte[] source, MutationDefinition definition)
    {
        int size = Math.Min(definition.SizeBytes, source.Length / 2);
        if (size == 0)
        {
            return Identity(source);
        }

        var random = new DeterministicPrng(definition.Seed);
        int from = random.NextInt32(source.Length - size + 1);
        int destination = random.NextInt32(source.Length - size + 1);

        if (destination == from)
        {
            destination = (destination + size) % (source.Length - size + 1);
        }

        byte[] moved = source.AsSpan(from, size).ToArray();
        var without = new byte[source.Length - size];
        source.AsSpan(0, from).CopyTo(without);
        source.AsSpan(from + size).CopyTo(without.AsSpan(from));

        int insertAt = Math.Min(destination, without.Length);
        var target = new byte[source.Length];
        without.AsSpan(0, insertAt).CopyTo(target);
        moved.AsSpan().CopyTo(target.AsSpan(insertAt));
        without.AsSpan(insertAt).CopyTo(target.AsSpan(insertAt + size));

        int start = Math.Min(from, insertAt);
        int end = Math.Min(target.Length, Math.Max(from, insertAt) + size);
        return new MutationResult(target, start, end, size);
    }

    private static MutationResult Reorder(byte[] source, MutationDefinition definition)
    {
        int blockSize = Math.Min(definition.SizeBytes, source.Length / 2);
        if (blockSize == 0)
        {
            return Identity(source);
        }

        int blockCount = source.Length / blockSize;
        if (blockCount < 2)
        {
            return Identity(source);
        }

        var random = new DeterministicPrng(definition.Seed);
        int first = random.NextInt32(blockCount);
        int second = random.NextInt32(blockCount - 1);
        if (second >= first)
        {
            second++;
        }

        var target = source.ToArray();
        int firstOffset = first * blockSize;
        int secondOffset = second * blockSize;
        byte[] temp = target.AsSpan(firstOffset, blockSize).ToArray();
        target.AsSpan(secondOffset, blockSize).CopyTo(target.AsSpan(firstOffset, blockSize));
        temp.AsSpan().CopyTo(target.AsSpan(secondOffset, blockSize));

        return new MutationResult(
            target,
            Math.Min(firstOffset, secondOffset),
            checked(Math.Max(firstOffset, secondOffset) + blockSize),
            checked(blockSize * 2L));
    }

    private static int ResolveOffset(int exclusiveMax, ulong seed)
    {
        if (exclusiveMax <= 1)
        {
            return 0;
        }

        return new DeterministicPrng(seed).NextInt32(exclusiveMax);
    }
}
