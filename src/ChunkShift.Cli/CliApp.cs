using ChunkShift.Patching;
using ChunkShift.Primitives;

namespace ChunkShift.Cli;

internal static class CliApp
{
    private const int IoBufferSize = 64 * 1024;

    internal static async Task<int> RunAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        // The renderer owns --json; handlers never need to parse the flag.
        int jsonCount = args.Count(static arg => arg == "--json");
        string[] commandArgs = args.Where(static arg => arg != "--json").ToArray();
        CliOutput.Configure(jsonCount != 0, OperationFor(commandArgs));
        if (jsonCount != 0)
        {
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        }

        if (jsonCount > 1)
        {
            return UsageError("--json may be given only once.");
        }

        if (commandArgs.Length == 0)
        {
            if (jsonCount != 0)
            {
                return UsageError("--json requires a command or --help.");
            }

            PrintUsage();
            return 2;
        }

        if (commandArgs[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        try
        {
            return commandArgs[0] switch
            {
                "manifest" => await RunManifestAsync(
                    commandArgs[1..], cancellationToken).ConfigureAwait(false),
                "create" => await RunCreateAsync(
                    commandArgs[1..], cancellationToken).ConfigureAwait(false),
                "inspect" => await RunInspectAsync(
                    commandArgs[1..], cancellationToken).ConfigureAwait(false),
                "patch" => await RunPatchAsync(
                    commandArgs[1..], cancellationToken).ConfigureAwait(false),
                "plan" => await RunPlanAsync(
                    commandArgs[1..], cancellationToken).ConfigureAwait(false),
                "verify" => await RunVerifyAsync(
                    commandArgs[1..], cancellationToken).ConfigureAwait(false),
                _ => UsageError($"Unknown command '{commandArgs[0]}'."),
            };
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            CliOutput.WriteError("cancelled", "Operation cancelled by user.");
            return 130;
        }
        catch (CliUsageException exception)
        {
            return UsageError(exception.Message);
        }
        catch (InvalidDataException exception)
        {
            CliOutput.WriteError("invalid_data", exception.Message);
            return 3;
        }
        catch (EndOfStreamException exception)
        {
            CliOutput.WriteError("invalid_data", exception.Message);
            return 3;
        }
        catch (UnauthorizedAccessException exception)
        {
            CliOutput.WriteError("access_denied", exception.Message);
            return 1;
        }
        catch (IOException exception)
        {
            CliOutput.WriteError("io_failure", exception.Message);
            return 1;
        }
        catch (NotSupportedException exception)
        {
            CliOutput.WriteError("unsupported", exception.Message);
            return 1;
        }
        catch (ArgumentException exception)
        {
            CliOutput.WriteError("invalid_input", exception.Message);
            return 1;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Never print an unstructured exception trace in machine mode.
            CliOutput.WriteError("internal_error",
                "Unexpected CLI failure: " + exception.Message);
            return 1;
        }
    }

    private static string OperationFor(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            return "cli";
        }

        return args[0] switch
        {
            "manifest" when args.Length > 1 => "manifest." + args[1],
            "patch" when args.Length > 1 => "patch." + args[1],
            "create" => "manifest.create",
            "inspect" => "manifest.inspect",
            "verify" => "manifest.verify",
            "plan" => "patch.plan",
            _ => "cli",
        };
    }

    private sealed class CliUsageException(string message) : Exception(message);

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
        bool overwrite = false;
        HashSuiteId? hashSuite = null;

        for (int index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--bidx":
                    includeBlockIndex = true;
                    break;
                case "--overwrite":
                    if (overwrite)
                    {
                        return UsageError("create: --overwrite may be given only once.");
                    }

                    overwrite = true;
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

        if (CheckDestination(manifestPath, overwrite, "create") is string destinationError)
        {
            return UsageError(destinationError);
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
            cancellationToken.ThrowIfCancellationRequested();
            if (CheckDestination(manifestPath, overwrite, "create") is string finalError)
            {
                return UsageError(finalError);
            }

            File.Move(temporaryPath, manifestPath, overwrite);

            CliOutput.Write(new CliReply("manifest.create")
            {
                Manifest = CliManifestData.From(info),
                Publication = new CliPublicationData(manifestPath, true),
            });
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

        var entries = new ChunkInfo[512];
        while (await reader.ReadAsync(
            entries,
            cancellationToken).ConfigureAwait(false) != 0)
        {
        }

        ManifestVerificationResult result =
            reader.VerificationResult
            ?? throw new InvalidOperationException(
                "ManifestReader completed without a verification result.");

        CliOutput.Write(new CliReply("manifest.inspect")
        {
            Manifest = CliManifestData.From(result.Manifest),
            Verification = CliVerificationData.From(result),
        });
        return result.IsValid ? 0 : 3;
    }

    private static async Task<int> RunPlanAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        string? basePath = null;
        string? targetPath = null;

        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--base":
                    if (basePath is not null)
                    {
                        return UsageError(
                            "plan: --base may be given only once.");
                    }

                    if (index + 1 == args.Length)
                    {
                        return UsageError(
                            "plan: --base requires <base.csm>.");
                    }

                    basePath = args[++index];
                    break;
                case "--target":
                    if (targetPath is not null)
                    {
                        return UsageError(
                            "plan: --target may be given only once.");
                    }

                    if (index + 1 == args.Length)
                    {
                        return UsageError(
                            "plan: --target requires <target.csm>.");
                    }

                    targetPath = args[++index];
                    break;
                default:
                    return UsageError(
                        $"Unknown plan option '{args[index]}'.");
            }
        }

        if (basePath is null || targetPath is null)
        {
            return UsageError(
                "plan requires --base <base.csm> and --target <target.csm>.");
        }

        PatchPlan plan;

        await using (FileStream baseManifest =
            OpenRead(Path.GetFullPath(basePath)))
        await using (FileStream targetManifest =
            OpenRead(Path.GetFullPath(targetPath)))
        {
            plan = await ChunkPatch.PlanAsync(
                baseManifest,
                targetManifest,
                cancellationToken).ConfigureAwait(false);
        }

        CliPlanData report = CliPlanData.From(plan);
        CliOutput.Write(new CliReply("patch.plan") { Plan = report });
        return report.Valid ? 0 : 3;
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

        CliOutput.Write(new CliReply("manifest.verify")
        {
            Manifest = CliManifestData.From(result.Manifest),
            Verification = CliVerificationData.From(result),
        });
        return result.IsValid ? 0 : 3;
    }


    // A2 canonical grouped commands are thin adapters to the exact existing
    // SDK-backed handlers. In particular, they do not duplicate file staging,
    // alias checks or CSP/CSM validation and cannot relax A1 publication.
    private static async Task<int> RunManifestAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            return UsageError("manifest requires 'create', 'inspect' or 'verify'.");
        }

        if (args.Length == 1 && IsHelpArgument(args[0]))
        {
            PrintManifestUsage();
            return 0;
        }

        if (args.Length == 2 && IsHelpArgument(args[1]) &&
            args[0] is "create" or "inspect" or "verify")
        {
            PrintManifestUsage();
            return 0;
        }

        return args[0] switch
        {
            "create" => await RunManifestCreateAsync(
                args[1..], cancellationToken).ConfigureAwait(false),
            "inspect" => await RunInspectAsync(
                args[1..], cancellationToken).ConfigureAwait(false),
            "verify" => await RunManifestVerifyAsync(
                args[1..], cancellationToken).ConfigureAwait(false),
            _ => UsageError($"Unknown manifest command '{args[0]}'."),
        };
    }

    private static async Task<int> RunManifestCreateAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        var options = NewOptions("-o");
        var positionals = new List<string>();
        var flags = new HashSet<string>(StringComparer.Ordinal);

        if (ParseOptions(
            "manifest create", args, options, positionals, flags,
            "--bidx", "--sha256", "--blake3", "--overwrite") is string error)
        {
            return UsageError(error);
        }

        if (positionals.Count != 1 || options["-o"] is not string output)
        {
            return UsageError("manifest create requires <content> and -o <manifest>.");
        }

        if (flags.Contains("--sha256") && flags.Contains("--blake3"))
        {
            return UsageError("manifest create: --sha256 and --blake3 are mutually exclusive.");
        }

        var forwarded = new List<string> { positionals[0], output };
        foreach (string flag in new[] { "--bidx", "--sha256", "--blake3", "--overwrite" })
        {
            if (flags.Contains(flag))
            {
                forwarded.Add(flag);
            }
        }

        return await RunCreateAsync(
            forwarded.ToArray(), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> RunManifestVerifyAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        var options = NewOptions("--content");
        var positionals = new List<string>();
        var flags = new HashSet<string>(StringComparer.Ordinal);

        if (ParseOptions(
            "manifest verify", args, options, positionals, flags) is string error)
        {
            return UsageError(error);
        }

        if (positionals.Count != 1)
        {
            return UsageError("manifest verify requires exactly <manifest> [--content <content>].");
        }

        string[] forwarded = options["--content"] is string content
            ? [positionals[0], content]
            : [positionals[0]];

        return await RunVerifyAsync(
            forwarded, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> RunPatchPlanAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        var options = NewOptions("--base-manifest", "--target-manifest");
        var positionals = new List<string>();
        var flags = new HashSet<string>(StringComparer.Ordinal);

        if (ParseOptions(
            "patch plan", args, options, positionals, flags) is string error)
        {
            return UsageError(error);
        }

        if (positionals.Count != 0 ||
            options["--base-manifest"] is not string baseManifest ||
            options["--target-manifest"] is not string targetManifest)
        {
            return UsageError(
                "patch plan requires --base-manifest <base.csm> and " +
                "--target-manifest <target.csm> with no positional arguments.");
        }

        return await RunPlanAsync(
            ["--base", baseManifest, "--target", targetManifest],
            cancellationToken).ConfigureAwait(false);
    }

    private static bool IsHelpArgument(string argument) =>
        argument is "-h" or "--help" or "help";

    private static void PrintManifestUsage()
    {
        CliOutput.WriteHelp("manifest",
            """
            ChunkShift manifest commands

            Usage:
              chunkshift manifest create <content> -o <manifest>
                                         [--bidx] [--sha256|--blake3] [--overwrite]
              chunkshift manifest inspect <manifest>
              chunkshift manifest verify <manifest> [--content <content>]
            """);
    }

    private static void PrintPatchUsage()
    {
        CliOutput.WriteHelp("patch",
            """
            ChunkShift patch commands

            Usage:
              chunkshift patch plan --base-manifest <base.csm>
                                    --target-manifest <target.csm>
              chunkshift patch create --target-manifest <target.csm>
                                      --target <target> -o <patch.csp>
                                      [--base-manifest <base.csm> --base <base>]
                                      [--overwrite]
              chunkshift patch apply <patch.csp> -o <output>
                                     [--base-manifest <base.csm> --base <base>]
                                     [--overwrite|--in-place]
            """);
    }

    private static async Task<int> RunPatchAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            return UsageError(
                "patch requires 'plan', 'create' or 'apply'.");
        }

        if ((args.Length == 1 && IsHelpArgument(args[0])) ||
            (args.Length == 2 && IsHelpArgument(args[1]) &&
             args[0] is "plan" or "create" or "apply"))
        {
            PrintPatchUsage();
            return 0;
        }

        return args[0] switch
        {
            "plan" => await RunPatchPlanAsync(
                args[1..],
                cancellationToken).ConfigureAwait(false),
            "create" => await RunPatchCreateAsync(
                args[1..],
                cancellationToken).ConfigureAwait(false),
            "apply" => await RunPatchApplyAsync(
                args[1..],
                cancellationToken).ConfigureAwait(false),
            _ => UsageError(
                $"Unknown patch command '{args[0]}'."),
        };
    }

    private static async Task<int> RunPatchCreateAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        var options = NewOptions("--base-manifest", "--base", "--target-manifest", "--target", "-o");
        var positionals = new List<string>();
        var flags = new HashSet<string>(StringComparer.Ordinal);

        if (ParseOptions("patch create", args, options, positionals, flags, "--overwrite") is string error)
        {
            return UsageError(error);
        }

        if (positionals.Count != 0)
        {
            return UsageError($"Unknown patch create option '{positionals[0]}'.");
        }

        string? baseManifestPath = options["--base-manifest"];
        string? basePath = options["--base"];
        string? targetManifestPath = options["--target-manifest"];
        string? targetPath = options["--target"];
        string? outputPath = options["-o"];
        bool overwrite = flags.Contains("--overwrite");

        if (targetManifestPath is null || targetPath is null || outputPath is null)
        {
            return UsageError(
                "patch create requires --target-manifest <target.csm>, " +
                "--target <target> and -o <patch.csp>.");
        }

        if ((baseManifestPath is null) != (basePath is null))
        {
            return UsageError(
                "patch create: --base-manifest and --base must be given together.");
        }

        string outputFullPath = Path.GetFullPath(outputPath);

        foreach ((string option, string? input) in new[]
        {
            ("--target-manifest", targetManifestPath),
            ("--target", targetPath),
            ("--base-manifest", baseManifestPath),
            ("--base", basePath),
        })
        {
            if (input is not null &&
                PathsReferToSameFile(outputFullPath, Path.GetFullPath(input)))
            {
                return UsageError(
                    $"patch create: -o must not be the same file as {option}.");
            }
        }

        if (CheckDestination(outputFullPath, overwrite, "patch create") is string destinationError)
        {
            return UsageError(destinationError);
        }

        string? directory = Path.GetDirectoryName(outputFullPath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException(
                "Patch destination must resolve to a directory.",
                nameof(args));
        }

        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(outputFullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            PatchInfo info;

            await using (FileStream targetManifest = OpenRead(Path.GetFullPath(targetManifestPath)))
            await using (FileStream targetContent = OpenRead(Path.GetFullPath(targetPath)))
            await using (FileStream destination = new(
                temporaryPath,
                new FileStreamOptions
                {
                    Access = FileAccess.Write,
                    Mode = FileMode.CreateNew,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous,
                    BufferSize = IoBufferSize,
                }))
            {
                if (baseManifestPath is not null && basePath is not null)
                {
                    await using FileStream baseManifest = OpenRead(Path.GetFullPath(baseManifestPath));
                    await using FileStream baseContent = OpenRead(Path.GetFullPath(basePath));

                    info = await ChunkPatch.CreateAsync(
                        baseManifest,
                        baseContent,
                        targetManifest,
                        targetContent,
                        destination,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    info = await ChunkPatch.CreateAsync(
                        targetManifest,
                        targetContent,
                        destination,
                        cancellationToken).ConfigureAwait(false);
                }

                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            // Report only after the patch is published, so a failed move is
            // never preceded by a success-looking summary.
            cancellationToken.ThrowIfCancellationRequested();
            if (CheckDestination(outputFullPath, overwrite, "patch create") is string finalError)
            {
                return UsageError(finalError);
            }

            File.Move(temporaryPath, outputFullPath, overwrite);

            CliOutput.Write(new CliReply("patch.create")
            {
                Patch = CliPatchData.From(info),
                Publication = new CliPublicationData(outputFullPath, true),
            });
            return 0;
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static async Task<int> RunPatchApplyAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        var options = NewOptions("-o", "--base-manifest", "--base");
        var positionals = new List<string>();
        var flags = new HashSet<string>(StringComparer.Ordinal);

        if (ParseOptions(
            "patch apply", args, options, positionals, flags, "--overwrite", "--in-place") is string error)
        {
            return UsageError(error);
        }

        if (positionals.Count > 1)
        {
            return UsageError($"Unknown patch apply option '{positionals[1]}'.");
        }

        string? outputPath = options["-o"];
        string? baseManifestPath = options["--base-manifest"];
        string? basePath = options["--base"];
        bool overwrite = flags.Contains("--overwrite");
        bool inPlace = flags.Contains("--in-place");

        if (positionals.Count == 0 || outputPath is null)
        {
            return UsageError("patch apply requires <patch.csp> and -o <output>.");
        }

        if ((baseManifestPath is null) != (basePath is null))
        {
            return UsageError(
                "patch apply: --base-manifest and --base must be given together.");
        }

        string patchFullPath = Path.GetFullPath(positionals[0]);
        string outputFullPath = Path.GetFullPath(outputPath);

        if (PathsReferToSameFile(outputFullPath, patchFullPath))
        {
            return UsageError(
                "patch apply: -o must not be the same file as <patch.csp>.");
        }

        if (baseManifestPath is not null &&
            PathsReferToSameFile(outputFullPath, Path.GetFullPath(baseManifestPath)))
        {
            return UsageError("patch apply: -o must not be the same file as --base-manifest.");
        }

        if (overwrite && inPlace)
        {
            return UsageError("patch apply: --overwrite and --in-place are mutually exclusive.");
        }

        bool matchesBase = basePath is not null &&
            PathsReferToSameFile(outputFullPath, Path.GetFullPath(basePath));
        if (matchesBase && !inPlace)
        {
            return UsageError("patch apply: updating the base requires --in-place.");
        }

        if (inPlace && (basePath is null ||
            !PathsHaveSameSpelling(outputFullPath, Path.GetFullPath(basePath))))
        {
            return UsageError(
                "patch apply: --in-place requires -o to name exactly the --base path.");
        }

        bool mayReplace = overwrite || inPlace;
        if (CheckDestination(outputFullPath, mayReplace, "patch apply") is string destinationError)
        {
            return UsageError(destinationError);
        }

        // ApplyAsync always replaces its destination on success. In the
        // no-clobber case the SDK writes to a private sibling path; only
        // after successful verification do we atomically move that file to
        // the user-visible destination with overwrite:false.
        string? stagedOutput = mayReplace
            ? null
            : Path.Combine(
                Path.GetDirectoryName(outputFullPath)!,
                $".{Path.GetFileName(outputFullPath)}.{Guid.NewGuid():N}.cli-stage");
        string sdkOutput = stagedOutput ?? outputFullPath;

        PatchApplyResult result;
        try
        {
            await using FileStream patch = OpenRead(patchFullPath);

            if (baseManifestPath is not null && basePath is not null)
            {
                await using FileStream baseManifest = OpenRead(Path.GetFullPath(baseManifestPath));

                // Explicit in-place publication requires delete sharing on Windows.
                await using FileStream baseContent = OpenReadShared(Path.GetFullPath(basePath));

                result = await ChunkPatch.ApplyAsync(
                    patch,
                    baseManifest,
                    baseContent,
                    sdkOutput,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                result = await ChunkPatch.ApplyAsync(
                    patch,
                    sdkOutput,
                    cancellationToken).ConfigureAwait(false);
            }

            if (result.IsApplied && stagedOutput is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // The final move, not a prior File.Exists check, enforces
                // the no-clobber rule if another file appears meanwhile.
                File.Move(stagedOutput, outputFullPath, overwrite: false);
            }
        }
        finally
        {
            if (stagedOutput is not null)
            {
                TryDelete(stagedOutput);
            }
        }

        CliOutput.Write(new CliReply("patch.apply")
        {
            Apply = CliApplyData.From(result),
            Publication = result.IsApplied
                ? new CliPublicationData(outputFullPath, true)
                : null,
        });
        return result.IsApplied ? 0 : 3;
    }

    private static Dictionary<string, string?> NewOptions(params string[] names) =>
        names.ToDictionary(static name => name, static _ => (string?)null, StringComparer.Ordinal);

    /// <summary>
    /// Reads "<c>option value</c>" pairs into <paramref name="options"/>, each
    /// option at most once; an argument that is not a known option and does not
    /// start with '-' goes to <paramref name="positionals"/>.
    /// </summary>
    /// <returns>A usage error message, or <see langword="null"/>.</returns>
    private static string? ParseOptions(
        string command,
        string[] args,
        Dictionary<string, string?> options,
        List<string> positionals,
        HashSet<string> flags,
        params string[] supportedFlags)
    {
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];

            if (supportedFlags.Contains(argument, StringComparer.Ordinal))
            {
                if (!flags.Add(argument))
                {
                    return $"{command}: {argument} may be given only once.";
                }

                continue;
            }

            if (!options.TryGetValue(argument, out string? value))
            {
                if (argument.StartsWith('-'))
                {
                    return $"Unknown {command} option '{argument}'.";
                }

                positionals.Add(argument);
                continue;
            }

            if (value is not null)
            {
                return $"{command}: {argument} may be given only once.";
            }

            if (index + 1 == args.Length)
            {
                return $"{command}: {argument} requires a value.";
            }

            if (args[index + 1].StartsWith('-'))
            {
                // Paths beginning with '-' can be spelled "./-name". Do not
                // consume another option as the value of this option.
                return $"{command}: {argument} requires a value.";
            }

            options[argument] = args[++index];
        }

        return null;
    }

    private static StringComparison FilePathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static bool PathsHaveSameSpelling(string first, string second) =>
        string.Equals(first, second, FilePathComparison);

    private static bool PathsReferToSameFile(string first, string second)
    {
        // Resolve existing file links and symlinked parent directories. Hard
        // links still require platform-specific file IDs and are not detected.
        return string.Equals(
            ResolveFinalPath(first),
            ResolveFinalPath(second),
            FilePathComparison);
    }

    private static string ResolveFinalPath(string path)
    {
        string fullPath = Path.GetFullPath(path);

        try
        {
            string resolvedParent = ResolveDirectoryPath(Path.GetDirectoryName(fullPath)!);
            string candidate = Path.Combine(resolvedParent, Path.GetFileName(fullPath));
            FileSystemInfo? target = File.ResolveLinkTarget(candidate, returnFinalTarget: true);
            return target is null ? candidate : Path.GetFullPath(target.FullName);
        }
        catch (IOException)
        {
            return fullPath;
        }
        catch (UnauthorizedAccessException)
        {
            return fullPath;
        }
    }

    private static string ResolveDirectoryPath(string path)
    {
        string? parent = Path.GetDirectoryName(path);
        if (parent is null || parent == path)
        {
            return path;
        }

        string candidate = Path.Combine(
            ResolveDirectoryPath(parent),
            Path.GetFileName(path));
        FileSystemInfo? target = Directory.ResolveLinkTarget(
            candidate, returnFinalTarget: true);
        return target is null ? candidate : Path.GetFullPath(target.FullName);
    }

    private static string? CheckDestination(
        string outputPath, bool mayReplace, string command)
    {
        // LinkTarget also identifies dangling symlinks: File.Exists does not.
        // Never replace through a destination symlink, even with --overwrite.
        if (new FileInfo(outputPath).LinkTarget is not null)
        {
            return $"{command}: destination must not be a symbolic link.";
        }

        if (Directory.Exists(outputPath))
        {
            return $"{command}: destination is a directory.";
        }

        if (!mayReplace && File.Exists(outputPath))
        {
            return $"{command}: destination exists; pass --overwrite to replace it.";
        }

        return null;
    }

    internal static FileStream OpenRead(string path) =>
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

    private static FileStream OpenReadShared(string path) =>
        new(
            path,
            new FileStreamOptions
            {
                Access = FileAccess.Read,
                Mode = FileMode.Open,
                Share = FileShare.Read | FileShare.Delete,
                Options =
                    FileOptions.Asynchronous
                    | FileOptions.SequentialScan,
                BufferSize = IoBufferSize,
            });

    private static void SetHashSuite(
        ref HashSuiteId? current,
        HashSuiteId requested)
    {
        if (current is not null &&
            current != requested)
        {
            throw new CliUsageException(
                "Choose only one of --sha256 or --blake3.");
        }

        current = requested;
    }

    private static int UsageError(string message)
    {
        CliOutput.WriteError("usage", message);
        if (!CliOutput.IsJson)
        {
            PrintUsage();
        }
        return 2;
    }

    private static void PrintUsage()
    {
        CliOutput.WriteHelp("root",
            """
            ChunkShift — manifest and binary patch utility

            Usage:
              chunkshift manifest create <content> -o <manifest>
                                         [--bidx] [--sha256|--blake3] [--overwrite]
              chunkshift manifest inspect <manifest>
              chunkshift manifest verify <manifest> [--content <content>]
              chunkshift patch plan --base-manifest <base.csm> --target-manifest <target.csm>
              chunkshift patch create --target-manifest <target.csm>
                                      --target <target> -o <patch.csp>
                                      [--base-manifest <base.csm> --base <base>]
                                      [--overwrite]
              chunkshift patch apply <patch.csp> -o <output>
                                     [--base-manifest <base.csm> --base <base>]
                                     [--overwrite|--in-place]

            Internal legacy aliases (unpublished, retained for CI migration):
              chunkshift create <content> <manifest> [--bidx] [--sha256|--blake3] [--overwrite]
              chunkshift inspect <manifest>
              chunkshift plan --base <base.csm> --target <target.csm>
              chunkshift verify <manifest> [content]

            Use 'chunkshift manifest --help' or 'chunkshift patch --help'
            for command-group help. --json emits a versioned UTF-8
            result on stdout or a versioned error on stderr.

            plan prints a read-only chunk reuse report for a base and a target
            manifest. unique-missing-bytes is the lower bound of the payload a
            chunk-granular patch would carry, before any CSP container overhead.

            patch create writes a CSP patch that reconstructs the target over
            the base; without --base-manifest and --base the patch is
            self-contained. patch apply verifies the patch and replaces
            <output> with the reconstructed target. Creating files refuses
            existing destinations unless --overwrite is given. --in-place
            explicitly permits patch apply to replace the --base file when
            -o names that same path. Destination symlinks are refused.

            Exit codes:
              0  success / verification valid
              1  operational error
              2  command-line usage error
              3  verification/integrity mismatch
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
