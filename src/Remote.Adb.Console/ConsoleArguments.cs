namespace Remote.Adb.Console;

/// <summary>
/// The console's pure command-line parsers: <c>--verbose</c> stripping, the <c>--key value</c> option syntax shared
/// by <c>emulator edit</c> and <c>emulator create</c>, and positional operands. No I/O, so they're unit-testable.
/// </summary>
internal static class ConsoleArguments
{
    /// <summary>The flag (accepted anywhere on the command line) that turns on verbose tool logging.</summary>
    public const string VerboseFlag = "--verbose";

    private const string CreateUsage = "Usage: emulator create -n <name> -k <package> -d <device> [--<key> <value>…]";

    private const string OptionPrefix = "--";

    /// <summary>
    /// The positional operand after the subcommand (<c>args[1]</c>), or <see langword="null"/> when it's missing or blank.
    /// </summary>
    /// <param name="args">The subcommand's arguments, the subcommand itself first (e.g. <c>["start", "Pixel_8"]</c>).</param>
    public static string? Operand(IReadOnlyList<string> args) =>
        args.Count > 1 && !string.IsNullOrWhiteSpace(args[1]) ? args[1] : null;

    /// <summary>Removes every <see cref="VerboseFlag"/> from <paramref name="args"/>, reporting whether there was one.</summary>
    /// <param name="args">The raw command line.</param>
    /// <param name="verbose">Whether <see cref="VerboseFlag"/> was present.</param>
    public static string[] StripVerbose(IReadOnlyList<string> args, out bool verbose)
    {
        verbose = args.Contains(VerboseFlag);
        return [.. args.Where(argument => argument != VerboseFlag)];
    }

    /// <summary>
    /// Parses <c>-n &lt;name&gt; -k &lt;package&gt; -d &lt;device&gt;</c> plus optional <c>--key value</c> (or
    /// <c>--key=value</c>) config overrides, in any order.
    /// </summary>
    /// <param name="tokens">The arguments after <c>emulator create</c>.</param>
    /// <param name="arguments">The parsed arguments, when parsing succeeds.</param>
    /// <param name="error">Why parsing failed (a usage line when a required flag is missing).</param>
    public static bool TryParseCreate(IReadOnlyList<string> tokens, out AvdCreateArguments? arguments, out string? error)
    {
        arguments = null;
        string? name = null;
        string? package = null;
        string? device = null;
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token is "-n" or "-k" or "-d")
            {
                // A flag's value is the next token — unless that's missing or another flag.
                if (i + 1 >= tokens.Count || tokens[i + 1].StartsWith('-'))
                {
                    error = $"Missing value for {token}.";
                    return false;
                }

                var value = tokens[++i];
                switch (token)
                {
                    case "-n":
                        name = value;
                        break;
                    case "-k":
                        package = value;
                        break;
                    default:
                        device = value;
                        break;
                }

                continue;
            }

            if (!TryReadOption(tokens, ref i, out var key, out var optionValue, out error))
            {
                return false;
            }

            if (optionValue is null)
            {
                error = $"Missing value for --{key}.";
                return false;
            }

            overrides[key] = optionValue;
        }

        if (name is null || package is null || device is null)
        {
            error = CreateUsage;
            return false;
        }

        arguments = new AvdCreateArguments(name, package, device, overrides);
        error = null;
        return true;
    }

    /// <summary>
    /// Parses <c>--key value</c> (or <c>--key=value</c>) pairs into config changes; a bare <c>--key</c> (followed by
    /// another option or nothing) becomes a removal.
    /// </summary>
    /// <param name="tokens">The arguments after <c>emulator edit &lt;avd-name&gt;</c>.</param>
    /// <param name="edits">The parsed edits, when parsing succeeds.</param>
    /// <param name="error">Why parsing failed.</param>
    public static bool TryParseEdits(IReadOnlyList<string> tokens, out ConfigEdits? edits, out string? error)
    {
        edits = null;
        var changes = new Dictionary<string, string>(StringComparer.Ordinal);
        var removals = new List<string>();

        for (var i = 0; i < tokens.Count; i++)
        {
            if (!TryReadOption(tokens, ref i, out var key, out var value, out error))
            {
                return false;
            }

            if (value is null)
            {
                removals.Add(key);
            }
            else
            {
                changes[key] = value;
            }
        }

        edits = new ConfigEdits(changes, removals);
        error = null;
        return true;
    }

    // Reads the option at tokens[index] — `--key=value`, `--key value` (consuming the value, advancing index), or a
    // bare `--key` (value null) when the next token is missing or is itself an option. A value that starts with `--`
    // can only be given with the `=` form. Fails for anything that isn't `--<key>`.
    private static bool TryReadOption(IReadOnlyList<string> tokens, ref int index, out string key, out string? value, out string? error)
    {
        var token = tokens[index];
        key = string.Empty;
        value = null;

        var separator = token.IndexOf('=');
        var name = separator < 0 ? token : token[..separator];
        if (!name.StartsWith(OptionPrefix, StringComparison.Ordinal) || name.Length == OptionPrefix.Length)
        {
            error = $"Expected --<key>, got '{token}'.";
            return false;
        }

        key = name[OptionPrefix.Length..];
        error = null;

        if (separator >= 0)
        {
            value = token[(separator + 1)..];
        }
        else if (index + 1 < tokens.Count && !tokens[index + 1].StartsWith(OptionPrefix, StringComparison.Ordinal))
        {
            value = tokens[++index];
        }

        return true;
    }
}
