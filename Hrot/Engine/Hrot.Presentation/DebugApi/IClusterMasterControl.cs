using System;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;

namespace Hrot.Presentation.DebugApi;

/// <summary>
/// ⭐⭐⭐ <c>R-3052</c>…<c>R-3055</c> — <b>the ONE way the debug surface drives the cluster: publish a master intent onto this host's
/// orchestration bus.</b> 📄 <c>docs/DESIGN_Http_Control_Through_The_Cluster_Master.md</c> §3.
/// </summary>
/// <remarks>
/// <para>⭐ On a node without the master the intent leaves through <c>ClusterOpEgressTranslator</c> (DDS); on the host that holds it
/// (the orchestrator, the editor's embedded core) the master reads the same bus. ⇒ ⛔ no route has an editor arm — the editor is a
/// one-node cluster.</para>
/// <para>⭐ Typed per intent, not "publish anything": the debug host may ask the master to act, never inject arbitrary events into a
/// node's control plane (the reason the per-intent delegates it replaces were narrow, kept).</para>
/// </remarks>
public interface IClusterMasterControl
{
    /// <summary>A cluster state change — the planner plans the way through the intermediate states (<c>R-3055</c>).</summary>
    void Transition(TransitionStateIntent intent);

    /// <summary>A replay seek round (<c>TargetWallTicks</c> after the replay's zero, <c>C9</c>).</summary>
    void SeekReplay(SeekReplayIntent intent);

    /// <summary>A storage operation (a scenario save, an export, …).</summary>
    void Storage(ExecuteStorageOpIntent intent);

    /// <summary>A named keyframe of the live recording (<c>R-3044</c>).</summary>
    void TakeCheckpoint(TakeCheckpointIntent intent);

    /// <summary>A restore of a named checkpoint.</summary>
    void RestoreCheckpoint(RestoreCheckpointIntent intent);

    /// <summary>The operator's cancel (<c>R-4004</c>).</summary>
    void Cancel(CancelOperationIntent intent);

    /// <summary>A cluster-wide diagnostic dump (<c>MD-006</c>).</summary>
    void Dump(ExecuteDiagnosticDumpIntent intent);
}

/// <summary>⭐ The one implementation: every intent goes onto the host's orchestration bus.</summary>
public sealed class OrchestrationBusMasterControl : IClusterMasterControl
{
    private readonly Func<FdpEventBus?> _bus;

    /// <param name="bus">The host's orchestration bus, read at publish time (a host builds it in <c>Initialize</c>).</param>
    public OrchestrationBusMasterControl(Func<FdpEventBus?> bus) => _bus = bus ?? throw new ArgumentNullException(nameof(bus));

    public void Transition(TransitionStateIntent intent) => Bus.PublishManaged(intent);
    public void SeekReplay(SeekReplayIntent intent) => Bus.PublishManaged(intent);
    public void Storage(ExecuteStorageOpIntent intent) => Bus.PublishManaged(intent);
    public void TakeCheckpoint(TakeCheckpointIntent intent) => Bus.PublishManaged(intent);
    public void RestoreCheckpoint(RestoreCheckpointIntent intent) => Bus.PublishManaged(intent);
    public void Cancel(CancelOperationIntent intent) => Bus.PublishManaged(intent);
    public void Dump(ExecuteDiagnosticDumpIntent intent) => Bus.PublishManaged(intent);

    private FdpEventBus Bus => _bus() ?? throw new InvalidOperationException("The host's orchestration bus is gone (shut down).");

    /// <summary>
    /// ⭐ The ONE way a subsystem contributes <see cref="ISubsystemDebugProvider.Master"/>. ⚠ The bus is re-read on every access —
    /// subsystem buses are created in <c>Initialize</c> and nulled in <c>Shutdown</c>.
    /// </summary>
    public static Func<IClusterMasterControl?> Via(Func<FdpEventBus?> orchestrationBus)
    {
        if (orchestrationBus is null) throw new ArgumentNullException(nameof(orchestrationBus));
        var control = new OrchestrationBusMasterControl(orchestrationBus);
        return () => orchestrationBus() is null ? null : control;
    }
}
