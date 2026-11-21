using System.Collections.Generic;
using CycloneDDS.Schema;
using Fdp.Core;
using Fdp.ModuleHost.Time;

namespace Fdp.Toolkit.Time.Messages
{
    [DdsTopic]
    [EventId(101)]
    [DataPolicy(DataPolicy.NoReplay)]
    public partial struct FrameOrderDescriptor
    {
        public long FrameID;
        
        public float FixedDelta;
        
        public long SequenceID;

        /// <summary>
        /// Time scale to apply when advancing sim-time by <see cref="FixedDelta"/>.
        /// Zero means "unchanged — keep the scale already in effect on the slave".
        /// Populated by <see cref="Fdp.Toolkit.Time.Controllers.SteppedMasterController.Step"/>
        /// so all slaves stay in lock-step with the master's effective scale.
        /// </summary>
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
        public double TargetSimTime;
    }

    
    [DdsTopic]
    [DdsQos(HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 16)]
    [EventId(102)]
    [DataPolicy(DataPolicy.NoReplay)]
    public partial struct FrameAckDescriptor
    {
        public long FrameID;
        
        public int NodeID;
        
        public int Checksum; // Optional state hash for sync verification
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
    [DdsTopic]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct SwitchTimeModeWireDto
    {
        /// <summary>
        /// Encoded <see cref="TimeMode"/> cast to <c>int</c>.
        /// 0 = <see cref="TimeMode.Continuous"/>, 1 = <see cref="TimeMode.Deterministic"/>.
        /// </summary>
        public int TargetModeInt;

        /// <summary><see cref="SwitchTimeModeEvent.BarrierWallTicks"/>.</summary>
        public long BarrierWallTicks;

        /// <summary><see cref="SwitchTimeModeEvent.FixedDelta"/>.</summary>
        public float FixedDelta;

        /// <summary><see cref="SwitchTimeModeEvent.SimTimeSnapshot"/>.</summary>
        public double SimTimeSnapshot;

        /// <summary><see cref="SwitchTimeModeEvent.TimeScale"/>.</summary>
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
    /// Sent by a slave node to initiate the NTP-style two-way time handshake.
    /// The master echoes it back inside a <see cref="TimeSyncResponse"/>.
    /// </summary>
    [DdsTopic]
    [EventId(108)]
    [DataPolicy(DataPolicy.NoReplay)]
    public partial struct TimeSyncRequest
    {
        /// <summary>Node ID of the slave initiating the handshake.</summary>
        [DdsKey]
        public int ClientNodeId;

        /// <summary>Raw UTC tick (<c>HighResUtcClock.GetTicks</c>) recorded just before publish.</summary>
        public long ClientSendTicks;
    }

    /// <summary>
    /// Published by the master node in reply to a <see cref="TimeSyncRequest"/>.
    /// Contains all four timestamps needed to compute clock offset via the NTP formula.
    /// </summary>
    [DdsTopic]
    [EventId(109)]
    [DataPolicy(DataPolicy.NoReplay)]
    public partial struct TimeSyncResponse
    {
        /// <summary>Echoed back from the request — identifies the slave this reply is addressed to.</summary>
        [DdsKey]
        public int ClientNodeId;

        /// <summary>Echoed back from the request.</summary>
        public long ClientSendTicks;

        /// <summary>Master OS tick recorded immediately upon receiving the request.</summary>
        public long MasterReceiveTicks;

        /// <summary>Master OS tick recorded immediately before writing the response to DDS.</summary>
        public long MasterTransmitTicks;
    }
}
