using System;
using System.Net.Http;
using System.Threading.Tasks;
using Hrot.Editor.DebugApi;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Foundation-level tests for the AI Debug API HTTP host (ADA-BATCH-01).
/// These tests run entirely in-process — no external assets required.
/// </summary>
public sealed class DebugApiFoundationTests : IDisposable
{
    private readonly HttpClient _client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

    public void Dispose() => _client.Dispose();

    // ── MainThreadJobQueue ────────────────────────────────────────────────

    [Fact]
    public async Task JobQueue_RunOnMainThread_ExecutesOnDrain()
    {
        var queue = new MainThreadJobQueue();
        var task  = queue.RunOnMainThread(() => 42);

        // Before draining the task should not be completed.
        Assert.False(task.IsCompleted);

        // Drain on the same thread (simulates main-thread drain).
        queue.DrainAll();

        var result = await task;
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task JobQueue_FaultingJob_FaultsTask()
    {
        var queue = new MainThreadJobQueue();
        var task  = queue.RunOnMainThread<int>(() => throw new InvalidOperationException("boom"));

        queue.DrainAll();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public void JobQueue_DrainAll_EmptyQueue_DoesNotThrow()
    {
        var queue = new MainThreadJobQueue();
        // Should not throw on an empty queue.
        queue.DrainAll();
    }

    [Fact]
    public async Task JobQueue_MultipleJobs_AllExecute()
    {
        var queue   = new MainThreadJobQueue();
        var taskA   = queue.RunOnMainThread(() => 1);
        var taskB   = queue.RunOnMainThread(() => 2);
        var taskC   = queue.RunOnMainThread(() => 3);

        queue.DrainAll();

        Assert.Equal(1, await taskA);
        Assert.Equal(2, await taskB);
        Assert.Equal(3, await taskC);
    }

    // ── DebugApiHost HTTP ─────────────────────────────────────────────────

    [Fact]
    public async Task DebugApiHost_Status_Returns200Ok()
    {
        var port  = FindFreePort();
        var queue = new MainThreadJobQueue();
        using var host = new DebugApiHost(port, queue, () => { });
        host.Start();

        var response = await _client.GetAsync($"http://localhost:{port}/status");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"ok\":true", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DebugApiHost_UnknownRoute_Returns404()
    {
        var port  = FindFreePort();
        var queue = new MainThreadJobQueue();
        using var host = new DebugApiHost(port, queue, () => { });
        host.Start();

        var response = await _client.GetAsync($"http://localhost:{port}/no-such-route");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DebugApiHost_Shutdown_InvokesCallback()
    {
        var port     = FindFreePort();
        var queue    = new MainThreadJobQueue();
        bool called  = false;
        using var host = new DebugApiHost(port, queue, () => called = true);
        host.Start();

        var response = await _client.PostAsync($"http://localhost:{port}/shutdown", content: null);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        // Give the callback a moment to fire (it's invoked on the handler thread).
        await Task.Delay(50);
        Assert.True(called);
    }

    [Fact]
    public void DebugApiHost_Dispose_DoesNotThrow()
    {
        var port  = FindFreePort();
        var queue = new MainThreadJobQueue();
        var host  = new DebugApiHost(port, queue, () => { });
        host.Start();
        host.Dispose(); // Must not throw.
        host.Dispose(); // Double-dispose must not throw either.
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static int FindFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
