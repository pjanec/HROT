using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.MuscleCharacter.Animation.Components;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4591</c> (P-4; 📄 docs/DESIGN_Ground_Patches_And_Road_Blockage.md G-7, D-1, V-7, V-10) — the crater record and the cratered ground: the rule
    /// (<c>craters.json</c>), the profile, the view's table (content-sorted, commutative), the SAME-TICK truth through the world query and the terrain's ground
    /// trace (no bake), and the burst → <see cref="CraterEvent"/> → <see cref="GroundDamageSystem"/> → record chain.
    /// </summary>
    public sealed class CraterTests
    {
        private const long Grenade = 6105, Bomb = 6106, Ball = 6104;

        /// <summary>A flat 400 × 400 m height grid at z 0 (a grid, so the ground trace is the grid's walk).</summary>
        private static TerrainWorld Flat()
        {
            int n = 201;
            return new TerrainWorld { Name = "flat", Height = new TerrainHeightGrid(new Vector2(-200f, -200f), 2f, n, n, new float[n * n]) };
        }

        private static EntityRepository World(TerrainWorld terrain)
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<GroundDamage>();
            repo.RegisterComponent<BuildingDamage>();
            repo.RegisterManagedComponent<TerrainObjectKey>();
            repo.SetSingletonManaged(terrain);
            return repo;
        }

        private static Entity Record(EntityRepository repo, params Crater[] craters)
        {
            var d = new GroundDamage();
            if (craters.Length > 0) (d.CellX, d.CellY) = GroundDamages.CellOf(craters[0].Centre);
            foreach (var c in craters) Assert.True(GroundDamages.Add(ref d, in c, out _));
            var e = repo.CreateEntity();
            repo.AddComponent(e, d);
            return e;
        }

        private static Crater Big(float x = 0f, float y = 0f) => new(new Vector2(x, y), 3.6f, 1.6f, 0.36f, (SimInstant)10.0, CraterKind.Large);

        // ── the rule ────────────────────────────────────────────────────────────────────────

        /// <summary>⭐ The cube-root sizes, the threshold, the air burst, the house's floor.</summary>
        [Fact]
        public void TheRule_SizesByTheCubeRootOfTheCharge_AndMakesNoneBelowTheThreshold_InTheAir_OrOnAFloor()
        {
            var t = CraterRuleTable.Shared;
            var flat = Flat();
            Assert.True(CraterRule.TryDecide(t, flat, new Vector3(10f, 10f, 0.1f), 89f, (SimInstant)5.0, out var bomb));     // a Mk-82's 89 kg
            Assert.Equal(0.8f * MathF.Cbrt(89f), bomb.Radius, 3);                                                // ≈ 3.57 m
            Assert.Equal(0.35f * MathF.Cbrt(89f), bomb.Depth, 3);                                                // ≈ 1.56 m
            Assert.Equal(CraterKind.Large, bomb.Kind);
            Assert.Equal(5.0, (double)bomb.BornAt);
            Assert.True(CraterRule.TryDecide(t, flat, new Vector3(10f, 10f, 0f), 7f, default, out var shell));
            Assert.Equal(CraterKind.Small, shell.Kind);                                                          // a 155 mm shell: ≈ 1.53 m
            Assert.False(CraterRule.TryDecide(t, flat, new Vector3(10f, 10f, 0f), 0.18f, default, out _));             // a grenade: below the threshold
            Assert.False(CraterRule.TryDecide(t, flat, new Vector3(10f, 10f, 5f), 89f, default, out _));               // an air burst 5 m up
            var house = BreachFixtures.House();
            Assert.False(CraterRule.TryDecide(t, house, new Vector3(25f, 24f, 0.1f), 89f, default, out _));            // inside the house: a floor
            Assert.True(CraterRule.TryDecide(t, house, new Vector3(25f, 14f, 0.1f), 89f, default, out _));             // 6 m south of it: open ground
        }

        // ── the profile and the table ───────────────────────────────────────────────────────

        /// <summary>⭐ The bowl, the rim, the untouched ground beyond; overlapping craters take the minimum, whatever order their records arrive in.</summary>
        [Fact]
        public void TheProfile_BowlRimAndBeyond_AndOverlappingCratersCombineByTheMinimum_InAnyOrder()
        {
            var c = Big();
            Assert.True(c.TryOffset(0f, 0f, out float centre)); Assert.Equal(-1.6f, centre, 4);
            Assert.True(c.TryOffset(3.6f - 1e-4f, 0f, out float edge)); Assert.Equal(0.36f, edge, 2);
            Assert.False(c.TryOffset(3.6f * Crater.RimOuterFactor + 0.01f, 0f, out _));

            var a = Big(0f, 0f); var b = Big(3f, 0f) with { Depth = 1.0f };
            var w1 = World(Flat()); Record(w1, a); Record(w1, b);
            var w2 = World(Flat()); Record(w2, b); Record(w2, a);
            var t1 = CraterTable.Of(w1)!; var t2 = CraterTable.Of(w2)!;
            for (float x = -6f; x <= 9f; x += 0.37f)
                Assert.Equal(t1.Offset(x, 0.3f), t2.Offset(x, 0.3f));                                          // commutative: the same bits
            a.TryOffset(1.5f, 0f, out float oa); b.TryOffset(1.5f, 0f, out float ob);
            Assert.Equal(MathF.Min(oa, ob), t1.Offset(1.5f, 0f));
            Assert.Equal(0f, t1.Offset(50f, 50f));
        }

        // ── the same-tick truth (D-1) ───────────────────────────────────────────────────────

        /// <summary>
        /// ⭐⭐ D-1 — the ground height a mover reads comes from the record in the SAME tick: no bake. 🔴 Red-proof: <c>TerrainWorldQuery.GroundHeightAt</c>
        /// back to <c>World.GroundHeightAt(x, y)</c> ⇒ the centre reads 0.
        /// </summary>
        [Fact]
        public void TheGroundHeight_IsTheCrateredGround_TheTickTheRecordIsWritten()
        {
            var terrain = Flat();
            var repo = World(terrain);
            var q0 = TerrainWorldQuery.ForView(repo, terrain);
            Assert.Equal(0f, q0.GroundHeightAt(0f, 0f));
            Record(repo, Big());
            var q = TerrainWorldQuery.ForView(repo, terrain);
            Assert.Equal(-1.6f, q.GroundHeightAt(0f, 0f), 4);
            Assert.Equal(-1.6f, q.SurfaceZ(0f, 0f, 0f), 4);
            Assert.Equal(-1.6f, q.HullSurfaceZ(0f, 0f, 0f), 4);
            Assert.Equal(-1.6f, q.ResolveLevel(0f, 0f, 0), 4);
            Assert.Equal(0f, q.GroundHeightAt(40f, 40f));
        }

        /// <summary>
        /// ⭐⭐ D-1 — a man lying in a crater is hidden by its rim from an observer on the level 100 m off, and seen by one standing on its rim; fire meets
        /// the ground on the same line. Without the crater the same man would be "under the ground" (malformed, ignored). 🔴 Red-proof: the terrain's
        /// ground trace back to the bare grid (<c>Height.Crosses</c>) ⇒ the far line is clear.
        /// </summary>
        [Fact]
        public void AManInTheCrater_IsHiddenByItsRim_FromTheLevel_AndSeenFromTheRim_SightAndFireAlike()
        {
            var terrain = Flat();
            var repo = World(terrain);
            Record(repo, Big());
            var doors = DoorStates.Of(repo, terrain);
            var man = new Vector3(0f, 0f, -1.6f + 0.3f);                 // prone, at the bottom
            var far = new Vector3(100f, 0f, 1.8f);                      // standing on the level 100 m east
            var rim = new Vector3(3.8f, 0f, 0.3f + 1.8f);               // standing on the rim
            Assert.True(terrain.SegmentBlocked(far, man, doors));
            Assert.True(terrain.SegmentBlocked(man, far, doors));
            Assert.False(terrain.SegmentBlocked(rim, man, doors));
            Assert.True(terrain.QuerySight(far, man, doors).Transmittance < TerrainWorld.SightThreshold);
            var fire = new List<TerrainWorld.FireCrossing>();
            terrain.QueryFire(far, man, fire, doors);
            Assert.Contains(fire, f => f.Kind == TerrainWorld.GroundKind);
            Assert.False(terrain.SegmentBlocked(far, man));              // no view: the bare grid, the man is under it (ignored)
            Assert.False(terrain.SegmentBlocked(far, new Vector3(-100f, 0f, 1.8f), doors));   // a line well above the crater: clear
        }

        /// <summary>⭐ R-2084 / R-220 — a steady frame (the records unchanged): the view's table and a ground read allocate 0 B, and hand back the same table.</summary>
        [Fact]
        public void ASteadyFrame_AllocatesNothing_AndKeepsTheTable()
        {
            var terrain = Flat();
            var repo = World(terrain);
            Record(repo, Big()); Record(repo, Big(20f, 0f));
            var first = DoorStates.Of(repo, terrain);
            var q = TerrainWorldQuery.ForView(repo, terrain);
            float sum = q.GroundHeightAt(1f, 1f) + q.SurfaceZ(2f, 0f, 0f);
            terrain.SegmentBlocked(new Vector3(100f, 0f, 1.8f), new Vector3(0f, 0f, -1.3f), first);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                var d = DoorStates.Of(repo, terrain);
                var qq = TerrainWorldQuery.ForView(repo, terrain);
                sum += qq.GroundHeightAt(1f, 1f) + qq.SurfaceZ(2f, 0f, 0f);
                terrain.SegmentBlocked(new Vector3(100f, 0f, 1.8f), new Vector3(0f, 0f, -1.3f), d);
                Assert.Same(first.Craters, d.Craters);
            }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.True(sum < 0f);
        }

        // ── the chain: a burst → the record ─────────────────────────────────────────────────

        private sealed class Chain
        {
            public readonly EntityRepository W = new();
            public readonly AreaEffectSystem Area = new();
            public readonly GroundDamageSystem Damage = new();

            public Chain(TerrainWorld terrain)
            {
                W.RegisterComponent<SimTransform>();
                W.RegisterComponent<Health>();
                W.RegisterComponent<PhysicsCollider>();
                W.RegisterComponent<StanceIntent>();
                W.RegisterComponent<DoorState>();
                W.RegisterComponent<BuildingDamage>();
                W.RegisterComponent<GroundDamage>();
                W.RegisterComponent<NetworkAuthority>();
                W.RegisterManagedComponent<TerrainObjectKey>();
                W.RegisterEvent<DetonationNotification>();
                W.RegisterEvent<DamageAssessedEvent>();
                W.RegisterEvent<DoorCommandEvent>();
                W.RegisterEvent<BuildingDamageEvent>();
                W.RegisterEvent<CraterEvent>();
                var db = new TkbDatabase();
                db.Register(new TkbTemplate("M67 grenade", Grenade) { DisType = new DISEntityType { Kind = 2 } });
                db.Register(new TkbTemplate("Mk-82 bomb", Bomb) { DisType = new DISEntityType { Kind = 2 } });
                db.Register(new TkbTemplate("5.56x45 ball", Ball));
                W.SetSingletonManaged<ITkbDatabase>(db);
                W.SetSingletonManaged(terrain);
            }

            public DetonationRecord? Burst(long ammo, Vector3 at)
            {
                W.Bus.Publish(new DetonationNotification { Target = Entity.Null, HitX = at.X, HitY = at.Y, HitZ = at.Z, Ammo = ammo });
                W.Bus.SwapBuffers();
                long before = DetonationLog.Peek(W)?.Count ?? 0;
                Area.Execute(W, 0.016f);
                W.Bus.SwapBuffers();
                var log = DetonationLog.Peek(W);
                return log != null && log.Count > before ? log.Recent(1).Single() : null;
            }

            public List<EntityCreationRequest> Tick()
            {
                Damage.Execute(W, 0.016f);
                W.Bus.SwapBuffers();
                return W.Bus.ReadManaged<EntityCreationRequest>().ToList();
            }

            public Entity Materialise(EntityCreationRequest r)
            {
                var e = W.CreateEntity();
                foreach (var c in r.InitialComponents!)
                    switch (c)
                    {
                        case SimTransform t: W.AddComponent(e, t); break;
                        case GroundDamage d: W.AddComponent(e, d); break;
                    }
                return e;
            }
        }

        /// <summary>
        /// ⭐⭐ G-7 — a bomb on open ground ⇒ ONE <see cref="CraterEvent"/>, a creation request born holding the crater (transient, its cell's anchor), and once
        /// the entity is made the ground there is the crater's; a second bomb in the same cell is written into the SAME entity; a grenade or a 5.56 round makes
        /// none. 🔴 Red-proof: drop the <c>MakeCrater</c> call in <c>Assess</c> ⇒ no event, no request.
        /// </summary>
        [Fact]
        public void ABomb_OnOpenGround_MakesACraterRecord_TheGroundFollows_AndASecondBombInTheCellJoinsIt()
        {
            var terrain = Flat();
            var chain = new Chain(terrain);
            var rec = chain.Burst(Bomb, new Vector3(10f, 10f, 0.1f));
            Assert.NotNull(rec);
            Assert.StartsWith("crater r ", rec!.Crater);
            var ev = Assert.Single(chain.W.Bus.Read<CraterEvent>().ToArray());
            Assert.Equal((byte)CraterKind.Large, ev.Kind);

            var request = Assert.Single(chain.Tick());
            Assert.Equal(GroundDamages.TkbType, (long)request.TkbType);
            Assert.True(request.IsTransient);
            var d0 = request.InitialComponents!.OfType<GroundDamage>().Single();
            Assert.Equal(1, d0.Count);
            Assert.Equal((0, 0), (d0.CellX, d0.CellY));
            chain.Materialise(request);
            Assert.Equal(-ev.Depth, TerrainWorldQuery.ForView(chain.W, terrain).GroundHeightAt(10f, 10f), 4);

            chain.Burst(Bomb, new Vector3(30f, 30f, 0.1f));                 // the same 64 m cell
            Assert.Empty(chain.Tick());                                     // the pending request is answered: no second entity asked for
            var all = new List<Entity>(); foreach (var x in chain.W.Query().With<GroundDamage>().Build()) all.Add(x);
            var e = Assert.Single(all);
            Assert.Equal(2, chain.W.GetComponentRO<GroundDamage>(e).Count);

            Assert.Null(chain.Burst(Grenade, new Vector3(-50f, -50f, 0f))?.Crater);
            chain.Burst(Ball, new Vector3(-50f, -50f, 0f));
            Assert.Empty(chain.W.Bus.Read<CraterEvent>().ToArray());
        }

        /// <summary>⭐ Capacity: past <see cref="GroundDamage.Capacity"/> craters in one cell a second entity is asked for; the view unions them.</summary>
        [Fact]
        public void PastCapacity_ASecondEntityIsAskedFor_AndTheViewUnionsBoth()
        {
            var terrain = Flat();
            var chain = new Chain(terrain);
            for (int i = 0; i < GroundDamage.Capacity; i++)
                chain.W.Bus.Publish(CraterEvent.Of(new Crater(new Vector2(1f + i, 1f), 1f, 0.4f, 0.1f, (SimInstant)i, CraterKind.Small), Entity.Null));
            chain.W.Bus.SwapBuffers();
            var first = Assert.Single(chain.Tick());
            Assert.Equal(1, first.InitialComponents!.OfType<GroundDamage>().Single().Count);   // born with the first; the rest wait for it
            chain.Materialise(first);
            Assert.Empty(chain.Tick());                                                          // the waiting 15 applied
            chain.W.Bus.Publish(CraterEvent.Of(new Crater(new Vector2(40f, 1f), 1f, 0.4f, 0.1f, (SimInstant)99, CraterKind.Small), Entity.Null));
            chain.W.Bus.SwapBuffers();
            var second = Assert.Single(chain.Tick());
            chain.Materialise(second);
            Assert.Equal(GroundDamage.Capacity + 1, CraterTable.Of(chain.W)!.Count);
        }
    }
}
