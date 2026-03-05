using System;
using System.Numerics;
using Fdp.Kernel;
using Fdp.Examples.NetworkDemo; // Correct namespace
using Fdp.Examples.NetworkDemo.Components; // For TurretState etc.
using FDP.Toolkit.Replication.Components;
// using FDP.Toolkit.CarKinem.Components; // Replaced by Fdp.Kernel (SimTransform)
using ModuleHost.Core.Network;

namespace Fdp.Examples.NetworkDemo.Tests.Extensions
{
    public static class NetworkDemoAppExtensions
    {
        public static Entity SpawnTank(this NetworkDemoApp app)
        {
            // Note: app.Tkb usage needs to be robust if API changed, but here we assume it works.
            // app.Tkb.TryGetByName("CommandTank", out var template)
            // Assuming "CommandTank" creates an entity with SimTransform.
            
            // For now, let's just assume we are patching up the components manually.
            var entity = app.World.CreateEntity();

            // Identity
            var netId = (long)app.InstanceId * 1000 + entity.Index; 
            app.World.SetComponent(entity, new NetworkIdentity { Value = netId });

            // Ownership
            app.World.AddComponent(entity, new NetworkOwnership 
            { 
                PrimaryOwnerId = app.LocalNodeId, 
                LocalNodeId = app.LocalNodeId 
            });
            
            app.World.AddComponent(entity, new NetworkAuthority(app.LocalNodeId, app.LocalNodeId));

            // Add TurretState to Root for Test Compatibility (Tests assume simplistic tank)
            app.World.AddComponent(entity, new TurretState());
            app.World.SetAuthority<TurretState>(entity, true);

            // Ensure Movement components are authoritative for tests
            if (!app.World.HasComponent<SimTransform>(entity)) app.World.AddComponent(entity, new SimTransform());
            app.World.SetAuthority<SimTransform>(entity, true);

            if (!app.World.HasComponent<SimVelocity>(entity)) app.World.AddComponent(entity, new SimVelocity());
            app.World.SetAuthority<SimVelocity>(entity, true);

            // Spawn Request — DisType 100 maps to TankTemplate (TkbType 100).
            // TkbType must be set so EntityMasterTranslator can carry it to peer nodes
            // and GhostPromotionSystem can look up the template on the receiving side.
            app.World.AddComponent(entity, new NetworkSpawnRequest 
            { 
                DisType = 100,
                TkbType = 100,
                OwnerId = (ulong)app.LocalNodeId 
            });
            
            // Initial Position
            app.World.SetComponent(entity, new SimTransform 
            { 
                Position = new Vector3(
                        Random.Shared.Next(-50, 50),
                        Random.Shared.Next(-50, 50),
                        0
                    )
                });
                
                app.World.SetComponent(entity, new NetworkTransform());
                
                app.World.AddComponent(entity, new EntityType { Name = "Tank", TypeId = 1 });
                
                app.EntityMap.Register(netId, entity);

                return entity;
            }
            // Fallback manually if template fails or just return what we have? 
            // Since we manually created entity above, we should just return it.
            // But wait, the 'if' was removed so 'return entity' is unconditional.
            // The closing brace below was for the 'if'.
            
            // Actually, let me remove the closing brace and the throw.
            // But I cannot see the closing brace in the oldString context easily.
            // Let's just fix the end of the method.


        public static long GetNetworkId(this NetworkDemoApp app, Entity entity)
        {
            if (app.World.HasComponent<NetworkIdentity>(entity))
            {
                return app.World.GetComponent<NetworkIdentity>(entity).Value;
            }
            throw new Exception($"Entity {entity} has no NetworkIdentity");
        }

        public static Entity GetEntityByNetId(this NetworkDemoApp app, long netId)
        {
            if (app.TryGetEntityByNetId(netId, out var entity))
                return entity;
            return Entity.Null;
        }

        public static bool TryGetEntityByNetId(this NetworkDemoApp app, long netId, out Entity entity)
        {
             var query = app.World.Query().With<NetworkIdentity>().Build();
             foreach(var e in query)
             {
                 if (app.World.GetComponent<NetworkIdentity>(e).Value == netId)
                 {
                     entity = e;
                     return true;
                 }
             }
             entity = Entity.Null;
             return false;
        }
    }
}
