using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.World;
using Hrot.MuscleCharacter.Animation.Components;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4603</c> (B-6; 📄 docs/DESIGN_Vehicle_Breaching.md §7.7, D-6 R-4241) — lone trees felled: the <see cref="TreeDamage"/> record keyed by the trunk's
    /// centimetre position, the view's <see cref="FelledTrees"/> (sight and fire skip a felled trunk the same tick), a blast fells the trunks in its radius
    /// (<c>trees.json</c>), the contact rule fells one for a tank or an IFV row, and the save keeps them.
    /// </summary>
    public sealed class TreeFellingTests
    {
        private const long Grenade = 6105, Bomb = 6106;

        private const string Field = """
            {"type":"FeatureCollection","hrot":{"schemaVersion":1,"bounds":[0,0,200,200],"groundZ":0},"features":[
             {"type":"Feature","properties":{"kind":"tree","label":"oak"},"geometry":{"type":"Point","coordinates":[50,50]}},
             {"type":"Feature","properties":{"kind":"tree","label":"ash"},"geometry":{"type":"Point","coordinates":[80,50]}}]}
            """;

        private static TerrainWorld World() => TerrainWorldParser.Parse(Field, "field");
        private static int Trunk(TerrainWorld w, float x) => w.Prisms.ToList().FindIndex(p => p.Kind == TerrainPrismKind.Tree && MathF.Abs((p.Min.X + p.Max.X) * 0.5f - x) < 0.5f);

        private static EntityRepository Repo(TerrainWorld w)
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<TreeDamage>();
            repo.RegisterComponent<NetworkAuthority>();
            repo.SetSingletonManaged(w);
            return repo;
        }

        private static Entity Fell(EntityRepository repo, params (int X, int Y)[] keys)
        {
            var d = new TreeDamage();
            (d.CellX, d.CellY) = TreeDamages.CellOf(keys[0]);
            foreach (var k in keys) TreeDamages.Add(ref d, k, out _);
            var e = repo.CreateEntity(); repo.AddComponent(e, d);
            return e;
        }

        /// <summary>
        /// ⭐ A lone trunk blocks a line through it; once its record says felled, sight and fire pass it the SAME tick (no bake). 🔴 Red-proof: drop the
        /// <c>felled.Felled(prism)</c> skip in the segment loops ⇒ the line stays blocked.
        /// </summary>
        [Fact]
        public void AFelledTrunk_NoLongerBlocksSightOrFire_TheTickItsRecordIsWritten()
        {
            var w = World();
            using var repo = Repo(w);
            var from = new Vector3(40f, 50f, 1.5f); var to = new Vector3(60f, 50f, 1.5f);
            Assert.True(w.SegmentBlocked(from, to, DoorStates.Of(repo, w)));
            Fell(repo, TreeDamages.KeyOf(w.Prisms[Trunk(w, 50f)]));
            var doors = DoorStates.Of(repo, w);
            Assert.NotNull(doors.Trees);
            Assert.False(w.SegmentBlocked(from, to, doors));
            var fire = new List<TerrainWorld.FireCrossing>();
            w.QueryFire(from, to, fire, doors);
            Assert.Empty(fire);
            Assert.True(w.SegmentBlocked(new Vector3(70f, 50f, 1.5f), new Vector3(90f, 50f, 1.5f), doors));   // the ash still stands
        }

        /// <summary>
        /// ⭐ A Mk-82 3 m from the oak fells it (one <see cref="TreeFellEvent"/>), not the ash 27 m off; a grenade fells nothing. 🔴 Red-proof: drop the
        /// <c>FellTrees</c> call in <c>Assess</c> ⇒ no event.
        /// </summary>
        [Fact]
        public void ABomb_FellsTheLoneTrunksInItsRadius_AGrenadeFellsNone()
        {
            var w = World();
            using var repo = new EntityRepository();
            repo.RegisterComponent<SimTransform>(); repo.RegisterComponent<Health>(); repo.RegisterComponent<PhysicsCollider>(); repo.RegisterComponent<StanceIntent>();
            repo.RegisterComponent<DoorState>(); repo.RegisterComponent<BuildingDamage>(); repo.RegisterComponent<GroundDamage>(); repo.RegisterComponent<TreeDamage>();
            repo.RegisterManagedComponent<TerrainObjectKey>();
            repo.RegisterEvent<DetonationNotification>(); repo.RegisterEvent<DamageAssessedEvent>(); repo.RegisterEvent<DoorCommandEvent>();
            repo.RegisterEvent<BuildingDamageEvent>(); repo.RegisterEvent<CraterEvent>(); repo.RegisterEvent<TreeFellEvent>();
            var db = new TkbDatabase();
            db.Register(new TkbTemplate("M67 grenade", Grenade) { DisType = new DISEntityType { Kind = 2 } });
            db.Register(new TkbTemplate("Mk-82 bomb", Bomb) { DisType = new DISEntityType { Kind = 2 } });
            repo.SetSingletonManaged<ITkbDatabase>(db);
            repo.SetSingletonManaged(w);
            var area = new AreaEffectSystem();
            TreeFellEvent[] Burst(long ammo, Vector3 at)
            {
                repo.Bus.Publish(new DetonationNotification { Target = Entity.Null, HitX = at.X, HitY = at.Y, HitZ = at.Z, Ammo = ammo });
                repo.Bus.SwapBuffers(); area.Execute(repo, 0.016f); repo.Bus.SwapBuffers();
                return repo.Bus.Read<TreeFellEvent>().ToArray();
            }
            Assert.Empty(Burst(Grenade, new Vector3(51f, 50f, 0f)));
            var felled = Burst(Bomb, new Vector3(53f, 50f, 0.1f));
            var ev = Assert.Single(felled);
            Assert.Equal(TreeDamages.KeyOf(w.Prisms[Trunk(w, 50f)]), (ev.X, ev.Y));
        }

        /// <summary>⭐ D-6 — the contact rule names a trunk as a TREE (its key), and only the tank and IFV rows fell one; a wheeled vehicle qualifies for no row.</summary>
        [Fact]
        public void TheContactRule_NamesATrunk_AndOnlyTheTankAndIfvRowsFellIt()
        {
            var w = World();
            int pi = Trunk(w, 50f);
            var hit = new global::CarKinem.Controllers.BarrierHit(false, pi, new Vector2(49.7f, 50f), -Vector2.UnitX);
            Assert.True(global::CarKinem.Controllers.ContactBreach.Identify(w, in hit, 0f, out _, out int panel, out int obstacle, out _, out var tree));
            Assert.Equal(-1, panel); Assert.Equal(-1, obstacle);
            Assert.Equal(TreeDamages.KeyOf(w.Prisms[pi]), tree);
            var table = Fdp.Toolkit.Terrain.ContactBreachTable.Shared;
            Assert.True(table.For(tracked: true, 60000f)!.Trees);
            Assert.True(table.For(tracked: true, 25000f)!.Trees);
            Assert.Null(table.For(tracked: false, 12000f));
        }

        /// <summary>⭐ The owned felled trunks are saved in <c>TerrainObjects["#trees"]</c> and read back; two slices merge to the union; a malformed row throws.</summary>
        [Fact]
        public void TheFelledTrunks_SaveAndReload_AndTwoSlicesMerge()
        {
            var w = World();
            using var repo = Repo(w);
            var oak = TreeDamages.KeyOf(w.Prisms[Trunk(w, 50f)]);
            Fell(repo, oak);
            var section = TerrainObjectsSection.Write(repo)!;
            var dom = new JsonObject { [TerrainObjectsSection.Name] = JsonNode.Parse(section.ToJsonString()) };
            Assert.Equal(new[] { oak }, TerrainObjectsSection.ReadTrees(dom).ToArray());
            var merged = TerrainObjectsSection.MergeTrees((JsonObject)section[TerrainObjectsSection.TreesKey]!, new JsonObject { ["felled"] = new JsonArray(new JsonArray(8000, 5000)) });
            Assert.Equal(2, TerrainObjectsSection.ReadTrees(new JsonObject { [TerrainObjectsSection.Name] = new JsonObject { [TerrainObjectsSection.TreesKey] = merged } }).Count);
            var bad = new JsonObject { [TerrainObjectsSection.Name] = new JsonObject { [TerrainObjectsSection.TreesKey] = new JsonObject { ["felled"] = new JsonArray(new JsonArray(1)) } } };
            Assert.Throws<InvalidOperationException>(() => TerrainObjectsSection.ReadTrees(bad));
        }
    }
}
