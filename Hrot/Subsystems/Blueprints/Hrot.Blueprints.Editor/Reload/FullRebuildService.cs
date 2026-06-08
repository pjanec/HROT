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

        // EnableRaisingEvents is required for WaitForExitAsync to function correctly
        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        // Asynchronously stream stdout and stderr directly to the console/message log
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) _outputConsole.LogInfo(e.Data);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) _outputConsole.LogError(e.Data);
        };

        try
        {
            if (!proc.Start())
            {
                sw.Stop();
                return new FullRebuildResult(false, -1, sw.ElapsedMilliseconds);
            }

            // Begin the asynchronous reads immediately after starting the process
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // Yield execution back to the caller (keeping the UI responsive) until the process exits
            await proc.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            _outputConsole.LogError($"Failed to start dotnet build: {ex.Message}");
            sw.Stop();
            return new FullRebuildResult(false, -1, sw.ElapsedMilliseconds);
        }

        sw.Stop();

        bool success = proc.ExitCode == 0;
        if (success) PendingDrainAfterBuild = true;

        return new FullRebuildResult(success, proc.ExitCode, sw.ElapsedMilliseconds);
    }
}
