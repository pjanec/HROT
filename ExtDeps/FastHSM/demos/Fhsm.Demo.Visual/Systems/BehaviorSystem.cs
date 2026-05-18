using System;
using System.Numerics;
using System.Collections.Generic;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;

namespace Fhsm.Demo.Visual
{
    public unsafe class BehaviorSystem
    {
        private Dictionary<string, HsmDefinitionBlob> _machines;
        private Dictionary<string, MachineMetadata> _machineMetadata;
        private List<Agent>? _currentAgents;
        private Random _random = new Random();
        
        public BehaviorSystem()
        {
            _machines = new Dictionary<string, HsmDefinitionBlob>();
            _machineMetadata = new Dictionary<string, MachineMetadata>();
            
            // Create state machines
            var patrol = MachineDefinitions.CreatePatrolMachine();
            _machines["patrol"] = patrol.Item1;
            _machineMetadata["patrol"] = patrol.Item2;
            
            var gather = MachineDefinitions.CreateGatherMachine();
            _machines["gather"] = gather.Item1;
            _machineMetadata["gather"] = gather.Item2;
            
            var combat = MachineDefinitions.CreateCombatMachine();
            _machines["combat"] = combat.Item1;
            _machineMetadata["combat"] = combat.Item2;
        }
        
        public void InitializeAgent(Agent agent)
        {
            if (!_machines.TryGetValue(agent.MachineName, out var blob))
            {
                throw new Exception($"Machine '{agent.MachineName}' not found");
            }
            
            // Initialize instance
            fixed (HsmInstance64* inst = &agent.Instance)
            {
                HsmInstanceManager.Initialize(inst, blob);
            }
            
            // Trigger initial entry
            HsmKernel.Trigger(ref agent.Instance);
        }
        
        public void Update(List<Agent> agents, float time, float dt)
        {
            _currentAgents = agents;
            
            // Setup agent lookup for actions
            var agentLookup = new Dictionary<int, Agent>();
            foreach (var agent in agents)
            {
                agentLookup[agent.Id] = agent;
            }
            Actions.SetAgentLookup(agentLookup);
            
            // Update combat agent scanning
            UpdateCombatScanning(agents);
            
            // Update all agents
            foreach (var agent in agents)
            {
                if (!_machines.TryGetValue(agent.MachineName, out var blob))
                    continue;
                
                // Update attack flash timer
                if (agent.AttackFlashTimer > 0)
                    agent.AttackFlashTimer -= dt;
                
                // Prepare context
                agent.Context.Time = time;
                agent.Context.DeltaTime = dt;
                agent.Context.AgentId = agent.Id;
                
                // Tick HSM
                fixed (AgentContext* ctx = &agent.Context)
                {
                    HsmKernel.Update(blob, ref agent.Instance, agent.Context, dt);
                }
                
                // Update active states for visualization
                UpdateActiveStates(agent, blob);
                
                // Fire periodic UpdateEvent for combat agents
                if (agent.Role == AgentRole.Combat && time - agent.Context.LastPatrolTime > 3.0f)
                {
                    agent.Context.LastPatrolTime = time;
                    var evt = new HsmEvent { EventId = MachineDefinitions.UpdateEvent };
                    fixed (HsmInstance64* inst = &agent.Instance)
                    {
                        HsmEventQueue.TryEnqueue(inst, 64, evt);
                    }
                }
                
                // Fire TimerExpired for patrol/gather agents
                if (agent.Role == AgentRole.Patrol && time - agent.Context.LastPatrolTime > 2.0f)
                {
                    agent.Context.LastPatrolTime = time;
                    var evt = new HsmEvent { EventId = MachineDefinitions.TimerExpired };
                    fixed (HsmInstance64* inst = &agent.Instance)
                    {
                        HsmEventQueue.TryEnqueue(inst, 64, evt);
                    }
                }
            }
        }
        
        private void UpdateCombatScanning(List<Agent> agents)
        {
            foreach (var agent in agents)
            {
                if (agent.Role != AgentRole.Combat) continue;
                if (agent.Context.HasTarget) continue;
                
                // Find closest non-combat agent
                float closestDistSq = 300f * 300f;
                Agent? target = null;
                
                foreach (var other in agents)
                {
                    if (other == agent) continue;
                    if (other.Role == AgentRole.Combat) continue;
                    
                    float dSq = Vector2.DistanceSquared(agent.Position, other.Position);
                    if (dSq < closestDistSq)
                    {
                        closestDistSq = dSq;
                        target = other;
                    }
                }
                
                if (target != null)
                {
                    agent.Context.HasTarget = true;
                    agent.Context.TargetAgentId = target.Id;
                    agent.Context.TargetPosition = target.Position;
                    agent.TargetPosition = target.Position;
                    
                    // Fire EnemyDetected event
                    var evt = new HsmEvent 
                    { 
                        EventId = MachineDefinitions.EnemyDetected,
                        Priority = EventPriority.Interrupt 
                    };
                    
                    fixed (HsmInstance64* inst = &agent.Instance)
                    {
                        HsmEventQueue.TryEnqueue(inst, 64, evt);
                    }
                }
                else
                {
                    // Random chance to investigate
                    if (_random.NextDouble() < 0.01) // 1% per frame
                    {
                        agent.Context.HasTarget = true;
                        agent.Context.TargetAgentId = -1;
                        agent.Context.TargetPosition = new Vector2(_random.Next(100, 1180), _random.Next(100, 620));
                        
                        var evt = new HsmEvent { EventId = MachineDefinitions.EnemyDetected };
                        fixed (HsmInstance64* inst = &agent.Instance)
                        {
                            HsmEventQueue.TryEnqueue(inst, 64, evt);
                        }
                    }
                }
            }
        }
        
        private void UpdateActiveStates(Agent agent, HsmDefinitionBlob blob)
        {
            // Read active leaf states from instance
            fixed (HsmInstance64* inst = &agent.Instance)
            {
                byte* ptr = (byte*)inst;
                
                // Active leaf IDs are stored after the header (16 bytes)
                // Offset: 16 (header) + potential padding
                // For HsmInstance64: header (16B) + activeLeafs (regionCount * 2B)
                int regionCount = blob.Header.RegionCount;
                if (regionCount == 0) regionCount = 1; // At least one region
                
                ushort* activeLeafs = (ushort*)(ptr + 16);
                
                agent.ActiveStates = new ushort[regionCount];
                for (int i = 0; i < regionCount; i++)
                {
                    agent.ActiveStates[i] = activeLeafs[i];
                }
            }
        }
        
        public Dictionary<string, HsmDefinitionBlob> GetMachines() => _machines;
        public Dictionary<string, MachineMetadata> GetMachineMetadata() => _machineMetadata;
    }
}
