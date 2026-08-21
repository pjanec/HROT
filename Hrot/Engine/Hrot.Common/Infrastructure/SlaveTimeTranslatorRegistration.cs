using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost;
using Fdp.Network.Cyclone.Modules;
using Fdp.Network.Cyclone.Systems;
using Fdp.Toolkit.Time;

namespace Hrot.Common.Infrastructure;

/// <summary>
/// Registers the three DDS translators every kernel-owning SLAVE node needs in order to
/// take part in cluster time control:
/// <list type="bullet">
///   <item><c>SwitchTimeModeDescriptorTranslator</c> — hears <c>SwitchTimeModeEvent</c>, i.e. the pause.</item>
///   <item><c>SlaveLockstepTranslator</c> — ingress <c>FrameOrder</c> → <c>AdvanceFrameIntent</c>,
///         egress <c>FrameStepCompletedEvent</c> → <c>FrameAck</c>, i.e. the ACK the master waits for.</item>
///   <item><c>SlaveTimeSyncTranslator</c> — the NTP handshake.</item>
/// </list>
///
/// <para>Extracted from <see cref="SharedApplicationBootstrapper"/> Phase 6c (`TM-002`) so that
/// <c>CgfSubsystem</c> — which composes its node through <see cref="HrotNodeBuilder"/> directly
/// rather than through the bootstrapper — wires the same set from the same code instead of a
/// second copy of it. Before that extraction the CGF node had a <c>SlaveSyncController</c> but no
/// translators at all: it never heard a pause, never received a frame order, and never ACKed —
/// while the orchestrator still listed it in the lockstep roster
/// (<c>SubsystemName is "SimHost" or "IG" or "CGF"</c>), so the master blocked on it forever.</para>
///
/// <para>All three factories accept a null participant and become safe no-ops in that case
/// (headless / offline test paths), so this is registered unconditionally: the
/// <c>SlaveSyncController</c> must stay reachable via the event bus either way.</para>
/// </summary>
public static class SlaveTimeTranslatorRegistration
{
    /// <summary>
    /// Creates the three slave time translators and registers their ingress/egress/cleanup
    /// systems as global systems on <paramref name="kernel"/>.
    /// </summary>
    public static void RegisterOn(
        ModuleHostKernel  kernel,
        DdsParticipant?   participant,
        FdpEventBus       eventBus,
        int               nodeId)
    {
        var timeSyncTranslators = new IDescriptorTranslator[]
        {
            TimeNetworkModule.CreateDescriptorTranslator(participant, eventBus),
            TimeNetworkModule.CreateSlaveLockstepTranslator(participant, eventBus, nodeId),
            TimeNetworkModule.CreateSlaveTimeSyncTranslator(participant, eventBus, nodeId),
        };

        var ingress = new List<IDescriptorTranslator>();
        var egress  = new List<IDescriptorTranslator>();
        foreach (var t in timeSyncTranslators)
        {
            if ((t.Direction & TranslatorDirection.Ingress) != 0) ingress.Add(t);
            if ((t.Direction & TranslatorDirection.Egress)  != 0) egress.Add(t);
        }

        if (ingress.Count > 0)
            kernel.RegisterGlobalSystem(new CycloneNetworkIngressSystem(ingress.ToArray()));
        if (egress.Count > 0)
            kernel.RegisterGlobalSystem(new CycloneEgressSystem(egress.ToArray()));
        kernel.RegisterGlobalSystem(new CycloneNetworkCleanupSystem(timeSyncTranslators));
    }
}
