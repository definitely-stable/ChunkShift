using System.Globalization;

namespace ChunkShift.Benchmarks.PatchLab;

/// <summary>Argument access shared by the patch-lab modes.</summary>
internal static class PatchLabArguments
{
    /// <summary>Returns the value that directly follows an option, or null.</summary>
    internal static string? Value(string[] args, string name) =>
        TryValue(args, name, out string value) ? value : null;

    /// <summary>
    /// Returns whether an option is followed by a value; a following token that
    /// starts with <c>--</c> is the next option, not a value.
    /// </summary>
    internal static bool TryValue(string[] args, string name, out string value)
    {
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (!string.Equals(args[index], name, StringComparison.Ordinal) ||
                args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            value = args[index + 1];
            return true;
        }

        value = string.Empty;
        return false;
    }

    /// <summary>Parses a positive integer option, or returns its fallback.</summary>
    internal static int PositiveInt(string[] args, string name, int fallback)
    {
        string? value = Value(args, name);

        if (value is null)
        {
            return fallback;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) || parsed < 1)
        {
            throw new PatchLabUsageException($"{name} requires a positive integer.");
        }

        return parsed;
    }

    /// <summary>Splits a comma-separated family list; null when the option is absent.</summary>
    internal static string[]? Families(string? value)
    {
        if (value is null)
        {
            return null;
        }

        string[] families = value.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (families.Length == 0)
        {
            throw new PatchLabUsageException("--families needs at least one family id.");
        }

        return families;
    }
}

/// <summary>The validated <c>patch-lab memory</c> command line.</summary>
internal sealed record PatchLabMemoryOptions(
    string Corpus,
    string Output,
    long MinBytes,
    string[]? Families,
    string? RunId,
    string? Work,
    string Lane)
{
    private const long DefaultMinBytes = 1024 * 1024;

    internal static bool TryParse(
        string[] args,
        out PatchLabMemoryOptions options,
        out string? error)
    {
        options = null!;
        error = null;

        if (!PatchLabArguments.TryValue(args, "--corpus", out string corpus))
        {
            error = "patch-lab memory requires --corpus.";
            return false;
        }

        if (!PatchLabArguments.TryValue(args, "--output", out string output))
        {
            error = "patch-lab memory requires --output.";
            return false;
        }

        long minBytes = DefaultMinBytes;
        string? minBytesValue = PatchLabArguments.Value(args, "--min-bytes");

        if (minBytesValue is not null &&
            (!long.TryParse(
                minBytesValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out minBytes) ||
             minBytes < 0))
        {
            error = "--min-bytes requires a non-negative integer.";
            return false;
        }

        string lane = PatchLabArguments.Value(args, "--lane") ?? "csp";

        if (!PatchLabLane.TryParse(lane, out _))
        {
            error = $"Unknown lane '{lane}'; expected one of: {string.Join(", ", PatchLabLane.Names)}, or enc-L{{9|19}}-K{{1|2|4}}-C{{8|16}}-{{copy|attach|prefix}}[-H{{n}}C{{n}}].";
            return false;
        }

        try
        {
            options = new PatchLabMemoryOptions(
                corpus,
                output,
                minBytes,
                PatchLabArguments.Families(PatchLabArguments.Value(args, "--families")),
                PatchLabArguments.Value(args, "--run-id"),
                PatchLabArguments.Value(args, "--work"),
                lane);
            return true;
        }
        catch (PatchLabUsageException exception)
        {
            error = exception.Message;
            return false;
        }
    }
}
