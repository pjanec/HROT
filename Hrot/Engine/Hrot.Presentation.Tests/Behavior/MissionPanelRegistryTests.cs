using Fdp.Toolkit.Behavior.Params;
using Hrot.Core.Mission;
using Hrot.Presentation.Behavior;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Panels;
using System.Threading.Tasks;
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

        // ── TryConsume: entity pick ───────────────────────────────────────────

        /// <summary>
        /// TryConsumeEntityPick returns false and entityId=0 when no entity pick has been resolved.
        /// </summary>
        [Fact]
        public void TryConsumeEntityPick_NoResolvedPick_ReturnsFalse()
        {
            IPickInteractionContext ctx = new MissionPanel();

            bool result = ctx.TryConsumeEntityPick(0, "TargetNetworkId", out long entityId);

            Assert.False(result);
            Assert.Equal(0L, entityId);
        }

        /// <summary>
        /// TryConsumeEntityPick returns true and provides the entity ID after the panel
        /// has processed a completed pick task (via TestHook_PollPickCompletion), and clears
        /// the buffered state on the first consumption.
        /// </summary>
        [Fact]
        public async Task TryConsumeEntityPick_AfterPickCompletes_ReturnsTrueAndClearsState()
        {
            var panel = new MissionPanel();
            panel.SelectedEntityId = 1;
            panel.HandleAddTask();   // ensures _draftPlan has one task so HandlePickEntity proceeds

            var pick = new StubMapPickService();
            panel.HandlePickEntity(0, pick, filterPresets: null);

            pick.CompleteEntity(42);
            await Task.Yield();

            panel.TestHook_PollPickCompletion();

            IPickInteractionContext ctx = panel;
            // _pendingPickPropertyName is null (set by HandlePickEntity directly, not RequestEntityPick)
            bool result = ctx.TryConsumeEntityPick(0, null!, out long entityId);

            Assert.True(result);
            Assert.Equal(42L, entityId);

            // Second call must return false (state cleared after first consume).
            bool second = ctx.TryConsumeEntityPick(0, null!, out long entityId2);
            Assert.False(second);
            Assert.Equal(0L, entityId2);
        }

        // ── TryConsume: location pick ─────────────────────────────────────────

        /// <summary>
        /// TryConsumeLocationPick returns false when no location pick has been resolved.
        /// </summary>
        [Fact]
        public void TryConsumeLocationPick_NoResolvedPick_ReturnsFalse()
        {
            IPickInteractionContext ctx = new MissionPanel();

            bool result = ctx.TryConsumeLocationPick(0, "PickableLocation", out PickableGeoPoint loc);

            Assert.False(result);
            Assert.Equal(0.0, loc.Latitude);
            Assert.Equal(0.0, loc.Longitude);
        }

        /// <summary>
        /// TryConsumeLocationPick returns true and provides the coordinates after the panel
        /// has processed a completed location pick task, and clears state on consumption.
        /// </summary>
        [Fact]
        public async Task TryConsumeLocationPick_AfterPickCompletes_ReturnsTrueAndClearsState()
        {
            var panel = new MissionPanel();
            panel.SelectedEntityId = 1;
            panel.HandleAddTask();   // ensures _draftPlan has one task so HandlePickLocation proceeds

            var pick = new StubMapPickService();
            panel.HandlePickLocation(0, pick);

            var expectedLocation = new GeoPoint(52.5, 13.4);
            pick.CompleteLocation(expectedLocation);
            await Task.Yield();

            panel.TestHook_PollPickCompletion();

            IPickInteractionContext ctx = panel;
            bool result = ctx.TryConsumeLocationPick(0, null!, out PickableGeoPoint loc);

            Assert.True(result);
            Assert.Equal(expectedLocation.Latitude,  loc.Latitude,  precision: 6);
            Assert.Equal(expectedLocation.Longitude, loc.Longitude, precision: 6);

            bool second = ctx.TryConsumeLocationPick(0, null!, out PickableGeoPoint loc2);
            Assert.False(second);
        }

        // ── Stub helpers ──────────────────────────────────────────────────────

        private sealed class StubMapPickService : IMapPickService
        {
            private readonly TaskCompletionSource<int>      _entityTcs  = new();
            private readonly TaskCompletionSource<GeoPoint> _locationTcs = new();

            public void CompleteEntity(int id)        => _entityTcs.TrySetResult(id);
            public void CompleteLocation(GeoPoint pt)  => _locationTcs.TrySetResult(pt);

            public Task<GeoPoint> PickLocationAsync(System.Threading.CancellationToken ct = default)
                => _locationTcs.Task;
            public Task<int> PickEntityAsync(string[]? filterPresets = null, System.Threading.CancellationToken ct = default)
                => _entityTcs.Task;
            public Task<System.Collections.Generic.IReadOnlyList<int>> PickAreaEntitiesAsync(string[]? filterPresets = null, System.Threading.CancellationToken ct = default)
                => Task.FromResult<System.Collections.Generic.IReadOnlyList<int>>(System.Array.Empty<int>());
        }
    }
}
