using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;

namespace Fhsm.Demo.Visual
{
    public class Agent
    {
        public int Id { get; set; }
        public Vector2 Position { get; set; }
        public Vector2 Velocity { get; set; }
        public float Rotation { get; set; }
        public Color Color { get; set; }
        
        // State machine
        public string MachineName;
        public unsafe HsmInstance64 Instance;
        public AgentContext Context;
        
        // AI state
        public Vector2 TargetPosition;
        public float Speed = 50f;
        public AgentRole Role;
        
        // Visual state
        public ushort[] ActiveStates = Array.Empty<ushort>();
        public List<TransitionRecord> RecentTransitions = new();
        public float AttackFlashTimer;  // Timer for attack visual effect
        
        public Agent(int id, Vector2 position, string machineName, AgentRole role)
        {
            Id = id;
            Position = position;
            MachineName = machineName;
            Role = role;
            Context = new AgentContext { AgentId = id };
            // Instance will be initialized by BehaviorSystem
            
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
    
    public struct AgentContext
    {
        // Patrol
        public int PatrolPointIndex;
        public float LastPatrolTime;
        
        // Gather
        public int ResourceCount;
        public Vector2 ResourcePosition;
        public Vector2 BasePosition;
        
        // Combat
        public bool HasTarget;
        public int TargetAgentId;
        public Vector2 TargetPosition;
        
        // Shared
        public float DistanceToTarget;
        public float DistanceToBase;
        public float Time;
        public float DeltaTime;
        
        // Agent ID for lookup (no managed reference)
        public int AgentId;
    }
    
    public struct TransitionRecord
    {
        public ushort FromState;
        public ushort ToState;
        public ushort EventId;
        public float Timestamp;
    }
}
