using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Events;

namespace Fdp.Toolkit.Behavior.Components
{
    /// <summary>
    /// ⭐ <c>CE-2076</c> — WHEN each kind of sensing change last happened to a unit, so a condition can ask
    /// "was hit within 5 s" or "contact within 10 s". 📐 A <see cref="SensorChangedEvent"/> lives one frame and the SOP
    /// wakes the NEXT frame (R-195), so a condition can never read the event itself — it reads this.
    /// Written only by <c>RecentSensesSystem</c> from the event; its producers are unchanged.
    /// 📄 <c>docs/DESIGN_Decision_Layer.md</c> §4.3.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.RecentSenses)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct RecentSenses
    {
        /// <summary>Sim time (s) of the last change of each kind, indexed by <see cref="SensorChange"/> (1–14).</summary>
        public double Acquired, Lost, TopChanged, FirstThreat, AllClear, Hit;
        /// <summary>⭐ <c>CE-3064</c> — the last near miss (kind 7).</summary>
        public double NearMiss;
        /// <summary>⭐ <c>CE-2123</c> — the danger-area kinds 8–10 (until then silently not recorded: the mask was a byte).</summary>
        public double AreaAhead, AreaThreatened, AreaCleared;
        /// <summary>⭐ <c>CE-2123</c> S2 — the squad kinds 11–13, recorded on the commander.</summary>
        public double SquadContact, SquadUnderFire, SquadAllClear;
        /// <summary>⭐ <c>CE-2125</c> — kind 14, on the commander.</summary>
        public double SquadLeaderDown;
        /// <summary>⭐ <c>CE-2152</c> C-c — kind 15, on the commander: a member went down.</summary>
        public double SquadMemberDown;
        /// <summary>Bit <c>1 &lt;&lt; (int)kind</c> set once that kind has happened at least once (kinds up to 15).</summary>
        public ushort SeenMask;

        /// <summary>Record a change of <paramref name="kind"/> at <paramref name="now"/>.</summary>
        public void Record(SensorChange kind, double now)
        {
            switch (kind)
            {
                case SensorChange.Acquired:    Acquired    = now; break;
                case SensorChange.Lost:        Lost        = now; break;
                case SensorChange.TopChanged:  TopChanged  = now; break;
                case SensorChange.FirstThreat: FirstThreat = now; break;
                case SensorChange.AllClear:    AllClear    = now; break;
                case SensorChange.Hit:         Hit         = now; break;
                case SensorChange.NearMiss:    NearMiss    = now; break;
                case SensorChange.AreaAhead:      AreaAhead      = now; break;
                case SensorChange.AreaThreatened: AreaThreatened = now; break;
                case SensorChange.AreaCleared:    AreaCleared    = now; break;
                case SensorChange.SquadContact:   SquadContact   = now; break;
                case SensorChange.SquadUnderFire: SquadUnderFire = now; break;
                case SensorChange.SquadAllClear:  SquadAllClear  = now; break;
                case SensorChange.SquadLeaderDown: SquadLeaderDown = now; break;
                case SensorChange.SquadMemberDown: SquadMemberDown = now; break;
                default: return;
            }
            SeenMask |= (ushort)(1 << (int)kind);
        }

        /// <summary>Did a change of <paramref name="kind"/> happen within the last <paramref name="seconds"/> before <paramref name="now"/>?</summary>
        public readonly bool Within(SensorChange kind, double seconds, double now)
            => TryGetLast(kind, out double at) && now - at <= seconds;

        /// <summary>⭐ <c>CE-2080</c> — when a change of <paramref name="kind"/> was last recorded; false when never.</summary>
        public readonly bool TryGetLast(SensorChange kind, out double at)
        {
            at = double.NegativeInfinity;
            if ((SeenMask & (1 << (int)kind)) == 0) return false;
            at = kind switch
            {
                SensorChange.Acquired    => Acquired,
                SensorChange.Lost        => Lost,
                SensorChange.TopChanged  => TopChanged,
                SensorChange.FirstThreat => FirstThreat,
                SensorChange.AllClear    => AllClear,
                SensorChange.Hit         => Hit,
                SensorChange.NearMiss    => NearMiss,
                SensorChange.AreaAhead      => AreaAhead,
                SensorChange.AreaThreatened => AreaThreatened,
                SensorChange.AreaCleared    => AreaCleared,
                SensorChange.SquadContact   => SquadContact,
                SensorChange.SquadUnderFire => SquadUnderFire,
                SensorChange.SquadAllClear  => SquadAllClear,
                SensorChange.SquadLeaderDown => SquadLeaderDown,
                SensorChange.SquadMemberDown => SquadMemberDown,
                _                        => double.NegativeInfinity,
            };
            return true;
        }
    }

    /// <summary>⭐ <c>CE-2076</c> — the one read path for conditions: was <paramref name="kind"/> seen within N s?</summary>
    public static class RecentSensesOf
    {
        /// <summary>True when the unit recorded a change of <paramref name="kind"/> within <paramref name="seconds"/> of now.</summary>
        public static bool Within(ISimulationView view, Entity unit, SensorChange kind, double seconds)
        {
            if (!view.HasComponent<RecentSenses>(unit)) return false;
            double now = view is EntityRepository repo && repo.HasSingleton<GlobalTime>() ? repo.GetSingleton<GlobalTime>().TotalTime : 0d;
            return view.GetComponentRO<RecentSenses>(unit).Within(kind, seconds, now);
        }

        /// <summary>⭐ <c>CE-2080</c> — <see cref="Within"/> AND recorded at or after <paramref name="since"/>.</summary>
        public static bool WithinSince(ISimulationView view, Entity unit, SensorChange kind, double seconds, double since)
        {
            if (!view.HasComponent<RecentSenses>(unit)) return false;
            double now = view is EntityRepository repo && repo.HasSingleton<GlobalTime>() ? repo.GetSingleton<GlobalTime>().TotalTime : 0d;
            return view.GetComponentRO<RecentSenses>(unit).TryGetLast(kind, out double at) && now - at <= seconds && at >= since;
        }
    }
}
