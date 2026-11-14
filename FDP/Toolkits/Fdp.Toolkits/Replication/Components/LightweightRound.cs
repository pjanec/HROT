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
    [StructLayout(LayoutKind.Sequential, Size = 1)]
    [ComponentId(GlobalComponentIds.LightweightRound)]
    [DataPolicy(DataPolicy.NoScenario | DataPolicy.NoPreview)]
    public struct LightweightRound { }
}
