using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Replication.Components
{
    /// <summary>
    /// ⭐⭐ LR-2 (Hrot docs/DESIGN_Visual_Effects.md §8.5, R-271) — <b>a lightweight replicated round:</b> a visible projectile (a shell, a
    /// mortar bomb, a grenade, a rocket, a missile, a tracer bullet) that the muscle REPLICATES as just <c>EntityMaster</c> (with the
    /// <c>Lightweight</c> flag) + <c>WorldPos</c>. 🔒 User: <i>"Projectiles need to be lightweight so their entiries should not use any
    /// lifecycle management and acks. they need just entity master and worldposition."</i>
    /// <list type="bullet">
    ///   <item>On the muscle: the fire path tags the round; <c>LightweightRoundIdentitySystem</c> gives it a network id; it is destroyed
    ///   directly when it bursts, and the network cleanup disposes its master (no <c>DestructionOrder</c>, no ELM).</item>
    ///   <item>On a viewer: the master's ingress creates the entity ACTIVE at once — no ghost, no promotion, no acks — and destroys it
    ///   when the master is disposed.</item>
    /// </list>
    /// </summary>
    /// <para>⭐ CE-11011 (RR-11, R-11005; docs/designs/vehicle-appearance/DESIGN_Look_Rules_For_Renderers.md) — it carries its exact LAUNCH TIME (sim seconds), set once
    /// where the round is made (<c>FireProcessingSystem.MarkIfVisible</c>) and sent with its <c>EntityMaster</c>, so a viewer or an external renderer knows how long it has
    /// flown without converting frame counts. 🔒 User, 2026-11-20: "old recordings not important; rather widen the marker" (it was an empty tag).</para>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.LightweightRound)]
    [DataPolicy(DataPolicy.NoScenario | DataPolicy.NoPreview)]
    public struct LightweightRound
    {
        /// <summary>The synced sim time the round was launched (0 = unknown: an older sender).</summary>
        public SimInstant LaunchedAt;
    }
}
