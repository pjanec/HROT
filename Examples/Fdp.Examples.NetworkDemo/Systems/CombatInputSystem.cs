using System;
using System.Numerics;
using Fdp.Kernel;
using Fdp.Examples.NetworkDemo.Components;
using Fdp.Examples.NetworkDemo.Configuration;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Extensions;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Examples.NetworkDemo.Systems
{
    [UpdateInPhase(SystemPhase.Input)]
    public class CombatInputSystem : IEcsModuleSystem
    {
        private readonly int _localNodeId;
        private readonly IEventBus _eventBus;
        private const float TANK_SPEED = 10.0f;
        private const float TANK_ROT_SPEED = 2.0f;

        public CombatInputSystem(int localNodeId, IEventBus eventBus)
        {
            _localNodeId = localNodeId;
            _eventBus = eventBus;
        }

        public void Execute(ISimulationView view, float dt)
        {
            try
            {
                if (!Console.KeyAvailable) return;
                
                var key = Console.ReadKey(true).Key;
                HandleInput(view, dt, key);
            }
            catch (InvalidOperationException) { /* Headless env */ }
        }

        private void HandleInput(ISimulationView view, float dt, ConsoleKey key)
        {
            // Find local tank
            var query = view.Query()
                .With<SimTransform>()
                .With<NetworkAuthority>() 
                .Build();

            var cmd = view.GetCommandBuffer();

            foreach (var entity in query)
            {
                // Verify ownership
                var auth = view.GetComponentRO<NetworkAuthority>(entity);
                if (auth.LocalNodeId != _localNodeId || auth.PrimaryOwnerId != _localNodeId)
                    continue;

                var tf = view.GetComponentRO<SimTransform>(entity);
                
                Vector3 move = Vector3.Zero;
                float rot = 0;

                switch (key)
                {
                    case ConsoleKey.W: move = new Vector3(0, 1, 0); break;
                    case ConsoleKey.S: move = new Vector3(0, -1, 0); break;
                    case ConsoleKey.A: rot = 1; break;
                    case ConsoleKey.D: rot = -1; break;
                    case ConsoleKey.Spacebar:
                        break;
                }

                if (move != Vector3.Zero || rot != 0)
                {
                    // Update transform
                    // Simple rotation around Z
                    var currentYaw = GetYaw(tf.Rotation);
                    var newYaw = currentYaw + rot * TANK_ROT_SPEED * dt;
                    var newRot = SimMath.FromYaw(newYaw);
                    
                    var forward = Vector3.Transform(Vector3.UnitY, newRot);
                    var newPos = tf.Position + (forward * move.Y * TANK_SPEED * dt);
                    
                    cmd.SetComponent(entity, new SimTransform {
                        Position = newPos,
                        Rotation = newRot
                    });
                }
            }
        }

        private float GetYaw(Quaternion q)
        {
            // Simple extraction for Z-up, Y-Forward
            // Rotate UnitY by q, find angle in XY plane
            Vector3 fwd = Vector3.Transform(Vector3.UnitY, q);
            return MathF.Atan2(fwd.X, fwd.Y);
        }
    }
}
