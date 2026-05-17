using Fdp.Kernel;
using System.Collections.Generic;

namespace Fdp.Examples.Showcase.Components
{
    /// <summary>
    /// Managed component for tracking combat history and statistics.
    /// This is intentionally managed (not a struct) to test managed component
    /// recording and playback in the Flight Recorder system.
    /// 
    /// This component supplements UnitStats (which has the actual health values)
    /// by tracking detailed combat history.
    /// </summary>
    public class CombatHistory
    {
        public int TotalDamageTaken { get; set; }
        
        public int TotalDamageDealt { get; set; }
        
        public int TimesHit { get; set; }
        
        public int TimesAttacked { get; set; }
        
        public int Kills { get; set; }
        
        public List<string> RecentEvents { get; set; } = new();
        
        public long CreationTimestamp { get; set; }
        
        public CombatHistory()
        {
            TotalDamageTaken = 0;
            TotalDamageDealt = 0;
            TimesHit = 0;
            TimesAttacked = 0;
            Kills = 0;
            RecentEvents = new List<string>();
            CreationTimestamp = System.DateTime.UtcNow.Ticks;
        }
        
        public void RecordDamageTaken(float amount, string source)
        {
            TotalDamageTaken += (int)amount;
            TimesHit++;
            AddEvent($"Took {amount:F0} damage from {source}");
        }
        
        public void RecordDamageDealt(float amount, string target)
        {
            TotalDamageDealt += (int)amount;
            TimesAttacked++;
            AddEvent($"Dealt {amount:F0} damage to {target}");
        }
        
        public void RecordKill(string target)
        {
            Kills++;
            AddEvent($"Killed {target}");
        }
        
        private void AddEvent(string eventText)
        {
            RecentEvents.Add(eventText);
            // Keep only last 10 events
            if (RecentEvents.Count > 10)
                RecentEvents.RemoveAt(0);
        }
    }
}
