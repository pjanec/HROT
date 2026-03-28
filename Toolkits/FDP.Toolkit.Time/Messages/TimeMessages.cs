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
    /// <b>DDS transport note:</b> wire the event via
    /// <c>BlitEventTranslator&lt;SwitchTimeModeEvent&gt;</c> in
    /// <c>TimeNetworkModule.RegisterTranslators()</c> at the composition root.
    /// The IDL registration avoids specifying <see cref="Fdp.Kernel.GlobalTime"/> enums
    /// directly in the IDL; the struct is kept as an unmanaged value type.
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
}
