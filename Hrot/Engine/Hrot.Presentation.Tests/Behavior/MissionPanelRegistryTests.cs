using Fdp.Toolkit.Behavior.Params;
using Hrot.Map.Definitions.Behavior;
using Hrot.Core.Mission;
using Hrot.Presentation.Behavior;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Panels;
using System.Threading.Tasks;
using Xunit;

namespace Hrot.Presentation.Tests.Behavior
{
    /// <summary>
    /// Unit tests for <see cref="MissionPanel"/>'s behaviour params — TASK-C010; since <c>CE-1023</c> the panel draws THE one
    /// params form (<see cref="BehaviorParamsForm"/>) from a params-TYPE lookup, not a UI registry.
    /// </summary>
    public sealed class MissionPanelRegistryTests
    {
        // ── C010 SC1 (superseded by DESIGN_Map_Picking_Unification P4) ────────────────

        /// <summary>
        /// ⭐⭐ <c>P4</c> — the mission panel draws its behaviour parameters against THE shared pick broker, not a pick
        /// state machine of its own. 🔴 It used to implement <c>IPickInteractionContext</c> with a private copy of the
        /// broker's logic (task+property keys, <c>long</c> results); both were deleted.
        /// </summary>
        [Fact]
        public void P4_MissionPanel_PicksThroughTheSharedBroker()
        {
            Assert.IsType<Hrot.Presentation.Facades.MapPickBroker>(new MissionPanel().Picks);
        }

        // ── CE-1023: the params form per task, from the host's type lookup ──

        /// <summary>
        /// ⭐ <c>CE-1023</c> — a task whose behaviour has a params type gets THE form (bound to its JSON); one with no known
        /// type gets none (the raw JSON box). The lookup is the host's — a passed one wins over the declared contracts.
        /// </summary>
        [Fact]
        public void CE1023_ATaskGetsTheOneForm_FromTheHostsLookup_AndAnUnknownBehaviourGetsNone()
        {
            var panel = new MissionPanel(paramsTypeOf: name => name == "Mine" ? typeof(FireAtTargetParamsJsonDto) : null);
            var id = System.Guid.NewGuid();

            var form = panel.ParamsFormFor(id, "Mine", "{\"targetNetworkId\":42}");
            Assert.NotNull(form);
            Assert.Equal(typeof(FireAtTargetParamsJsonDto), form!.Type);
            Assert.Same(form, panel.ParamsFormFor(id, "Mine", "{\"targetNetworkId\":42}"));   // kept across frames
            Assert.Null(panel.ParamsFormFor(id, "NoSuchBehaviour", "{}"));

            // ⭐ the default lookup is the declared contracts (what a host with no registry — ExCon — can know)
            Assert.NotNull(new MissionPanel().ParamsFormFor(id, FireAtTargetParamsJsonDto.BehaviorId, "{}"));
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

        // ── TryConsume through the panel's broker, keyed $.tasks[i].Prop ──────────────

        private static readonly string EntityPath   = MissionPanel.PickScope(0) + ".TargetNetworkId";
        private static readonly string LocationPath = MissionPanel.PickScope(0) + ".PickableLocation";

        [Fact]
        public void TryConsumeEntityPick_NoResolvedPick_ReturnsFalse()
        {
            bool result = new MissionPanel().Picks.TryConsumeEntityPick(EntityPath, out var picked);
            Assert.False(result);
            Assert.True(picked.IsNone);
        }

        /// <summary>A completed entity pick is consumed ONCE, as an <c>EntityRef</c>, for the path it was asked for.</summary>
        [Fact]
        public async Task TryConsumeEntityPick_AfterPickCompletes_ReturnsTrueAndClearsState()
        {
            var panel = new MissionPanel();
            var pick  = new StubMapPickService();
            panel.TestHook_SetFramePickService(pick);

            panel.Picks.RequestEntityPick(EntityPath, filterPresets: null);
            Assert.True(panel.IsEntityPickPending);
            pick.CompleteEntity(42);
            await Task.Yield();

            Assert.False(panel.Picks.TryConsumeEntityPick(LocationPath, out _));   // another field's path: not its pick
            Assert.True(panel.Picks.TryConsumeEntityPick(EntityPath, out var picked));
            Assert.Equal(new Fdp.Toolkit.Replication.EntityRef(42), picked);
            Assert.False(panel.Picks.TryConsumeEntityPick(EntityPath, out _));     // consumed once
        }

        [Fact]
        public void TryConsumeLocationPick_NoResolvedPick_ReturnsFalse()
        {
            bool result = new MissionPanel().Picks.TryConsumeLocationPick(LocationPath, out PickableGeoPoint loc);
            Assert.False(result);
            Assert.Equal(0.0, loc.Latitude);
            Assert.Equal(0.0, loc.Longitude);
        }

        [Fact]
        public async Task TryConsumeLocationPick_AfterPickCompletes_ReturnsTrueAndClearsState()
        {
            var panel = new MissionPanel();
            var pick  = new StubMapPickService();
            panel.TestHook_SetFramePickService(pick);

            panel.Picks.RequestLocationPick(LocationPath);
            var expectedLocation = new GeoPoint(52.5, 13.4);
            pick.CompleteLocation(expectedLocation);
            await Task.Yield();

            Assert.True(panel.Picks.TryConsumeLocationPick(LocationPath, out PickableGeoPoint loc));
            Assert.Equal(expectedLocation.Latitude,  loc.Latitude,  precision: 6);
            Assert.Equal(expectedLocation.Longitude, loc.Longitude, precision: 6);
            Assert.False(panel.Picks.TryConsumeLocationPick(LocationPath, out _));
        }

        /// <summary>No pick service this frame (a headless host) ⇒ a request is ignored, nothing is pending.</summary>
        [Fact]
        public void ARequestWithNoFrameService_IsIgnored()
        {
            var panel = new MissionPanel();
            panel.Picks.RequestEntityPick(EntityPath, null);
            Assert.False(panel.IsEntityPickPending);
            Assert.False(panel.Picks.IsPickPendingFor(EntityPath));
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

        /// <summary>⭐ <c>CE-1059</c> — a task's <c>EntityRef</c> param can be chosen from the searchable entity LIST as well as on the
        /// map: a host that passes <see cref="MissionPanel.EntityList"/> gives the panel's broker the List… capability, and the choice
        /// lands at the task's own scoped path, like a map pick. Without it the panel stays map-only.</summary>
        [Fact]
        public void CE1059_TheEntityList_ReachesTheTasksParamsForm_AtTheTasksScope()
        {
            Assert.False(new MissionPanel().Picks.CanListEntities);

            string[]? asked = null;
            var panel = new MissionPanel { EntityList = (presets, chosen) => { asked = presets; chosen(42); return true; } };
            var task0 = new Fdp.Presentation.Editing.ScopedMapPickContext(panel.Picks, MissionPanel.PickScope(0));
            Assert.True(task0.CanListEntities);

            task0.RequestEntityListPick("$.TargetNetworkId", new[] { "units_ground" });
            Assert.Equal(new[] { "units_ground" }, asked);
            Assert.False(new Fdp.Presentation.Editing.ScopedMapPickContext(panel.Picks, MissionPanel.PickScope(1))
                .TryConsumeEntityPick("$.TargetNetworkId", out _));                         // another task's field: not its pick
            Assert.True(task0.TryConsumeEntityPick("$.TargetNetworkId", out var picked));
            Assert.Equal(42L, picked.NetworkId);
        }
    }
}
