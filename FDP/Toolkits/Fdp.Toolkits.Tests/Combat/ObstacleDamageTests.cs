using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.MuscleCharacter.Animation.Components;
using Xunit;
using Xunit.Abstractions;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-6003</c> (city battle O-1; 📄 docs/DESIGN_Peek_And_Fire.md §9.9) — a destructible static obstacle: the blast law
    /// (<see cref="ObstacleDamage.BlastDamage"/>, charge × closeness), the area effect that applies it (<see cref="AreaEffectSystem"/>, its own
    /// prism never shielding it), the Health owner that removes it at 0 (<see cref="ObstacleRemovalSystem"/>), and the seam the bulldozer's blade
    /// calls (<see cref="ObstacleDamage.Damage"/>, CE-7012). The warheads are the reference library's, through the system's own resolver.
    /// ⭐ Parallel contract (CE-6012 reruns these with <c>HROT_PARALLEL_SIM=1 HROT_READ_CHECK=1 HROT_WORLD_GUARD=1</c> unchanged): each test drives the
    /// systems on one world, no statics, no clock.
    /// </summary>
    public sealed class ObstacleDamageTests : IDisposable
    {
        private const long Charge8Kg = 6043, Mortar120 = 6044, Mortar81 = 6045, Grenade = 6046, Ball = 6047;
        private const float ConcreteHp = 600f;   // NedTkbCatalog's concrete block (Hrot.Core) — stated in ObstacleDamage's unit
        private readonly EntityRepository _w = new();
        private readonly AreaEffectSystem _area = new();
        private readonly HealthApplicationSystem _apply = new();
        private readonly ObstacleRemovalSystem _remove = new();
        private readonly ITestOutputHelper _out;

        public ObstacleDamageTests(ITestOutputHelper output)
        {
            _out = output;
            _w.RegisterComponent<SimTransform>();
            _w.RegisterComponent<Health>();
            _w.RegisterComponent<PhysicsCollider>();
            _w.RegisterComponent<StanceIntent>();
            _w.RegisterComponent<StaticObstacle>();
            _w.RegisterComponent<ObstacleShape>();
            _w.RegisterComponent<NetworkAuthority>();
            _w.RegisterComponent<NetworkIdentity>();
            _w.RegisterEvent<DetonationNotification>();
            _w.RegisterEvent<DamageAssessedEvent>();
            _w.RegisterManagedEvent<Fdp.Toolkit.NetworkSpawning.Events.DestroyEntityCommand>();
            var db = new TkbDatabase();
            db.Register(new TkbTemplate("8 kg demolition charge", Charge8Kg) { DisType = new DISEntityType { Kind = 2 } });
            db.Register(new TkbTemplate("120mm mortar HE", Mortar120) { DisType = new DISEntityType { Kind = 2 } });
            db.Register(new TkbTemplate("81mm mortar HE", Mortar81) { DisType = new DISEntityType { Kind = 2 } });
            db.Register(new TkbTemplate("M67 grenade", Grenade) { DisType = new DISEntityType { Kind = 2 } });
            db.Register(new TkbTemplate("5.56x45 ball", Ball));
            _w.SetSingletonManaged<ITkbDatabase>(db);
        }

        public void Dispose() => _w.Dispose();

        /// <summary>A concrete block (2 × 1 × 1 m) at <paramref name="at"/>, yaw 0, Health <paramref name="hp"/> — and baked into the terrain as its own
        /// prism, as every node holds it (R-243): the prism the old person / hull law would have traced through.</summary>
        private Entity Block(Vector3 at, float hp = ConcreteHp, string material = "concrete")
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _w.AddComponent(e, new StaticObstacle { Material = new FixedString32(material) });
            var shape = new ObstacleShape { Length = 2f, Width = 1f, Height = 1f };
            _w.AddComponent(e, shape);
            _w.AddComponent(e, new PhysicsCollider { Radius = 1.12f, Height = 1f });
            _w.AddComponent(e, new Health { Current = hp, Max = hp });
            var prisms = _w.HasSingletonManaged<TerrainWorld>() ? _w.GetSingletonManaged<TerrainWorld>().Prisms.ToList() : new();
            prisms.Add(TerrainObstacles.PrismOf(at, 0f, shape, TerrainMaterialLibrary.Shared.Get("concrete", "test"), TerrainObstacles.LabelPrefix + e.Index));
            _w.SetSingletonManaged(new TerrainWorld { BoundsMin = new Vector2(-50, -50), BoundsMax = new Vector2(50, 50), Prisms = prisms });
            return e;
        }

        /// <summary>One burst of <paramref name="ammo"/> at <paramref name="at"/> through the area effect; the damage each target got.</summary>
        private float[] Burst(long ammo, Vector3 at, params Entity[] targets)
        {
            _w.Bus.Publish(new DetonationNotification { HitX = at.X, HitY = at.Y, HitZ = at.Z, Ammo = ammo });
            _w.Bus.SwapBuffers();
            _area.Execute(_w, 0.016f);
            _w.Bus.SwapBuffers();
            var events = _w.Bus.Read<DamageAssessedEvent>().ToArray();
            return targets.Select(t => events.Where(d => d.HitEntity == t).Sum(d => d.TotalDamage)).ToArray();
        }

        /// <summary>The owner's frame after the burst: the damage applied, then the removal; the command buffer played back.</summary>
        private void OwnerFrame()
        {
            _apply.Execute(_w, 0.016f);
            _remove.Execute(_w, 0.016f);
            ((EntityCommandBuffer)((ISimulationView)_w).GetCommandBuffer()).Playback(_w);
        }

        // ── the law ─────────────────────────────────────────────────────────────────────────────

        /// <summary>⭐ The law in numbers: full at contact (Z ≤ 0.5), linear to nothing at Z = 1.5, ∝ kg. 🔴 Red-proof: drop the ∛kg scaling
        /// (Z = d) ⇒ the 8 kg charge at 1.6 m reads 0 ⇒ red.</summary>
        [Fact]
        public void CE6003_L1_TheBlastLaw_ChargeTimesCloseness()
        {
            Assert.Equal(800f, ObstacleDamage.BlastDamage(8f, 0f), 3);
            Assert.Equal(800f, ObstacleDamage.BlastDamage(8f, 1.0f), 3);              // Z = 0.5
            Assert.Equal(400f, ObstacleDamage.BlastDamage(8f, 2.0f), 3);              // Z = 1.0 — half way
            Assert.Equal(0f, ObstacleDamage.BlastDamage(8f, 3.0f));                   // Z = 1.5
            Assert.True(ObstacleDamage.BlastDamage(8f, 1.4f) > 600f);                 // the charge 1.4 m off still removes a block (Z 0.7)
            Assert.Equal(300f, ObstacleDamage.BlastDamage(3f, 0.1f), 3);              // a 120 mm round at the block: half a block
            Assert.Equal(0f, ObstacleDamage.BlastDamage(0f, 0f));                     // a kinetic round
            // the nearest point of a 2 × 1 × 1 block at the origin, yaw 90°: its length lies along Y
            var p = ObstacleDamage.NearestPoint(Vector3.Zero, MathF.PI / 2f, new ObstacleShape { Length = 2f, Width = 1f, Height = 1f }, new Vector3(-3f, 5f, 0.5f));
            Assert.Equal(-0.5f, p.X, 3); Assert.Equal(1f, p.Y, 3); Assert.Equal(0.5f, p.Z, 3);
        }

        // ── the area effect ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐⭐⭐ <c>CE6003_A1</c> — THE DONE CONDITION's damage half: an 8 kg demolition charge set 0.5 m off a concrete block's face empties its
        /// Health in ONE burst, though the block is baked into the terrain as its own prism; the owner then asks for its removal.
        /// 🔴 Red-proof: drop the obstacle branch in <c>AreaEffectSystem.Assess</c> (the person / hull law runs) ⇒ measured in the report.
        /// </summary>
        [Fact]
        public void CE6003_A1_ADemolitionCharge_EmptiesAConcreteBlock_ItsOwnPrismDoesNotShieldIt()
        {
            var block = Block(new Vector3(10, 0, 0));
            var dmg = Burst(Charge8Kg, new Vector3(8.5f, 0, 0.1f), block);   // the face is at x = 9
            _out.WriteLine($"8 kg at 0.5 m: {dmg[0]:0.0}");
            Assert.True(dmg[0] >= ConcreteHp, $"the charge took {dmg[0]} of {ConcreteHp}");
            var effect = DetonationLog.Peek(_w)!.Recent(1).Single().Effects.Single(x => x.Entity == block);
            Assert.Equal("Obstacle", effect.Stance);
            OwnerFrame();
            Assert.False(_w.IsAlive(block), "an emptied obstacle is removed by its Health owner (offline: through the command buffer)");
        }

        /// <summary>
        /// ⭐⭐ <c>CE6003_A2</c> — small rounds do not trivially erase a block: an 81 mm mortar round at its face takes off less than a fifth, a
        /// 120 mm round half; an M67 grenade far less; a rifle ball (kinetic) and a burst 4 m off nothing. The kind of obstacle is its Health
        /// (TKB): the same 120 mm round removes a crate (60) and a car (150). 🔴 Red-proof: <c>BlastDamage</c> without the kg factor ⇒ the grenade
        /// equals the charge ⇒ red.
        /// </summary>
        [Fact]
        public void CE6003_A2_SmallRoundsDoNotErase_TheTypesHealthDecides()
        {
            var block = Block(new Vector3(10, 0, 0));
            float m81 = Burst(Mortar81, new Vector3(8.95f, 0, 0.1f), block)[0];
            float m120 = Burst(Mortar120, new Vector3(8.95f, 0, 0.1f), block)[0];
            float grenade = Burst(Grenade, new Vector3(8.95f, 0, 0.1f), block)[0];
            float ball = Burst(Ball, new Vector3(8.95f, 0, 0.1f), block)[0];
            float far = Burst(Mortar81, new Vector3(5f, 0, 0.1f), block)[0];
            _out.WriteLine($"81 mm {m81:0.0} · 120 mm {m120:0.0} · M67 {grenade:0.0} · ball {ball} · 81 mm at 4 m {far}");
            Assert.InRange(m81, 50f, ConcreteHp / 5f);
            Assert.InRange(m120, 200f, ConcreteHp * 0.6f);
            Assert.True(grenade < m81 / 2f);
            Assert.Equal(0f, ball);
            Assert.Equal(0f, far);
            Assert.True(m120 >= 150f && m120 >= 60f, "a 120 mm round at its face removes a car (150) and a crate (60)");
        }

        // ── the owner removes it ────────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐⭐ <c>CE6003_R1</c> — a REPLICATED obstacle at 0 is removed through the one teardown, ONCE: a <c>DestroyEntityCommand</c> with its
        /// network id, marked TearDown so it leaves the obstacle set at once; ⛔ a node that does not hold its Health claim does nothing; ⛔ a dead
        /// BODY (not an obstacle) is never removed (the CE-267 revert). 🔴 Red-proof: drop the <c>HealthOwnership.AppliesHere</c> test ⇒ the
        /// non-owner asks too ⇒ red.
        /// </summary>
        [Fact]
        public void CE6003_R1_TheHealthOwnerRemovesAnEmptiedObstacle_OnceThroughTheTeardown_NobodyElse_NeverABody()
        {
            var mine = Block(new Vector3(0, 0, 0), hp: 0f);
            _w.AddComponent(mine, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            _w.SetAuthority<Health>(mine, true);
            _w.AddComponent(mine, new NetworkIdentity(7001));
            var theirs = Block(new Vector3(5, 0, 0), hp: 0f);
            _w.AddComponent(theirs, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
            _w.AddComponent(theirs, new NetworkIdentity(7002));
            var body = _w.CreateEntity();
            _w.AddComponent(body, new SimTransform { Position = new Vector3(20, 0, 0), Rotation = Quaternion.Identity });
            _w.AddComponent(body, new Health { Current = 0f, Max = 100f });

            _w.Bus.SwapBuffers();
            OwnerFrame();
            _w.Bus.SwapBuffers();
            var asked = _w.Bus.ReadManaged<Fdp.Toolkit.NetworkSpawning.Events.DestroyEntityCommand>().ToArray();
            Assert.Equal(7001, Assert.Single(asked).NetworkId);
            Assert.Equal(EntityLifecycle.TearDown, _w.GetLifecycleState(mine));
            Assert.Equal(EntityLifecycle.Active, _w.GetLifecycleState(theirs));
            Assert.True(_w.IsAlive(body));

            OwnerFrame();
            _w.Bus.SwapBuffers();
            Assert.Empty(_w.Bus.ReadManaged<Fdp.Toolkit.NetworkSpawning.Events.DestroyEntityCommand>());   // asked once
            Assert.Equal(1, _remove.Removed);
        }

        /// <summary>
        /// ⭐⭐ <c>CE6003_S1</c> — THE BLADE'S SEAM (CE-7012): a push is <c>ObstacleDamage.Damage</c> once per tick of contact; the same
        /// <see cref="DamageAssessedEvent"/> every burst sends, so the obstacle empties after <c>Health / (rate · dt)</c> ticks and is removed. A
        /// non-obstacle (a man) refuses it. 🔴 Red-proof: <c>Damage</c> ignoring <c>IsDestructible</c> ⇒ the man is accepted ⇒ red.
        /// </summary>
        [Fact]
        public void CE6003_S1_ABladePush_IsDamagePerTick_ThroughTheSameEvent()
        {
            var block = Block(new Vector3(0, 0, 0));
            var dozer = _w.CreateEntity();
            const float rate = 150f, dt = 0.5f;   // per second; the block lasts 600 / 75 = 8 ticks
            int ticks = 0;
            while (_w.IsAlive(block) && ticks < 20)
            {
                Assert.True(ObstacleDamage.Damage(_w, block, rate * dt, dozer));
                _w.Bus.SwapBuffers();
                OwnerFrame();
                ticks++;
            }
            Assert.Equal(8, ticks);
            Assert.False(_w.IsAlive(block));
            var man = _w.CreateEntity();
            _w.AddComponent(man, new Health { Current = 100f, Max = 100f });
            Assert.False(ObstacleDamage.Damage(_w, man, 10f, dozer));
            Assert.False(ObstacleDamage.Damage(_w, block, 10f, dozer));   // gone
        }

        /// <summary>⭐ HOT PATH (R-2084) — the removal system's steady frame over standing obstacles allocates 0 B. 🔴 Red-proof: build the query each
        /// frame ⇒ bytes ⇒ red.</summary>
        [Fact]
        public void CE6003_H1_TheRemovalSystem_ASteadyFrameAllocatesNothing()
        {
            for (int i = 0; i < 50; i++) Block(new Vector3(i * 3f, 0, 0));
            for (int i = 0; i < 5; i++) _remove.Execute(_w, 0.016f);   // warm: the query, the JIT
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) _remove.Execute(_w, 0.016f);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(bytes == 0, $"100 steady frames allocated {bytes} B");
            Assert.Equal(0, _remove.Removed);
        }
    }
}
