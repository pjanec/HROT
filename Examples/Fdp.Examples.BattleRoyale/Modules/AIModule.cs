namespace Fdp.Examples.BattleRoyale.Modules;

using Fdp.Kernel;
using Fdp.Examples.BattleRoyale.Components;
using ModuleHost.Core;
using ModuleHost.Core.Abstractions;
using System.Numerics;

public class AIModule : IModule
{
    public string Name => "AI";
    public ExecutionPolicy Policy => ExecutionPolicy.SlowBackground(10);
    
    public void RegisterSystems(ISystemRegistry registry) { }

    public IEnumerable<Type> GetRequiredComponents()
    {
        yield return typeof(SimTransform);
        yield return typeof(AIState);
        yield return typeof(Health);
        yield return typeof(SimVelocity);
        yield return typeof(Damage); 
    }
    
    public void Tick(ISimulationView view, float deltaTime)
    {
        var cmd = view.GetCommandBuffer();
        
        // Get bots
        var bots = view.Query().With<SimTransform>().With<AIState>().Build();
        
        // Get all players (for targeting)
        var players = view.Query().With<SimTransform>().With<Health>().Build();
        
        int decisionsCount = 0;
        int projectilesSpawned = 0;
        
        foreach (var bot in bots)
        {
            ref readonly var tfBot = ref view.GetComponentRO<SimTransform>(bot);
            ref readonly var aiState = ref view.GetComponentRO<AIState>(bot);
            
            // Find nearest player
            Entity target = Entity.Null;
            float minDist = float.MaxValue;
            
            foreach (var player in players)
            {
                ref readonly var tfPlayer = ref view.GetComponentRO<SimTransform>(player);
                float distSq = (tfPlayer.Position - tfBot.Position).LengthSquared();
                
                if (distSq < minDist)
                {
                    minDist = distSq;
                    target = player;
                }
            }
            
            if (target != Entity.Null)
            {
                ref readonly var tfTarget = ref view.GetComponentRO<SimTransform>(target);
                
                // Move toward target
                var toTarget = tfTarget.Position - tfBot.Position;
                float dist = toTarget.Length();
                
                Vector3 n = Vector3.Zero;
                if (dist > 0.001f)
                {
                     n = Vector3.Normalize(toTarget);
                }
                
                if (dist > 0.1f)
                {
                    cmd.SetComponent(bot, new SimVelocity
                    {
                        Linear = n * 5.0f * aiState.AggressionLevel,
                        Angular = Vector3.Zero
                    });
                    
                    decisionsCount++;
                }
                
                // Shoot if close enough
                if (dist < 20.0f && aiState.AggressionLevel > 0.5f)
                {
                    // Spawn projectile
                    var proj = cmd.CreateEntity();
                    cmd.AddComponent(proj, new SimTransform { Position = tfBot.Position, Rotation = Quaternion.Identity });
                    cmd.AddComponent(proj, new SimVelocity { Linear = n * 30.0f, Angular = Vector3.Zero });
                    cmd.AddComponent(proj, new Damage { Amount = 10.0f });
                    
                    projectilesSpawned++;
                }
            }
        }
        
        if (view.Tick % 60 == 0)
            Console.WriteLine($"[AI @ T={view.Time:F1}s] {decisionsCount} decisions, {projectilesSpawned} shots");
    }
}
