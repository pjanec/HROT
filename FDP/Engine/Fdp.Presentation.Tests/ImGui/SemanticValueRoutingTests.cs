using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Renderers;
using Fdp.Presentation.Utils;
using Xunit;

namespace Fdp.Presentation.Tests
{
    /// <summary>
    /// ⭐ S0 item 6 — a <see cref="WorldPoint"/> / <see cref="WorldRotation"/> / <see cref="Bearing"/> field draws, formats and classifies exactly as the
    /// <see cref="Vector3"/> / <see cref="Quaternion"/> / <c>float</c> it replaced, because the generic widgets route through
    /// <see cref="SemanticValueTypes"/>. ⚠ Red-proof: drop the <c>ToStorage</c> line in <c>ImGuiPropertyTree.FormatLeaf</c> → the first two fail.
    /// </summary>
    public sealed class SemanticValueRoutingTests
    {
        [Fact]
        public void FormatLeaf_ASemanticValue_ReadsAsItsStorageValueDid()
        {
            Assert.Equal(ImGuiPropertyTree.FormatLeaf(new Vector3(1, 2, 3)), ImGuiPropertyTree.FormatLeaf(new WorldPoint(1, 2, 3)));
            Assert.Equal(ImGuiPropertyTree.FormatLeaf(12.5f), ImGuiPropertyTree.FormatLeaf(new Bearing(12.5f)));
        }

        [Fact]
        public void IsLeafType_ASemanticType_IsALeafAsItsStorageTypeIs()
        {
            Assert.True(ImGuiPropertyTree.IsLeafType(typeof(WorldPoint)));
            Assert.True(ImGuiPropertyTree.IsLeafType(typeof(WorldRotation)));
            Assert.True(ImGuiPropertyTree.IsLeafType(typeof(Bearing)));
        }

        [Fact]
        public void TheRendererRegistry_ServesASemanticTypeWithItsStorageTypesRenderer()
        {
            var q = Quaternion.CreateFromYawPitchRoll(0.4f, 0.1f, 0.2f);
            Assert.Equal(ImGuiRendererRegistry.GetRenderer(typeof(Vector3))!.GetSummary(new Vector3(1, 2, 3)),
                         ImGuiRendererRegistry.GetRenderer(typeof(WorldPoint))!.GetSummary(new WorldPoint(1, 2, 3)));
            Assert.Equal(ImGuiRendererRegistry.GetRenderer(typeof(Quaternion))!.GetSummary(q),
                         ImGuiRendererRegistry.GetRenderer(typeof(WorldRotation))!.GetSummary(new WorldRotation(q)));
        }
    }
}
