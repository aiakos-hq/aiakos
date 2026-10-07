using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;

using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;

namespace Aiakos.Cli;

public sealed record CliRequest(string Command, string Instance, bool Json, bool Verbose,
    IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, IReadOnlyList<string>> Options);

public sealed record CliParseResult(CliRequest? Request, string? Error, string? Help, bool Version);

public static partial class CliCommandLine
{
    private const string Invalid = "Invalid command line.";
    private const string ExitCodes = "Exit codes: 0 done; 1 unsuccessful outcome; 2 invalid input; 3 instance unavailable; 4 incompatible version; 5 authentication failed.";
    private const string RootDescription = "Aiakos: a file-defined control plane for teams of AI coding agents.";
    private static readonly Regex InstancePattern = InstanceRegex();

    public static CliParseResult Parse(string[] args, string? environmentInstance)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Any(static argument => argument is null))
            return InvalidResult();

        var tree = CreateTree();
        var parseArgs = args.Length == 0 ? new[] { "--help" } : args;
        var parse = tree.Root.Parse(parseArgs, new ParserConfiguration { ResponseFileTokenReplacer = null });
        if (parse.Errors.Count != 0 || parse.UnmatchedTokens.Count != 0)
            return InvalidResult();

        var command = parse.CommandResult.Command;
        var path = tree.Paths[command];
        foreach (var option in tree.Options)
        {
            var result = parse.GetResult(option);
            if (result is null || result.Implicit)
                continue;
            if (option.Name == "--seat")
            {
                if (result.IdentifierTokenCount != result.Tokens.Count)
                    return InvalidResult();
            }
            else if (result.IdentifierTokenCount > 1)
                return InvalidResult();
        }

        var literal = false;
        foreach (var token in parse.Tokens)
        {
            if (token.Type == TokenType.DoubleDash)
                literal = true;
            else if (!literal && token.Type == TokenType.Argument && token.Value.StartsWith('-') &&
                     !(path == "send" && token.Value == "-"))
                return InvalidResult();
        }

        var explicitInstance = parse.GetValue(tree.Instance);
        if (explicitInstance is { Length: 0 } ||
            !tree.TryReadOptions(path, parse, out var options) || !ValidateOptionValues(options))
            return InvalidResult();

        var positionals = tree.PositionalArguments(command, parse);
        if (positionals.Any(static value => value.Length == 0))
            return InvalidResult();

        var versionRequested = parse.GetValue(tree.Version);
        if (versionRequested && path.Length != 0)
            return InvalidResult();
        if (parse.GetValue(tree.Help) || path == "instance")
            return new CliParseResult(null, null, tree.RenderHelp(command), false);
        if (versionRequested)
            return new CliParseResult(null, null, null, true);
        if (path.Length == 0 || (path is "capture" or "send" or "attach" && positionals.Count == 0))
            return InvalidResult();

        var selectedInstance = explicitInstance ??
            (string.IsNullOrEmpty(environmentInstance) ? "release" : environmentInstance);
        if (!InstancePattern.IsMatch(selectedInstance))
            return InvalidResult();

        var immutableOptions = new ReadOnlyDictionary<string, IReadOnlyList<string>>(
            options.ToDictionary(static pair => pair.Key,
                static pair => (IReadOnlyList<string>)Array.AsReadOnly(pair.Value.ToArray()),
                StringComparer.Ordinal));
        var request = new CliRequest(path, selectedInstance, parse.GetValue(tree.Json),
            parse.GetValue(tree.Verbose), Array.AsReadOnly(positionals.ToArray()), immutableOptions);
        return new CliParseResult(request, null, null, false);
    }

    private static bool ValidateOptionValues(IReadOnlyDictionary<string, string[]> options)
    {
        foreach (var (name, values) in options)
        {
            if (values.Any(static value => value.Length == 0))
            {
                return false;
            }

            if (name is "timeout" or "port-base" or "lines")
            {
                if (values.Length != 1 || !int.TryParse(values[0], NumberStyles.None,
                        CultureInfo.InvariantCulture, out var number))
                {
                    return false;
                }

                var (minimum, maximum) = name switch
                {
                    "timeout" => (1, 86400),
                    "port-base" => (1, 51535),
                    _ => (1, 10000)
                };
                if (number < minimum || number > maximum)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static CliParseResult InvalidResult() => new(null, Invalid, null, false);

    private static string EnsureFinalLf(string text) => text.EndsWith('\n') ? text : text + "\n";

    [GeneratedRegex(@"\A[a-z][a-z0-9-]{0,62}\z", RegexOptions.CultureInvariant)]
    private static partial Regex InstanceRegex();

    private static CommandTree CreateTree()
    {
        var root = new RootCommand(RootDescription) { TreatUnmatchedTokensAsErrors = true };
        root.Options.Clear();
        root.SetAction(static _ => { });
        var help = new Option<bool>("--help", "-h") { Recursive = true, Arity = ArgumentArity.Zero, Description = "Show help information." };
        var instance = new Option<string>("--instance") { Recursive = true, Description = "Released instance name." };
        var json = new Option<bool>("--json") { Recursive = true, Arity = ArgumentArity.Zero, Description = "Write JSON output." };
        var verbose = new Option<bool>("--verbose", "-v") { Recursive = true, Arity = ArgumentArity.Zero, Description = "Enable verbose output." };
        var version = new Option<bool>("--version") { Arity = ArgumentArity.Zero, Description = "Show version information." };
        root.Options.Add(help);
        root.Options.Add(instance);
        root.Options.Add(json);
        root.Options.Add(verbose);
        root.Options.Add(version);

        var paths = new Dictionary<Command, string> { [root] = string.Empty };
        var local = new Dictionary<string, List<Option>>(StringComparer.Ordinal);
        var positional = new Dictionary<Command, List<Argument>>(ReferenceEqualityComparer.Instance)
        {
            [root] = []
        };

        Command AddCommand(Command parent, string name, string path, string description)
        {
            var command = new Command(name, description) { TreatUnmatchedTokensAsErrors = true };
            parent.Subcommands.Add(command);
            paths.Add(command, path);
            local.TryAdd(path, []);
            positional.TryAdd(command, []);
            return command;
        }

        Option<T> AddOption<T>(Command command, string path, string name, string description,
            string? defaultValue = null, bool flag = false, string[]? aliases = null)
        {
            var option = new Option<T>(name, aliases ?? []) { Description = description };
            if (defaultValue is not null && option is Option<string> stringOption)
            {
                stringOption.DefaultValueFactory = _ => defaultValue;
            }

            if (flag)
            {
                option.Arity = ArgumentArity.Zero;
            }

            command.Options.Add(option);
            local[path].Add(option);
            return option;
        }

        Argument<T> AddArgument<T>(Command command, string name, ArgumentArity arity, bool array = false)
        {
            var argument = new Argument<T>(name) { Arity = arity };
            if (array && argument is Argument<string[]> strings)
            {
                strings.DefaultValueFactory = _ => [];
            }

            command.Arguments.Add(argument);
            positional[command].Add(argument);
            return argument;
        }

        var instanceCommand = AddCommand(root, "instance", "instance", "Manage a released instance.");
        instanceCommand.SetAction(static _ => { });
        var init = AddCommand(instanceCommand, "init", "instance init", "Initialize instance configuration.");
        AddOption<string>(init, "instance init", "--port-base", "Base port for the instance.");
        AddOption<string>(init, "instance init", "--distro", "WSL distribution name.", "Ubuntu");
        AddOption<string>(init, "instance init", "--node-id", "Node identifier.", "wsl-local");
        AddOption<string>(init, "instance init", "--database-url-file", "Database URL file.");
        AddOption<bool>(init, "instance init", "--force", "Replace existing configuration.", flag: true);
        var start = AddCommand(instanceCommand, "start", "instance start", "Start the instance host.");
        AddOption<string>(start, "instance start", "--timeout", "Timeout in seconds.", "90");
        var stop = AddCommand(instanceCommand, "stop", "instance stop", "Stop the instance host.");
        AddOption<bool>(stop, "instance stop", "--keep-seats", "Keep seats running.", flag: true);
        AddOption<bool>(stop, "instance stop", "--keep-database", "Keep the database running.", flag: true);
        AddCommand(instanceCommand, "status", "instance status", "Show instance status.");
        AddCommand(instanceCommand, "run", "instance run", "Run the instance host in the foreground.");

        var up = AddCommand(root, "up", "up", "Validate a rig and launch seats.");
        AddArgument<string>(up, "rig-dir", ArgumentArity.ZeroOrOne);
        AddOption<string>(up, "up", "--env", "Environment file.");
        var seats = AddOption<string[]>(up, "up", "--seat", "Seat to launch.");
        seats.Arity = ArgumentArity.OneOrMore;
        seats.AllowMultipleArgumentsPerToken = false;
        AddOption<bool>(up, "up", "--fresh", "Start fresh sessions.", flag: true);
        AddOption<string>(up, "up", "--note", "Note for launch records.");
        AddOption<bool>(up, "up", "--dry-run", "Validate and describe without launching.", flag: true);
        AddOption<bool>(up, "up", "--no-wait", "Return without waiting for seats.", flag: true);

        var down = AddCommand(root, "down", "down", "Stop selected seats.");
        AddArgument<string[]>(down, "seat", ArgumentArity.ZeroOrMore, array: true);
        AddOption<string>(down, "down", "--rig", "Rig directory.");
        AddOption<bool>(down, "down", "--all", "Stop all seats.", flag: true);
        AddOption<bool>(down, "down", "--no-wait", "Return without waiting for seats.", flag: true);

        var send = AddCommand(root, "send", "send", "Deliver input to a seat.");
        AddArgument<string>(send, "seat", ArgumentArity.ZeroOrOne);
        AddArgument<string>(send, "text", ArgumentArity.ZeroOrOne);
        AddOption<string>(send, "send", "--file", "Read input from a file.");
        AddOption<bool>(send, "send", "--force", "Force delivery.", flag: true);
        AddOption<string>(send, "send", "--wait", "Wait for delivery.", "delivery");
        AddOption<string>(send, "send", "--timeout", "Delivery timeout in seconds.", "1800");

        var capture = AddCommand(root, "capture", "capture", "Capture pane evidence.");
        AddArgument<string>(capture, "seat", ArgumentArity.ZeroOrOne);
        AddOption<string>(capture, "capture", "--lines", "Number of lines to capture.", "200");

        var ps = AddCommand(root, "ps", "ps", "Show seat state.");
        AddArgument<string>(ps, "seat", ArgumentArity.ZeroOrOne);
        AddOption<string>(ps, "ps", "--rig", "Rig directory.");
        AddOption<bool>(ps, "ps", "--wide", "Show wide state.", flag: true);
        AddOption<bool>(ps, "ps", "--watch", "Watch state changes.", flag: true);

        var attach = AddCommand(root, "attach", "attach", "Attach to a seat pane.");
        AddArgument<string>(attach, "seat", ArgumentArity.ZeroOrOne);
        AddOption<bool>(attach, "attach", "--write", "Allow writing to the pane.", flag: true);

        return new CommandTree(root, help, instance, json, verbose, version, paths, local, positional);
    }

    private sealed class CommandTree
    {
        private readonly IReadOnlyDictionary<string, List<Option>> _local;
        private readonly IReadOnlyDictionary<Command, List<Argument>> _positional;
        private readonly Command _root;
        public CommandTree(Command root, Option<bool> help, Option<string> instance, Option<bool> json,
            Option<bool> verbose, Option<bool> version, IReadOnlyDictionary<Command, string> paths,
            IReadOnlyDictionary<string, List<Option>> local, IReadOnlyDictionary<Command, List<Argument>> positional)
        {
            _root = root;
            _local = local;
            _positional = positional;
            Root = root; Help = help; Instance = instance; Json = json; Verbose = verbose; Version = version; Paths = paths;
        }
        public Command Root { get; }
        public Option<bool> Help { get; }
        public Option<string> Instance { get; }
        public Option<bool> Json { get; }
        public Option<bool> Verbose { get; }
        public Option<bool> Version { get; }
        public IReadOnlyDictionary<Command, string> Paths { get; }

        public IEnumerable<Option> Options => Root.Options.Concat(_local.Values.SelectMany(static options => options));

        public string RenderHelp(Command command)
        {
            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            Root.Options.Remove(Help);
            Root.Options.Add(new HelpOption { Recursive = true });
            void WriteHelp(Command selected)
            {
                var tokens = Paths[selected].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Append("--help").ToArray();
                var helpParse = Root.Parse(tokens, new ParserConfiguration { ResponseFileTokenReplacer = null });
                helpParse.Invoke(new InvocationConfiguration { Output = writer, Error = writer });
            }
            WriteHelp(command);
            if (command == _root)
                foreach (var candidate in Paths.Where(static item => item.Value.Length > 0)
                             .OrderBy(static item => item.Value, StringComparer.Ordinal))
                    WriteHelp(candidate.Key);
            writer.WriteLine(ExitCodes);
            return EnsureFinalLf(writer.ToString().Replace("\r\n", "\n", StringComparison.Ordinal));
        }

        public bool TryReadOptions(string path, ParseResult parse, out Dictionary<string, string[]> values)
        {
            values = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var option in _local.GetValueOrDefault(path) ?? [])
            {
                var result = parse.GetResult(option);
                var key = option.Name.TrimStart('-');
                if (option.ValueType == typeof(bool))
                {
                    if (parse.GetValue((Option<bool>)option))
                    {
                        values.Add(key, []);
                    }
                }
                else if (option.ValueType == typeof(string[]))
                {
                    var items = parse.GetValue((Option<string[]>)option) ?? [];
                    if (items.Length > 0)
                    {
                        values.Add(key, items);
                    }
                }
                else
                {
                    var item = parse.GetValue((Option<string>)option);
                    if (item is not null)
                    {
                        values.Add(key, [item]);
                    }
                }
            }

            return true;
        }

        public List<string> PositionalArguments(Command command, ParseResult parse)
        {
            var result = new List<string>();
            if (!_positional.TryGetValue(command, out var arguments))
            {
                return result;
            }

            foreach (var argument in arguments)
            {
                if (argument.ValueType == typeof(string[]))
                {
                    result.AddRange(parse.GetValue((Argument<string[]>)argument) ?? []);
                }
                else
                {
                    var value = parse.GetValue((Argument<string>)argument);
                    if (value is not null)
                    {
                        result.Add(value);
                    }
                }
            }

            return result;
        }
    }
}
