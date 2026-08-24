using System;
using System.Threading;
using CycloneDDS.Runtime;
using Fdp.Network.Cyclone.Services;

namespace Hrot.Network.NED.Factory;

/// <summary>
/// Wraps <see cref="DdsIdAllocatorServer"/> in a background polling thread with
/// a well-defined lifetime.  <see cref="Dispose"/> cancels the thread and blocks
/// via <see cref="Thread.Join"/> before returning, guaranteeing the allocator
/// server is fully stopped before the owning <see cref="CycloneDDS.Runtime.DdsParticipant"/>
/// is destroyed.
/// Created and owned by <see cref="NedNetworkFactory.CreateIdAllocatorServer"/>.
/// </summary>
internal sealed class HostedIdAllocatorServer : IDisposable, Fdp.Toolkit.NetworkSpawning.IWorldIdAuthority
{
    private readonly DdsIdAllocatorServer       _server;
    private readonly CancellationTokenSource    _cts;
    private readonly Thread                     _thread;
    private bool _disposed;

    public HostedIdAllocatorServer(DdsParticipant participant)
    {
        _server = new DdsIdAllocatorServer(
            participant ?? throw new ArgumentNullException(nameof(participant)));
        _cts    = new CancellationTokenSource();
        _thread = new Thread(RunLoop)
        {
            IsBackground = true,
            Name         = "Orchestrator-IdAllocServer",
        };
        _thread.Start();
    }

    /// <summary>
    /// ⭐ <c>HN-037</c> — forwards the world-boundary reset to the hosted authority.
    /// <para>⭐⭐ This is the reason <c>CreateIdAllocatorServer</c>'s <see cref="IDisposable"/> handle is
    /// TYPE-TESTED for <see cref="Fdp.Toolkits.NetworkSpawning.IWorldIdAuthority"/> by the orchestrator
    /// rather than the factory signature being widened: 📐 four factories implement
    /// <c>INetworkFactory</c> and only this one hosts a real authority — a widened return type would force
    /// three null implementations that mean <i>"I am not an authority"</i>, which is exactly what a failed
    /// type-test already says. 📌 Same idiom as <c>IRestorableIdAllocator</c>.</para>
    /// </summary>
    public void ResetToBase(long firstId) => _server.ResetToBase(firstId);

    /// <summary>
    /// Cancels the polling thread and waits (up to 2 seconds) for it to exit,
    /// then disposes the underlying <see cref="DdsIdAllocatorServer"/>.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _thread.Join(TimeSpan.FromSeconds(2));
        _cts.Dispose();
        _server.Dispose();
    }

    private void RunLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            _server.ProcessRequests();
            Thread.Sleep(1);
        }
    }
}
