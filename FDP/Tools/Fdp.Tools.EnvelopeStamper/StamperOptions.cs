using CommandLine;

namespace Fdp.Tools.EnvelopeStamper;

internal sealed class StamperOptions
{
    [Option('r', "root", Required = true,
        HelpText = "Workspace root directory to walk.")]
    public string Root { get; set; } = string.Empty;

    [Option("dry-run", Default = false,
        HelpText = "Print what would be stamped without writing files.")]
    public bool DryRun { get; set; }
}
