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
    /// Creates this node's three slave time-sync translators — and NOTHING ELSE. No kernel, no
    /// registration, no ECS.
    ///
    /// <para>
    /// ⭐ Split out of <see cref="RegisterOn"/> on 2026-09-03 because the creation is shared by every
    /// node that follows cluster time, while the kernel registration is only meaningful on a node that
    /// HAS a kernel. 📐 Measured: <c>ExConSubsystem</c> follows cluster time, has no
    /// <c>ModuleHostKernel</c> at all, and therefore hand-built these same three calls at
    /// <c>ExConSubsystem.cs:268-270</c> — a duplicate that existed only because the shared helper
    /// insisted on a kernel.
    /// </para>
    ///
    /// <para>
    /// ⚠ The three are returned NAMED, not as an array, because a kernel-less host addresses them
    /// individually: ExCon interleaves <c>SlaveSyncController.Update()</c> between their
    /// <c>PollIngress</c> and <c>ScanAndPublish</c> calls (<c>:443-452</c>), which the kernel path
    /// expresses instead as separate ingress/egress systems the scheduler orders.
    /// </para>
    ///
    /// <para>
    /// All three accept a null <paramref name="participant"/> and become safe no-ops in that case
    /// (headless / test mode).
    /// </para>
    ///
    /// 📄 docs/DESIGN_Subsystem_Composition_Unification.md §4.1Q.
    /// </summary>
    public static SlaveTimeTranslators Create(
        DdsParticipant? participant,
        FdpEventBus     eventBus,
        int             nodeId)
        => new(
            Mode:          TimeNetworkModule.CreateDescriptorTranslator(participant, eventBus),
            SlaveLockstep: TimeNetworkModule.CreateSlaveLockstepTranslator(participant, eventBus, nodeId),
            SlaveTimeSync: TimeNetworkModule.CreateSlaveTimeSyncTranslator(participant, eventBus, nodeId));

    public static void RegisterOn(
        ModuleHostKernel  kernel,
        DdsParticipant?   participant,
        FdpEventBus       eventBus,
        int               nodeId)
    {
        // ⭐ The creation is the shared half (see Create); everything below is the KERNEL half —
        //    splitting ingress from egress and registering the three global systems. A node with no
        //    kernel calls Create directly and drives the translators itself.
        IDescriptorTranslator[] timeSyncTranslators = Create(participant, eventBus, nodeId).All;

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

/// <summary>
/// The three slave time-sync translators a node needs to follow cluster time, named so a
/// kernel-less host can address them individually.
///
/// <para>
/// 📄 docs/DESIGN_Subsystem_Composition_Unification.md §4.1Q. Created by
/// <see cref="SlaveTimeTranslatorRegistration.Create"/>.
/// </para>
/// </summary>
/// <param name="Mode">Time-mode descriptor translator.</param>
/// <param name="SlaveLockstep">Slave lockstep translator (ingress + egress).</param>
/// <param name="SlaveTimeSync">Slave time-sync translator (ingress + egress).</param>
public readonly record struct SlaveTimeTranslators(
    IDescriptorTranslator Mode,
    IDescriptorTranslator SlaveLockstep,
    IDescriptorTranslator SlaveTimeSync)
{
    /// <summary>All three, in the order the kernel path has always registered them.</summary>
    public IDescriptorTranslator[] All => new[] { Mode, SlaveLockstep, SlaveTimeSync };
}
