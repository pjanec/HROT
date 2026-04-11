using System;
using System.Collections.Generic;
using FDP.Kernel.Logging;
using Fdp.Kernel;
using Fdp.Interfaces;
using ModuleHost.Core.Abstractions;
using FDP.Toolkit.Lifecycle.Events;
using FDP.Toolkit.Lifecycle.Systems;

namespace FDP.Toolkit.Lifecycle
{
    /// <summary>
    /// Coordinates entity lifecycle across distributed modules.
    /// Ensures entities are fully initialized before becoming Active,
    /// and properly cleaned up before destruction.
    /// </summary>
    public class EntityLifecycleModule : IEcsModule
    {
        public string Name => "EntityLifecycleManager";
        
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();
        
        // Reactive: listen for ACK events
        public IReadOnlyList<Type>? WatchEvents => new[]
        {
            typeof(ConstructionAck),
            typeof(DestructionAck)
        };
        
        public IReadOnlyList<Type>? WatchComponents => null;
        
        private readonly ITkbDatabase _tkb;
        
        /// <summary>
        /// Global participants that care about all entities.
        /// </summary>
        private readonly HashSet<int> _globalParticipants;
        
        /// <summary>
        /// Blueprint-specific participants.
        /// </summary>
        private readonly Dictionary<long, HashSet<int>> _blueprintRequirements = new();
        
        private readonly int _timeoutFrames;
        
        private readonly long _localNodeId;
        
        private readonly Dictionary<Entity, PendingConstruction> _pendingConstruction = new();
        private readonly Dictionary<Entity, PendingDestruction> _pendingDestruction = new();
        
        private int _totalConstructed;
        private int _totalDestructed;
        private int _timeouts;
        
        public EntityLifecycleModule(
            ITkbDatabase tkb,
            IEnumerable<int> participatingModuleIds,
            int timeoutFrames = 300,
            long localNodeId = 0) 
        {
            _tkb = tkb;
            _globalParticipants = new HashSet<int>(participatingModuleIds);
            _timeoutFrames = timeoutFrames;
            _localNodeId = localNodeId;
        }
        
        public void RegisterSystems(ISystemRegistry registry)
        {
            registry.RegisterSystem(new BlueprintApplicationSystem(_tkb));
            registry.RegisterSystem(new LifecycleSystem(this));
        }
        
        public void Tick(ISimulationView view, float deltaTime)
        {
            // Main logic in LifecycleSystem
        }
        
        // === Public API ===
        
        public void RegisterModule(int moduleId)
        {
            _globalParticipants.Add(moduleId);
        }

        public void UnregisterModule(int moduleId)
        {
            _globalParticipants.Remove(moduleId);
        }

        public void RegisterRequirement(long blueprintId, int moduleId)
        {
            if (!_blueprintRequirements.TryGetValue(blueprintId, out var set))
            {
                set = new HashSet<int>();
                _blueprintRequirements[blueprintId] = set;
            }
            set.Add(moduleId);
        }

        public void AcknowledgeConstruction(Entity entity, int moduleId, uint frame, IEntityCommandBuffer cmd)
        {
            cmd.PublishEvent(new ConstructionAck
            {
                Entity = entity,
                ModuleId = moduleId,
                Success = true
            });
        }
        
        /// <summary>
        /// Begins construction of a new entity.
        /// Publishes ConstructionOrder and tracks pending ACKs.
        /// </summary>
        public void BeginConstruction(Entity entity, long blueprintId, uint currentFrame, IEntityCommandBuffer cmd, int initiator = 0)
        {
            if (_pendingConstruction.ContainsKey(entity))
            {
                throw new InvalidOperationException($"Entity {entity.Index} already in construction");
            }
            
            // Calculate participants
            var participants = new HashSet<int>(_globalParticipants);
            if (_blueprintRequirements.TryGetValue(blueprintId, out var reqs))
            {
                participants.UnionWith(reqs);
            }

            var order = new ConstructionOrder
            {
                Entity = entity,
                BlueprintId = blueprintId,
                FrameNumber = currentFrame,
                InitiatorModuleId = initiator
            };

            if (participants.Count == 0)
            {
                // No ACKs needed — register as pending with empty RemainingAcks.
                // DrainInstantComplete (called by LifecycleSystem each tick) will promote to Active.
                _pendingConstruction[entity] = new PendingConstruction
                {
                    Entity = entity,
                    BlueprintId = blueprintId,
                    StartFrame = currentFrame,
                    RemainingAcks = new HashSet<int>()
                };
                cmd.PublishEvent(order);
                return;
            }

            // Track pending state
            _pendingConstruction[entity] = new PendingConstruction
            {
                Entity = entity,
                BlueprintId = blueprintId,
                StartFrame = currentFrame,
                RemainingAcks = participants
            };
            
            // Publish order event
            cmd.PublishEvent(order);
        }
        
        /// <summary>
        /// Begins teardown of an entity.
        /// Publishes DestructionOrder and tracks pending ACKs.
        /// </summary>
        public void BeginDestruction(Entity entity, uint currentFrame, FixedString64 reason, IEntityCommandBuffer cmd)
        {
            if (_pendingDestruction.ContainsKey(entity))
            {
                return; // Already in teardown
            }

            var order = new DestructionOrder
            {
                Entity = entity,
                FrameNumber = currentFrame,
                Reason = reason
            };

            if (_globalParticipants.Count == 0)
            {
                // No ACKs needed — register as pending with empty RemainingAcks.
                // DrainInstantComplete (called by LifecycleSystem each tick) will destroy the entity.
                _pendingDestruction[entity] = new PendingDestruction
                {
                    Entity = entity,
                    StartFrame = currentFrame,
                    RemainingAcks = new HashSet<int>(),
                    Reason = reason
                };
                cmd.PublishEvent(order);
                return;
            }
            
            _pendingDestruction[entity] = new PendingDestruction
            {
                Entity = entity,
                StartFrame = currentFrame,
                RemainingAcks = new HashSet<int>(_globalParticipants), // Default to global only for now
                Reason = reason
            };
            
            cmd.PublishEvent(order);
        }

        public void BeginDestruction(Entity entity, uint currentFrame, string reason, IEntityCommandBuffer cmd)
        {
             BeginDestruction(entity, currentFrame, new FixedString64(reason), cmd);
        }
        
        // === Internal Logic (called by LifecycleSystem) ===
        
        public void ProcessConstructionAck(ConstructionAck ack, uint currentFrame, IEntityCommandBuffer cmd)
        {
            if (!_pendingConstruction.TryGetValue(ack.Entity, out var pending))
            {
                return;
            }
            
            if (!ack.Success)
            {
                FdpLog<EntityLifecycleModule>.Error(
                    $"[ELM] Construction failed for {ack.Entity.Index}: {ack.ErrorMessage}");
                
                _pendingConstruction.Remove(ack.Entity);
                cmd.DestroyEntity(ack.Entity);
                return;
            }
            
            pending.RemainingAcks.Remove(ack.ModuleId);
            
            if (pending.RemainingAcks.Count == 0)
            {
                FdpLog<EntityLifecycleModule>.Debug(
                    "[Node-{0}] ELM: Entity {1} received all ACKs. Promoting to Active.", _localNodeId, ack.Entity.Index);
                // All ACKs received - activate entity
                cmd.SetLifecycleState(ack.Entity, EntityLifecycle.Active);
                FdpLog<EntityLifecycleModule>.Debug(
                    "[Node-{0}] ELM: Entity {1} promoted to Active", _localNodeId, ack.Entity.Index);
                _pendingConstruction.Remove(ack.Entity);
                _totalConstructed++;
            }
        }
        
        public void ProcessDestructionAck(DestructionAck ack, uint currentFrame, IEntityCommandBuffer cmd)
        {
            if (!_pendingDestruction.TryGetValue(ack.Entity, out var pending))
            {
                return;
            }
            
            pending.RemainingAcks.Remove(ack.ModuleId);
            
            if (pending.RemainingAcks.Count == 0)
            {
                // All ACKs received - destroy entity
                cmd.DestroyEntity(ack.Entity);
                _pendingDestruction.Remove(ack.Entity);
                _totalDestructed++;
            }
        }
        
        /// <summary>
        /// Immediately promotes/destroys any entities whose pending ack sets are already empty.
        /// Called by <see cref="LifecycleSystem"/> each frame so that zero-participant constructions
        /// and destructions complete within one lifecycle tick after the order is issued.
        /// </summary>
        public void DrainInstantComplete(IEntityCommandBuffer cmd)
        {
            // Promote zero-ack constructions to Active
            var ready = new List<Entity>();
            foreach (var kvp in _pendingConstruction)
                if (kvp.Value.RemainingAcks.Count == 0)
                    ready.Add(kvp.Key);

            foreach (var entity in ready)
            {
                FdpLog<EntityLifecycleModule>.Debug(
                    "[Node-{0}] ELM: Entity {1} instant-complete (no participants). Promoting to Active.", _localNodeId, entity.Index);
                cmd.SetLifecycleState(entity, EntityLifecycle.Active);
                FdpLog<EntityLifecycleModule>.Debug(
                    "[Node-{0}] ELM: Entity {1} promoted to Active", _localNodeId, entity.Index);
                _pendingConstruction.Remove(entity);
                _totalConstructed++;
            }

            // Destroy zero-ack destructions
            var readyDestruct = new List<Entity>();
            foreach (var kvp in _pendingDestruction)
                if (kvp.Value.RemainingAcks.Count == 0)
                    readyDestruct.Add(kvp.Key);

            foreach (var entity in readyDestruct)
            {
                cmd.DestroyEntity(entity);
                _pendingDestruction.Remove(entity);
                _totalDestructed++;
            }
        }

        public void CheckTimeouts(uint currentFrame, IEntityCommandBuffer cmd)
        {
            var timedOutConstruction = new List<Entity>();
            foreach (var kvp in _pendingConstruction)
            {
                if (currentFrame - kvp.Value.StartFrame > _timeoutFrames)
                {
                    timedOutConstruction.Add(kvp.Key);
                }
            }
            
            foreach (var entity in timedOutConstruction)
            {
                var pending = _pendingConstruction[entity];
                Console.Error.WriteLine(
                    $"[ELM] Construction timeout for {entity.Index}. Missing ACKs from modules: {string.Join(", ", pending.RemainingAcks)}");
                
                _pendingConstruction.Remove(entity);
                cmd.DestroyEntity(entity);
                _timeouts++;
            }
            
            var timedOutDestruction = new List<Entity>();
            foreach (var kvp in _pendingDestruction)
            {
                if (currentFrame - kvp.Value.StartFrame > _timeoutFrames)
                {
                    timedOutDestruction.Add(kvp.Key);
                }
            }
            
            foreach (var entity in timedOutDestruction)
            {
                Console.Error.WriteLine(
                    $"[ELM] Destruction timeout for {entity.Index}. Forcing deletion.");
                
                _pendingDestruction.Remove(entity);
                cmd.DestroyEntity(entity);
                _timeouts++;
            }
        }
        
        public (int constructed, int destructed, int timeouts, int pending) GetStatistics()
        {
            return (_totalConstructed, _totalDestructed, _timeouts, 
                    _pendingConstruction.Count + _pendingDestruction.Count);
        }
    }
    
    internal class PendingConstruction
    {
        public Entity Entity;
        public long BlueprintId;
        public uint StartFrame;
        public HashSet<int> RemainingAcks = new();
    }
    
    internal class PendingDestruction
    {
        public Entity Entity;
        public uint StartFrame;
        public HashSet<int> RemainingAcks = new();
        public FixedString64 Reason;
    }
}
