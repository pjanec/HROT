using System;
using System.Numerics;
using Raylib_cs;
using Fbt;
using Fbt.Runtime;

namespace Fbt.Demo.Visual
{
    public class Agent
    {
        public int Id { get; set; }
        public Vector2 Position { get; set; }
        public Vector2 Velocity { get; set; }
        public float Rotation { get; set; }
        public Color Color { get; set; }
        
        // Behavior tree
        public string TreeName;
        public AgentBlackboard Blackboard = new AgentBlackboard(); 
        public BehaviorTreeState State;
        
        // AI state
        public Vector2 TargetPosition;
        public float Speed = 50f;
        public AgentRole Role;
        
        // Visual state
        public TreeExecutionHighlight? CurrentNode;
        public float AttackFlashTimer;  // Timer for attack visual effect
        
        public Agent(int id, Vector2 position, string treeName, AgentRole role)
        {
            Id = id;
            Position = position;
            TreeName = treeName;
            Role = role;
            // Blackboard is struct, already initialized
            State = new BehaviorTreeState();
            
            Color = role switch
            {
                AgentRole.Patrol => Color.Blue,
                AgentRole.Gather => Color.Green,
                AgentRole.Combat => Color.Red,
                _ => Color.White
            };
        }
        
        public void UpdateMovement(float dt)
        {
            // Move towards target
            var diff = TargetPosition - Position;
            if (diff.LengthSquared() > 100.0f) // 10px threshold (10*10=100) - allows getting close enough to attack
            {
                 var direction = Vector2.Normalize(diff);
                 // Check for NaN if diff was zero (handled by length check but good to be safe)
                 if (!float.IsNaN(direction.X) && !float.IsNaN(direction.Y))
                 {
                    Velocity = direction * Speed;
                    Position += Velocity * dt;
                    Rotation = MathF.Atan2(direction.Y, direction.X);
                 }
            }
            else
            {
                Velocity = Vector2.Zero;
            }
        }
    }
    
    public enum AgentRole
    {
        Patrol,
        Gather,
        Combat
    }
    
    public struct AgentBlackboard
    {
        public int PatrolPointIndex;
        public float LastPatrolTime;
        public int ResourceCount;
        public bool HasTarget;
        public int TargetAgentId;  // Which agent we're chasing (-1 if none)
    }
    
    public struct TreeExecutionHighlight
    {
        public int NodeIndex;
        public NodeStatus Status;
        public float Timestamp;
    }
}
