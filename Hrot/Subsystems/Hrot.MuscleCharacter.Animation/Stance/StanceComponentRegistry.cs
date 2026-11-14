using Fdp.Core;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Events;

namespace Hrot.MuscleCharacter.Animation.Stance
{
    /// <summary>
    /// ⭐ <c>CE-2121</c> — the components the body-stance path needs on a node: the Brain's request
    /// (<see cref="StanceIntent"/>), the Muscle's report (<see cref="StanceStatus"/>), and the two internal components
    /// <see cref="Systems.AnimationRuntimeBridgeSystem"/> registers an entity with the backend by. Called by the SimHost and CGF
    /// registries (the editor calls both). ⚠ <see cref="Translators.AnimationTkbTranslator"/> adds a component only when its type
    /// is registered, so before this NO host carried them and every stance request was impossible to make.
    /// ⭐ <c>CE-3252</c> (LD-F) — the LOOK-AT set too (channel, report, executor state, and FDP's <c>LookState</c>). ⛔ The montage set
    /// (queues) stays unregistered — the rest of the pipeline is <c>CE-3010</c>.
    /// 📄 docs/DESIGN_Decision_Layer.md §3.3g.
    /// </summary>
    public static class StanceComponentRegistry
    {
        public static void RegisterAll(EntityRepository world)
        {
            if (!world.IsComponentTypeRegistered<StanceIntent>())                 world.RegisterComponent<StanceIntent>();
            if (!world.IsComponentTypeRegistered<StanceStatus>())                 world.RegisterComponent<StanceStatus>();
            if (!world.IsComponentTypeRegistered<CharacterAnimationDefRuntime>()) world.RegisterComponent<CharacterAnimationDefRuntime>();
            if (!world.IsComponentTypeRegistered<AnimationExecutorState>())       world.RegisterComponent<AnimationExecutorState>();
            // ⭐ CE-3252 (LD-F, G1) — the look-at channel, its report and the executor's state: a person's eyes ride the same road.
            if (!world.IsComponentTypeRegistered<LookAtChannel>())                world.RegisterComponent<LookAtChannel>();
            if (!world.IsComponentTypeRegistered<LookAtChannelStatus>())          world.RegisterComponent<LookAtChannelStatus>();
            if (!world.IsComponentTypeRegistered<LookAtExecutorState>())          world.RegisterComponent<LookAtExecutorState>();
            if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Look.LookState>()) world.RegisterComponent<Fdp.Toolkit.Perception.Look.LookState>();

            // ⚠ The events the module's systems publish (the reporter's StanceChangedEvent; the notify emitter's set when the fake
            //   backend's footstep cadence fires) — production enforces explicit event registration, so an unregistered publish throws.
            world.RegisterEvent<StanceChangedEvent>();
            world.RegisterEvent<MontageEndedEvent>();
            world.RegisterEvent<FootstepEvent>();
            world.RegisterEvent<HitWindowOpenedEvent>();
            world.RegisterEvent<HitWindowClosedEvent>();
            world.RegisterEvent<AnimNotifyEvent>();
        }
    }
}
