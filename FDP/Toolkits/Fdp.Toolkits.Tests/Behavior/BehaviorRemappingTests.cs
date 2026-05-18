using System.Collections.Generic;
using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Attributes;
using Fdp.Toolkit.Behavior.Params;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    // ─── C005c Tests ────────────────────────────────────────────────────────────

    public class BehaviorParamRemapperCompilerTests
    {
        // Private DTO used only for the caching test to guarantee the first compile.
        private class CachingProbeDto
        {
            [JsonPropertyName("entityId")]
            [RemapNetworkId]
            public long EntityId { get; set; }
        }

        /// <summary>C005c SC1: FireAtTarget TargetNetworkId remapped; other fields unchanged.</summary>
        [Fact]
        public void C005c_FireAtTarget_TargetNetworkId_Remapped()
        {
            var remap = BehaviorParamRemapperCompiler.Compile<FireAtTargetParamsJsonDto>();
            const string json = "{\"targetNetworkId\":1001,\"maxRounds\":5,\"cooldownSeconds\":1.0}";
            var map = new Dictionary<long, long> { { 1001L, 2001L } };

            var result = remap(json, map);

            Assert.NotNull(result);
            Assert.Contains("\"targetNetworkId\":2001", result);
            Assert.Contains("\"maxRounds\":5", result);
            Assert.Contains("\"cooldownSeconds\":1", result);
        }

        /// <summary>C005c SC2: FollowRoute RouteEntityId remapped.</summary>
        [Fact]
        public void C005c_FollowRoute_RouteEntityId_Remapped()
        {
            var remap = BehaviorParamRemapperCompiler.Compile<FollowRouteParamsJsonDto>();
            const string json = "{\"routeEntityId\":999}";
            var map = new Dictionary<long, long> { { 999L, 888L } };

            var result = remap(json, map);

            Assert.NotNull(result);
            Assert.Contains("\"routeEntityId\":888", result);
        }

        /// <summary>C005c SC3: ID not in map passes through unchanged.</summary>
        [Fact]
        public void C005c_IdNotInMap_PassesThrough()
        {
            var remap = BehaviorParamRemapperCompiler.Compile<FireAtTargetParamsJsonDto>();
            const string json = "{\"targetNetworkId\":1001,\"maxRounds\":3,\"cooldownSeconds\":0.5}";
            var map = new Dictionary<long, long>();  // empty map

            var result = remap(json, map);

            Assert.NotNull(result);
            Assert.Contains("\"targetNetworkId\":1001", result);
        }

        /// <summary>C005c SC4: Null/empty JSON returns unchanged.</summary>
        [Fact]
        public void C005c_NullOrEmptyJson_ReturnsUnchanged()
        {
            var remap = BehaviorParamRemapperCompiler.Compile<FireAtTargetParamsJsonDto>();
            var map   = new Dictionary<long, long> { { 1L, 2L } };

            Assert.Null(remap(null, map));
            Assert.Equal(string.Empty, remap(string.Empty, map));
        }

        /// <summary>C005c SC5: MoveToLocation has no remappable fields — identity delegate.</summary>
        [Fact]
        public void C005c_MoveToLocation_NoRemappableFields_IdentityDelegate()
        {
            const string json = "{\"targetLat\":1.0,\"targetLon\":2.0,\"speed\":10.0,\"arrivalRadius\":5.0}";
            var remap = BehaviorParamRemapperCompiler.Compile<MoveToLocationParamsJsonDto>();
            var map   = new Dictionary<long, long> { { 1L, 99L } };

            var result = remap(json, map);

            // Identity delegate: same string reference returned unchanged.
            Assert.Same(json, result);
        }

        /// <summary>C005c SC6: Delegate compiled only once per type (caching).</summary>
        [Fact]
        public void C005c_DelegateCompiledOnlyOnce_CachingVerified()
        {
            // Use a private DTO type unique to this test to guarantee a fresh cache entry.
            int countBefore = BehaviorParamRemapperCompiler.CompileCallCount;

            var d1 = BehaviorParamRemapperCompiler.Compile<CachingProbeDto>();
            var d2 = BehaviorParamRemapperCompiler.Compile<CachingProbeDto>();
            var d3 = BehaviorParamRemapperCompiler.Compile<CachingProbeDto>();

            // CompileCallCount increments only on cache miss (first compile).
            Assert.Equal(countBefore + 1, BehaviorParamRemapperCompiler.CompileCallCount);

            // All three calls return the same cached delegate instance.
            Assert.True(ReferenceEquals(d1, d2), "second call must return cached delegate");
            Assert.True(ReferenceEquals(d1, d3), "third call must return cached delegate");
        }
    }

    // ─── C005d Tests ────────────────────────────────────────────────────────────

    public class ScenarioBehaviorRemapperTests
    {
        /// <summary>C005d SC1: Registered behavior JSON is remapped correctly.</summary>
        [Fact]
        public void C005d_RegisteredBehavior_IdsRemapped()
        {
            var remapper = new ScenarioBehaviorRemapper();
            remapper.Register<FireAtTargetParamsJsonDto>("FireAtTarget");

            const string json = "{\"targetNetworkId\":1001,\"maxRounds\":2,\"cooldownSeconds\":0.5}";
            var map = new Dictionary<long, long> { { 1001L, 2001L } };

            var result = remapper.RemapJson("FireAtTarget", json, map);

            Assert.NotNull(result);
            Assert.Contains("\"targetNetworkId\":2001", result);
            Assert.Contains("\"maxRounds\":2", result);
        }

        /// <summary>C005d SC2: Unregistered behavior returns JSON unchanged without exception.</summary>
        [Fact]
        public void C005d_UnregisteredBehavior_PassesThrough()
        {
            var remapper = new ScenarioBehaviorRemapper();
            const string json = "{\"targetNetworkId\":1001}";
            var map = new Dictionary<long, long> { { 1001L, 2001L } };

            var result = remapper.RemapJson("SomeUnknownBehavior", json, map);

            Assert.Equal(json, result);
        }

        /// <summary>C005d SC3: Double-registration throws InvalidOperationException.</summary>
        [Fact]
        public void C005d_DoubleRegistration_Throws()
        {
            var remapper = new ScenarioBehaviorRemapper();
            remapper.Register<FireAtTargetParamsJsonDto>("FireAtTarget");

            var ex = Assert.Throws<InvalidOperationException>(
                () => remapper.Register<FireAtTargetParamsJsonDto>("FireAtTarget"));

            Assert.Contains("FireAtTarget", ex.Message);
        }
    }
}
