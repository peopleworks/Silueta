using Silueta.Cli;

// A hand-rolled argument parser, because Core has no dependencies and the tool that demonstrates it
// should not need three of them to read two flags. Everything the commands do lives in Commands: what
// is written, in what order, and what is refused is a rule, and a rule inside a top-level entry point
// is a rule no test can reach.
if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Commands.PrintUsage(Console.Out);
    return 0;
}

string command = args[0];
Dictionary<string, string> options = Commands.ParseOptions(args.AsSpan(1));

return command switch
{
    "redact" => Commands.Redact(options, Console.Out, Console.Error),
    "evaluate" => Commands.Evaluate(options, Console.Out, Console.Error),
    "demo" => Commands.Demo(Console.Out),
    "lineage" => Commands.Lineage(Console.Out),
    "lists" => Commands.Lists(Console.Out),
    _ => Commands.Unknown(command, Console.Out, Console.Error),
};
