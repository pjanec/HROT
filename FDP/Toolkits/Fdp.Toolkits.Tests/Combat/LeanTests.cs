using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Perception.LineOfSight;
using Fdp.Toolkit.Physics.BodyParts;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Terrain;
using Xunit;

namespace Fdp.Toolkit.Tests.Combat
{
    /// <summary>
    /// ⭐⭐ <c>CE-3257</c> (BR-5, Building Clearing CB-21, R-2032; docs/designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md §13) — a man
    /// LEANING round a corner: his eye moves 0.4 m to that side (he sees and shoots from there), the points he is seen at lean with him, and a
    /// round meets only the half of him he shows. New feature, no suite before.
    /// </summary>
    public sealed class LeanTests : IDisposable
    {
        private readonly EntityRepository _world = new();

        public LeanTests()
        {
            _world.RegisterComponent<SimTransform>();
            _world.RegisterComponent<PhysicsCollider>();
            _world.RegisterComponent<Lean>();
        }

        public void Dispose() => _world.Dispose();

        private Entity Man(Vector3 at, float yawRadians = 0f)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yawRadians) });
            return e;
        }

        /// <summary>
        /// LN-3 / LN-4 — a pillar 2 m in front of him hides a man 10 m off; leaning LEFT (his +y) his eye clears its edge and he sees him; a
        /// prone man does not lean. ✅ Red-proof: <c>EyeOf</c> without the lean ⇒ still hidden ⇒ red.
        /// </summary>
        [Fact]
        public void CE3257_ALeaningMan_SeesRoundTheCorner_AManHeCannotSeeUpright()
        {
            var watcher = Man(Vector3.Zero);
            var target = Man(new Vector3(10f, 0f, 0f));
            var pillar = _world.CreateEntity();
            _world.AddComponent(pillar, new SimTransform { Position = new Vector3(2f, 0f, 0f), Rotation = Quaternion.Identity });
            _world.AddComponent(pillar, new PhysicsCollider { Radius = 0.25f });
            var los = new TerrainWorldLosStrategy(() => (TerrainWorld?)null,
                (v, e) => v.HasComponent<PhysicsCollider>(e) ? v.GetComponentRO<PhysicsCollider>(e).Radius : 0f, (_, _) => 0f);
            ISimulationView view = _world;

            los.BeginBatch(view);
            Assert.False(los.IsVisible(view, watcher, target), "upright: the pillar hides him");

            Assert.True(Leans.Set(_world, watcher, LeanSide.LeanLeft));
            los.BeginBatch(view);
            Assert.True(los.IsVisible(view, watcher, target), "leaning left: his eye clears the pillar");
            var eye = TerrainWorldLosStrategy.EyeOf(view, watcher, Fdp.Toolkit.Tkb.Domain.StanceId.Standing);
            Assert.Equal(0.4f, eye.Y, 3);
            Assert.Equal(TerrainWorldLosStrategy.DefaultMount.Standing, eye.Z, 3);

            Leans.Set(_world, watcher, LeanSide.LeanRight);
            Assert.Equal(-0.4f, TerrainWorldLosStrategy.EyeOf(view, watcher, Fdp.Toolkit.Tkb.Domain.StanceId.Standing).Y, 3);
            Assert.Equal(Vector3.Zero, Leans.Offset(view, watcher, Fdp.Toolkit.Tkb.Domain.StanceId.Prone, 0.35f, 0.35f));
        }

        /// <summary>LN-3 — the round leaves from the leaning eye too (<c>ShotOrigin.Eye</c>), and the shooter aims at the middle of the half the
        /// leaning target shows (LN-6: the sheared centreline + 0.1 m into that side).</summary>
        [Fact]
        public void CE3257_HeShootsFromHisLeaningEye_AndIsAimedAtOnTheHalfHeShows()
        {
            var man = Man(Vector3.Zero, MathF.PI * 0.5f);   // facing +y: his left is −x
            Leans.Set(_world, man, LeanSide.LeanLeft);
            var eye = ShotOrigin.Eye(_world, man, out _);
            Assert.Equal(-0.4f, eye.X, 3);
            Assert.Equal(0f, eye.Y, 3);

            var middle = AimPoint.Middle(_world, man, out _, out _);
            float aim = AimPoint.Chest(Fdp.Toolkit.Physics.BodyParts.PersonBody.PoseOf(_world, man)).Z;   // ⭐ CE-2325 — his chest
            Assert.Equal(-(0.4f * aim / TerrainWorldLosStrategy.DefaultMount.Standing + Leans.ExposedHalf), middle.X, 3);
            Assert.Equal(aim, middle.Z, 3);
        }

        /// <summary>
        /// LN-5 — the half he shows: leaning left, a round across his RIGHT shoulder misses (it hit him upright), and a round where his head now
        /// is (0.4 m to his left at eye height — outside his upright body) strikes his HEAD.
        /// ✅ Red-proof: <c>BodyPartRay.Person</c> ignoring the lean ⇒ the right shoulder is still hit ⇒ red.
        /// </summary>
        [Fact]
        public void CE3257_ARoundMeetsOnlyTheHalfHeShows_AndHisHeadWhereItLeansTo()
        {
            var man = Man(Vector3.Zero);   // facing +x: his left is +y
            var tf = _world.GetComponentRO<SimTransform>(man);
            var rightShoulder = (A: new Vector3(-20f, -0.18f, 1.35f), B: new Vector3(20f, -0.18f, 1.35f));
            Assert.Equal(BodyPartTest.Hit, BodyPartRay.Person(_world, man, tf, 0f, rightShoulder.A, rightShoulder.B, out _));
            float eye = TerrainWorldLosStrategy.DefaultMount.Standing;
            var leanedHead = (A: new Vector3(-20f, 0.4f, eye - 0.05f), B: new Vector3(20f, 0.4f, eye - 0.05f));
            Assert.NotEqual(BodyPartTest.Hit, BodyPartRay.Person(_world, man, tf, 0f, leanedHead.A, leanedHead.B, out _));

            Leans.Set(_world, man, LeanSide.LeanLeft);
            Assert.NotEqual(BodyPartTest.Hit, BodyPartRay.Person(_world, man, tf, 0f, rightShoulder.A, rightShoulder.B, out _));
            Assert.Equal(BodyPartTest.Hit, BodyPartRay.Person(_world, man, tf, 0f, leanedHead.A, leanedHead.B, out var head));
            Assert.Equal(PersonPart.Head, head.Part);
            Assert.InRange(head.T, 0.49f, 0.51f);   // t on the ORIGINAL segment: the head is at its middle
        }

        /// <summary>
        /// ⭐ AQ104 G (R-2079) — the lean's DEPTH by skill: a trained man leaning shallow (0.25 m, weapon first) moves his eye 0.25 m, a
        /// militiaman 0.5 m, an unset depth the old 0.4 m; a round where a 0.4 m lean puts his head misses the shallow man and the head of
        /// the deep man is where HIS depth puts it; the depth keeps his side, and survives straightening up. ✅ Red-proof: <c>Leans.ShiftOf</c>
        /// returning the constant ⇒ every eye at 0.4 m ⇒ red.
        /// </summary>
        [Fact]
        public void AQ104G_TheLeansDepth_IsHisOwn_EyeBodyAndHitBoxesAgree()
        {
            var man = Man(Vector3.Zero);   // facing +x: his left is +y
            var tf = _world.GetComponentRO<SimTransform>(man);
            ISimulationView view = _world;
            var standing = Fdp.Toolkit.Tkb.Domain.StanceId.Standing;
            Leans.Set(_world, man, LeanSide.LeanLeft);
            Assert.Equal(Leans.Shift, TerrainWorldLosStrategy.EyeOf(view, man, standing).Y, 3);   // no depth: today's 0.4 m

            Assert.True(Leans.SetDepth(_world, man, 0.25f));
            Assert.Equal(LeanSide.LeanLeft, Leans.Of(view, man));
            Assert.Equal(0.25f, TerrainWorldLosStrategy.EyeOf(view, man, standing).Y, 2);
            float eye = TerrainWorldLosStrategy.DefaultMount.Standing;
            var at40 = (A: new Vector3(-20f, 0.4f, eye - 0.05f), B: new Vector3(20f, 0.4f, eye - 0.05f));
            var at25 = (A: new Vector3(-20f, 0.25f, eye - 0.05f), B: new Vector3(20f, 0.25f, eye - 0.05f));
            Assert.NotEqual(BodyPartTest.Hit, BodyPartRay.Person(_world, man, tf, 0f, at40.A, at40.B, out _));   // shallow: his head is not out there
            Assert.Equal(BodyPartTest.Hit, BodyPartRay.Person(_world, man, tf, 0f, at25.A, at25.B, out var head));
            Assert.Equal(PersonPart.Head, head.Part);

            Leans.SetDepth(_world, man, 0.5f);
            Assert.Equal(0.5f, TerrainWorldLosStrategy.EyeOf(view, man, standing).Y, 2);
            var at50 = (A: new Vector3(-20f, 0.5f, eye - 0.05f), B: new Vector3(20f, 0.5f, eye - 0.05f));
            Assert.Equal(BodyPartTest.Hit, BodyPartRay.Person(_world, man, tf, 0f, at50.A, at50.B, out head));
            Assert.Equal(PersonPart.Head, head.Part);

            Leans.Set(_world, man, LeanSide.LeanNone);
            Leans.Set(_world, man, LeanSide.LeanRight);
            Assert.Equal(-0.5f, TerrainWorldLosStrategy.EyeOf(view, man, standing).Y, 2);   // the depth is his, not the lean's
            Assert.Equal(Leans.MaxShift, Leans.ShiftOf(new Lean { Depth = 255 }), 4);
            Assert.Equal(1, Leans.DepthOf(0f));   // never 0 (= default)
        }

        /// <summary>
        /// The frame's measured ratio — rounds aimed at his CENTRE with the same spread: fewer strike a leaning man than an upright one in the
        /// open (he shows half; measured 0.28), and aiming at the half he shows (<c>AimPoint.Middle</c>, LN-6) does better.
        /// ✅ Red-proof: the clip to the leaning half skipped ⇒ the ratio rises out of its band ⇒ red.
        /// </summary>
        [Fact]
        public void CE3257_RoundsAimedAtHisCentre_MissALeaningManMoreOften_AMeasuredRatio()
        {
            var man = Man(Vector3.Zero, MathF.PI);   // facing the shooter at −x
            var tf = _world.GetComponentRO<SimTransform>(man);
            var shooter = new Vector3(-50f, 0f, 1.5f);
            var centre = new Vector3(0f, 0f, TerrainWorldLosStrategy.DefaultMount.Standing * 0.5f);

            int Hits(Vector3 centre)
            {
                var rng = new Random(3257);
                int hits = 0;
                for (int i = 0; i < 4000; i++)
                {
                    float dy = Gauss(rng) * 0.3f, dz = Gauss(rng) * 0.3f;
                    var through = centre + new Vector3(0f, dy, dz);
                    var end = shooter + (through - shooter) * 1.2f;
                    if (BodyPartRay.Person(_world, man, tf, 0f, shooter, end, out _) == BodyPartTest.Hit) hits++;
                }
                return hits;
            }

            int upright = Hits(centre);
            Leans.Set(_world, man, LeanSide.LeanRight);
            int leaning = Hits(centre);
            float ratio = leaning / (float)upright;
            Assert.True(upright > 1000, $"upright hits {upright}");
            // 📐 measured 2026-10-19: 0.28 — half the silhouette, and its upper part sheared away from the centre line
            Assert.InRange(ratio, 0.15f, 0.45f);

            // LN-6 — a shooter aiming where AimPoint.Middle puts it (the half he shows) does better than one aiming at the centre
            int aimed = Hits(AimPoint.Middle(_world, man, out _, out _));
            Assert.True(aimed > leaning * 1.3f, $"aimed at the half he shows {aimed} vs at the centre {leaning}");
        }

        private static float Gauss(Random rng)
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }
    }
}
