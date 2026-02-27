using Fdp.Kernel;
using Fdp.Examples.NetworkDemo.Events;
using Fdp.Examples.NetworkDemo.Descriptors;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Network.Cyclone.Translators;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using ModuleHost.Core.Abstractions;

namespace Fdp.Examples.NetworkDemo.Translators
{
    /// <summary>
    /// Translator for multi-entity fire events.
    /// Inherits from CycloneNativeEventTranslator for Zero-Alloc Struct support.
    /// </summary>
    public class FireEventTranslator : CycloneNativeEventTranslator<FireInteractionEvent, NetworkFireEvent>, IDescriptorTranslator
    {
        public new long DescriptorOrdinal => 300;

        public FireEventTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
            : base(participant, "FDP.Evt_FireInteraction", entityMap)
        {
        }

        public new void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
             // FDP.Kernel.Logging.FdpLog<FireEventTranslator>.Info("PollIngress Called");
             base.PollIngress(cmd, view);
        }

        // INGRESS: Network -> ECS
        protected override bool TryDecode(in NetworkFireEvent dds, out FireInteractionEvent ecs)
        {
            FDP.Kernel.Logging.FdpLog<FireEventTranslator>.Info(
                "Decoded FireEvent: Atk={0} Tgt={1}",
                dds.AttackerNetId,
                dds.TargetNetId);

            ecs = default;
            
            // Drop event if entities don't exist locally
            if (!EntityMap.TryGetEntity(dds.AttackerNetId, out var attacker)) 
            {
                return false;
            }
            
            Entity target = Entity.Null;
            if (dds.TargetNetId != 0)
            {
                 EntityMap.TryGetEntity(dds.TargetNetId, out target);
            }

            ecs = new FireInteractionEvent
            {
                AttackerRoot = attacker,
                TargetRoot = target,
                WeaponInstanceId = dds.WeaponInstanceId,
                Damage = dds.Damage,
                IsRemote = true
            };
            
            return true;
        }

        // EGRESS: ECS -> Network
        protected override bool TryEncode(in FireInteractionEvent ecs, out NetworkFireEvent dds)
        {
            dds = default;
            if (ecs.IsRemote) return false;

            if (!EntityMap.TryGetNetworkId(ecs.AttackerRoot, out long attId)) {
                FDP.Kernel.Logging.FdpLog<FireEventTranslator>.Warn("TryEncode: Failed to get Attacker ID");
                return false;
            }
            
            long tgtId = 0;
            if (ecs.TargetRoot != Entity.Null)
            {
                if (!EntityMap.TryGetNetworkId(ecs.TargetRoot, out tgtId)) {
                   FDP.Kernel.Logging.FdpLog<FireEventTranslator>.Warn("TryEncode: Failed to get Target ID");
                   // Maybe allow failure if target is local-only? But FireEvent usually implies network relevance.
                }
            }

            dds = new NetworkFireEvent
            {
                AttackerNetId = attId,
                TargetNetId = tgtId,
                WeaponInstanceId = ecs.WeaponInstanceId,
                Damage = ecs.Damage
            };
            
             FDP.Kernel.Logging.FdpLog<FireEventTranslator>.Info(
                 "Encoded FireEvent: Atk={0} Tgt={1}",
                 attId,
                 tgtId);
            return true;
        }
    }
}
