using System;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.Interfaces;
using FDP.Kernel.Logging;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Network;
using Fdp.Network.Cyclone.Topics;

namespace Fdp.Examples.NetworkDemo.Translators
{
    /// <summary>
    /// Ingress-only translator that receives <see cref="EntityMasterTopic"/> announcements
    /// from remote nodes and creates ghost entities locally so the rest of the replication
    /// pipeline can track and synchronise them.
    /// </summary>
    public class EntityMasterIngressTranslator : Fdp.Interfaces.IDescriptorTranslator
    {
        private readonly DdsReader<EntityMasterTopic> _reader;
        private readonly FDP.Toolkit.Replication.Services.NetworkEntityMap _entityMap;
        private readonly GhostCreationSystem _ghostCreationSystem;
        private readonly Fdp.Network.Cyclone.Services.NodeIdMapper _nodeMapper;
        private readonly int _localInternalId;

        // Use a distinctive negative ordinal so this translator never conflicts with egress
        // descriptor-ordinal book-keeping.
        public string TopicName => "SST_EntityMaster";
        public long DescriptorOrdinal => -10L;

        public EntityMasterIngressTranslator(
            DdsParticipant participant,
            FDP.Toolkit.Replication.Services.NetworkEntityMap entityMap,
            GhostCreationSystem ghostCreationSystem,
            Fdp.Network.Cyclone.Services.NodeIdMapper nodeMapper,
            int localInternalId)
        {
            _reader              = new DdsReader<EntityMasterTopic>(participant);
            _entityMap           = entityMap           ?? throw new ArgumentNullException(nameof(entityMap));
            _ghostCreationSystem = ghostCreationSystem ?? throw new ArgumentNullException(nameof(ghostCreationSystem));
            _nodeMapper          = nodeMapper          ?? throw new ArgumentNullException(nameof(nodeMapper));
            _localInternalId     = localInternalId;
        }

        /// <summary>
        /// Drains the DDS reader and creates ghost entities for any newly-announced remote
        /// entities.  Disposed instances trigger an entity teardown request.
        /// </summary>
        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (view is not EntityRepository repo)
                return;

            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                var info = sample.Info;

                if (info.InstanceState != CycloneDDS.Runtime.DdsInstanceState.Alive)
                {
                    // DDS DisposeInstance sends a notification where IsValid may be false
                    // but the key field (EntityId) is still populated. Access it safely.
                    long removedNetId = 0;
                    try { removedNetId = sample.Data.EntityId; }
                    catch (Exception) { /* key data not accessible for this sample */ }

                    if (removedNetId != 0)
                    {
                        FdpLog<EntityMasterIngressTranslator>.Info(
                            "Received Death Note for NetID={0}", removedNetId);
                        FdpLog<EntityMasterIngressTranslator>.Info(
                            "Destroying... entity with NetID={0}", removedNetId);
                        repo.Bus.PublishManaged(new DestroyEntityCommand
                        {
                            NetworkId = removedNetId,
                            Reason    = "EntityMaster disposed"
                        });
                    }
                    continue;
                }

                if (!sample.IsValid)
                    continue;

                ProcessSample(sample.Data, cmd, repo);
            }
        }

        private void ProcessSample(
            in EntityMasterTopic master,
            IEntityCommandBuffer cmd,
            EntityRepository repo)
        {
            long netId = master.EntityId;

            // Loopback prevention: skip if this entity is owned by the local node.
            // Using the OwnerId from the topic is more reliable than the entity map
            // since manually-created entities may not be registered in the map.
            int ownerInternalId = _nodeMapper.GetOrRegisterInternalId(master.OwnerId);
            if (ownerInternalId == _localInternalId)
                return; // Own entity broadcast back to us – ignore

            // Skip entities we already know (duplicate sample or late-joiner replay).
            if (_entityMap.TryGetEntity(netId, out _))
                return;

            FdpLog<EntityMasterIngressTranslator>.Debug(
                "[EntityMasterIngress] EntityMaster received for NetID={0}, TkbType={1} -> Ghost spawn",
                netId, master.TkbTypeValue);

            // Create an ECS ghost entity and register it in the shared entity map.
            var entity = _ghostCreationSystem.CreateGhost(repo, netId);

            // Resolve the owner's internal node ID so that queries using NetworkOwnership
            // (e.g. CombatSystemTests.FindRemoteEntity) can locate this ghost entity.
            int remoteInternalId = _nodeMapper.GetOrRegisterInternalId(master.OwnerId);

            // Add NetworkOwnership directly (not via command buffer) so it is immediately
            // visible to queries in the same or next frame.  Mirrors the pattern used
            // inside GhostCreationSystem which also adds components directly to avoid
            // one-frame delays on freshly created entities.
            repo.AddComponent(entity, new NetworkOwnership
            {
                PrimaryOwnerId = remoteInternalId,
                LocalNodeId    = _localInternalId
            });

            // Permanent identity component — drives GhostPromotionSystem.
            cmd.AddComponent(entity, new TkbIdentity { TkbType = master.TkbTypeValue });

            // Store DIS entity type natively in entity header.
            repo.SetDisType(entity, new DISEntityType { Value = master.DisTypeValue });

            // NOTE: AssertLogContains("Created Proxy Entity") in ReplicationTests depends
            //       on this exact substring being present in the log output.
            FdpLog<EntityMasterIngressTranslator>.Info(
                "Created Proxy Entity for NetID={0}, TkbType={1}", netId, master.TkbTypeValue);
        }

        /// <summary>Egress is not used by this translator.</summary>
        public void ScanAndPublish(ISimulationView view) { }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

        public void Dispose(long networkEntityId) { }
    }
}
