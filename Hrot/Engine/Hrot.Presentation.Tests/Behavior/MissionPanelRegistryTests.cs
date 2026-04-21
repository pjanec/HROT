using Hrot.Presentation.Behavior;
using Hrot.UI.Common.Panels;
using Xunit;

namespace Hrot.Presentation.Tests.Behavior
{
    /// <summary>
    /// Unit tests for <see cref="MissionPanel"/> integration with
    /// <see cref="BehaviorUiRegistry"/> — TASK-C010.
    /// </summary>
    public sealed class MissionPanelRegistryTests
    {
        // ── C010 SC1: MissionPanel implements IPickInteractionContext ─────────

        /// <summary>
        /// C010 SC1: <see cref="MissionPanel"/> is declared as implementing
        /// <see cref="IPickInteractionContext"/>, verified via reflection so the
        /// test fails immediately if the interface is accidentally removed.
        /// </summary>
        [Fact]
        public void C010_MissionPanel_ImplementsIPickInteractionContext()
        {
            Assert.True(
                typeof(IPickInteractionContext).IsAssignableFrom(typeof(MissionPanel)),
                "MissionPanel must implement IPickInteractionContext");
        }

        // ── C010 SC2: Constructor accepts a pre-populated BehaviorUiRegistry ──

        /// <summary>
        /// C010 SC2: <see cref="MissionPanel"/> can be constructed with an
        /// externally-created <see cref="BehaviorUiRegistry"/> without throwing.
        /// </summary>
        [Fact]
        public void C010_MissionPanel_Constructor_WithRegistryArg_Succeeds()
        {
            var registry = new BehaviorUiRegistry();
            registry.Register<Fdp.Toolkit.Behavior.Params.FireAtTargetParamsJsonDto>("FireAtTarget");

            var panel = new MissionPanel(behaviorUiRegistry: registry);

            Assert.NotNull(panel);
        }

        // ── C010 SC3: MissionPanel is defined in Hrot.Presentation assembly ──

        /// <summary>
        /// C010 SC3: Confirms that the canonical <see cref="MissionPanel"/> type
        /// lives in the <c>Hrot.Presentation</c> assembly (not the inactive
        /// Hrot.UI.Common project copy).
        /// </summary>
        [Fact]
        public void C010_MissionPanel_IsInHrotPresentationAssembly()
        {
            Assert.Equal("Hrot.Presentation", typeof(MissionPanel).Assembly.GetName().Name);
        }
    }
}
