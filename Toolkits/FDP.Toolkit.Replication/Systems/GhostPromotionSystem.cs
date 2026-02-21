using System;
using System.Collections.Generic;
using System.Diagnostics;
using Fdp.Kernel;
using Fdp.Interfaces;
using FDP.Toolkit.Replication.Components;
using FDP.Kernel.Logging;
using FDP.Toolkit.Lifecycle.Events;

namespace FDP.Toolkit.Replication.Systems
{
    public class GhostPromotionSystem : ComponentSystem
    {
        private ITkbDatabase? _tkbDatabase;
        private ISerializationRegistry? _serializationRegistry;
        
        private readonly Queue<Entity> _promotionQueue = new Queue<Entity>();
        private readonly HashSet<Entity> _inQueue = new HashSet<Entity>();
        private readonly Stopwatch _stopwatch = new Stopwatch();
        
        // 2ms budget per frame (ticks = 0.002 * Frequency)
        private static readonly long PROMOTION_BUDGET_TICKS = (long)(0.002 * Stopwatch.Frequency);

        protected override void OnCreate()
        {
        }

        protected override void OnUpdate()
        {
            if (_tkbDatabase == null && World.HasSingletonManaged<ITkbDatabase>())
            {
                _tkbDatabase = World.GetSingletonManaged<ITkbDatabase>();
            }
            if (_serializationRegistry == null && World.HasSingletonManaged<ISerializationRegistry>())
            {
                _serializationRegistry = World.GetSingletonManaged<ISerializationRegistry>();
            }
            
            if (_tkbDatabase == null || _serializationRegistry == null) return;

            if (!World.HasSingletonUnmanaged<GlobalTime>()) return;
            var globalTime = World.GetSingletonUnmanaged<GlobalTime>();
            uint currentFrame = (uint)globalTime.FrameNumber;

            // Step 1: Enqueue ready ghosts
            EnqueueReadyGhosts(currentFrame);

            // Step 2: Process Queue with Budget
            if (_promotionQueue.Count == 0) return;

            _stopwatch.Restart();

            while (_promotionQueue.Count > 0)
            {
                // Check budget
                if (_stopwatch.ElapsedTicks > PROMOTION_BUDGET_TICKS)
                {
                     break;
                }

                Entity entity = _promotionQueue.Dequeue();
                _inQueue.Remove(entity);

                if (!World.IsAlive(entity)) continue;
                
                // Rely on component presence instead of GetLifecycleState
                if (!World.HasComponent<NetworkSpawnRequest>(entity) || !World.HasComponent<BinaryGhostStore>(entity)) continue;

                PromoteGhost(entity);
            }
            
            _stopwatch.Stop();
        }

        private void EnqueueReadyGhosts(uint currentFrame)
        {
            // Query for ghosts that have NetworkSpawnRequest (Identified)
            var query = World.Query()
                .With<NetworkSpawnRequest>()
                .WithManaged<BinaryGhostStore>()
                .Build();

            foreach (var entity in query)
            {
                if (_inQueue.Contains(entity)) continue;

                var spawnReq = World.GetComponent<NetworkSpawnRequest>(entity)!;
                var store = World.GetComponent<BinaryGhostStore>(entity);
                
                if (store == null) continue;

                if (store.IdentifiedAtFrame == 0)
                {
                    store.IdentifiedAtFrame = currentFrame;
                }

                if (!_tkbDatabase!.TryGetByType(spawnReq.TkbType, out var template))
                {
                    continue; 
                }

                if (template!.AreAllRequirementsMet(store.StashedData.Keys, currentFrame, store.IdentifiedAtFrame))
                {
                    _promotionQueue.Enqueue(entity);
                    _inQueue.Add(entity);
                }
            }
        }

        private void PromoteGhost(Entity entity)
        {
            var spawnReq = World.GetComponent<NetworkSpawnRequest>(entity)!;
            var store = World.GetComponent<BinaryGhostStore>(entity)!;
            var template = _tkbDatabase.GetByType(spawnReq.TkbType)!;

            template.ApplyTo(World, entity, preserveExisting: false);

            // Step 3: Spawn child blueprints (sub-entities)
            if (template.ChildBlueprints.Count > 0)
            {
                ChildMap childMap;
                if (World.HasComponent<ChildMap>(entity))
                {
                    childMap = World.GetComponent<ChildMap>(entity);
                }
                else
                {
                    childMap = new ChildMap();
                    World.SetComponent(entity, childMap);
                }

                foreach (var childDef in template.ChildBlueprints)
                {
                    var childEntity = World.CreateEntity();
                    World.SetLifecycleState(childEntity, EntityLifecycle.Constructing);
                    
                    // Link to parent
                    World.AddComponent(childEntity, new PartMetadata
                    {
                        ParentEntity = entity,
                        InstanceId = childDef.InstanceId
                    });
                    
                    // Apply child blueprint
                    if (_tkbDatabase.TryGetByType(childDef.ChildTkbType, out var childTemplate))
                    {
                        childTemplate.ApplyTo(World, childEntity, preserveExisting: false);
                    }
                    else
                    {
                         FdpLog<GhostPromotionSystem>.Warn($"[GhostPromotion] Missing child template {childDef.ChildTkbType}");
                    }
                    
                    childMap.InstanceToEntity[childDef.InstanceId] = childEntity;
                }
            }

            using (var ecb = new EntityCommandBuffer())
            {
                foreach (var kvp in store.StashedData)
                {
                    long packedKey = kvp.Key;
                    byte[] data = kvp.Value;
                    
                    int ordinal = PackedKey.GetOrdinal(packedKey);
                    if (_serializationRegistry!.TryGet(ordinal, out var provider))
                    {
                        // Handle Instance Routing
                        int instanceId = PackedKey.GetInstanceId(packedKey);
                        
                        if (instanceId == 0)
                        {
                            provider.Apply(entity, data, ecb);
                        }
                        else
                        {
                             // Find the child entity
                             if (World.HasComponent<ChildMap>(entity))
                             {
                                 var childMap = World.GetComponent<ChildMap>(entity);
                                 if (childMap.InstanceToEntity.TryGetValue(instanceId, out var childEntity))
                                 {
                                     provider.Apply(childEntity, data, ecb);
                                 }
                             }
                        }
                    }
                }
                
                ecb.RemoveManagedComponent<BinaryGhostStore>(entity);
                ecb.RemoveComponent<NetworkSpawnRequest>(entity);
                ecb.SetLifecycleState(entity, EntityLifecycle.Constructing);
                
                ecb.PublishEvent(new ConstructionOrder 
                {
                     Entity = entity,
                     BlueprintId = spawnReq.TkbType, // Assuming spawnReq is still accessible here. Yes it is defined at start of method.
                     FrameNumber = World.GlobalVersion
                });
                
                ecb.Playback(World);
            }
        }
    }
}
