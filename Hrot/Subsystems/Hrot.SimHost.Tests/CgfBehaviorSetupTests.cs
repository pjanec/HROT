using System.Collections.Generic;
using Hrot.CGF.Configuration;
using Hrot.Presentation.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="CgfBehaviorSetup"/> factory methods — TASK-C011.
    /// </summary>
    public sealed class CgfBehaviorSetupTests
    {
        // ── C011 SC1: CreateBehaviorRemapper remaps FireAtTarget JSON ─────────

        /// <summary>
        /// C011 SC1: <see cref="CgfBehaviorSetup.CreateBehaviorRemapper"/> returns
        /// a remapper that translates the <c>targetNetworkId</c> field in
        /// FireAtTarget param JSON according to the supplied ID map.
        /// </summary>
        [Fact]
        public void C011_CreateBehaviorRemapper_RemapsFireAtTargetJson()
        {
            var remapper = CgfBehaviorSetup.CreateBehaviorRemapper();
            const string json = "{\"targetNetworkId\":999,\"maxRounds\":3,\"cooldownSeconds\":1.0}";
            var idMap = new Dictionary<long, long> { { 999L, 1999L } };

            var result = remapper.RemapJson("FireAtTarget", json, idMap);

            Assert.NotNull(result);
            Assert.Contains("\"targetNetworkId\":1999", result);
        }

        // ── C011 SC2: CreateBehaviorRemapper remaps FollowRoute JSON ─────────

        /// <summary>
        /// C011 SC2: <see cref="CgfBehaviorSetup.CreateBehaviorRemapper"/> returns
        /// a remapper that translates the <c>routeEntityId</c> field in
        /// FollowRoute param JSON according to the supplied ID map.
        /// </summary>
        [Fact]
        public void C011_CreateBehaviorRemapper_RemapsFollowRouteJson()
        {
            var remapper = CgfBehaviorSetup.CreateBehaviorRemapper();
            const string json = "{\"routeEntityId\":888,\"speed\":15.0}";
            var idMap = new Dictionary<long, long> { { 888L, 1888L } };

            var result = remapper.RemapJson("FollowRoute", json, idMap);

            Assert.NotNull(result);
            Assert.Contains("\"routeEntityId\":1888", result);
        }

        // ── C011 SC3: BehaviorUiSetup.CreateRegistry has all three behaviors ──

        /// <summary>
        /// C011 SC3: <see cref="BehaviorUiSetup.CreateRegistry"/> returns
        /// a registry that has draw delegates registered for FireAtTarget, FollowRoute,
        /// and MoveToLocation.
        /// </summary>
        [Fact]
        public void C011_CreateBehaviorUiRegistry_HasAllThreeBehaviors()
        {
            BehaviorUiRegistry registry = BehaviorUiSetup.CreateRegistry();

            Assert.True(registry.TryGet("FireAtTarget",   out _), "FireAtTarget should be registered");
            Assert.True(registry.TryGet("FollowRoute",    out _), "FollowRoute should be registered");
            Assert.True(registry.TryGet("MoveToLocation", out _), "MoveToLocation should be registered");
        }

        /// <summary>
        /// ⭐⭐⭐ <c>CE-416</c> ② — <b>a curated behaviour with a layout and NO resolver runs, and its JSON is parsed.</b>
        /// <para>🔴 Measured before the fix, through this exact path: <c>JoinFormation</c> assigned fine, got NO root block
        /// (the ingress gated the attach on <c>ParseParams != null</c>), and the next brain tick THREW "no ROOT PARAMS
        /// slot". Two fixes, two red-proofs: the ingress attaches whenever the block has a width (gate rail in
        /// <c>BehaviorIngressSystemTests</c>), and the curated generator emits the identity parse when no
        /// <c>[BehaviorResolver]</c> names the behaviour (Q75 decision D, state ②) — remove it and the 42 below is 0.</para>
        /// </summary>
        [Fact]
        public unsafe void CE416_ACuratedBehaviourWithNoResolver_RunsAndParsesItsJson()
        {
            using var repo = new Fdp.Core.EntityRepository();
            Hrot.SimHost.SimHostComponentRegistry.RegisterAll(repo);
            Hrot.SimHost.CognitiveComponentRegistry.RegisterAll(repo);
            Fdp.Toolkit.Blueprints.Partitioning.BlueprintTierTable.RegisterAll(repo);

            var registry = new Fdp.Toolkit.Behavior.BehaviorRegistry();
            CgfBehaviorSetup.LoadFromAiAssembly(registry);
            Assert.True(registry.TryGetId("JoinFormation", out int id));
            Assert.True(registry.TryGetDefinition(id, out var def));
            Assert.NotNull(def.ParseParams);   // ⭐ generated identity parse (no [BehaviorResolver] names it)

            var ingress = new Fdp.Toolkit.Behavior.Systems.BehaviorIngressSystem(registry);
            var tick    = new Fdp.Toolkit.Behavior.Systems.BrainTickSystem(registry);
            var e = repo.CreateEntity();
            repo.AddComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>(e, default);

            repo.Bus.PublishManaged(new Fdp.Toolkit.Behavior.Events.AssignBehaviorEvent
                { Entity = e, BehaviorName = "JoinFormation", JsonParams = "{\"LeaderNetworkId\":42}" });
            repo.Bus.SwapBuffers();
            ingress.Execute(repo, 0.1f);

            Assert.True(Fdp.Toolkit.Behavior.RootParamsAccess.TryGetRootBytes(repo, e, out byte* root, out int len));
            Assert.Equal(sizeof(Hrot.AI.Behaviors.Brains.CgfNodes.JoinFormationParams), len);
            Assert.Equal(42, ((Hrot.AI.Behaviors.Brains.CgfNodes.JoinFormationParams*)root)->LeaderNetworkId);

            Assert.Null(Record.Exception(() => tick.Execute(repo, 0.1f)));   // ⛔ it threw "no ROOT PARAMS slot"
        }
    }
}
