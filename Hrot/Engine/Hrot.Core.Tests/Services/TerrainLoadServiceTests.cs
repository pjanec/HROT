using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Terrain;
using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.Map.Common.Services;
using Xunit;

namespace Hrot.Core.Tests.Services
{
    /// <summary>
    /// C1 — <see cref="TerrainLoadService"/>: idempotent, hash-checked, and provable WITHOUT a cluster.
    ///
    /// <para>⭐ "Provable without a cluster" is the success condition, not a convenience: the whole point
    /// of one shared implementation is that the local scenario-load path and the 2PC round take the same
    /// branch, so the branch can be tested on a bare repository.</para>
    ///
    /// ⚠ Deliberately does NOT call <c>ComponentTypeRegistry.Clear()</c> — batch ① reddened three
    /// untouched classes that way. A fresh <see cref="EntityRepository"/> per test is enough.
    ///
    /// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §3, §9.7.
    /// </summary>
    public sealed class TerrainLoadServiceTests
    {
        /// <summary>Records what it was asked to build, so a "did it rebuild?" claim is measured.</summary>
        private sealed class RecordingTileLoader : IZoneTileLoader
        {
            public readonly List<ulong> Built = new();
            public bool Result = true;

            public bool Build(Vector2 min, Vector2 max, ulong footprintHash)
            {
                Built.Add(footprintHash);
                return Result;
            }
        }

        private static EntityRepository NewRepo()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<TkbIdentity>();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<TerrainAssetLoadState>();
            repo.RegisterManagedComponent<EditablePolyline>();
            return repo;
        }

        private static List<Vector2> Triangle() => new()
        {
            new Vector2(-53f, -88.5f), new Vector2(47f, -88.5f), new Vector2(47f, 11.5f),
        };

        private static Entity NewZone(EntityRepository repo, Vector3 origin, List<Vector2> points,
                                      long tkbType = TkbEntityTypes.TerrainZone)
        {
            var e = repo.CreateEntity();
            repo.AddComponent(e, new TkbIdentity { TkbType = tkbType });
            repo.AddComponent(e, new SimTransform { Position = origin });
            repo.SetManagedComponent(e, new EditablePolyline { Points = points });
            return e;
        }

        // ── Idempotency ──────────────────────────────────────────────────────────────────────

        [Fact]
        public void CallingTwiceWithNoEdit_DoesNothingTheSecondTime()
        {
            using var repo = NewRepo();
            var loader = new RecordingTileLoader();
            var svc = new TerrainLoadService(loader);
            var zone = NewZone(repo, new Vector3(670f, 473.5f, 0f), Triangle());

            Assert.True(svc.EnsureLoaded(repo, zone));    // first call does work
            Assert.False(svc.EnsureLoaded(repo, zone));   // second is a no-op

            Assert.Single(loader.Built);
        }

        [Fact]
        public void AfterLoading_TheMarkerIsLoadedAndCarriesTheCurrentFootprint()
        {
            using var repo = NewRepo();
            var svc = new TerrainLoadService(new RecordingTileLoader());
            var origin = new Vector3(670f, 473.5f, 0f);
            var points = Triangle();
            var zone = NewZone(repo, origin, points);

            svc.EnsureLoaded(repo, zone);

            var marker = repo.GetComponent<TerrainAssetLoadState>(zone);
            Assert.Equal(LoadPhase.Loaded, marker.Phase);
            Assert.Equal(ZoneFootprint.Compute(origin, points), marker.SourceHash);
        }

        // ── Staleness — the two edits that must both rebuild ──────────────────────────────────

        [Fact]
        public void AfterAMove_ItRebuilds()
        {
            // ⭐ THE CASE A COUNTER CANNOT SEE: points are relative, so a move leaves them byte-identical.
            using var repo = NewRepo();
            var loader = new RecordingTileLoader();
            var svc = new TerrainLoadService(loader);
            var zone = NewZone(repo, new Vector3(670f, 473.5f, 0f), Triangle());

            svc.EnsureLoaded(repo, zone);
            repo.SetComponent(zone, new SimTransform { Position = new Vector3(770f, 473.5f, 0f) });

            Assert.True(svc.EnsureLoaded(repo, zone));
            Assert.Equal(2, loader.Built.Count);
            Assert.NotEqual(loader.Built[0], loader.Built[1]);
        }

        [Fact]
        public void AfterAReshape_ItRebuilds()
        {
            using var repo = NewRepo();
            var loader = new RecordingTileLoader();
            var svc = new TerrainLoadService(loader);
            var zone = NewZone(repo, new Vector3(670f, 473.5f, 0f), Triangle());

            svc.EnsureLoaded(repo, zone);

            var reshaped = Triangle();
            reshaped[2] = new Vector2(47f, 40f);
            repo.SetManagedComponent(zone, new EditablePolyline { Points = reshaped });

            Assert.True(svc.EnsureLoaded(repo, zone));
            Assert.Equal(2, loader.Built.Count);
        }

        // ── Failure is a state, not an exception swallowed ────────────────────────────────────

        [Fact]
        public void AFailedBuild_StampsFailed_AndIsRetriedNextCall()
        {
            using var repo = NewRepo();
            var loader = new RecordingTileLoader { Result = false };
            var svc = new TerrainLoadService(loader);
            var zone = NewZone(repo, new Vector3(1f, 2f, 0f), Triangle());

            svc.EnsureLoaded(repo, zone);
            Assert.Equal(LoadPhase.Failed, repo.GetComponent<TerrainAssetLoadState>(zone).Phase);

            // ⭐ A failed zone is NOT cached as done — the operator's retry must actually retry.
            Assert.True(svc.EnsureLoaded(repo, zone));
            Assert.Equal(2, loader.Built.Count);
        }

        // ── EnsureAllLoaded ───────────────────────────────────────────────────────────────────

        [Fact]
        public void EnsureAllLoaded_LoadsEveryZoneOnce_AndIsANoOpOnTheSecondPass()
        {
            using var repo = NewRepo();
            var loader = new RecordingTileLoader();
            var svc = new TerrainLoadService(loader);

            NewZone(repo, new Vector3(0f, 0f, 0f), Triangle());
            NewZone(repo, new Vector3(500f, 0f, 0f), Triangle());
            NewZone(repo, new Vector3(0f, 500f, 0f), Triangle());

            Assert.Equal(3, svc.EnsureAllLoaded(repo));
            Assert.Equal(0, svc.EnsureAllLoaded(repo));   // idempotent across the whole world
            Assert.Equal(3, loader.Built.Count);
        }

        [Fact]
        public void EnsureAllLoaded_IgnoresEntitiesThatAreNotZones()
        {
            // ⛔ TkbType is THE discriminator: an AREA (8803) that happens to have a polyline is not a
            //    zone, and loading terrain for it would be wrong.
            using var repo = NewRepo();
            var loader = new RecordingTileLoader();
            var svc = new TerrainLoadService(loader);

            NewZone(repo, Vector3.Zero, Triangle(), tkbType: TkbEntityTypes.TacGraphic_Area);
            NewZone(repo, Vector3.Zero, Triangle(), tkbType: TkbEntityTypes.TacGraphic_Route);
            NewZone(repo, new Vector3(10f, 0f, 0f), Triangle());   // the only real zone

            Assert.Equal(1, svc.EnsureAllLoaded(repo));
            Assert.Single(loader.Built);
        }
    }
}
