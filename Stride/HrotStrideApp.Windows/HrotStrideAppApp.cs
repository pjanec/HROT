using System;
using System.Linq;
using HrotStrideApp;

// BATCH-11 (STR-LOG-1): Initialize NLog file logging BEFORE the game runs so that startup, Stride
// engine messages, the per-second diagnostics dump, and any unhandled (incl. GPU-path) crash all land
// in <BaseDirectory>/logs/editor_stride.log.
StrideLogging.Configure();
try
{
    // ⭐⭐⭐ CE-207 / S4 — TWO MODES, ONE EXECUTABLE.
    //
    //   --mode editor  (default)  mode 1: the hosted editor, unchanged.
    //   --mode node               mode 2: join a cluster as a Muscle + Perception node beside CGF.
    //
    //   ⭐ CLI args, not env vars, per Q66 §3C: env vars are mode 1's debug switches and already
    //     sprawl. --node-id defaults to 700, still free per the retired mock design's §9.2.
    string mode   = ArgValue(args, "--mode") ?? "editor";
    int    nodeId = int.TryParse(ArgValue(args, "--node-id"), out var n) ? n : StrideNodeShell.DefaultNodeId;
    int    domain = int.TryParse(ArgValue(args, "--domain"),  out var d) ? d : 0;

    using var game = new StrideHrotGame();

    if (string.Equals(mode, "node", StringComparison.OrdinalIgnoreCase))
    {
        game.NodeMode     = true;
        game.NodeDomainId = domain;
        game.NodeId       = nodeId;
        Console.WriteLine($"[StrideApp] CE-207 mode 2 — joining DDS domain {domain} as node {nodeId}.");
    }

    game.Run();
}
finally
{
    // Flush + close the log file so it is complete even on a clean exit.
    StrideLogging.Shutdown();
}

static string? ArgValue(string[] argv, string name)
{
    // Accepts "--name value" and "--name=value".
    for (int i = 0; i < argv.Length; i++)
    {
        if (string.Equals(argv[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < argv.Length)
            return argv[i + 1];
        if (argv[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            return argv[i].Substring(name.Length + 1);
    }
    return null;
}
