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
    }
}
