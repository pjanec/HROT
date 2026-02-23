using System.Numerics;
using Fdp.Kernel;
using Fdp.Examples.BattleRoyale.Components;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Network;

namespace Fdp.Examples.BattleRoyale.Systems;

public static class EntityFactory
{
    /// <summary>
    /// Registers all component types with the repository.
    /// Must be called before spawning entities.
    /// </summary>
    public static void RegisterAllComponents(EntityRepository world)
    {
        // Unmanaged components
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<SimVelocity>();
        world.RegisterComponent<Health>();
        world.RegisterComponent<AIState>();
        world.RegisterComponent<Inventory>();
        world.RegisterComponent<NetworkState>();
        world.RegisterComponent<ItemType>();
        world.RegisterComponent<Damage>();
        world.RegisterComponent<SafeZone>();
        
        // Network components
        world.RegisterComponent<NetworkOwnership>();
        world.RegisterComponent<NetworkPosition>();
        world.RegisterComponent<NetworkIdentity>();
        world.RegisterComponent<NetworkSpawnRequest>();
        
        // Managed component
        world.RegisterComponent<PlayerInfo>();
        
        world.RegisterComponent<Team>();
    }
    
    /// <summary>
    /// Spawn players at random positions.
    /// </summary>
    public static void SpawnPlayers(EntityRepository world, int count = 100)
    {
        for (int i = 0; i < count; i++)
        {
            var entity = world.CreateEntity();
            
            // Position
            world.AddComponent(entity, new SimTransform
            {
                Position = new Vector3(Random.Shared.NextSingle() * 1000f, Random.Shared.NextSingle() * 1000f, 0f),
                Rotation = Quaternion.Identity
            });
            
            // Velocity (initially at rest)
            world.AddComponent(entity, new SimVelocity
            {
                Linear = Vector3.Zero,
                Angular = Vector3.Zero
            });
            
            // Health
            world.AddComponent(entity, new Health
            {
                Current = 100f,
                Max = 100f
            });

            // Network Identity
            world.AddComponent(entity, new NetworkIdentity { Value = 0 });
            world.AddComponent(entity, new NetworkSpawnRequest { DisType = 1, OwnerId = 0 });
            
            // Inventory (starting equipment)
            world.AddComponent(entity, new Inventory
            {
                Weapon = 1,
                Ammo = 30,
                HealthKits = 2
            });
            
            // Network state
            world.AddComponent(entity, new NetworkState
            {
                LastUpdateTick = 0,
                DirtyFlags = 0xFF  // All dirty initially
            });
            
            // Player info (managed)
            var playerName = $"Player_{i + 1}";
            world.AddComponent(entity, new PlayerInfo(playerName, Guid.NewGuid()));

            // Team (managed) - 50% of players
            if (i % 2 == 0)
            {
                world.AddComponent(entity, new Team(
                    i < 50 ? "Alpha" : "Bravo",
                    i < 50 ? 1 : 2,
                    new[] { playerName }
                ));
            }
        }
    }
    
    /// <summary>
    /// Spawn AI bots at random positions.
    /// </summary>
    public static void SpawnBots(EntityRepository world, int count = 50)
    {
        for (int i = 0; i < count; i++)
        {
            var entity = world.CreateEntity();
            
            // Position
            world.AddComponent(entity, new SimTransform
            {
                Position = new Vector3(Random.Shared.NextSingle() * 1000f, Random.Shared.NextSingle() * 1000f, 0f),
                Rotation = Quaternion.Identity
            });
            
            // Velocity (initially at rest)
            world.AddComponent(entity, new SimVelocity
            {
                Linear = Vector3.Zero,
                Angular = Vector3.Zero
            });
            
            // Health
            world.AddComponent(entity, new Health
            {
                Current = 80f,  // Bots have slightly less health
                Max = 80f
            });
            
            // AI State
            world.AddComponent(entity, new AIState
            {
                TargetEntity = Entity.Null,
                AggressionLevel = Random.Shared.NextSingle()  // Random aggression 0-1
            });
        }
    }
    
    /// <summary>
    /// Spawn items (health kits, weapons, ammo) at random positions.
    /// </summary>
    public static void SpawnItems(EntityRepository world, int count = 100)
    {
        for (int i = 0; i < count; i++)
        {
            var entity = world.CreateEntity();
            
            // Position
            world.AddComponent(entity, new SimTransform
            {
                Position = new Vector3(Random.Shared.NextSingle() * 1000f, Random.Shared.NextSingle() * 1000f, 0f),
                Rotation = Quaternion.Identity
            });
            
            // Random item type
            var itemType = (ItemTypeEnum)(i % 3);  // Distribute evenly
            world.AddComponent(entity, new ItemType
            {
                Type = itemType
            });
        }
    }
    
    /// <summary>
    /// Create the safe zone entity.
    /// </summary>
    public static Entity CreateSafeZone(EntityRepository world)
    {
        var entity = world.CreateEntity();
        
        // Center of the map
        world.AddComponent(entity, new SimTransform
        {
            Position = new Vector3(500f, 500f, 0f),
            Rotation = Quaternion.Identity
        });
        
        // Initial safe zone radius
        world.AddComponent(entity, new SafeZone
        {
            Radius = 800f
        });
        
        return entity;
    }
    
    /// <summary>
    /// Create a projectile fired from a position in a direction.
    /// </summary>
    public static Entity CreateProjectile(
        EntityRepository world,
        SimTransform transform,
        SimVelocity velocity,
        float damage)
    {
        var entity = world.CreateEntity();
        
        world.AddComponent(entity, transform);
        world.AddComponent(entity, velocity);
        world.AddComponent(entity, new Damage
        {
            Amount = damage
        });
        
        return entity;
    }
}
