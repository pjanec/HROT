using Fdp.Core;
using Fdp.ModuleHost.Time;

namespace Fdp.Toolkit.Time.Messages
{
    // ⭐ CE-4208 (DESIGN_Time_Architecture.md §19) — the time EVENTS that ride the engine bus. The DDS wire types of the time layer
    // (FrameOrder/FrameAck/SwitchTimeModeWireDto/TimeSyncRequest/TimeSyncResponse) live in Fdp.Network.Cyclone/Time/Messages.

    /// <summary>
    /// Network event to switch time mode across a distributed cluster.
    /// Published by the Master (<see cref="Fdp.Toolkit.Time.Controllers.DistributedTimeCoordinator"/>),
    /// consumed by every Slave (<see cref="Fdp.Toolkit.Time.Controllers.SlaveTimeModeListener"/>).
    /// <para>
    /// Each node performs the controller swap when its own
    /// <see cref="Fdp.Core.GlobalTime.TotalWallTicks"/> reaches
    /// <see cref="BarrierWallTicks"/>: the FDP PLL-synchronized virtual wall clock —
    /// not a frame counter or OS clock — guaranteeing cluster-wide alignment
    /// regardless of per-node frame rates.
    /// </para>
    /// <para>
    /// <b>DDS transport note:</b> use <c>SwitchTimeModeWireDto</c> with
    /// <c>SwitchTimeModeDescriptorTranslator</c> via
    /// <c>TimeNetworkModule.CreateDescriptorTranslator</c> at the composition root.
    /// Do not register <c>SwitchTimeModeEvent</c> directly on DDS (see
    /// <c>SwitchTimeModeWireDto</c> XML).
    /// </para>
    /// </summary>
    [EventId(103)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct SwitchTimeModeEvent
    {
        /// <summary>Target time mode: <see cref="TimeMode.Continuous"/> or <see cref="TimeMode.Deterministic"/>.</summary>
        public TimeMode TargetMode;

        /// <summary>
        /// Absolute <see cref="Fdp.Core.GlobalTime.TotalWallTicks"/> at which every node
        /// must perform the mode swap. Derived from the master's virtual wall clock at the
        /// moment of publishing, plus a configurable lookahead
        /// (≈ 200 ms by default, expressed as 100-ns UTC ticks).
        /// </summary>
        public long BarrierWallTicks;

        /// <summary>Fixed delta time (seconds) for Deterministic mode. Ignored for Continuous.</summary>
        public float FixedDelta;

        /// <summary>
        /// The master node's authoritative simulation time (seconds) at the moment the mode
        /// switch is broadcast.  Non-zero only in <see cref="TimeMode.Continuous"/> events
        /// (Resume).  Slaves use this to seed their new controller at the master's post-step
        /// time rather than their own locally-accumulated slave time, preventing the
        /// UI time jump-back visible after Pause → Step → Resume.
        /// </summary>
        public double SimTimeSnapshot;

        /// <summary>
        /// Time scale to apply when the controller is installed on a slave or master.
        /// Zero means "unchanged — keep the current scale".
        /// Carried in both Pause (so slaves know the active speed) and Resume
        /// (so the user-requested speed is applied atomically with the resume swap).
        /// </summary>
        public float TimeScale;
    }

    /// <summary>
    /// Local-bus-only event published by <c>SlaveTimeSyncTranslator</c>
    /// after computing the NTP offset at the network boundary (precise <c>t4</c> capture).
    /// Consumed by <see cref="Fdp.Toolkit.Time.Controllers.SlaveSyncController"/> to update
    /// <c>_masterWallClockOffset</c>.  Never sent over DDS.
    /// </summary>
    [EventId(110)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct TimeSyncOffsetCalculatedEvent
    {
        /// <summary>Round-trip time in 100-ns UTC ticks. Controller checks against MaxRttTicks.</summary>
        public long Rtt;
        /// <summary>NTP-computed master-minus-slave clock offset in 100-ns UTC ticks.</summary>
        public long NewOffset;
    }

    /// <summary>
    /// ⭐ CE-4208 — the engine-side request for an NTP probe, published by <see cref="Fdp.Toolkit.Time.Controllers.SlaveSyncController"/>.
    /// <c>SlaveTimeSyncTranslator</c> (Fdp.Network.Cyclone) is the ONE place that turns it into the wire <c>TimeSyncRequest</c>.
    /// Local-bus-only; never sent over DDS.
    /// </summary>
    [EventId(111)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct TimeSyncProbeEvent
    {
        /// <summary>Node ID of the slave initiating the handshake.</summary>
        public Fdp.Core.NodeId ClientNodeId;
        /// <summary>Raw UTC tick recorded when the controller asked (the translator re-stamps it at the network boundary).</summary>
        public long ClientSendTicks;
    }
}
