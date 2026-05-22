using System.Diagnostics;

namespace Hrot.Blueprints.Editor.Reload;

public sealed class FullRebuildService
{
    private readonly IOutputConsole _outputConsole;
    private readonly string _buildTarget;

    public bool PendingDrainAfterBuild { get; private set; }

    public FullRebuildService(IOutputConsole outputConsole, string buildTarget = "")
    {
        _outputConsole = outputConsole ?? throw new ArgumentNullException(nameof(outputConsole));
        _buildTarget   = buildTarget;
    }

    public async Task<FullRebuildResult> TriggerAsync()
    {
        var sw = Stopwatch.StartNew();
        _outputConsole.LogInfo("Starting full rebuild...");

        var args = string.IsNullOrEmpty(_buildTarget)
            ? "build"
            : $"build {_buildTarget}";

        var psi = new ProcessStartInfo("dotnet", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        using var proc = Process.Start(psi);
        if (proc == null)
        {
            sw.Stop();
            return new FullRebuildResult(false, -1, sw.ElapsedMilliseconds);
        }

        string stdout = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();
        sw.Stop();

        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            _outputConsole.LogInfo(line.TrimEnd());

        bool success = proc.ExitCode == 0;
        if (success) PendingDrainAfterBuild = true;

        return new FullRebuildResult(success, proc.ExitCode, sw.ElapsedMilliseconds);
    }
}
