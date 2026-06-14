using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ADA-BATCH-02 corrective C2 + Tier-2 extended process smoke.
///
/// <para>
/// Launches the real runner process (<c>-m editor --debug-api --headless</c>), polls
/// <c>GET /status</c> for 200, exercises <c>GET /entities</c> and <c>GET /sim/state</c>,
/// then <c>POST /shutdown</c> WITH a body (HttpListener returns 411 on a bodyless POST) and
/// asserts the process exits cleanly (exit 0).
/// </para>
///
/// <para>
/// Gated behind the <c>ADA_RUN_HEADLESS_SMOKE=1</c> environment variable: the editor mode
/// boots the full kernel stack (AI behaviors build, scenario assets, NLog file targets) which
/// is heavy and environment-sensitive, so it is opt-in for the dev lead / CI lane rather than
/// part of the default fast suite. The lead re-runs it manually per the batch instructions.
/// </para>
/// </summary>
public sealed class DebugApiHeadlessSmokeTests
{
    private const string GateEnvVar = "ADA_RUN_HEADLESS_SMOKE";

    private static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable(GateEnvVar), "1", StringComparison.Ordinal);

    [Fact]
    public async Task HeadlessEditor_StatusEntitiesSimState_ThenCleanShutdown()
    {
        if (!Enabled)
            return; // opt-in only — see class summary.

        int port = FindFreePort();
        var dll  = Path.Combine(AppContext.BaseDirectory, "Hrot.ClusterRunner.dll");
        Assert.True(File.Exists(dll), $"Runner dll not found next to tests: {dll}");

        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory       = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        };
        psi.ArgumentList.Add(dll);
        psi.ArgumentList.Add("-m");
        psi.ArgumentList.Add("editor");
        psi.ArgumentList.Add("--debug-api");
        psi.ArgumentList.Add("--debug-api-port");
        psi.ArgumentList.Add(port.ToString());
        psi.ArgumentList.Add("--headless");

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        Assert.True(proc.Start(), "Runner process failed to start.");
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            string baseUrl = $"http://localhost:{port}";

            // 1) Poll GET /status for 200 (editor boot can take several seconds).
            bool ready = await PollAsync(async () =>
            {
                try
                {
                    var r = await client.GetAsync($"{baseUrl}/status");
                    return r.StatusCode == System.Net.HttpStatusCode.OK;
                }
                catch { return false; }
            }, timeoutMs: 60_000);

            Assert.True(ready,
                $"GET /status never returned 200.\n--- STDOUT ---\n{stdout}\n--- STDERR ---\n{stderr}");

            // 2) Tier-2 extended: GET /entities and GET /sim/state must respond 200.
            var entities = await client.GetAsync($"{baseUrl}/entities");
            Assert.Equal(System.Net.HttpStatusCode.OK, entities.StatusCode);
            var entitiesBody = await entities.Content.ReadAsStringAsync();
            Assert.Contains("\"ok\":true", entitiesBody, StringComparison.OrdinalIgnoreCase);

            var simState = await client.GetAsync($"{baseUrl}/sim/state");
            Assert.Equal(System.Net.HttpStatusCode.OK, simState.StatusCode);
            var simStateBody = await simState.Content.ReadAsStringAsync();
            Assert.Contains("\"ok\":true", simStateBody, StringComparison.OrdinalIgnoreCase);

            // 3) POST /shutdown WITH a body (HttpListener 411s on a bodyless POST).
            var shutdown = await client.PostAsync(
                $"{baseUrl}/shutdown",
                new StringContent("{}", Encoding.UTF8, "application/json"));
            Assert.Equal(System.Net.HttpStatusCode.OK, shutdown.StatusCode);

            // 4) Process exits cleanly (exit 0).
            bool exited = proc.WaitForExit(30_000);
            Assert.True(exited,
                $"Process did not exit after /shutdown.\n--- STDOUT ---\n{stdout}\n--- STDERR ---\n{stderr}");
            Assert.Equal(0, proc.ExitCode);
        }
        finally
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
        }
    }

    private static async Task<bool> PollAsync(Func<Task<bool>> condition, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return true;
            await Task.Delay(250);
        }
        return false;
    }

    private static int FindFreePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
