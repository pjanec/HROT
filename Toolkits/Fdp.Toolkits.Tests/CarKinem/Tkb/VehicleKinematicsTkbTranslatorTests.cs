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
    }
}
