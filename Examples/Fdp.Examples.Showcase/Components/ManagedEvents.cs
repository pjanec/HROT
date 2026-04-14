using Fdp.Kernel;
using MessagePack;
using System;
using System.Collections.Generic;

namespace Fdp.Examples.Showcase.Components
{
    /// <summary>
    /// Managed event for tracking detailed entity damage information.
    /// This is intentionally a managed class (not a struct) to test managed event
    /// recording and playback in the Flight Recorder system.
    /// </summary>
    [MessagePackObject]
    public class EntityDamagedEvent
    {
        [Key(0)]
        public int AttackerIndex { get; set; }
        
        [Key(1)]
        public int AttackerGeneration { get; set; }
        
        [Key(2)]
        public int TargetIndex { get; set; }
        
        [Key(3)]
        public int TargetGeneration { get; set; }
        
        [Key(4)]
        public float DamageAmount { get; set; }
        
        [Key(5)]
        public string DamageType { get; set; } = string.Empty;
        
        [Key(6)]
        public string AttackerTypeName { get; set; } = string.Empty;
        
        [Key(7)]
        public string TargetTypeName { get; set; } = string.Empty;
        
        [Key(8)]
        public long Timestamp { get; set; }
        
        [Key(9)]
        public bool WasKillingBlow { get; set; }
        
        [Key(10)]
        public float TargetHealthRemaining { get; set; }
        
        public EntityDamagedEvent()
        {
            Timestamp = DateTime.UtcNow.Ticks;
        }
        
        [IgnoreMember]
        public Entity Attacker => new Entity(AttackerIndex, (ushort)AttackerGeneration);
        
        [IgnoreMember]
        public Entity Target => new Entity(TargetIndex, (ushort)TargetGeneration);
        
        public override string ToString()
        {
            return $"{AttackerTypeName}[{AttackerIndex}] dealt {DamageAmount:F1} {DamageType} damage to {TargetTypeName}[{TargetIndex}]" +
                   (WasKillingBlow ? " (KILLED)" : $" (HP: {TargetHealthRemaining:F1})");
        }
    }
    
    /// <summary>
    /// Managed event for tracking entity death with detailed information.
    /// </summary>
    [MessagePackObject]
    public class EntityDeathEvent
    {
        [Key(0)]
        public int EntityIndex { get; set; }
        
        [Key(1)]
        public int EntityGeneration { get; set; }
        
        [Key(2)]
        public string EntityTypeName { get; set; } = string.Empty;
        
        [Key(3)]
        public int KillerIndex { get; set; }
        
        [Key(4)]
        public int KillerGeneration { get; set; }
        
        [Key(5)]
        public string KillerTypeName { get; set; } = string.Empty;
        
        [Key(6)]
        public long Timestamp { get; set; }
        
        [Key(7)]
        public int TotalDamageTaken { get; set; }
        
        [Key(8)]
        public int TimesHit { get; set; }
        
        [Key(9)]
        public float PositionX { get; set; }
        
        [Key(10)]
        public float PositionY { get; set; }
        
        public EntityDeathEvent()
        {
            Timestamp = DateTime.UtcNow.Ticks;
        }
        
        [IgnoreMember]
        public Entity Entity => new Entity(EntityIndex, (ushort)EntityGeneration);
        
        [IgnoreMember]
        public Entity Killer => new Entity(KillerIndex, (ushort)KillerGeneration);
        
        public override string ToString()
        {
            return $"{EntityTypeName}[{EntityIndex}] was killed by {KillerTypeName}[{KillerIndex}] " +
                   $"after taking {TotalDamageTaken} damage in {TimesHit} hits at ({PositionX:F1}, {PositionY:F1})";
        }
    }
}
