using System.Numerics;
using CarKinem.Commands;
using CarKinem.Core;
using CarKinem.Systems;
using Fdp.Core;
using Xunit;

namespace CarKinem.Tests.Commands
{
    public class SpawnCommandTests
    {
        [Fact]
        public void SpawnCommand_CreatesVehicleWithComponents()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<VehicleState>();
            repo.RegisterComponent<VehicleParams>();
            repo.RegisterComponent<NavState>();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();
            repo.RegisterEvent<CmdSpawnVehicle>();
            
            var system = new VehicleCommandSystem();
            system.Create(repo);
            
            // Pre-allocate entity
            var entity = repo.CreateEntity();
            
            // Issue spawn command
            repo.Bus.Publish(new CmdSpawnVehicle
            {
                Entity = entity,
                Position = new Vector2(100, 50),
                Heading = new Vector2(1, 0), // East
                Class = VehicleClass.PersonalCar
            });
            
            // Wait, does CmdSpawnVehicle change? No, it's just a command.
            // But VehicleCommandSystem processes it and adds components.
            
            // Process command
            repo.Bus.SwapBuffers();
            system.Run();
            
            // Verify components
            Assert.True(repo.HasComponent<VehicleState>(entity));
            Assert.True(repo.HasComponent<VehicleParams>(entity));
            Assert.True(repo.HasComponent<NavState>(entity));
            Assert.True(repo.HasComponent<SimTransform>(entity));
            Assert.True(repo.HasComponent<SimVelocity>(entity));
            
            var tf = repo.GetComponent<SimTransform>(entity);
            Assert.Equal(new Vector3(100, 50, 0), tf.Position);
            
            // Heading was (1, 0) East.
            // Check if rotation is correct approximately.
            // Convention: Forward is UnitX.
            Vector3 fwd = Vector3.Transform(Vector3.UnitX, tf.Rotation);
            // Expected East (1, 0, 0)
            Assert.Equal(1f, fwd.X, precision: 3);
            Assert.Equal(0f, fwd.Y, precision: 3);
            
            repo.Dispose();
        }
        
        [Fact]
        public void SpawnCommand_IgnoresDeadEntity()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<VehicleState>();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<SimVelocity>();
            repo.RegisterEvent<CmdSpawnVehicle>();
            
            var system = new VehicleCommandSystem();
            system.Create(repo);
            
            // Create and destroy entity
            var entity = repo.CreateEntity();
            repo.DestroyEntity(entity);
            
            // Try to spawn on dead entity
            repo.Bus.Publish(new CmdSpawnVehicle
            {
                Entity = entity,
                Position = Vector2.Zero,
                Heading = new Vector2(1, 0),
                Class = VehicleClass.PersonalCar
            });
            
            repo.Bus.SwapBuffers();
            system.Run();
            
            // Should not crash, command ignored
            Assert.False(repo.IsAlive(entity));
            
            repo.Dispose();
        }
    }
}
