using System.Globalization;

namespace ChunkShift.Benchmarks;

/// <summary>
/// Writes the production chunk sequence of each input file for offline studies
/// (for example the CSP payload-encoding study in
/// <c>benchmarks/scripts/csp_encoding_study.py</c>).
/// <code>
/// chunks --list &lt;list.tsv&gt;
/// </code>
/// Every line of the list is <c>input&lt;TAB&gt;output</c>. The output gets one
/// <c>offset&lt;TAB&gt;length&lt;TAB&gt;chunk-id-hex</c> line per chunk, produced by
/// <see cref="ChunkScanner"/> with the default profile and hash suite, the same
/// sequence a manifest of that input records.
/// </summary>
internal static class ChunkDumpHarness
{
    internal static int Run(string[] args)
    {
        if (args.Length != 2 || !string.Equals(args[0], "--list", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("usage: chunks --list <list.tsv>");
            return 2;
        }

        foreach (string line in File.ReadLines(args[1]))
        {
            if (line.Length == 0)
            {
                continue;
            }

            string[] parts = line.Split('\t');
            if (parts.Length != 2)
            {
                Console.Error.WriteLine($"chunks: expected 'input<TAB>output', got '{line}'");
                return 2;
            }

            DumpAsync(parts[0], parts[1]).GetAwaiter().GetResult();
        }

        return 0;
    }

    private static async Task DumpAsync(string input, string output)
    {
        await using var source = File.OpenRead(input);
        await using var writer = new StreamWriter(output);

        await ChunkScanner.ScanAsync(source, (chunk, _, _) =>
        {
            writer.Write(chunk.Offset.ToString(CultureInfo.InvariantCulture));
            writer.Write('\t');
            writer.Write(chunk.Length.ToString(CultureInfo.InvariantCulture));
            writer.Write('\t');
            writer.WriteLine(chunk.Id.Value.ToHexLower());
            return ValueTask.CompletedTask;
        }).ConfigureAwait(false);
    }
}
