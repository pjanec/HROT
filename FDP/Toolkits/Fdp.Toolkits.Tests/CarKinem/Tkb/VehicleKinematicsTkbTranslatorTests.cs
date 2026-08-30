using CarKinem.Core;
using CarKinem.Tkb;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace CarKinem.Tkb.Tests
{
    public class VehicleKinematicsTkbTranslatorTests
    {
        private static TkbTemplate MakeTemplate(float length = 6f, float width = 2.5f,
            float maxSpeedFwd = 20f, float maxAccel = 2.5f)
        {
            var t = new TkbTemplate("TestVehicle", 999L);
            t.AddDescriptor(new VehicleParametersDto
            {
                Length = length, Width = width,
                MaxSpeedFwd = maxSpeedFwd, MaxAccel = maxAccel
            });
            return t;
        }

        private static EntityRepository MakeWorldWithComponents()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<VehicleParams>();
            repo.RegisterComponent<VehicleState>();
            repo.RegisterComponent<NavState>();
            repo.RegisterComponent<PhysicsCollider>();
            return repo;
        }

        [Fact]
        public void GetConsumedDescriptors_ReturnsVehicleParametersDto()
        {
            var translator = new VehicleKinematicsTkbTranslator();
            Assert.Contains(typeof(VehicleParametersDto), translator.GetConsumedDescriptors());
        }

        [Fact]
        public void Inject_WithAllComponentsRegistered_AddsVehicleParams()
        {
            var repo = MakeWorldWithComponents();
            var entity = repo.CreateEntity();
            var template = MakeTemplate(length: 6f, width: 2.5f, maxSpeedFwd: 20f, maxAccel: 2.5f);

            new VehicleKinematicsTkbTranslator().Inject(repo, entity, template);

            Assert.True(repo.HasComponent<VehicleParams>(entity));
            var p = repo.GetComponent<VehicleParams>(entity);
            Assert.Equal(6f, p.Length);
            Assert.Equal(2.5f, p.Width);
            Assert.Equal(6f * 0.6f, p.WheelBase);
            Assert.Equal(20f, p.MaxSpeedFwd);
            Assert.Equal(2.5f, p.MaxAccel);
        }

        [Fact]
        public void Inject_WithAllComponentsRegistered_AddsVehicleState()
        {
            var repo = MakeWorldWithComponents();
            var entity = repo.CreateEntity();
            var template = MakeTemplate();

            new VehicleKinematicsTkbTranslator().Inject(repo, entity, template);

            Assert.True(repo.HasComponent<VehicleState>(entity));
            var s = repo.GetComponent<VehicleState>(entity);
            Assert.Equal(0f, s.Speed);
            Assert.Equal(0f, s.SteerAngle);
        }

        [Fact]
        public void Inject_WithAllComponentsRegistered_AddsNavState()
        {
            var repo = MakeWorldWithComponents();
            var entity = repo.CreateEntity();
            var template = MakeTemplate();

            new VehicleKinematicsTkbTranslator().Inject(repo, entity, template);

            Assert.True(repo.HasComponent<NavState>(entity));
            var nav = repo.GetComponent<NavState>(entity);
            Assert.Equal(KinematicsMode.None, nav.Mode);
        }

        [Fact]
        public void Inject_WithAllComponentsRegistered_AddsPhysicsCollider()
        {
            var repo = MakeWorldWithComponents();
            var entity = repo.CreateEntity();
            var template = MakeTemplate(length: 6f, width: 2.5f);

            new VehicleKinematicsTkbTranslator().Inject(repo, entity, template);

            Assert.True(repo.HasComponent<PhysicsCollider>(entity));
            var col = repo.GetComponent<PhysicsCollider>(entity);
            Assert.Equal(System.Math.Max(6f, 2.5f) / 2f, col.Radius);
        }

        [Fact]
        public void Inject_TemplateWithoutVehicleParametersDto_AddsNoComponents()
        {
            var repo = MakeWorldWithComponents();
            var entity = repo.CreateEntity();
            // Template with no VehicleParametersDto descriptor
            var template = new TkbTemplate("Empty", 1L);

            new VehicleKinematicsTkbTranslator().Inject(repo, entity, template);

            Assert.False(repo.HasComponent<VehicleParams>(entity));
            Assert.False(repo.HasComponent<VehicleState>(entity));
            Assert.False(repo.HasComponent<NavState>(entity));
            Assert.False(repo.HasComponent<PhysicsCollider>(entity));
        }

        [Fact]
        public void Inject_WorldWithoutVehicleParamsRegistered_DoesNotThrow()
        {
            var repo = new EntityRepository(); // no registered components
            var entity = repo.CreateEntity();
            var template = MakeTemplate();

            // Must complete without exception
            new VehicleKinematicsTkbTranslator().Inject(repo, entity, template);

            Assert.False(repo.HasComponent<VehicleParams>(entity));
        }

        // ── CE-113 ───────────────────────────────────────────────────────────────
        // Every test above passed against the defective five-field write, because each
        // asserts only fields the old code happened to set.  The kinematic envelope that
        // decides whether a vehicle can actually move -- AccelGain, MaxSteerAngle,
        // MaxSteerRate -- was left at zero and nothing looked at it.  These do.

        /// <summary>
        /// The defect: a tracked vehicle derived a car's (in fact, a zeroed) steering and
        /// acceleration envelope, so the brain planned a valid path and the muscle could
        /// neither accelerate along it nor turn.
        /// </summary>
        [Fact]
        public void A_tank_gets_a_driveable_envelope_not_a_zeroed_one()
        {
            var repo   = MakeWorldWithComponents();
            var entity = repo.CreateEntity();

            var t = new TkbTemplate("M1 Abrams", 100L);
            t.AddDescriptor(new VehicleParametersDto
            {
                Mass = 61_000f, Length = 7.93f, Width = 3.66f,
                MaxSpeedFwd = 20f, MaxSpeedRev = 12f, MaxAccel = 2.5f,
                TurnRate = 15f, VehicleClass = VehicleClass.Tank,
            });

            new VehicleKinematicsTkbTranslator().Inject(repo, entity, t);

            var p = repo.GetComponent<VehicleParams>(entity);

            // The class must survive: it is what selects the whole preset baseline.
            Assert.Equal(VehicleClass.Tank, p.Class);

            // From the preset -- no TKB descriptor carries these, and at zero the
            // vehicle cannot accelerate or steer at all.
            Assert.Equal(1.8f, p.AccelGain);
            Assert.Equal(0.8f, p.MaxSteerAngle);
            Assert.True(p.MaxDecel        > 0f, "MaxDecel came through as zero");
            Assert.True(p.MaxLatAccel     > 0f, "MaxLatAccel came through as zero");
            Assert.True(p.AvoidanceRadius > 0f, "AvoidanceRadius came through as zero");
            Assert.True(p.LookaheadTimeMax > 0f, "LookaheadTimeMax came through as zero");

            // Authored, and converted deg/s -> rad/s.
            Assert.Equal(15f * (System.MathF.PI / 180f), p.MaxSteerRate, precision: 5);

            // Authored dimensions still override the preset's.
            Assert.Equal(7.93f, p.Length);
            Assert.Equal(3.66f, p.Width);
            Assert.Equal(7.93f * 0.6f, p.WheelBase, precision: 5);
            Assert.Equal(20f, p.MaxSpeedFwd);
            Assert.Equal(12f, p.MaxSpeedRev);
            Assert.Equal(2.5f, p.MaxAccel);
        }

        /// <summary>
        /// The guard that keeps the defect from returning through the other producer.  A
        /// TKB zip authored against the older six-field schema has no TurnRate, so the
        /// field arrives as 0 -- which must mean "keep the preset", never "steer rate 0".
        /// A tank that accelerates but cannot change its steer angle drives straight
        /// forever, which is the same bug wearing a different face.
        /// </summary>
        [Fact]
        public void An_absent_TurnRate_keeps_the_presets_steer_rate_rather_than_zeroing_it()
        {
            var repo   = MakeWorldWithComponents();
            var entity = repo.CreateEntity();

            var t = new TkbTemplate("Tank, no TurnRate authored", 101L);
            t.AddDescriptor(new VehicleParametersDto
            {
                Length = 7f, Width = 3.5f, VehicleClass = VehicleClass.Tank,
                // TurnRate deliberately absent
            });

            new VehicleKinematicsTkbTranslator().Inject(repo, entity, t);

            var p = repo.GetComponent<VehicleParams>(entity);
            Assert.Equal(1.2f, p.MaxSteerRate);   // the Tank preset's value
            Assert.True(p.MaxSteerRate > 0f, "an absent TurnRate zeroed the steer rate");
        }

        /// <summary>
        /// VehicleClass is nullable so that "absent" is distinguishable from "authored as
        /// PersonalCar" (which is 0).  Absence must still yield a driveable vehicle --
        /// the wrong class, but never a zeroed envelope.
        /// </summary>
        [Fact]
        public void An_absent_VehicleClass_falls_back_to_a_real_preset_not_to_zeros()
        {
            var repo   = MakeWorldWithComponents();
            var entity = repo.CreateEntity();

            var t = new TkbTemplate("No class authored", 102L);
            t.AddDescriptor(new VehicleParametersDto { Length = 4.5f, Width = 2f });

            new VehicleKinematicsTkbTranslator().Inject(repo, entity, t);

            var p = repo.GetComponent<VehicleParams>(entity);
            Assert.Equal(VehicleClass.PersonalCar, p.Class);
            Assert.True(p.AccelGain     > 0f, "fallback produced AccelGain 0");
            Assert.True(p.MaxSteerAngle > 0f, "fallback produced MaxSteerAngle 0");
        }
    }
}
