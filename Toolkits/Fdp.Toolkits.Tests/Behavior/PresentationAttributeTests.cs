using System.Reflection;
using Fdp.Toolkit.Behavior.Attributes;
using Fdp.Toolkit.Behavior.Params;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>Tests for TASK-C008 presentation attributes.</summary>
    public class PresentationAttributeTests
    {
        /// <summary>C008 SC1: FireAtTarget.TargetNetworkId has both MapPickableEntityAttribute and RemapNetworkIdAttribute.</summary>
        [Fact]
        public void C008_FireAtTarget_TargetNetworkId_HasBothPickAndRemapAttributes()
        {
            var prop = typeof(FireAtTargetParamsJsonDto).GetProperty("TargetNetworkId");
            Assert.NotNull(prop);
            Assert.NotNull(prop!.GetCustomAttribute<MapPickableEntityAttribute>());
            Assert.NotNull(prop.GetCustomAttribute<RemapNetworkIdAttribute>());
        }

        /// <summary>C008 SC2: MoveToLocation lat/lon have MapPickableWorldLocationAttribute; no property has RemapNetworkIdAttribute.</summary>
        [Fact]
        public void C008_MoveToLocation_LatLon_HaveWorldLocationAttr_NoRemapAttr()
        {
            var latProp = typeof(MoveToLocationParamsJsonDto).GetProperty("TargetLat");
            var lonProp = typeof(MoveToLocationParamsJsonDto).GetProperty("TargetLon");
            Assert.NotNull(latProp);
            Assert.NotNull(lonProp);
            Assert.NotNull(latProp!.GetCustomAttribute<MapPickableWorldLocationAttribute>());
            Assert.NotNull(lonProp!.GetCustomAttribute<MapPickableWorldLocationAttribute>());

            var allProps = typeof(MoveToLocationParamsJsonDto)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var p in allProps)
                Assert.Null(p.GetCustomAttribute<RemapNetworkIdAttribute>());
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
