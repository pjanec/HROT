using System.Threading;
using CycloneDDS.Runtime;
using FDP.Kernel.Logging;
using ModuleHost.Network.Cyclone.Services;

namespace Bagira.SimHost.Network;

/// <summary>
/// Hosts the DDS network-ID allocator server in-process when SimHost runs without a central orchestrator.
/// Keeps <see cref="SimHostApp"/> free of direct <c>DdsIdAllocatorServer</c> references (CGF1-S0103).
/// </summary>
internal sealed class LocalIdAllocatorFallbackHost : IDisposable
{
    private DdsIdAllocatorServer? _server;
    private CancellationTokenSource? _cts;
    private Thread? _thread;

    public LocalIdAllocatorFallbackHost(DdsParticipant participant)
    {
        Participant = participant;
    }

    public DdsParticipant Participant { get; }

    public void Start()
    {
        if (_server != null) return;
        _server = new DdsIdAllocatorServer(Participant);
        _cts = new CancellationTokenSource();
        var srv = _server;
        _thread = new Thread(() => RunLoop(srv, _cts.Token))
        {
            IsBackground = true,
            Name = "SimHost-IdAllocServer-Fallback"
        };
        _thread.Start();
        FdpLog<LocalIdAllocatorFallbackHost>.Info("[SimHost] Local ID allocator server started (fallback).");
    }

    private static void RunLoop(DdsIdAllocatorServer server, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            server.ProcessRequests();
            Thread.Sleep(1);
        }
        FdpLog<LocalIdAllocatorFallbackHost>.Info("[SimHost] Local ID allocator fallback loop exited.");
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(2));
        _cts?.Dispose();
        _cts = null;
        _thread = null;
        _server?.Dispose();
        _server = null;
    }
}
