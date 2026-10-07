using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Translators;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core.Tkb;
using Hrot.Map.Common;
using Hrot.MuscleCharacter.Animation;
using Hrot.MuscleCharacter.Animation.Baking;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Fake;
using Hrot.MuscleCharacter.Animation.Stance;
using Hrot.MuscleCharacter.Animation.Systems;
using Hrot.MuscleCharacter.Animation.Translators;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// ⭐ <c>CE-2121</c> — body stance on one world: the soldier's TKB gives it a stance it may change, the Brain asks for prone, the
/// Muscle (<see cref="AnimationMuscleModule"/> over the existing <see cref="FakeAnimationBackend"/>) performs it, and the map line
/// follows the BODY. 📄 docs/DESIGN_Decision_Layer.md §3.3g.
/// </summary>
public class BodyStanceTests
{
    private sealed class Recorder : ISystemRegistry
    {
        public readonly List<IEcsModuleSystem> Systems = new();
        public void RegisterSystem<T>(T system) where T : IEcsModuleSystem => Systems.Add(system);
        public IEcsModuleSystem RegisterManualSystem<T>(T system) where T : IEcsModuleSystem { Systems.Add(system); return system; }
    }

    private static (EntityRepository world, Entity soldier, List<IEcsModuleSystem> systems) Soldier()
    {
        var world = new EntityRepository();
        world.RegisterComponent<ActorCapabilityState>();
        world.RegisterComponent<PreviousCapabilities>();
        StanceComponentRegistry.RegisterAll(world);
        ITkbDatabase tkb = HrotEnvironment.CreateTkb();
        Assert.True(tkb.TryGetByType(UrbanCombatTkbCatalog.TkbInfantrySoldier, out var template));
        var soldier = world.CreateEntity();
        new BehaviorTkbTranslator().Inject(world, soldier, template!);
        new AnimationTkbTranslator(null).Inject(world, soldier, template!);
        var rec = new Recorder();
        new AnimationMuscleModule(new FakeAnimationBackend(), new BakedAnimationCache(null)).RegisterSystems(rec);
        return (world, soldier, rec.Systems);
    }

    private static void Tick(EntityRepository world, List<IEcsModuleSystem> systems, float dt)
    {
        foreach (var s in systems) s.Execute(world, dt);
        world.Bus.SwapBuffers();
    }

    [Fact]
    public void CE2121_TheSoldierMayChangeStance_AndStartsStanding()
    {
        var (world, soldier, systems) = Soldier();
        Assert.True(world.GetComponentRO<ActorCapabilityState>(soldier).Capabilities.HasFlag(ActorCapabilities.CanChangeStance));
        Assert.Equal(StanceId.Standing, world.GetComponentRO<StanceStatus>(soldier).CurrentStance);
        Assert.Contains(systems, s => s is StanceTransitionSystem);   // the module now applies a stance request
        Assert.Null(StanceGizmo.Text(world, soldier, world.GetComponentRO<StanceStatus>(soldier)));   // standing: no line
    }

    [Fact]
    public void CE2121_AskedForProne_TheBodyGoesDownOverTheBlendTime_AndTheMapFollowsTheBody()
    {
        var (world, soldier, systems) = Soldier();
        Tick(world, systems, 0.1f);                                                       // registers with the backend

        Assert.True(StanceRequest.Set(world, soldier, StanceId.Prone, 1.0f));
        uint v = world.GetComponentRO<StanceIntent>(soldier).Version;
        Assert.True(StanceRequest.Set(world, soldier, StanceId.Prone, 1.0f));             // asking again changes nothing
        Assert.Equal(v, world.GetComponentRO<StanceIntent>(soldier).Version);

        Tick(world, systems, 0.1f);
        var status = world.GetComponentRO<StanceStatus>(soldier);
        Assert.Equal(StanceTransitionPhase.Transitioning, status.Phase);
        Assert.Equal(StanceId.Standing, status.CurrentStance);                            // not down yet
        Assert.Equal("→ Prone", StanceGizmo.Text(world, soldier, status));

        for (int i = 0; i < 12; i++) Tick(world, systems, 0.1f);                           // > 1.0 s
        status = world.GetComponentRO<StanceStatus>(soldier);
        Assert.Equal(StanceId.Prone, status.CurrentStance);
        Assert.Equal("Prone", StanceGizmo.Text(world, soldier, status));

        Assert.True(StanceRequest.Set(world, soldier, StanceId.Standing, 0.8f));           // and up again
        for (int i = 0; i < 12; i++) Tick(world, systems, 0.1f);
        Assert.Equal(StanceId.Standing, world.GetComponentRO<StanceStatus>(soldier).CurrentStance);
    }

    [Fact]
    public void CE2121_AUnitWithNoStance_CannotBeAsked()
    {
        var world = new EntityRepository();
        StanceComponentRegistry.RegisterAll(world);
        Assert.False(StanceRequest.Set(world, world.CreateEntity(), StanceId.Prone, 1f));
        Assert.False(StanceRequest.Set(new EntityRepository(), new EntityRepository().CreateEntity(), StanceId.Prone, 1f));
    }
}
