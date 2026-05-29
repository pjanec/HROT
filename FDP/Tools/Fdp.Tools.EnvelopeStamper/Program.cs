using CommandLine;
using Fdp.Tools.EnvelopeStamper;

internal static class Program
{
    public static int Main(string[] args)
        => RunMain(args, Console.Out, Console.Error);

    internal static int RunMain(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var result = Parser.Default.ParseArguments<StamperOptions>(args);
        return result.MapResult(
            opts => Execute(opts, stdout, stderr),
            _ => 1);
    }

    private static int Execute(StamperOptions opts, TextWriter stdout, TextWriter stderr)
    {
        if (!Directory.Exists(opts.Root))
        {
            stderr.WriteLine($"Error: directory not found: {opts.Root}");
            return 2;
        }

        var summary = FixtureStamper.StampDirectory(opts.Root, opts.DryRun, stdout, stderr);
        stdout.WriteLine();
        stdout.WriteLine($"Done. Stamped={summary.Stamped}, AlreadyStamped={summary.AlreadyStamped}, Skipped={summary.Skipped}, Errors={summary.Errors}");
        return summary.Errors > 0 ? 3 : 0;
    }
}
