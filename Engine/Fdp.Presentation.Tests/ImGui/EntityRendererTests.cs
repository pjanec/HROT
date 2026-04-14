using Fdp.Kernel;
using FDP.Toolkit.ImGui.Renderers;
using Xunit;

namespace FDP.Toolkit.ImGui.Tests
{
    /// <summary>
    /// Tests for <see cref="EntityRenderer"/> — the built-in renderer that shows
    /// "[index, vGeneration]" inline while keeping the node expandable.
    /// </summary>
    public class EntityRendererTests
    {
        private readonly EntityRenderer _renderer = new();

        [Fact]
        public void GetSummary_LiveEntity_ReturnsIndexAndGeneration()
        {
            var entity = new Entity(12, 3);
            Assert.Equal("[12, v3]", _renderer.GetSummary(entity));
        }

        [Fact]
        public void GetSummary_NullEntity_ReturnsNullLabel()
        {
            Assert.Equal("[null]", _renderer.GetSummary(Entity.Null));
        }

        [Fact]
        public void RenderValue_AlwaysReturnsFalse_KeepingNodeExpandable()
        {
            // RenderValue returning false means "I did not render; keep the node foldable".
            Assert.False(_renderer.RenderValue(new Entity(1, 1)));
        }

        [Fact]
        public void GetSummary_EntityWithIndex0Generation0_ReturnsNullLabel()
        {
            // Entity(0, 0) → Entity.Null (IsNull = Index < 0 || Generation == 0)
            var entity = new Entity(0, 0);
            Assert.Equal("[null]", _renderer.GetSummary(entity));
        }

        [Fact]
        public void GetSummary_EntityWithNegativeIndex_ReturnsNullLabel()
        {
            var entity = new Entity(-1, 1);
            Assert.Equal("[null]", _renderer.GetSummary(entity));
        }
    }
}
