using System.CommandLine;
using System.Diagnostics;

// CLI140-A2 disposable parser prototype, deliberately NOT a product command
// implementation. Uses the public System.CommandLine 3.0.0 API in a small
// representative nested grammar; no IO, SDK or publication side effects.
RootCommand root = Build();

if (args is ["--probe"])
{
    string[] valid = ["manifest", "create", "input.bin", "-o", "out.csm"];
    string[] invalid = ["patch", "plan", "--base-manifest", "a.csm", "--base-manifest", "b.csm"];
    const int iterations = 10_000;

    var watch = Stopwatch.StartNew();
    for (int i = 0; i < iterations; i++)
    {
        if (root.Parse(valid).Errors.Count != 0)
        {
            throw new InvalidOperationException("Valid representative command was rejected.");
        }
    }

    watch.Stop();
    Console.WriteLine($"version=System.CommandLine-3.0.0");
    Console.WriteLine($"parse-count={iterations}");
    Console.WriteLine($"parse-ms={watch.Elapsed.TotalMilliseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
    Console.WriteLine($"duplicate-option-errors={root.Parse(invalid).Errors.Count}");
    return 0;
}

return root.Parse(args).Invoke();

static RootCommand Build()
{
    var root = new RootCommand("CLI140-A2 isolated System.CommandLine parser prototype");
    var manifest = new Command("manifest");
    var create = new Command("create");
    var content = new Argument<string>("content");
    var output = new Option<string>("-o");
    create.Arguments.Add(content);
    create.Options.Add(output);
    create.SetAction(parse =>
    {
        if (parse.GetValue(content) is null || parse.GetValue(output) is null)
        {
            return 2;
        }

        Console.WriteLine("parsed=manifest.create");
        return 0;
    });

    manifest.Subcommands.Add(create);
    root.Subcommands.Add(manifest);

    var patch = new Command("patch");
    var plan = new Command("plan");
    var baseManifest = new Option<string>("--base-manifest");
    var targetManifest = new Option<string>("--target-manifest");
    plan.Options.Add(baseManifest);
    plan.Options.Add(targetManifest);
    plan.SetAction(parse =>
    {
        if (parse.GetValue(baseManifest) is null ||
            parse.GetValue(targetManifest) is null)
        {
            return 2;
        }

        Console.WriteLine("parsed=patch.plan");
        return 0;
    });

    patch.Subcommands.Add(plan);
    root.Subcommands.Add(patch);
    return root;
}
