using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.MuscleCharacter.Animation.Components;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// ⭐ AQ85 (R-216) — the deterministic aim deflection: σ from the mount and the shooter's state, d(k) over the shot count, and the
    /// hit chance the AI shares with the shot. 📄 docs/blueprints/Architect_Question_85_Hit_Chance.md §3.
    /// </summary>
    public sealed class HitModelTests
    {
        private static readonly WeaponMountDto Rifle = new() { DispersionMils = 6f };

        private static (EntityRepository repo, Entity e) Unit()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<SimVelocity>();
            repo.RegisterComponent<UnderFire>();
            repo.RegisterComponent<ShotOrdinal>();
            repo.RegisterComponent<StanceIntent>();
            return (repo, repo.CreateEntity());
        }

        [Fact]
        public void NoDispersion_MeansExactAim_ZeroSigma_CertainHit()
        {
            var (repo, e) = Unit();
            Assert.Equal(0f, HitModel.Sigma(repo, e, new WeaponMountDto(), 0));
            Assert.Equal(0f, HitModel.Sigma(repo, e, null, 0));
            Assert.Equal(1f, HitModel.HitChance(0f, 500f, 0.3f));
            Assert.Equal(0f, HitModel.Deflection(0f, 7));
        }

        [Fact]
        public void Sigma_DoublesMovingAndUnderFire_HalvesProne_ThreeQuartersCrouched()
        {
            var (repo, e) = Unit();
            Assert.Equal(0.006f, HitModel.Sigma(repo, e, Rifle, 0), 6);
            repo.AddComponent(e, new SimVelocity { Linear = new Vector3(1, 0, 0) });
            Assert.Equal(0.012f, HitModel.Sigma(repo, e, Rifle, 0), 6);
            repo.AddComponent(e, new UnderFire { LastTime = 10 });
            Assert.Equal(0.024f, HitModel.Sigma(repo, e, Rifle, 12), 6);   // 2 s after: suppressed
            Assert.Equal(0.012f, HitModel.Sigma(repo, e, Rifle, 16), 6);   // 6 s after: not any more
            repo.AddComponent(e, new StanceIntent { TargetStance = StanceId.Prone });
            Assert.Equal(0.006f, HitModel.Sigma(repo, e, Rifle, 16), 6);   // the LOGICAL stance halves it
            repo.SetComponent(e, new StanceIntent { TargetStance = StanceId.Crouched });
            Assert.Equal(0.009f, HitModel.Sigma(repo, e, Rifle, 16), 6);
        }

        [Fact]
        public void HitChance_IsTheDesignedCalibration_HalfAt100m_AboutAllAt37m()
        {
            Assert.Equal(0.5f, HitModel.HitChance(0.006f, 100f, 0.3f), 2);
            Assert.Equal(1f, HitModel.HitChance(0.006f, 37f, 0.3f), 1);
            Assert.Equal(0.42f, HitModel.HitChance(0.006f, 120f, 0.3f), 2);
            Assert.Equal(0.25f, HitModel.HitChance(0.012f, 100f, 0.3f), 2);   // moving
        }

        [Fact]
        public void TheSequence_CoversTheSpreadEvenly_AndTheShareInsideTheTargetMatchesHitChance()
        {
            // 1000 shots at 100 m, σ = 6 mils: the share whose deflection keeps them inside a 0.3 m circle ≈ HitChance.
            float sigma = 0.006f, half = MathF.Atan(0.3f / 100f);
            int inside = Enumerable.Range(1, 1000).Count(k => MathF.Abs(HitModel.Deflection(sigma, (uint)k)) <= half);
            Assert.InRange(inside / 1000f, 0.48f, 0.52f);
            Assert.All(Enumerable.Range(1, 1000), k => Assert.InRange(HitModel.Sequence((uint)k), -1f, 1f));
        }

        [Fact]
        public void OrdinalsCountFromOne_PerShooter_AndRotateKeepsTheVerticalComponent()
        {
            var (repo, a) = Unit();
            var b = repo.CreateEntity();
            Assert.Equal(new uint[] { 1, 2, 3 }, new[] { HitModel.NextOrdinal(repo, a), HitModel.NextOrdinal(repo, a), HitModel.NextOrdinal(repo, a) });
            Assert.Equal(1u, HitModel.NextOrdinal(repo, b));
            var d = HitModel.Rotate(new Vector3(1, 0, 0.1f), MathF.PI / 2);
            Assert.Equal(0f, d.X, 5); Assert.Equal(1f, d.Y, 5); Assert.Equal(0.1f, d.Z, 5);
        }

        [Fact]
        public void StampUnderFire_AddsThenRefreshes()
        {
            var (repo, e) = Unit();
            HitModel.StampUnderFire(repo, e, 3);
            HitModel.StampUnderFire(repo, e, 7);
            Assert.Equal(7, repo.GetComponent<UnderFire>(e).LastTime);
        }
    }
}
