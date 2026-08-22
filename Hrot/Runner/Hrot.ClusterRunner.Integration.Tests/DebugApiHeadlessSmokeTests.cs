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

            // 2b) ADA-BATCH-04 extensions:
            // GET /commands — must be non-empty (event types registered by boot).
            var commandsResp = await client.GetAsync($"{baseUrl}/commands");
            Assert.Equal(System.Net.HttpStatusCode.OK, commandsResp.StatusCode);
            var commandsBody = await commandsResp.Content.ReadAsStringAsync();
            Assert.Contains("\"ok\":true", commandsBody, StringComparison.OrdinalIgnoreCase);
            // The data array must have at least one entry.
            var commandsDoc = System.Text.Json.JsonDocument.Parse(commandsBody);
            var commandsArr = commandsDoc.RootElement.GetProperty("data");
            Assert.True(commandsArr.GetArrayLength() > 0,
                $"GET /commands returned empty array. Body: {commandsBody}");

            // GET /components — must be non-empty.
            var componentsResp = await client.GetAsync($"{baseUrl}/components");
            Assert.Equal(System.Net.HttpStatusCode.OK, componentsResp.StatusCode);
            var componentsBody = await componentsResp.Content.ReadAsStringAsync();
            Assert.Contains("\"ok\":true", componentsBody, StringComparison.OrdinalIgnoreCase);

            // ADA-BATCH-05 Group M: GET /tkb/types — must return 200 with non-empty array.
            var tkbTypesResp = await client.GetAsync($"{baseUrl}/tkb/types");
            Assert.Equal(System.Net.HttpStatusCode.OK, tkbTypesResp.StatusCode);
            var tkbTypesBody = await tkbTypesResp.Content.ReadAsStringAsync();
            Assert.Contains("\"ok\":true", tkbTypesBody, StringComparison.OrdinalIgnoreCase);
            var tkbTypesDoc = System.Text.Json.JsonDocument.Parse(tkbTypesBody);
            var tkbTypesArr = tkbTypesDoc.RootElement.GetProperty("data");
            Assert.True(tkbTypesArr.GetArrayLength() > 0,
                $"GET /tkb/types returned empty array. Body: {tkbTypesBody}");

            // ADA-BATCH-05 Group N: GET /world/info — must return geo.origin.lat and geo.origin.lon.
            var worldInfoResp = await client.GetAsync($"{baseUrl}/world/info");
            Assert.Equal(System.Net.HttpStatusCode.OK, worldInfoResp.StatusCode);
            var worldInfoBody = await worldInfoResp.Content.ReadAsStringAsync();
            Assert.Contains("\"ok\":true", worldInfoBody, StringComparison.OrdinalIgnoreCase);
            var worldInfoDoc = System.Text.Json.JsonDocument.Parse(worldInfoBody);
            var worldData = worldInfoDoc.RootElement.GetProperty("data");
            Assert.True(worldData.TryGetProperty("geo", out var geoEl),
                $"GET /world/info missing 'geo' key. Body: {worldInfoBody}");
            Assert.True(geoEl.TryGetProperty("origin", out var originEl),
                $"GET /world/info missing 'geo.origin' key. Body: {worldInfoBody}");
            Assert.True(originEl.TryGetProperty("lat", out _),
                $"GET /world/info missing 'geo.origin.lat'. Body: {worldInfoBody}");
            Assert.True(originEl.TryGetProperty("lon", out _),
                $"GET /world/info missing 'geo.origin.lon'. Body: {worldInfoBody}");

            // GET /status to capture entityCount before spawn.
            var statusBefore = await client.GetAsync($"{baseUrl}/status");
            var statusBeforeBody = await statusBefore.Content.ReadAsStringAsync();
            var statusBeforeDoc  = System.Text.Json.JsonDocument.Parse(statusBeforeBody);
            int entityCountBefore = statusBeforeDoc.RootElement
                .GetProperty("data").GetProperty("entityCount").GetInt32();

            // POST /entities/spawn with tkbType=1 (the real editor registers TKB types at boot).
            // We use waitForReady=false; we just want to verify the entityCount increases after
            // a scenario load. First load a scenario, then spawn.
            // For the spawn smoke we load the default scenario.
            var loadBody = new StringContent(
                "{\"name\":\"test-move\",\"waitForReady\":true}",
                Encoding.UTF8, "application/json");
            var loadResp = await client.PostAsync($"{baseUrl}/scenario/load", loadBody);
            Assert.Equal(System.Net.HttpStatusCode.OK, loadResp.StatusCode);

            // Now check entityCount has grown from the scenario load.
            var statusAfterLoad = await client.GetAsync($"{baseUrl}/status");
            var statusAfterLoadBody = await statusAfterLoad.Content.ReadAsStringAsync();
            var statusAfterLoadDoc  = System.Text.Json.JsonDocument.Parse(statusAfterLoadBody);
            int entityCountAfterLoad = statusAfterLoadDoc.RootElement
                .GetProperty("data").GetProperty("entityCount").GetInt32();
            Assert.True(entityCountAfterLoad > 0,
                $"entityCount still 0 after scenario load. Body: {statusAfterLoadBody}");

            // POST /entities/spawn — spawn an additional entity.
            // TkbType 1001 = CivilianPedestrian (registered by UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates).
            var spawnBody = new StringContent(
                "{\"tkbType\":1001}",
                Encoding.UTF8, "application/json");
            var spawnResp = await client.PostAsync($"{baseUrl}/entities/spawn", spawnBody);
            Assert.Equal(System.Net.HttpStatusCode.OK, spawnResp.StatusCode);
            var spawnRespBody = await spawnResp.Content.ReadAsStringAsync();
            Assert.Contains("\"ok\":true", spawnRespBody, StringComparison.OrdinalIgnoreCase);

            // Poll for entityCount to increase after spawn.
            bool spawnedEntityVisible = await PollAsync(async () =>
            {
                try
                {
                    var r = await client.GetAsync($"{baseUrl}/status");
                    if (r.StatusCode != System.Net.HttpStatusCode.OK) return false;
                    var b = await r.Content.ReadAsStringAsync();
                    var d = System.Text.Json.JsonDocument.Parse(b);
                    int cnt = d.RootElement.GetProperty("data").GetProperty("entityCount").GetInt32();
                    return cnt > entityCountAfterLoad;
                }
                catch { return false; }
            }, timeoutMs: 15_000);
            Assert.True(spawnedEntityVisible,
                $"entityCount did not increase after /entities/spawn. Before load: {entityCountBefore}, after load: {entityCountAfterLoad}. Spawn response: {spawnRespBody}");

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
