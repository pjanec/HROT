using System.Collections.Generic;
using CycloneDDS.Schema;
using MessagePack;
using Fdp.Kernel;
using ModuleHost.Core.Time;

namespace FDP.Toolkit.Time.Messages
{
    [MessagePackObject]
    [EventId(101)]
    public struct FrameOrderDescriptor
    {
        [Key(0)]
        public long FrameID { get; set; }
        
        [Key(1)]
        public float FixedDelta { get; set; }
        
        [Key(2)]
        public long SequenceID { get; set; }
    }
    
    [MessagePackObject]
    [DdsTopic("TimePulse")]
    [EventId(100)]
    public partial struct TimePulseDescriptor
    {
        [Key(0)]
        [DdsId(0)]
        public long MasterWallTicks { get; set; }
        
        [Key(1)]
        [DdsId(1)]
        public double SimTimeSnapshot { get; set; }
        
        [Key(2)]
        [DdsId(2)]
        public float TimeScale { get; set; }
        
        [Key(3)]
        [DdsId(3)]
        public long SequenceId { get; set; }
    }
    
    [MessagePackObject]
    [EventId(102)]
    public struct FrameAckDescriptor
    {
        [Key(0)]
        public long FrameID { get; set; }
        
        [Key(1)]
        public int NodeID { get; set; }
        
        [Key(2)]
        public int Checksum { get; set; } // Optional state hash for sync verification
    }

    /// <summary>
    /// Network event to switch time mode across a distributed cluster.
    /// Published by the Master (<see cref="FDP.Toolkit.Time.Controllers.DistributedTimeCoordinator"/>),
    /// consumed by every Slave (<see cref="FDP.Toolkit.Time.Controllers.SlaveTimeModeListener"/>).
    /// <para>
    /// Each node performs the controller swap when its own
    /// <see cref="Fdp.Kernel.GlobalTime.TotalWallTicks"/> reaches
    /// <see cref="BarrierWallTicks"/>: the FDP PLL-synchronized virtual wall clock —
    /// not a frame counter or OS clock — guaranteeing cluster-wide alignment
    /// regardless of per-node frame rates.
    /// </para>
    /// <para>
    /// <b>DDS transport note:</b> use <see cref="SwitchTimeModeWireDto"/> with
    /// <see cref="FDP.Toolkit.Time.SwitchTimeModeDescriptorTranslator"/> via
    /// <c>TimeNetworkModule.CreateDescriptorTranslator</c> at the composition root.
    /// Do not register <c>SwitchTimeModeEvent</c> directly on DDS (see
    /// <see cref="SwitchTimeModeWireDto"/> XML).
    /// </para>
    /// </summary>
    [MessagePackObject]
    [EventId(103)]
    public struct SwitchTimeModeEvent
    {
        /// <summary>Target time mode: <see cref="TimeMode.Continuous"/> or <see cref="TimeMode.Deterministic"/>.</summary>
        [Key(0)]
        public TimeMode TargetMode { get; set; }

        /// <summary>
        /// Absolute <see cref="Fdp.Kernel.GlobalTime.TotalWallTicks"/> at which every node
        /// must perform the mode swap. Derived from the master's virtual wall clock at the
        /// moment of publishing, plus a configurable lookahead
        /// (≈ 200 ms by default, expressed as Stopwatch ticks).
        /// </summary>
        [Key(1)]
        public long BarrierWallTicks { get; set; }

        /// <summary>Fixed delta time (seconds) for Deterministic mode. Ignored for Continuous.</summary>
        [Key(2)]
        public float FixedDelta { get; set; }
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
    public partial struct SwitchTimeModeWireDto
    {
        /// <summary>
        /// Encoded <see cref="TimeMode"/> cast to <c>int</c>.
        /// 0 = <see cref="TimeMode.Continuous"/>, 1 = <see cref="TimeMode.Deterministic"/>.
        /// </summary>
        [DdsId(0)]
        public int TargetModeInt { get; set; }

        /// <summary><see cref="SwitchTimeModeEvent.BarrierWallTicks"/>.</summary>
        [DdsId(1)]
        public long BarrierWallTicks { get; set; }

        /// <summary><see cref="SwitchTimeModeEvent.FixedDelta"/>.</summary>
        [DdsId(2)]
        public float FixedDelta { get; set; }

        /// <summary>Converts a <see cref="SwitchTimeModeEvent"/> to its wire representation.</summary>
        public static SwitchTimeModeWireDto ToWire(SwitchTimeModeEvent evt) =>
            new SwitchTimeModeWireDto
            {
                TargetModeInt    = (int)evt.TargetMode,
                BarrierWallTicks = evt.BarrierWallTicks,
                FixedDelta       = evt.FixedDelta
            };

        /// <summary>Converts a wire DTO back to a <see cref="SwitchTimeModeEvent"/>.</summary>
        public SwitchTimeModeEvent ToEvent() =>
            new SwitchTimeModeEvent
            {
                TargetMode       = (TimeMode)TargetModeInt,
                BarrierWallTicks = BarrierWallTicks,
                FixedDelta       = FixedDelta
            };
    }}
