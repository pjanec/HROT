using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Time;

namespace Hrot.UI.Common.Effects;

/// <summary>
/// ⭐ CE-4671 LK-S1 (DESIGN_Vehicle_Appearance_State.md §7 LK-2, §7b) — the two HUD FEEDS, moved out of <see cref="EffectSpawnSystem"/> (they are not effects):
/// <list type="bullet">
///   <item>⭐ CE-1461 W-M7 (SR-106) — every <see cref="Fdp.Toolkit.ScenarioRules.PlayerMessageEvent"/> of this frame into the world's
///   <see cref="Hrot.UI.Common.Map3D.PlayerMessages"/> (the subtitle and the log read that one store), while <c>hud.messages</c> is on;</item>
///   <item>⭐ H2 / PH-5 (CE-4228) — every <see cref="Fdp.Toolkit.Combat.Events.ManDownedEvent"/> of this frame (behaviors' ONE kill authority) into the world's
///   <see cref="Hrot.UI.Common.Map3D.KillFeed"/>, while <c>hud.kill-feed</c> is on.</item>
/// </list>
/// Presentation memory only, so it runs in a REPLAY too (the recorded events come back on the bus). Scheduled right BEFORE the spawner, where they were
/// recorded until now — on every map host (<c>MapInteraction</c>) and in the Replay Browser.
/// </summary>
[UpdateInPhase(SystemPhase.PostSimulation)]
[TickMode(TickPolicy.WhenTimeAdvances)]
public sealed class HudFeedSystem : IEcsModuleSystem
{
    /// <summary>⭐ CE-4148 (Node Features S2) — this node's feature gate, read lazily (a host builds the map before its node exists). Null ⇒ ungated.</summary>
    public Func<Fdp.ModuleHost.IFeatureGate?>? FeatureGate { get; set; }

    /// <summary>⭐ CE-1461 W-M7 — are the player's messages kept on this node right now (<c>hud.messages</c>)?</summary>
    internal bool RecordsMessages => Hrot.Common.Infrastructure.HudFeatures.IsOn(FeatureGate?.Invoke(), Hrot.Common.Infrastructure.HudFeatures.Messages);

    /// <summary>⭐ H2 / PH-5 (CE-4228) — is the kill feed kept on this node right now (<c>hud.kill-feed</c>)?</summary>
    internal bool RecordsKills => Hrot.Common.Infrastructure.HudFeatures.IsOn(FeatureGate?.Invoke(), Hrot.Common.Infrastructure.HudFeatures.KillFeed);

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository world || !world.IsComponentTypeRegistered<Lifespan>()) return;   // the map's world (as the spawner's gate)
        double now = SimClock.Of(world).TotalTime;
        if (RecordsMessages && world.Bus.IsRegisteredManaged<Fdp.Toolkit.ScenarioRules.PlayerMessageEvent>())
            foreach (var m in view.ReadManagedEvents<Fdp.Toolkit.ScenarioRules.PlayerMessageEvent>())
                Hrot.UI.Common.Map3D.PlayerMessages.In(world).Record(m);
        if (RecordsKills && world.Bus.IsRegistered<Fdp.Toolkit.Combat.Events.ManDownedEvent>())
            foreach (ref readonly var down in view.ReadEvents<Fdp.Toolkit.Combat.Events.ManDownedEvent>())
                Hrot.UI.Common.Map3D.KillFeed.In(world).Record(world, in down, now);
    }
}
