using System.Collections.Generic;
using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Behavior.Params;
using Fdp.Toolkit.Behavior.Tests.Fixtures;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    // ─── C005c Tests ────────────────────────────────────────────────────────────

    public class EntityRefJsonRemapTests
    {
        // Private DTO used only for the caching test to guarantee the first compile.
        private class CachingProbeDto
        {
            [JsonPropertyName("entityId")]
            public EntityRef EntityId { get; set; }
        }

        /// <summary>C005c SC1: FireAtTarget TargetNetworkId remapped; other fields unchanged.</summary>
        [Fact]
        public void C005c_FireAtTarget_TargetNetworkId_Remapped()
        {
            var remap = EntityRefRemap.CompileJson(typeof(FireAtTargetParamsJsonDto));
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
            var remap = EntityRefRemap.CompileJson(typeof(FollowRouteParamsJsonDto));
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
            var remap = EntityRefRemap.CompileJson(typeof(FireAtTargetParamsJsonDto));
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
            var remap = EntityRefRemap.CompileJson(typeof(FireAtTargetParamsJsonDto));
            var map   = new Dictionary<long, long> { { 1L, 2L } };

            Assert.Null(remap(null, map));
            Assert.Equal(string.Empty, remap(string.Empty, map));
        }

        /// <summary>C005c SC5: MoveToLocation has no remappable fields — identity delegate.</summary>
        [Fact]
        public void C005c_MoveToLocation_NoRemappableFields_IdentityDelegate()
        {
            const string json = "{\"targetLat\":1.0,\"targetLon\":2.0,\"speed\":10.0,\"arrivalRadius\":5.0}";
            var remap = EntityRefRemap.CompileJson(typeof(MoveToLocationParamsJsonDto));
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
            int countBefore = EntityRefRemap.JsonCompileCount;

            var d1 = EntityRefRemap.CompileJson(typeof(CachingProbeDto));
            var d2 = EntityRefRemap.CompileJson(typeof(CachingProbeDto));
            var d3 = EntityRefRemap.CompileJson(typeof(CachingProbeDto));

            // CompileCallCount increments only on cache miss (first compile).
            Assert.Equal(countBefore + 1, EntityRefRemap.JsonCompileCount);

            // All three calls return the same cached delegate instance.
            Assert.True(ReferenceEquals(d1, d2), "second call must return cached delegate");
            Assert.True(ReferenceEquals(d1, d3), "third call must return cached delegate");
        }
    }

    // ─── CE-2054 (S8o) — the plan is a property of the contract TYPE, so it nests ───────────────────────────────

    public class CE2054_NestedContractRemapTests
    {
        /// <summary>The shape of a generated blueprint behaviour <c>Params</c>: FIELDS, some typed as curated contracts.</summary>
        private struct MissionParams
        {
            public MoveDto Move;
            public FireDto Fire;
            public long Plain;      // ⛔ a bare long is NOT a reference ⇒ never touched
            public EntityRef Escort;
        }

        private struct MoveDto { public float X { get; set; } }

        private struct FireDto
        {
            [JsonPropertyName("targetNetworkId")] public EntityRef TargetNetworkId { get; set; }
            [JsonPropertyName("maxRounds")] public int MaxRounds { get; set; }
        }

        private static readonly Dictionary<long, long> Map = new() { [1006] = 1001, [7] = 70 };

        /// <summary>
        /// ⭐⭐ The id one object down is rewritten; every other byte comes back as authored — no key added, none
        /// re-cased, no number reformatted. 🔴 Red before CE-2054: the old compiler saw no top-level
        /// reference PROPERTY on <c>MissionParams</c> and returned the identity delegate.
        /// </summary>
        [Fact]
        public void AnIdNestedInAContractTypedField_IsRemapped_AndNothingElseMoves()
        {
            const string json = "{\"move\":{\"x\":600.50},\"Fire\":{\"targetNetworkId\":1006,\"maxRounds\":3},\"plain\":1006}";

            string? result = EntityRefRemap.CompileJson(typeof(MissionParams))(json, Map);

            Assert.Equal("{\"move\":{\"x\":600.50},\"Fire\":{\"targetNetworkId\":1001,\"maxRounds\":3},\"plain\":1006}", result);
        }

        /// <summary>⭐ An <c>EntityRef</c> FIELD is a reference too (C005a: "properties/fields").</summary>
        [Fact]
        public void ATaggedLongField_IsRemapped()
        {
            string? result = EntityRefRemap.CompileJson(typeof(MissionParams))("{\"escort\":7}", Map);
            Assert.Equal("{\"escort\":70}", result);
        }

        /// <summary>
        /// ⭐ Nothing to remap ⇒ the SAME string. ⛔ A round-trip would write every member back, so an absent key would
        /// arrive as 0 and override the Parameter's declared default.
        /// </summary>
        [Fact]
        public void NoIdInTheMap_ReturnsTheSameString_WithNoKeyAdded()
        {
            const string json = "{\"Fire\":{\"maxRounds\":3}}";
            string? result = EntityRefRemap.CompileJson(typeof(MissionParams))(json, Map);
            Assert.Same(json, result);
        }

        /// <summary>⭐ The registry is the source of a behaviour's contract type: a behaviour no
        /// <see cref="ScenarioBehaviorRemapper.Register{TDto}"/> call ever named is remapped by its
        /// <see cref="BehaviorDefinition.JsonParamsDtoType"/>. 🔴 Red before CE-2054: unknown name ⇒ passed through.</summary>
        [Fact]
        public void TheRemapper_ResolvesAnUnregisteredBehaviour_ThroughTheRegistry()
        {
            var registry = new BehaviorRegistry();
            registry.Register("Demo_Plan", new BehaviorDefinition
            {
                Name              = "Demo_Plan",
                BrainTier         = BehaviorConstants.BrainTierBlueprint,
                JsonParamsDtoType = typeof(MissionParams),
            });

            string? result = new ScenarioBehaviorRemapper(registry)
                .RemapJson("Demo_Plan", "{\"Fire\":{\"targetNetworkId\":1006}}", Map);

            Assert.Equal("{\"Fire\":{\"targetNetworkId\":1001}}", result);
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

        /// <summary>
        /// ⭐⭐ <c>CE-2023</c> ③ (S8n) — a STRUCT contract is remapped in place. 🔴 With a by-value setter the new id was written
        /// into a copy and the JSON came back with the old one.
        /// </summary>
        [Fact]
        public void CE2023_AStructContract_IsRemappedInPlace()
        {
            var remap = EntityRefRemap.CompileJson(typeof(StructRemapDto));
            string? result = remap("{\"targetNetworkId\":42,\"maxRounds\":3}", new Dictionary<long, long> { [42] = 1042 });
            Assert.Contains("1042", result);
            Assert.Contains("\"maxRounds\":3", result);
        }

        private struct StructRemapDto
        {
            [System.Text.Json.Serialization.JsonPropertyName("targetNetworkId")]
            public EntityRef TargetNetworkId { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("maxRounds")]
            public int MaxRounds { get; set; }
        }
    }
}
