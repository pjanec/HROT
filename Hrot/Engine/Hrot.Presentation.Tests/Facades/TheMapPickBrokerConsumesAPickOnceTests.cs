using System.Threading.Tasks;
using Hrot.Core.Mission;
using Hrot.Presentation.Facades;
using Hrot.UI.Common.Facades;
using Xunit;

namespace Hrot.Presentation.Tests.Facades
{
    /// <summary>
    /// ⭐ P-7 7d (CE-4303) — the pick claims <c>MissionPanelRegistryTests</c> pinned through the retired mission panel's <c>Picks</c>, re-homed onto the broker itself: it is the
    /// one <c>IMapPickContext</c> every host hands the Orders tab (<c>host.Pick</c>) and every other params form. A completed entity pick is consumed ONCE, as an
    /// <c>EntityRef</c>, for the path it was asked for; a location pick likewise; a request with no pick service this frame is ignored.
    /// 🔴 Red-proof (compiles): <c>TryConsumeEntityPick</c> not clearing its state ⇒ the "consumed once" row ⇒ red.
    /// </summary>
    public sealed class TheMapPickBrokerConsumesAPickOnceTests
    {
        private const string EntityPath   = "$.tasks[0].TargetNetworkId";
        private const string LocationPath = "$.tasks[0].Location";

        private sealed class StubMapPickService : IMapPickService
        {
            private readonly TaskCompletionSource<int>      _entityTcs   = new();
            private readonly TaskCompletionSource<GeoPoint> _locationTcs = new();

            public void CompleteEntity(int id)        => _entityTcs.TrySetResult(id);
            public void CompleteLocation(GeoPoint pt) => _locationTcs.TrySetResult(pt);

            public Task<GeoPoint> PickLocationAsync(System.Threading.CancellationToken ct = default) => _locationTcs.Task;
            public Task<int> PickEntityAsync(string[]? filterPresets = null, System.Threading.CancellationToken ct = default) => _entityTcs.Task;
            public Task<System.Collections.Generic.IReadOnlyList<int>> PickAreaEntitiesAsync(string[]? filterPresets = null, System.Threading.CancellationToken ct = default)
                => Task.FromResult<System.Collections.Generic.IReadOnlyList<int>>(System.Array.Empty<int>());

            public Fdp.Core.WorldPoint? Selection;   // ⭐ N-5
            public bool TryGetSelectionPosition(out Fdp.Core.WorldPoint position) { position = Selection ?? default; return Selection.HasValue; }
        }

        [Fact]
        public void TryConsumeEntityPick_NoResolvedPick_ReturnsFalse()
        {
            bool result = new MapPickBroker(() => null).TryConsumeEntityPick(EntityPath, out var picked);
            Assert.False(result);
            Assert.True(picked.IsNone);
        }

        /// <summary>A completed entity pick is consumed ONCE, as an <c>EntityRef</c>, for the path it was asked for.</summary>
        [Fact]
        public async Task TryConsumeEntityPick_AfterPickCompletes_ReturnsTrueAndClearsState()
        {
            var pick   = new StubMapPickService();
            var broker = new MapPickBroker(() => pick);

            broker.RequestEntityPick(EntityPath, filterPresets: null);
            Assert.True(broker.IsEntityPickPending);
            pick.CompleteEntity(42);
            await Task.Yield();

            Assert.False(broker.TryConsumeEntityPick(LocationPath, out _));   // another field's path: not its pick
            Assert.True(broker.TryConsumeEntityPick(EntityPath, out var picked));
            Assert.Equal(new Fdp.Toolkit.Replication.EntityRef(42), picked);
            Assert.False(broker.TryConsumeEntityPick(EntityPath, out _));     // consumed once
        }

        [Fact]
        public void TryConsumeLocationPick_NoResolvedPick_ReturnsFalse()
        {
            bool result = new MapPickBroker(() => null).TryConsumeLocationPick(LocationPath, out Fdp.Toolkit.Replication.PointLink loc);
            Assert.False(result);
            Assert.Equal(0f, loc.Local.Y);
            Assert.Equal(0f, loc.Local.X);
        }

        [Fact]
        public async Task TryConsumeLocationPick_AfterPickCompletes_ReturnsTrueAndClearsState()
        {
            var pick   = new StubMapPickService();
            var broker = new MapPickBroker(() => pick);

            broker.RequestLocationPick(LocationPath);
            var expectedLocation = new GeoPoint(52.5, 13.4);
            pick.CompleteLocation(expectedLocation);
            await Task.Yield();

            Assert.True(broker.TryConsumeLocationPick(LocationPath, out Fdp.Toolkit.Replication.PointLink loc));
            // the stub service has no frame: the default local conversion carries (lon, lat) as (x, y)
            Assert.Equal(expectedLocation.Latitude,  loc.Local.Y, precision: 3);
            Assert.Equal(expectedLocation.Longitude, loc.Local.X, precision: 3);
            Assert.False(broker.TryConsumeLocationPick(LocationPath, out _));
        }

        /// <summary>No pick service this frame (a headless host) ⇒ a request is ignored, nothing is pending.</summary>
        [Fact]
        public void ARequestWithNoFrameService_IsIgnored()
        {
            var broker = new MapPickBroker(() => null);
            broker.RequestEntityPick(EntityPath, null);
            Assert.False(broker.IsEntityPickPending);
            Assert.False(broker.IsPickPendingFor(EntityPath));
        }

        /// <summary>⭐ N-5 (DESIGN_Struct_Editor §5) — the entity picker's distance column measures from the host's selection: the broker AND the form scope forward it
        /// (a wrapper that dropped it would silently show no distances — the silent-default pattern). Red-proof: the scope's member removed ⇒ the default answers false.</summary>
        [Fact]
        public void TheSelectionPosition_IsForwardedByTheBroker_AndByAFormsScope()
        {
            var pick = new StubMapPickService();
            var broker = new MapPickBroker(() => pick);
            Assert.False(broker.TryGetSelectionPosition(out _));
            pick.Selection = new Fdp.Core.WorldPoint(10, 20, 0);
            Assert.True(broker.TryGetSelectionPosition(out var p));
            Assert.Equal(new Fdp.Core.WorldPoint(10, 20, 0), p);
            Fdp.Presentation.Editing.IMapPickContext scoped = new Fdp.Presentation.Editing.ScopedMapPickContext(broker, "$.tasks[2]");
            Assert.True(scoped.TryGetSelectionPosition(out var q));
            Assert.Equal(p, q);
            Assert.False(new MapPickBroker(() => null).TryGetSelectionPosition(out _));
        }
    }
}
