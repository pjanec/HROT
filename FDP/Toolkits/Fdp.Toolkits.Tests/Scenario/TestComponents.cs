using System.Collections.Generic;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Scenario;

// ── Test-only component IDs (200–255 reserved block) ────────────────────────
// ScenarioIgnoreTag = 200 (defined in FDP.Toolkit.Scenario)
// EpisodeTag = 84 (now Fdp.Core.EpisodeTag, unmanaged struct, Guid EpisodeId)
// Test components occupy IDs 210–219.

namespace Fdp.Toolkit.Scenario.Tests
{
    // ────────────────────────────────────────────────────────────────────────────
    // Saveable components
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>Simple 3-D position component — saveable, no excluded fields.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(210)]
    public struct DummyPosition
    {
        public float X;
        public float Y;
        public float Z;
    }

    /// <summary>
    /// Component used for the N:M translator test.
    /// "BallisticProjectile" equivalent for the ordnance translator.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(211)]
    public struct TestBallisticProjectile
    {
        public float Damage;
        public float Speed;
    }

    /// <summary>
    /// Component used for the N:M translator / consumption-mask test.
    /// "PhysicsCollider" equivalent for the ordnance translator.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(212)]
    public struct TestPhysicsCollider
    {
        public float Radius;
    }

    /// <summary>Component that holds a cross-reference to another entity via GUID.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(213)]
    public struct GuidedTarget
    {
        /// <summary>
        /// Target entity handle.  During serialization this is replaced with a stable
        /// GUID string via <see cref="IGuidResolver"/>; during deserialization the GUID
        /// is resolved back to a live entity handle.
        /// </summary>
        public Entity TargetId;
    }

    /// <summary>
    /// Component used to test <c>[ScenarioIgnore]</c> field exclusion.
    /// <c>MaxSpeed</c> should appear in the DOM; <c>CachedWheelAngle</c> should not.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(215)]
    public struct CachedSpeedComponent
    {
        /// <summary>Serialized: present in scenario DOM.</summary>
        public float MaxSpeed;

        /// <summary>Runtime cache — excluded from scenario DOM via <see cref="ScenarioIgnoreAttribute"/>.</summary>
        [ScenarioIgnore]
        public float CachedWheelAngle;
    }

    // ────────────────────────────────────────────────────────────────────────────
    // NoSave component
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Transient velocity component — marked <c>NoSave</c> so it must never appear
    /// in the scenario DOM.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(214)]
    [DataPolicy(DataPolicy.NoSave)]
    public struct NoSaveVelocity
    {
        public float Speed;
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Test-only N:M translator
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Test N:M translator that compresses <see cref="TestBallisticProjectile"/> +
    /// <see cref="TestPhysicsCollider"/> into a single <c>"OrdnanceDef"</c> DOM entry.
    /// </summary>
    public sealed class MissileOrdnanceTranslator : IEntityScenarioTranslator
    {
        private static readonly int _ballisticId =
            ComponentTypeRegistry.GetOrRegisterManaged(typeof(TestBallisticProjectile));
        private static readonly int _colliderId =
            ComponentTypeRegistry.GetOrRegisterManaged(typeof(TestPhysicsCollider));

        public BitMask256 GetConsumedComponentsMask()
        {
            var mask = new BitMask256();
            mask.SetBit(_ballisticId);
            mask.SetBit(_colliderId);
            return mask;
        }

        public bool CanTranslate(EntityRepository repo, Entity entity)
            => repo.HasComponent<TestBallisticProjectile>(entity)
            && repo.HasComponent<TestPhysicsCollider>(entity);

        public Dictionary<string, object> Extract(
            EntityRepository repo, Entity entity, IGuidResolver guidResolver)
        {
            var bp = repo.GetComponent<TestBallisticProjectile>(entity);
            var pc = repo.GetComponent<TestPhysicsCollider>(entity);

            // Compress into a single "OrdnanceDef" JSON object.
            var obj = new System.Text.Json.Nodes.JsonObject
            {
                ["Damage"] = System.Text.Json.Nodes.JsonValue.Create(bp.Damage),
                ["Speed"]  = System.Text.Json.Nodes.JsonValue.Create(bp.Speed),
                ["Radius"] = System.Text.Json.Nodes.JsonValue.Create(pc.Radius),
            };

            return new Dictionary<string, object> { ["OrdnanceDef"] = obj };
        }

        public void Inject(
            EntityRepository repo, Entity entity,
            Dictionary<string, object> scenarioData, IGuidResolver guidResolver)
        {
            if (!scenarioData.TryGetValue("OrdnanceDef", out var rawNode)) return;
            var obj = (System.Text.Json.Nodes.JsonObject)rawNode;

            var bp = new TestBallisticProjectile
            {
                Damage = obj["Damage"]!.GetValue<float>(),
                Speed  = obj["Speed"]!.GetValue<float>(),
            };
            var pc = new TestPhysicsCollider
            {
                Radius = obj["Radius"]!.GetValue<float>(),
            };

            repo.SetComponent(entity, bp);
            repo.SetComponent(entity, pc);
        }

        /// <summary>Declares "OrdnanceDef" as a custom output DOM key so the
        /// auto-serializer unknown-key check skips it.</summary>
        public System.Collections.Generic.IEnumerable<string> GetOutputDomKeys()
            => new[] { "OrdnanceDef" };
    }
}
