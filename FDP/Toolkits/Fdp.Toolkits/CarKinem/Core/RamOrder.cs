using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace CarKinem.Core
{
    /// <summary>
    /// ⭐⭐ <c>CE-4622</c> K-2 (docs/designs/vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md RM-2, RM-5, KV-6) — a HULL's "I am ramming": written on the vehicle itself
    /// by its brain's <c>RamTarget</c> order (a per-entity write) and switched OFF (<see cref="Active"/> 0) when the order ends — never removed, so the off replicates as a change (the <c>MineClearing</c> pattern). Read on the node that MOVES the vehicle (it replicates there,
    /// <c>dtRamOrder</c>): <c>CarKinematicsSystem</c>'s avoidance does not give way to <see cref="Target"/> — to no person at all when <see cref="Crowd"/> is set —
    /// and <c>HullBreakGate.MayBreak</c> lets the hull break a weak barrier it meets, pushing at full throttle. A car without it can do neither.
    /// Unmanaged, recorded; Brain group.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.RamOrder)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct RamOrder
    {
        /// <summary>1 while the order runs.</summary>
        public byte Active;
        /// <summary>The body rammed (its network id); none with <see cref="Crowd"/> = a crowd at a point.</summary>
        public Fdp.Toolkit.Replication.EntityRef Target;
        /// <summary>1 = the target is a CROWD: the hull gives way to no person.</summary>
        public byte Crowd;

        /// <summary>⭐ <c>CE-2445</c> (RM-2, R-2134) — the RUN-IN (m): inside it the ram is a lethal act (<c>RamTarget</c>) and the hull drives the STRAIGHT line to
        /// its move's end, never a path round what is in the way (<c>NavigationIntentBridgeSystem</c>) — the break gate decides at the obstacle. One value for both.</summary>
        public const float RunInMetres = 50f;

        /// <summary>⭐ <c>CE-2445</c> — is <paramref name="hull"/> ramming and its move's end at <paramref name="destination"/> inside the run-in?</summary>
        public static bool InRunIn(Fdp.Core.EntityRepository repo, Fdp.Core.Entity hull, System.Numerics.Vector3 destination)
        {
            if (!On(repo, hull, out _) || !repo.HasComponent<Fdp.Core.SimTransform>(hull)) return false;
            var at = (System.Numerics.Vector3)repo.GetComponentRO<Fdp.Core.SimTransform>(hull).Position;
            float dx = destination.X - at.X, dy = destination.Y - at.Y;
            return dx * dx + dy * dy <= RunInMetres * RunInMetres;
        }

        /// <summary>True when <paramref name="hull"/> is ramming now.</summary>
        public static bool On(Fdp.Core.EntityRepository repo, Fdp.Core.Entity hull, out RamOrder order)
        {
            order = default;
            if (!repo.IsComponentTypeRegistered<RamOrder>() || !repo.HasComponent<RamOrder>(hull)) return false;
            order = repo.GetComponentRO<RamOrder>(hull);
            return order.Active != 0;
        }
    }
}
