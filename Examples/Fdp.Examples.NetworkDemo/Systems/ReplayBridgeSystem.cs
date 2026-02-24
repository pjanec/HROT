using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Fdp.Examples.NetworkDemo.Components;
using Fdp.Kernel;
using FDP.Interfaces.Abstractions;
using Fdp.Kernel.FlightRecorder;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Network;
using Fdp.Examples.NetworkDemo.Configuration;
using FDP.Kernel.Logging;

namespace Fdp.Examples.NetworkDemo.Systems
{
    [UpdateInPhase(SystemPhase.Input)]
    public class ReplayBridgeSystem : IModuleSystem, IDisposable
    {
        private struct ComponentCopyInstruction
        {
            public Type Type;
            public int ShadowTypeId;
            public int LiveTypeId;
            public int SizeBytes;
            public long DescriptorOrdinal;
            public bool IsManaged;
            public string DebugName;
        }

        private readonly List<ComponentCopyInstruction> _copyInstructions = new();

        private readonly string _recordingPath;
        private EntityRepository? _shadowRepo;
        private RecordingReader? _reader;
        private double _accumulator;
        private readonly Dictionary<long, Entity> _liveEntityMap = new Dictionary<long, Entity>();

        private readonly int _localNodeId;

        public ReplayBridgeSystem(string recordingPath, int localNodeId)
        {
            _recordingPath = recordingPath;
            _localNodeId = localNodeId;
            InitializeShadowWorld();
            BuildCopyInstructions();
        }

        private void BuildCopyInstructions()
        {
            var allTypes = DemoComponentRegistry.GetAllTypes();
            var explicitMappings = DemoComponentRegistry.GetExplicitOrdinalMappings();

            foreach (var type in allTypes)
            {
                long descriptorOrdinal;
                bool isManaged;

                var attr = type.GetCustomAttribute<FdpDescriptorAttribute>();
                if (attr != null)
                {
                    descriptorOrdinal = attr.Ordinal;
                    isManaged = !type.IsValueType;
                }
                else if (explicitMappings.TryGetValue(type, out var mappedOrdinal))
                {
                    descriptorOrdinal = mappedOrdinal;
                    isManaged = !type.IsValueType;
                }
                else
                {
                    continue;
                }

                var typeId = _shadowRepo!.GetComponentTypeId(type);

                _copyInstructions.Add(new ComponentCopyInstruction
                {
                    Type = type,
                    ShadowTypeId = typeId,
                    LiveTypeId = -1,
                    SizeBytes = type.IsValueType ? Marshal.SizeOf(type) : 0,
                    DescriptorOrdinal = descriptorOrdinal,
                    IsManaged = isManaged,
                    DebugName = type.Name
                });
            }

            FdpLog<ReplayBridgeSystem>.Info($"Built {_copyInstructions.Count} copy instructions");
        }

        public void RegisterDynamicType<T>(long descriptorOrdinal) where T : class
        {
             var type = typeof(T);
             if (_copyInstructions.Any(x => x.Type == type)) return;

             var typeId = -1;
             try 
             {
                 try { typeId = _shadowRepo!.GetComponentTypeId(type); } 
                 catch {
                     _shadowRepo!.RegisterManagedComponent<T>();
                     typeId = _shadowRepo.GetComponentTypeId(type);
                 }
             }
             catch (Exception ex)
             {
                 FdpLog<ReplayBridgeSystem>.Warn($"RegisterDynamicType: Failed to register {type.Name}: {ex.Message}");
                 return;
             }

             _copyInstructions.Add(new ComponentCopyInstruction
             {
                 Type = type,
                 ShadowTypeId = typeId,
                 LiveTypeId = -1,
                 DescriptorOrdinal = descriptorOrdinal,
                 IsManaged = true,
                 DebugName = type.Name,
                 SizeBytes = 0
             });
             
             FdpLog<ReplayBridgeSystem>.Info($"Dynamic Registered Type: {type.Name}");
        }

        private void InitializeShadowWorld()
        {
            _shadowRepo = new EntityRepository();
            DemoComponentRegistry.Register(_shadowRepo);
            
            // Replicate registration order from NetworkDemoApp to align Component IDs
            // Note: Singletons consume IDs even if not recorded in chunks
            _shadowRepo.RegisterManagedComponent<Fdp.Interfaces.ITkbDatabase>();
            _shadowRepo.RegisterManagedComponent<Fdp.Interfaces.ISerializationRegistry>();
            
            try
            {
                _reader = new RecordingReader(_recordingPath);
            }
            catch
            {
                _reader = null;
            }
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (_reader == null) return;
            
            EntityRepository? liveRepo = view as EntityRepository;
            bool canResolveIds = liveRepo != null;

            // 1. Advance Recording (Loop if EOF)
            bool hasFrame = _reader.ReadNextFrame(_shadowRepo!);
            
            var shadowCount = _shadowRepo!.Query().Build().Count();
            
            if (!hasFrame)
            {
                 FdpLog<ReplayBridgeSystem>.Info("Looping Replay...");
                 DisposeReader();
                InitializeShadowWorld();
                if (_reader != null)
                {
                    _reader.ReadNextFrame(_shadowRepo!);
                }
            }

            _accumulator += deltaTime;

            // 2. Update Singleton
            if (liveRepo != null)
            {
                try { liveRepo.RegisterComponent<ReplayTime>(); } catch { }
                
                liveRepo.SetSingleton(new ReplayTime
                {
                    Time = _accumulator,
                    Frame = _shadowRepo!.GlobalVersion
                });
            }

            // 3. Inject Entities
            var ecb = view.GetCommandBuffer();

            // Refresh Live Map
            _liveEntityMap.Clear();
            var liveQuery = view.Query().With<NetworkIdentity>().Build();
            foreach (var entity in liveQuery)
            {
                // Use GetComponentRO for view
                var id = view.GetComponentRO<NetworkIdentity>(entity).Value;
                _liveEntityMap[id] = entity;
            }

            // Sync Shadow Entities
            var shadowQuery = _shadowRepo!.Query().With<NetworkIdentity>().Build();
            foreach (var shadowEntity in shadowQuery)
            {
                if (!_shadowRepo.TryGetComponent<NetworkIdentity>(shadowEntity, out var netId))
                    continue;

                // Check Root Authority (Key 0)
                if (!HasAuthority(shadowEntity, 0)) 
                {
                    continue;
                }

                Entity liveEntity;

                if (_liveEntityMap.TryGetValue(netId.Value, out liveEntity))
                {
                    // Existing entity
                    // Update Authority
                    if (_shadowRepo!.TryGetComponent<NetworkAuthority>(shadowEntity, out var netAuth))
                    {
                        ecb.SetComponent(liveEntity, netAuth);
                    }
                }
                else
                {
                    // New entity
                    liveEntity = ecb.CreateEntity();
                    ecb.AddComponent(liveEntity, netId);

                    if (_shadowRepo!.TryGetComponent<NetworkAuthority>(shadowEntity, out var netAuth))
                    {
                        ecb.AddComponent(liveEntity, netAuth);
                    }

                    if (_shadowRepo.TryGetComponent<DescriptorOwnership>(shadowEntity, out var src))
                    {
                        var dst = new DescriptorOwnership();
                        foreach (var kvp in src.Map) dst.Map[kvp.Key] = kvp.Value;
                        ecb.SetManagedComponent(liveEntity, dst);
                    }
                }

                // Inject Components based on dynamic instructions
                for (int i=0; i < _copyInstructions.Count; i++)
                {
                    var instr = _copyInstructions[i];
                    
                    if (instr.LiveTypeId == -1 && canResolveIds)
                    {
                           try { instr.LiveTypeId = liveRepo!.GetComponentTypeId(instr.Type); } 
                           catch { instr.LiveTypeId = -2; FdpLog<ReplayBridgeSystem>.Warn($"Missing Type: {instr.DebugName}"); }
                           _copyInstructions[i] = instr;
                    }
                    if (instr.LiveTypeId < 0) continue;

                    try {
                    // 1. Check if Shadow has component
                    if (!_shadowRepo.HasComponentByTypeId(shadowEntity, instr.ShadowTypeId)) 
                        continue;
                    
                    // 2. Check Authority using cached Ordinal
                    if (!HasAuthority(shadowEntity, instr.DescriptorOrdinal)) 
                        continue;
                    
                    // 3. Raw Copy
                    if (!instr.IsManaged)
                    {
                        unsafe
                        {
                            var ptr = _shadowRepo.GetComponentPointer(shadowEntity, instr.ShadowTypeId);
                            ecb.SetComponentRaw(liveEntity, instr.LiveTypeId, ptr, instr.SizeBytes);
                        }
                    }
                    else
                    {
                        var obj = _shadowRepo.GetManagedComponentByTypeId(shadowEntity, instr.ShadowTypeId);
                        ecb.SetManagedComponentRaw(liveEntity, instr.LiveTypeId, obj);
                    }
                    } catch (Exception ex) { FdpLog<ReplayBridgeSystem>.Warn($"Copy failed for {instr.DebugName}: {ex.Message}"); }
                }
            }
        }

        private bool HasAuthority(Entity entity, long key)
        {
            // If localNodeId is -1, we assume "Observer Mode" and replay everything
            if (_localNodeId == -1) return true;

            if (_shadowRepo!.TryGetComponent<DescriptorOwnership>(entity, out var ownership))
            {
                if (ownership.Map.TryGetValue((int)key, out int ownerNode))
                {
                    return ownerNode == _localNodeId;
                }
            }

            if (_shadowRepo!.TryGetComponent<NetworkAuthority>(entity, out var auth))
            {
                return auth.PrimaryOwnerId == _localNodeId;
            }

            return true;
        }

        private void DisposeReader()
        {
            _reader?.Dispose();
            _reader = null;
            _shadowRepo?.Dispose();
            _shadowRepo = null;
        }

        public void Dispose()
        {
            DisposeReader();
        }
    }
}
