using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit;
using Fdp.Kernel;

namespace Fdp.Tests
{
    /// <summary>
    /// Unit tests for R0.1: Deterministic ECS component IDs.
    /// Covers <see cref="ComponentIdAttribute"/>, <see cref="GlobalComponentIds"/>,
    /// and the updated <see cref="ComponentTypeRegistry"/> enforcement and collision logic.
    /// </summary>
    public class ComponentIdAttributeTests
    {
        // ── Test-only component structs ─────────────────────────────────────────
        // Private nested to avoid polluting the global registry across other tests.
        // Each test calls ComponentTypeRegistry.Clear() for full isolation.

        [ComponentId(42)]
        private struct IdA_42 { public int Value; }

        [ComponentId(42)] // Intentional collision with IdA_42
        private struct IdB_42 { public float Value; }

        [ComponentId(100)]
        private struct IdC_100 { public int X; }

        [ComponentId(200)]
        private struct IdD_200 { public long Timestamp; }

        private struct NoAttributeStruct { public int Data; }

        // ── Tests ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Two component structs that both declare [ComponentId(42)] must cause an
        /// InvalidOperationException describing the collision when the second is registered.
        /// SC: ID collision between two structs → throws (R0.1 SC-6).
        /// </summary>
        [Fact]
        public void ComponentTypeRegistry_ThrowsOnExplicitIdCollision()
        {
            ComponentTypeRegistry.Clear();

            // First registration succeeds.
            var idA = ComponentTypeRegistry.GetOrRegister<IdA_42>();
            Assert.Equal(42, idA);

            // Second registration with the same explicit ID must throw.
            var ex = Assert.Throws<InvalidOperationException>(
                () => ComponentTypeRegistry.GetOrRegister<IdB_42>());

            Assert.Contains("collision", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("IdA_42", ex.Message);
            Assert.Contains("IdB_42", ex.Message);
            Assert.Contains("42", ex.Message);
        }

        /// <summary>
        /// Registering a struct that has no [ComponentId] attribute must always throw
        /// with a descriptive message (enforcement is unconditional after R0.1).
        /// SC: Struct without attribute → throws (R0.1 SC-6).
        /// </summary>
        [Fact]
        public void ComponentTypeRegistry_AlwaysEnforcesExplicitIds()
        {
            ComponentTypeRegistry.Clear();

            var ex = Assert.Throws<InvalidOperationException>(
                () => ComponentTypeRegistry.GetOrRegister<NoAttributeStruct>());

            Assert.Contains("missing", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ComponentId", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("NoAttributeStruct", ex.Message);
        }

        /// <summary>
        /// The ID returned by the registry must be exactly the value declared in
        /// [ComponentId], not a sequential auto-increment value.
        /// SC: Explicit ID is returned (not auto-incremented value) (R0.1 SC-6).
        /// </summary>
        [Fact]
        public void ComponentTypeRegistry_ReturnsExplicitId_NotAutoIncrement()
        {
            ComponentTypeRegistry.Clear();

            var id = ComponentTypeRegistry.GetOrRegister<IdC_100>();
            Assert.Equal(100, id);
        }

        /// <summary>
        /// After Clear(), re-registering the same type must read the ID from the
        /// [ComponentId] attribute again, not from any cached state.
        /// SC: Registry clear re-reads from attribute (R0.1 SC-6).
        /// </summary>
        [Fact]
        public void ComponentTypeRegistry_AfterClear_ReReadsIdFromAttribute()
        {
            // First registration.
            ComponentTypeRegistry.Clear();
            var firstId = ComponentTypeRegistry.GetOrRegister<IdD_200>();
            Assert.Equal(200, firstId);

            // Clear and re-register — must get the same explicit ID.
            ComponentTypeRegistry.Clear();
            var secondId = ComponentTypeRegistry.GetOrRegister<IdD_200>();
            Assert.Equal(200, secondId);
        }

        /// <summary>
        /// Multiple explicit-ID registrations must all return their declared IDs
        /// and all be retrievable via GetType(id).
        /// SC: Multiple explicit IDs are stored and retrievable from the registry.
        /// </summary>
        [Fact]
        public void ComponentTypeRegistry_StoresMultipleExplicitIds_AllRetrievable()
        {
            ComponentTypeRegistry.Clear();

            var idC = ComponentTypeRegistry.GetOrRegister<IdC_100>();
            var idD = ComponentTypeRegistry.GetOrRegister<IdD_200>();

            Assert.Equal(100, idC);
            Assert.Equal(200, idD);

            Assert.Equal(typeof(IdC_100), ComponentTypeRegistry.GetType(100));
            Assert.Equal(typeof(IdD_200), ComponentTypeRegistry.GetType(200));
        }

        /// <summary>
        /// GlobalComponentIds constants must be in range [0, 255] (BitMask256 limit)
        /// and each constant name must match its expected ID block.
        /// SC: All GlobalComponentIds constants are within their declared block ranges.
        /// </summary>
        [Fact]
        public void GlobalComponentIds_AllConstantsAreInExpectedRanges()
        {
            // Kernel block (0–19)
            Assert.InRange<byte>(GlobalComponentIds.SimTransform,        0,  19);
            Assert.InRange<byte>(GlobalComponentIds.SimVelocity,         0,  19);
            Assert.InRange<byte>(GlobalComponentIds.HealthData,          0,  19);
            Assert.InRange<byte>(GlobalComponentIds.GlobalTime,          0,  19);
            Assert.InRange<byte>(GlobalComponentIds.IsActiveTag,         0,  19);
            Assert.InRange<byte>(GlobalComponentIds.LifecycleDescriptor, 0,  19);
            Assert.InRange<byte>(GlobalComponentIds.HierarchyNode,       0,  19);
            Assert.InRange<byte>(GlobalComponentIds.PartDescriptor,      0,  19);

            // Replication block (50–79)
            Assert.InRange<byte>(GlobalComponentIds.NetworkIdentity,     50, 79);
            Assert.InRange<byte>(GlobalComponentIds.NetworkAuthority,    50, 79);
            Assert.InRange<byte>(GlobalComponentIds.NetworkTransform,    50, 79);
            Assert.InRange<byte>(GlobalComponentIds.NetworkVelocity,     50, 79);
            Assert.InRange<byte>(GlobalComponentIds.NetworkSpawnRequest, 50, 79);
            Assert.InRange<byte>(GlobalComponentIds.PartMetadata,        50, 79);

            // Vis2D block (80–109)
            Assert.InRange<byte>(GlobalComponentIds.MapDisplayComponent, 80, 109);
            Assert.InRange<byte>(GlobalComponentIds.VisHierarchyNode,    80, 109);
            Assert.InRange<byte>(GlobalComponentIds.AggregateState,      80, 109);
            Assert.InRange<byte>(GlobalComponentIds.AggregateRoot,       80, 109);

            // IG block (110–139)
            Assert.InRange<byte>(GlobalComponentIds.ResolvedStyle,       110, 139);
            Assert.InRange<byte>(GlobalComponentIds.CullingState,        110, 139);
            Assert.InRange<byte>(GlobalComponentIds.SelectionState,      110, 139);
            Assert.InRange<byte>(GlobalComponentIds.VisualEffectState,   110, 139);
            Assert.InRange<byte>(GlobalComponentIds.TracerTarget,        110, 139);
        }

        /// <summary>
        /// Every <c>const byte</c> field on <see cref="GlobalComponentIds"/> must have a
        /// unique value. A duplicate would mean two component types share the same ID,
        /// causing silent data corruption in the ECS.
        /// </summary>
        [Fact]
        public void GlobalComponentIds_NoToolkitBlockDuplicates()
        {
            var fields = typeof(GlobalComponentIds)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(byte))
                .ToList();

            var seen = new Dictionary<byte, string>();
            foreach (var field in fields)
            {
                var value = (byte)field.GetRawConstantValue()!;
                if (seen.TryGetValue(value, out var existing))
                    Assert.Fail($"Duplicate GlobalComponentId value {value}: '{existing}' and '{field.Name}'");
                seen[value] = field.Name;
            }
        }
    }
}
