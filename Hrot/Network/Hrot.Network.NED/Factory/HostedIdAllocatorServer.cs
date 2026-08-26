using System;
using System.Threading;
using CycloneDDS.Runtime;
using Fdp.Core.Logging;
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
    private volatile Exception? _lastFault;

    /// <summary>
    /// ⭐ <c>QA-003</c> — the fault that stopped <see cref="RunLoop"/>, or <see langword="null"/> while
    /// the loop is healthy. ⛔ Non-null means the allocator is SILENT: requests are no longer served.
    /// Exposed so a rail or a health check can assert on the fault instead of the process dying.
    /// </summary>
    internal Exception? LastFault
    {
        get => _lastFault;
        private set => _lastFault = value;
    }

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

    /// <summary>
    /// ⭐⭐⭐ <c>QA-003</c> — <b>the fault that stops this THREAD must never stop the PROCESS.</b>
    ///
    /// <para>📐 <b>Measured 2026-08-26:</b> this loop had no exception handling, so a
    /// <c>DdsException: dds_take failed: -3 (BadParameter)</c> raised here became an <b>unhandled
    /// exception on a background thread</b> and killed the whole process — in the integration suite
    /// that is the xUnit test host, aborting the run mid-flight and destroying every remaining
    /// test's verdict. ⛔ In production it takes down the node for one DDS hiccup.</para>
    ///
    /// <para>⛔ This is NOT a swallow (<c>R-131</c>): the fault is logged at ERROR, kept on
    /// <see cref="LastFault"/> for tests and health checks to assert on, and the loop STOPS — a
    /// reader whose handle is bad will not recover by being polled again. ⭐ The allocator becoming
    /// silent is observable; the process dying is not.</para>
    /// </summary>
    private void RunLoop()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                _server.ProcessRequests();
                Thread.Sleep(1);
            }
        }
        catch (Exception ex)
        {
            // A fault raised while we are tearing down is expected: Dispose() cancels and then
            // destroys the reader, so an in-flight take can legitimately see a dead handle.
            if (_cts.IsCancellationRequested || _disposed) return;

            LastFault = ex;
            FdpLog<HostedIdAllocatorServer>.Error(
                "Id-allocator poll loop stopped after an unhandled fault; the allocator is now " +
                "SILENT (requests will time out rather than be served): {0}", ex);
        }
    }
}
