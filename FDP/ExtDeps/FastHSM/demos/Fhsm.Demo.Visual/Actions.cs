using System;
using System.Numerics;
using Fhsm.Kernel;
using Fhsm.Kernel.Attributes;
using Fhsm.Kernel.Data;

namespace Fhsm.Demo.Visual
{
    public static unsafe class Actions
    {
        private static Random _random = new Random();
        
        // Agent lookup helper (set by BehaviorSystem)
        private static System.Collections.Generic.Dictionary<int, Agent>? _agentLookup;
        
        public static void SetAgentLookup(System.Collections.Generic.Dictionary<int, Agent> lookup)
        {
            _agentLookup = lookup;
        }
        
        private static Agent GetAgent(AgentContext* ctx)
        {
            if (_agentLookup == null || !_agentLookup.TryGetValue(ctx->AgentId, out var agent))
            {
                throw new Exception($"Agent {ctx->AgentId} not found in lookup");
            }
            return agent;
        }
        
        // ==================== PATROL ACTIONS ====================
        
        [HsmAction(Name = "FindPatrolPoint")]
        public static void FindPatrolPoint(void* instance, void* context, HsmCommandWriter* writer)
        {
            var ctx = (AgentContext*)context;
            var agent = GetAgent(ctx);
            var target = new Vector2(_random.Next(100, 1180), _random.Next(100, 620));
            
            ctx->TargetPosition = target;
            agent.TargetPosition = target;
            ctx->PatrolPointIndex = (ctx->PatrolPointIndex + 1) % 4;
            
            // Immediately fire PointSelected event
            var evt = new HsmEvent { EventId = MachineDefinitions.PointSelected };
            var inst = (HsmInstance64*)instance;
            fixed (HsmInstance64* instPtr = &agent.Instance)
            {
                HsmEventQueue.TryEnqueue(instPtr, 64, evt);
            }
        }
        
        [HsmAction(Name = "MoveToTarget")]
        public static void MoveToTarget(void* instance, void* context, HsmCommandWriter* writer)
        {
            var ctx = (AgentContext*)context;
            var agent = GetAgent(ctx);
            
            // Calculate distance
            var distance = Vector2.Distance(agent.Position, agent.TargetPosition);
            ctx->DistanceToTarget = distance;
            
            // If arrived, fire event
            if (distance < 10f)
            {
                var evt = new HsmEvent { EventId = MachineDefinitions.Arrived };
                fixed (HsmInstance64* instPtr = &agent.Instance)
                {
                    HsmEventQueue.TryEnqueue(instPtr, 64, evt);
                }
            }
        }
        
        // ==================== GATHER ACTIONS ====================
        
        [HsmAction(Name = "FindResource")]
        public static void FindResource(void* instance, void* context, HsmCommandWriter* writer)
        {
            var ctx = (AgentContext*)context;
            var agent = GetAgent(ctx);
            var resourcePos = new Vector2(_random.Next(100, 1180), _random.Next(100, 620));
            
            ctx->ResourcePosition = resourcePos;
            ctx->TargetPosition = resourcePos;
            agent.TargetPosition = resourcePos;
            
            var evt = new HsmEvent { EventId = MachineDefinitions.ResourceFound };
            fixed (HsmInstance64* instPtr = &agent.Instance)
            {
                HsmEventQueue.TryEnqueue(instPtr, 64, evt);
            }
        }
        
        [HsmAction(Name = "MoveToResource")]
        public static void MoveToResource(void* instance, void* context, HsmCommandWriter* writer)
        {
            var ctx = (AgentContext*)context;
            var agent = GetAgent(ctx);
            var distance = Vector2.Distance(agent.Position, ctx->ResourcePosition);
            ctx->DistanceToTarget = distance;
            
            if (distance < 10f)
            {
                var evt = new HsmEvent { EventId = MachineDefinitions.Arrived };
                fixed (HsmInstance64* instPtr = &agent.Instance)
                {
                    HsmEventQueue.TryEnqueue(instPtr, 64, evt);
                }
            }
        }
        
        [HsmAction(Name = "Gather")]
        public static void Gather(void* instance, void* context, HsmCommandWriter* writer)
        {
            var ctx = (AgentContext*)context;
            var agent = GetAgent(ctx);
            ctx->ResourceCount++;
            
            var evt = new HsmEvent { EventId = MachineDefinitions.ResourceCollected };
            fixed (HsmInstance64* instPtr = &agent.Instance)
            {
                HsmEventQueue.TryEnqueue(instPtr, 64, evt);
            }
        }
        
        [HsmAction(Name = "MoveToBase")]
        public static void MoveToBase(void* instance, void* context, HsmCommandWriter* writer)
        {
            var ctx = (AgentContext*)context;
            var agent = GetAgent(ctx);
            var basePos = new Vector2(50, 50);
            ctx->BasePosition = basePos;
            agent.TargetPosition = basePos;
            
            var distance = Vector2.Distance(agent.Position, basePos);
            ctx->DistanceToBase = distance;
            
            if (distance < 20f)
            {
                var evt = new HsmEvent { EventId = MachineDefinitions.Arrived };
                fixed (HsmInstance64* instPtr = &agent.Instance)
                {
                    HsmEventQueue.TryEnqueue(instPtr, 64, evt);
                }
            }
        }
        
        [HsmAction(Name = "DepositResources")]
        public static void DepositResources(void* instance, void* context, HsmCommandWriter* writer)
        {
            var ctx = (AgentContext*)context;
            var agent = GetAgent(ctx);
            ctx->ResourceCount = 0;
            
            var evt = new HsmEvent { EventId = MachineDefinitions.ResourcesDeposited };
            fixed (HsmInstance64* instPtr = &agent.Instance)
            {
                HsmEventQueue.TryEnqueue(instPtr, 64, evt);
            }
        }
        
        // ==================== COMBAT ACTIONS ====================
        
        [HsmAction(Name = "FindRandomPoint")]
        public static void FindRandomPoint(void* instance, void* context, HsmCommandWriter* writer)
        {
            var ctx = (AgentContext*)context;
            var agent = GetAgent(ctx);
            var target = new Vector2(_random.Next(100, 1180), _random.Next(100, 620));
            
            ctx->TargetPosition = target;
            agent.TargetPosition = target;
            
            var evt = new HsmEvent { EventId = MachineDefinitions.PointSelected };
            fixed (HsmInstance64* instPtr = &agent.Instance)
            {
                HsmEventQueue.TryEnqueue(instPtr, 64, evt);
            }
        }
        
        [HsmAction(Name = "ScanForEnemy")]
        public static void ScanForEnemy(void* instance, void* context, HsmCommandWriter* writer)
        {
            // This is handled by BehaviorSystem.UpdateCombatScanning()
            // which injects EnemyDetected events
        }
        
        [HsmAction(Name = "ChaseEnemy")]
        public static void ChaseEnemy(void* instance, void* context, HsmCommandWriter* writer)
        {
            var ctx = (AgentContext*)context;
            var agent = GetAgent(ctx);
            
            if (!ctx->HasTarget)
            {
                // Lost target, fire EnemyLost
                var lostEvt = new HsmEvent { EventId = MachineDefinitions.EnemyLost };
                fixed (HsmInstance64* instPtr = &agent.Instance)
                {
                    HsmEventQueue.TryEnqueue(instPtr, 64, lostEvt);
                }
                return;
            }
            
            // Update agent target position
            agent.TargetPosition = ctx->TargetPosition;
            
            // Check if in attack range
            var distance = Vector2.Distance(agent.Position, ctx->TargetPosition);
            if (distance < 30f)
            {
                var evt = new HsmEvent { EventId = MachineDefinitions.Arrived };
                fixed (HsmInstance64* instPtr = &agent.Instance)
                {
                    HsmEventQueue.TryEnqueue(instPtr, 64, evt);
                }
            }
        }
        
        [HsmAction(Name = "Attack")]
        public static void Attack(void* instance, void* context, HsmCommandWriter* writer)
        {
            var ctx = (AgentContext*)context;
            var agent = GetAgent(ctx);
            agent.AttackFlashTimer = 0.3f;
            
            // Lose target after attack
            ctx->HasTarget = false;
            ctx->TargetAgentId = -1;
            
            var evt = new HsmEvent { EventId = MachineDefinitions.EnemyLost };
            fixed (HsmInstance64* instPtr = &agent.Instance)
            {
                HsmEventQueue.TryEnqueue(instPtr, 64, evt);
            }
        }
        
        // ==================== GUARDS ====================
        
        [HsmGuard(Name = "HasTarget")]
        public static bool HasTarget(void* instance, void* context, ushort eventId)
        {
            var ctx = (AgentContext*)context;
            return ctx->HasTarget;
        }
        
        [HsmGuard(Name = "IsAtTarget")]
        public static bool IsAtTarget(void* instance, void* context, ushort eventId)
        {
            var ctx = (AgentContext*)context;
            return ctx->DistanceToTarget < 10f;
        }
        
        [HsmGuard(Name = "IsAtBase")]
        public static bool IsAtBase(void* instance, void* context, ushort eventId)
        {
            var ctx = (AgentContext*)context;
            return ctx->DistanceToBase < 20f;
        }
    }
}
