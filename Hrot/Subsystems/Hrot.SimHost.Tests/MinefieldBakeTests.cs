using System;
using System.IO;
using System.Numerics;
using System.Threading;
using CarKinem.Road;
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Recast;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Services;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4624</c> MS-T3 (docs/designs/minefields/DESIGN_Minefields.md MR-3) — the static-obstacle bake carries MINEFIELDS: a field entity is gathered on the frame thread, joins the bake's signature and dirty
    /// regions (its box, grown 3 m), is stamped into the muscle's navmesh, and a side that knows it routes round — a lane record then opens it to the side that cleared it (a bake of the field's tiles), and the record
    /// gone (a seek back) closes it again. A display node (no navmesh) bakes none.
    /// </summary>
    public sealed class MinefieldBakeTests : IDisposable
    {
        private const int Blue = 1;
        private static readonly Guid FieldGuid = Guid.Parse("a7000000-0000-4000-8000-0000000000f1");
        private readonly EntityRepository _world = new();
        private readonly string _staging = Path.Combine(Path.GetTempPath(), "ms3-" + Guid.NewGuid().ToString("N"));
        private readonly SwitchableNavmeshProvider _node = new();
        private readonly TerrainWorld _flat;
        private double _wall;

        public MinefieldBakeTests()
        {
            SimHostComponentRegistry.RegisterAll(_world);
            Directory.CreateDirectory(_staging);
            int n = 61;   // a flat 60 × 60 m field, 1 m posts
            _flat = new TerrainWorld { Name = "range", BoundsMin = Vector2.Zero, BoundsMax = new Vector2(60f, 60f), Height = new TerrainHeightGrid(Vector2.Zero, 1f, n, n, new float[n * n]) };
        }

        public void Dispose() { _world.Dispose(); try { Directory.Delete(_staging, recursive: true); } catch { } }

        private StaticObstacleBakeSystem Sys() => new(() => _wall);

        private void Node(bool muscle)
        {
            var residency = new TerrainResidency(_staging, new RoadNetworkHolder());
            INavmeshProvider? baseline = null;
            if (muscle)
            {
                var factory = new RecastNavmeshFactory { Layers = NavLayerMask.Infantry | NavLayerMask.Vehicle, TileCache = new NavTileCache() };
                baseline = factory.Build(_flat);
                residency.AttachNavmesh(factory, _node);
            }
            residency.Commit(_world, new TerrainResidency.Staged { Definition = new TerrainDefinition { Name = "range" }, World = _flat, TerrainName = "range", Navmesh = baseline });
        }

        private void Bake(StaticObstacleBakeSystem sys, int bakes)
        {
            sys.Execute(_world, 0.016f);
            _wall += 0.6;
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (sys.Bakes < bakes && DateTime.UtcNow < deadline)
            {
                sys.Execute(_world, 0.016f);
                if (sys.Bakes < bakes) Thread.Sleep(5);
            }
            Assert.Equal(bakes, sys.Bakes);
        }

        private TerrainWorld Resident => _world.GetSingletonManaged<TerrainWorld>()!;

        /// <summary>A baseline bake (a small crater far from the field) so the next bake is DIFFED against it.</summary>
        private void Baseline(StaticObstacleBakeSystem sys)
        {
            var d = new GroundDamage();
            var c = new Crater(new Vector2(5f, 5f), 1f, 0.4f, 0.1f, (SimInstant)0.5, CraterKind.Small);
            (d.CellX, d.CellY) = GroundDamages.CellOf(c.Centre);
            GroundDamages.Add(ref d, in c, out _);
            _world.AddComponent(_world.CreateEntity(), d);
            Bake(sys, 1);
        }

        /// <summary>A field x 15..45 × y 0..50 (the north 10 m is open), known at start to Blue.</summary>
        private Entity Field()
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            _world.AddComponent(e, new Minefield { Type = MineType.AntiPersonnel, SpacingM = 4f, KnownAtStartSides = (ushort)(1 << Blue) });
            _world.AddComponent(e, new EntityGuid(FieldGuid));
            _world.SetManagedComponent(e, new EditablePolyline { Points = new[] { new Vector2(15, 0), new Vector2(45, 0), new Vector2(45, 50), new Vector2(15, 50) } });
            return e;
        }

        private float Cost(int side) => _node.PathCost(new Vector3(5, 30, 0), new Vector3(55, 30, 0), (uint)NavLayerMask.Infantry | NavLayerSelection.SideBits(side), DoorStates.Of(_world, Resident));

        /// <summary>
        /// ⭐⭐ MR-3 — a field is gathered, baked and priced: the diffed bake's dirty region is the field's box (± its clearance ring + 3 m, one rectangle — CE-4655); the resident world holds it; a side that knows it goes round, one that does not crosses.
        /// Then a runtime LANE record (cleared by Blue) re-bakes the field's tiles — Blue crosses at the lane — and the record gone (a seek back) re-bakes again: Blue goes round.
        /// 🔴 Red-proof: leave <c>MinefieldSet.SignatureOf</c> out of the bake's signature ⇒ no bake after the field is added (a timeout); drop <c>MinefieldDirt</c> ⇒ no dirty rectangle.
        /// </summary>
        [Fact]
        public void AField_IsBakedIntoTheMusclesNavmesh_ALaneRecordRebakesIt_AndASeekBackRestoresTheDetour()
        {
            Node(muscle: true);
            var sys = Sys();
            Baseline(sys);

            var field = Field();
            Bake(sys, 2);
            Assert.Single(Resident.Minefields);
            var dirty = Assert.Single(sys.LastDirty!);
            Assert.Equal(new Vector2(7f, -8f), dirty.Min);    // ⭐ CE-4655 — the field's box ± (its clearance ring 5 m + 3 m)
            Assert.Equal(new Vector2(53f, 58f), dirty.Max);
            float red = Cost(2), blue = Cost(Blue);
            Assert.True(red < 56f, $"a side that does not know the field crosses it ({red:F0} m)");
            Assert.True(blue > 70f, $"a side that knows it goes round its north end ({blue:F0} m)");

            var lanes = new MinefieldLanes { Field = FieldGuid };
            lanes.TryAdd(new LaneItem(FieldGuid, new Vector2(10, 30), new Vector2(50, 30), 6f, (byte)(Blue + 1)));
            var laneEntity = _world.CreateEntity(); _world.AddComponent(laneEntity, lanes);
            Bake(sys, 3);
            Assert.Single(Resident.Minefields[0].Lanes);
            Assert.Single(sys.LastDirty!);
            Assert.True(Cost(Blue) < 56f, $"the side that cleared the lane crosses at it ({Cost(Blue):F0} m)");
            Assert.True(Cost(2) < 56f);

            _world.DestroyEntity(laneEntity);   // a seek back to before the lane
            Bake(sys, 4);
            Assert.Empty(Resident.Minefields[0].Lanes);
            Assert.True(Cost(Blue) > 70f, $"the lane is gone: round again ({Cost(Blue):F0} m)");

            // a steady frame bakes nothing (the signature is stable)
            for (int i = 0; i < 20; i++) sys.Execute(_world, 0.016f);
            Assert.Equal(4, sys.Bakes);
            Assert.False(sys.Baking);
        }

        /// <summary>⭐ a DISPLAY node (no navmesh factory) bakes no minefield — its terrain is never queried (the building damage's rule): the field changes nothing there.</summary>
        [Fact]
        public void ADisplayNode_BakesNoMinefield()
        {
            Node(muscle: false);
            var sys = Sys();
            Baseline(sys);
            Field();
            for (int i = 0; i < 30; i++) { sys.Execute(_world, 0.016f); _wall += 0.6; Thread.Sleep(2); }
            Assert.Equal(1, sys.Bakes);
            Assert.Empty(Resident.Minefields);
        }
    }
}
