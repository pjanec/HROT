namespace Fdp.Examples.BattleRoyale.Modules;

using Fdp.Kernel;
using Fdp.Examples.BattleRoyale.Components;
using ModuleHost.Core;
using ModuleHost.Core.Abstractions;

public class WorldManagerModule : IModule
{
    public string Name => "WorldManager";
    public ExecutionPolicy Policy => ExecutionPolicy.SlowBackground(1);
    
    public void RegisterSystems(ISystemRegistry registry) { }

    public void Tick(ISimulationView view, float deltaTime)
    {
        var cmd = view.GetCommandBuffer();
        
        // Find safe zone
        var safeZoneQuery = view.Query().With<SafeZone>().Build();
        foreach (var zone in safeZoneQuery)
        {
            ref readonly var safeZone = ref view.GetComponentRO<SafeZone>(zone);
            
            // Shrink 1% per second
            float newRadius = safeZone.Radius * 0.99f;
            cmd.SetComponent(zone, new SafeZone { Radius = newRadius });
            
            Console.WriteLine($"[WorldManager @ T={view.Time:F1}s] Safe zone: {newRadius:F1} units");
        }
        
        // Spawn random items (30% chance per second)
        if (Random.Shared.NextDouble() < 0.3)
        {
            var item = cmd.CreateEntity();
            cmd.AddComponent(item, new SimTransform
            {
                Position = new System.Numerics.Vector3(
                    Random.Shared.Next(0, 1000),
                    Random.Shared.Next(0, 1000),
                    0),
                Rotation = System.Numerics.Quaternion.Identity
            });
            cmd.AddComponent(item, new ItemType
            {
                Type = (ItemTypeEnum)Random.Shared.Next(0, 3)
            });
        }
    }
}
