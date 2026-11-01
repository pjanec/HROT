using System.Numerics;
using Fdp.Core;
using Xunit;

namespace Fdp.Toolkits.Tests.Replication
{
    /// <summary>⭐ S0 item 6 — the ONE storage mapping every type-switch site routes through (<c>SemanticValueTypes</c>).</summary>
    public sealed class SemanticValueTypesTests
    {
        [Fact]
        public void StorageTypeOf_MapsTheThreeSemanticTypes_AndLeavesEveryOtherTypeAlone()
        {
            Assert.Equal(typeof(Vector3), SemanticValueTypes.StorageTypeOf(typeof(WorldPoint)));
            Assert.Equal(typeof(Quaternion), SemanticValueTypes.StorageTypeOf(typeof(WorldRotation)));
            Assert.Equal(typeof(float), SemanticValueTypes.StorageTypeOf(typeof(Bearing)));
            Assert.Equal(typeof(Vector3), SemanticValueTypes.StorageTypeOf(typeof(Vector3)));
            Assert.Equal(typeof(string), SemanticValueTypes.StorageTypeOf(typeof(string)));
        }

        [Fact]
        public void ToStorage_And_FromStorage_RoundTrip_EachSemanticValue()
        {
            Assert.Equal(new Vector3(1, 2, 3), SemanticValueTypes.ToStorage(new WorldPoint(1, 2, 3)));
            Assert.Equal(new WorldPoint(1, 2, 3), SemanticValueTypes.FromStorage(new Vector3(1, 2, 3), typeof(WorldPoint)));
            var q = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f);
            Assert.Equal(q, SemanticValueTypes.ToStorage(new WorldRotation(q)));
            Assert.Equal(new WorldRotation(q), SemanticValueTypes.FromStorage(q, typeof(WorldRotation)));
            Assert.Equal(45f, SemanticValueTypes.ToStorage(new Bearing(45f)));
            Assert.Equal(new Bearing(45f), SemanticValueTypes.FromStorage(45f, typeof(Bearing)));
        }

        [Fact]
        public void AnythingElse_PassesThroughUnchanged()
        {
            Assert.Null(SemanticValueTypes.ToStorage(null));
            Assert.Equal(7, SemanticValueTypes.ToStorage(7));
            Assert.Equal(new Vector3(1, 2, 3), SemanticValueTypes.FromStorage(new Vector3(1, 2, 3), typeof(Vector3)));
            Assert.Equal(new WorldPoint(1, 2, 3), SemanticValueTypes.FromStorage(new WorldPoint(1, 2, 3), typeof(WorldPoint)));
        }
    }
}
