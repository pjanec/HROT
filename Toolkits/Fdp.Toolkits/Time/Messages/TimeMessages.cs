using System.Collections.Generic;
using CycloneDDS.Schema;
using MessagePack;
using Fdp.Core;
using Fdp.ModuleHost.Time;

namespace Fdp.Toolkit.Time.Messages
{
    [MessagePackObject]
    [DdsTopic("FrameOrder")]
    [EventId(101)]
    [DataPolicy(DataPolicy.NoRecord)]
    public partial struct FrameOrderDescriptor
    {
        [Key(0)]
        [DdsId(0)]
        public long FrameID;
        
        [Key(1)]
        [DdsId(1)]
        public float FixedDelta;
        
        [Key(2)]
        [DdsId(2)]
        public long SequenceID;

        /// <summary>
        /// Time scale to apply when advancing sim-time by <see cref="FixedDelta"/>.
        /// Zero means "unchanged — keep the scale already in effect on the slave".
        /// Populated by <see cref="Fdp.Toolkit.Time.Controllers.SteppedMasterController.Step"/>
        /// so all slaves stay in lock-step with the master's effective scale.
        /// </summary>
        [Key(3)]
        [DdsId(3)]
        public float TimeScale;

        /// <summary>
        /// Master's authoritative <see cref="Fdp.Core.GlobalTime.TotalTime"/> (seconds)
        /// AFTER advancing by this step.  When non-zero, slaves must set their own
        /// <c>TotalTime</c> to this value rather than computing <c>TotalTime += delta</c>
        /// from their locally-seeded state.
        ///
        /// <para>Without this field every slave arrives at a different <c>TotalTime</c>
        /// because each slave seeds its <see cref="Fdp.Toolkit.Time.Controllers.SteppedSlaveController"/>
        /// at its own local wall-clock moment (before the barrier is crossed), while the master
        /// seeds <see cref="Fdp.Toolkit.Time.Controllers.SteppedMasterController"/> at the barrier
        /// time.  The two seeds differ by up to the barrier look-ahead (~200 ms of sim-time),
        /// so after one step the slave's <c>TotalTime</c> is ~200 ms behind the master.</para>
        /// </summary>
        [Key(4)]
        [DdsId(4)]
        public double TargetSimTime;
    }

    
    [MessagePackObject]
    [DdsTopic("FrameAck")]
    [DdsQos(HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 16)]
    [EventId(102)]
    [DataPolicy(DataPolicy.NoRecord)]
    public partial struct FrameAckDescriptor
    {
        [Key(0)]
        [DdsId(0)]
        public long FrameID;
        
        [Key(1)]
        [DdsId(1)]
        public int NodeID;
        
        [Key(2)]
        [DdsId(2)]
        public int Checksum; // Optional state hash for sync verification
    }

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
    /// <b>DDS transport note:</b> use <see cref="SwitchTimeModeWireDto"/> with
    /// <see cref="Fdp.Toolkit.Time.SwitchTimeModeDescriptorTranslator"/> via
    /// <c>TimeNetworkModule.CreateDescriptorTranslator</c> at the composition root.
    /// Do not register <c>SwitchTimeModeEvent</c> directly on DDS (see
    /// <see cref="SwitchTimeModeWireDto"/> XML).
    /// </para>
    /// </summary>
    [MessagePackObject]
    [EventId(103)]
    [DataPolicy(DataPolicy.NoRecord)]
    public struct SwitchTimeModeEvent
    {
        /// <summary>Target time mode: <see cref="TimeMode.Continuous"/> or <see cref="TimeMode.Deterministic"/>.</summary>
        [Key(0)]
        public TimeMode TargetMode;

        /// <summary>
        /// Absolute <see cref="Fdp.Core.GlobalTime.TotalWallTicks"/> at which every node
        /// must perform the mode swap. Derived from the master's virtual wall clock at the
        /// moment of publishing, plus a configurable lookahead
        /// (≈ 200 ms by default, expressed as 100-ns UTC ticks).
        /// </summary>
        [Key(1)]
        public long BarrierWallTicks;

        /// <summary>Fixed delta time (seconds) for Deterministic mode. Ignored for Continuous.</summary>
        [Key(2)]
        public float FixedDelta;

        /// <summary>
        /// The master node's authoritative simulation time (seconds) at the moment the mode
        /// switch is broadcast.  Non-zero only in <see cref="TimeMode.Continuous"/> events
        /// (Resume).  Slaves use this to seed their new controller at the master's post-step
        /// time rather than their own locally-accumulated slave time, preventing the
        /// UI time jump-back visible after Pause → Step → Resume.
        /// </summary>
        [Key(3)]
        public double SimTimeSnapshot;

        /// <summary>
        /// Time scale to apply when the controller is installed on a slave or master.
        /// Zero means "unchanged — keep the current scale".
        /// Carried in both Pause (so slaves know the active speed) and Resume
        /// (so the user-requested speed is applied atomically with the resume swap).
        /// </summary>
        [Key(4)]
        public float TimeScale;
    }
    /// <summary>
    /// Blittable wire DTO for <see cref="SwitchTimeModeEvent"/> over CycloneDDS.
    ///
    /// <para>
    /// <see cref="SwitchTimeModeEvent"/> cannot carry <see cref="TimeMode"/> directly in
    /// the DDS IDL because the Cyclone source generator cannot represent arbitrary C# enums
    /// in CDR-scope.  This struct uses <c>int</c> for the time-mode field and is the only
    /// type registered with <see cref="CycloneDDS.Runtime.DdsReader{T}"/> /
    /// <see cref="CycloneDDS.Runtime.DdsWriter{T}"/>.
    ///
    /// Conversion helpers: <see cref="ToWire"/> / <see cref="ToEvent"/>.
    /// </para>
    /// </summary>
    [DdsTopic("SwitchTimeModeEvent")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct SwitchTimeModeWireDto
    {
        /// <summary>
        /// Encoded <see cref="TimeMode"/> cast to <c>int</c>.
        /// 0 = <see cref="TimeMode.Continuous"/>, 1 = <see cref="TimeMode.Deterministic"/>.
        /// </summary>
        [DdsId(0)]
        public int TargetModeInt;

        /// <summary><see cref="SwitchTimeModeEvent.BarrierWallTicks"/>.</summary>
        [DdsId(1)]
        public long BarrierWallTicks;

        /// <summary><see cref="SwitchTimeModeEvent.FixedDelta"/>.</summary>
        [DdsId(2)]
        public float FixedDelta;

        /// <summary><see cref="SwitchTimeModeEvent.SimTimeSnapshot"/>.</summary>
        [DdsId(3)]
        public double SimTimeSnapshot;

        /// <summary><see cref="SwitchTimeModeEvent.TimeScale"/>.</summary>
        [DdsId(4)]
        public float TimeScale;

        /// <summary>Converts a <see cref="SwitchTimeModeEvent"/> to its wire representation.</summary>
        public static SwitchTimeModeWireDto ToWire(SwitchTimeModeEvent evt) =>
            new SwitchTimeModeWireDto
            {
                TargetModeInt    = (int)evt.TargetMode,
                BarrierWallTicks = evt.BarrierWallTicks,
                FixedDelta       = evt.FixedDelta,
                SimTimeSnapshot  = evt.SimTimeSnapshot,
                TimeScale        = evt.TimeScale,
            };

        /// <summary>Converts a wire DTO back to a <see cref="SwitchTimeModeEvent"/>.</summary>
        public SwitchTimeModeEvent ToEvent() =>
            new SwitchTimeModeEvent
            {
                TargetMode       = (TimeMode)TargetModeInt,
                BarrierWallTicks = BarrierWallTicks,
                FixedDelta       = FixedDelta,
                SimTimeSnapshot  = SimTimeSnapshot,
                TimeScale        = TimeScale,
            };
    }

    /// <summary>
    /// Local-bus-only event published by <see cref="Fdp.Toolkit.Time.Translators.SlaveTimeSyncTranslator"/>
    /// after computing the NTP offset at the network boundary (precise <c>t4</c> capture).
    /// Consumed by <see cref="Fdp.Toolkit.Time.Controllers.SlaveSyncController"/> to update
    /// <c>_masterWallClockOffset</c>.  Never sent over DDS.
    /// </summary>
    [EventId(110)]
    [DataPolicy(DataPolicy.NoRecord)]
    public struct TimeSyncOffsetCalculatedEvent
    {
        /// <summary>Round-trip time in 100-ns UTC ticks. Controller checks against MaxRttTicks.</summary>
        public long Rtt;
        /// <summary>NTP-computed master-minus-slave clock offset in 100-ns UTC ticks.</summary>
        public long NewOffset;
    }

    /// <summary>
    /// Sent by a slave node to initiate the NTP-style two-way time handshake.
    /// The master echoes it back inside a <see cref="TimeSyncResponse"/>.
    /// </summary>
    [MessagePackObject]
    [DdsTopic("TimeSyncRequest")]
    [EventId(108)]
    [DataPolicy(DataPolicy.NoRecord)]
    public partial struct TimeSyncRequest
    {
        /// <summary>Node ID of the slave initiating the handshake.</summary>
        [Key(0)] [DdsId(0), DdsKey]
        public int ClientNodeId;

        /// <summary>Raw UTC tick (<c>HighResUtcClock.GetTicks</c>) recorded just before publish.</summary>
        [Key(1)] [DdsId(1)]
        public long ClientSendTicks;
    }

    /// <summary>
    /// Published by the master node in reply to a <see cref="TimeSyncRequest"/>.
    /// Contains all four timestamps needed to compute clock offset via the NTP formula.
    /// </summary>
    [MessagePackObject]
    [DdsTopic("TimeSyncResponse")]
    [EventId(109)]
    [DataPolicy(DataPolicy.NoRecord)]
    public partial struct TimeSyncResponse
    {
        /// <summary>Echoed back from the request — identifies the slave this reply is addressed to.</summary>
        [Key(0)] [DdsId(0), DdsKey]
        public int ClientNodeId;

        /// <summary>Echoed back from the request.</summary>
        [Key(1)] [DdsId(1)]
        public long ClientSendTicks;

        /// <summary>Master OS tick recorded immediately upon receiving the request.</summary>
        [Key(2)] [DdsId(2)]
        public long MasterReceiveTicks;

        /// <summary>Master OS tick recorded immediately before writing the response to DDS.</summary>
        [Key(3)] [DdsId(3)]
        public long MasterTransmitTicks;
    }
}
