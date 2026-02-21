using Fdp.Examples.NetworkDemo.Components;
using Fdp.Examples.NetworkDemo.Descriptors;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Replication;
using FDP.Toolkit.Replication.Components;

namespace Fdp.Examples.NetworkDemo.Configuration
{
    public static class TankTemplate
    {
        public static void Register(ITkbDatabase tkb)
        {
            var tank = new TkbTemplate("CommandTank", 100);
            
            // Core components
            tank.AddComponent(new DemoPosition());
            // TurretState removed from root - moved to child
            tank.AddComponent(new Health { Value = 100, MaxValue = 100 });
            
            // Network components
            tank.AddComponent(new NetworkIdentity());
            tank.AddComponent(new NetworkPosition());
            tank.AddComponent(new NetworkVelocity());
            tank.AddComponent(new ModuleHost.Core.Network.NetworkOwnership());
            
            // Define child: Turret (Instance 1)
            tank.ChildBlueprints.Add(new ChildBlueprintDefinition 
            { 
                InstanceId = 1, 
                ChildTkbType = 101 
            });

            // [NEW] 1. Add Lifecycle Component
            // Default state is Constructing. 
            // RequiredModulesMask will be populated by ELM during BeginConstruction.
            tank.AddComponent(new LifecycleDescriptor 
            { 
                State = EntityState.Constructing,
                CreatedTime = 0,
                RequiredModulesMask = 0, 
                AckedModulesMask = 0
            });

            // HARD REQUIREMENT: EntityMaster (ordinal 1) — ghost stays unspawned until this arrives
            tank.MandatoryDescriptors.Add(new MandatoryDescriptor {
                PackedKey = PackedKey.Create(DemoDescriptors.Master, 0),
                IsHard = true
            });
            
            // SOFT REQUIREMENT: Physics / Position (ordinal 2) — spawn after timeout even if absent
            tank.MandatoryDescriptors.Add(new MandatoryDescriptor {
                PackedKey = PackedKey.Create(DemoDescriptors.Physics, 0),
                IsHard = false,
                SoftTimeoutFrames = 60 // 1 second at 60 Hz
            });
            
            tkb.Register(tank);

            // Turret template (new)
            var turret = new TkbTemplate("TankTurret", 101);
            turret.AddComponent(new TurretState());
            turret.AddComponent(new WeaponState());
            tkb.Register(turret);
        }
    }
}
