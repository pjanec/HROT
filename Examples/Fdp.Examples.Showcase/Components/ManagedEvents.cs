using Fdp.Kernel;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Fdp.Examples.Showcase.Components
{
    /// <summary>
    /// Managed event for tracking detailed entity damage information.
    /// This is intentionally a managed class (not a struct) to test managed event
    /// recording and playback in the Flight Recorder system.
    /// </summary>
    public class EntityDamagedEvent
    {
        public int AttackerIndex { get; set; }
        
        public int AttackerGeneration { get; set; }
        
        public int TargetIndex { get; set; }
        
        public int TargetGeneration { get; set; }
        
        public float DamageAmount { get; set; }
        
        public string DamageType { get; set; } = string.Empty;
        
        public string AttackerTypeName { get; set; } = string.Empty;
        
        public string TargetTypeName { get; set; } = string.Empty;
        
        public long Timestamp { get; set; }
        
        public bool WasKillingBlow { get; set; }
        
        public float TargetHealthRemaining { get; set; }
        
        public EntityDamagedEvent()
        {
            Timestamp = DateTime.UtcNow.Ticks;
        }
        
        [JsonIgnore]
        public Entity Attacker => new Entity(AttackerIndex, (ushort)AttackerGeneration);
        
        [JsonIgnore]
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
    public class EntityDeathEvent
    {
        public int EntityIndex { get; set; }
        
        public int EntityGeneration { get; set; }
        
        public string EntityTypeName { get; set; } = string.Empty;
        
        public int KillerIndex { get; set; }
        
        public int KillerGeneration { get; set; }
        
        public string KillerTypeName { get; set; } = string.Empty;
        
        public long Timestamp { get; set; }
        
        public int TotalDamageTaken { get; set; }
        
        public int TimesHit { get; set; }
        
        public float PositionX { get; set; }
        
        public float PositionY { get; set; }
        
        public EntityDeathEvent()
        {
            Timestamp = DateTime.UtcNow.Ticks;
        }
        
        [JsonIgnore]
        public Entity Entity => new Entity(EntityIndex, (ushort)EntityGeneration);
        
        [JsonIgnore]
        public Entity Killer => new Entity(KillerIndex, (ushort)KillerGeneration);
        
        public override string ToString()
        {
            return $"{EntityTypeName}[{EntityIndex}] was killed by {KillerTypeName}[{KillerIndex}] " +
                   $"after taking {TotalDamageTaken} damage in {TimesHit} hits at ({PositionX:F1}, {PositionY:F1})";
        }
    }
}
