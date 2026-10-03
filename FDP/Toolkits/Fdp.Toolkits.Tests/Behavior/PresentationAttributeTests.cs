using System.Linq;
using System.Reflection;
using Fdp.Toolkit.Behavior.Attributes;
using Fdp.Toolkit.Behavior.Params;
using Fdp.Toolkit.Behavior.Tests.Fixtures;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>Tests for TASK-C008 presentation attributes.</summary>
    public class PresentationAttributeTests
    {
        /// <summary>⭐ DESIGN_Entity_Reference.md D5 — the TYPE marks an entity reference (picker and remap); the attribute
        /// only narrows the pick. (C008 SC1 asserted the old pair of attributes on a <c>long</c>.)</summary>
        [Fact]
        public void C008_FireAtTarget_TargetNetworkId_IsAnEntityRef()
        {
            var prop = typeof(FireAtTargetParamsJsonDto).GetProperty("TargetNetworkId");
            Assert.NotNull(prop);
            Assert.Equal(typeof(Fdp.Toolkit.Replication.EntityRef), prop!.PropertyType);
            Assert.True(Fdp.Toolkit.Replication.EntityRefRemap.HoldsRefs(typeof(FireAtTargetParamsJsonDto)));
        }

        /// <summary>C008 SC2 (P5 of DESIGN_Map_Picking_Unification): MoveToLocation's composite PickableLocation is a
        /// <c>PickableGeoPoint</c> — the TYPE that makes it map-pickable; the scalar TargetLat/TargetLon are plain doubles,
        /// so only the composite gets a picker (the two-button problem stays fixed). It holds no entity reference.</summary>
        [Fact]
        public void C008_MoveToLocation_PickableLocation_IsTheOnlyPickableMember_NoEntityRef()
        {
            var pickable = typeof(MoveToLocationParamsJsonDto).GetProperties()
                .Where(p => p.PropertyType == typeof(Fdp.Toolkit.Behavior.Params.PickableGeoPoint))
                .Select(p => p.Name).ToArray();
            Assert.Equal(new[] { "PickableLocation" }, pickable);
            Assert.Equal(typeof(double), typeof(MoveToLocationParamsJsonDto).GetProperty("TargetLat")!.PropertyType);

            Assert.False(Fdp.Toolkit.Replication.EntityRefRemap.HoldsRefs(typeof(MoveToLocationParamsJsonDto)));
        }

        /// <summary>C008 SC3: MapPickableEntityAttribute stores filter presets correctly.</summary>
        [Fact]
        public void C008_MapPickableEntityAttribute_StoresFilterPresets()
        {
            var attr = new MapPickableEntityAttribute("roads", "graphs");
            Assert.NotNull(attr.FilterPresets);
            Assert.Equal(2, attr.FilterPresets!.Length);
            Assert.Contains("roads", attr.FilterPresets);
            Assert.Contains("graphs", attr.FilterPresets);
        }
    }
}
