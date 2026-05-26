using System.Collections.Generic;
using CycloneDDS.Runtime;
using CycloneDDS.Schema;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication.Services;
using Hrot.Animation.Replication.Translators.Channels;
using Hrot.Animation.Replication.Translators.Descriptors;
using Hrot.Animation.Replication.Translators.Events;
using Hrot.Animation.Replication.Translators.SideBuffers;
using Hrot.Common;

namespace Hrot.Animation.Replication;

/// <summary>
/// QoS policy entry for one animation DDS topic (DD-2 §6).
/// All animation topics are Reliable; state-bearing topics are TransientLocal,
/// event topics are Volatile.
/// </summary>
public sealed class AnimTopicQosPolicy
{
    public string TopicName { get; init; } = "";
    public DdsReliability Reliability { get; init; }
    public DdsDurability Durability { get; init; }
}

/// <summary>
/// Registers all 15 animation replication translators for one simulation node.
///
/// <para><b>Brain node</b>: 4 intent egress + 4 status ingress + 7 event ingress = 15</para>
/// <para><b>Muscle node</b>: 4 intent ingress + 4 status egress + 7 event egress = 15</para>
///
/// Use <see cref="AllTranslators"/> to obtain the list for registration with the
/// network gateway system.
/// </summary>
public sealed class AnimationReplicationModule
{
    /// <summary>
    /// All 15 translators configured for this node's role.
    /// </summary>
    public IReadOnlyList<INetworkTranslator> AllTranslators { get; }

    /// <summary>
    /// Deterministic QoS policy table for all 15 animation DDS topics (DD-2 §6).
    /// State-bearing topics are Reliable+TransientLocal; event topics are Reliable+Volatile.
    /// </summary>
    public static IReadOnlyList<AnimTopicQosPolicy> TopicQosPolicies { get; } = BuildTopicQosPolicies();

    /// <summary>
    /// Constructs the module and instantiates all translators based on <paramref name="role"/>.
    /// </summary>
    /// <param name="participant">
    /// Live DDS participant. May be <c>null</c> in unit-test mode (readers/writers become no-ops).
    /// </param>
    /// <param name="entityMap">Entity &lt;-&gt; NetworkId registry.</param>
    /// <param name="role">Node role that determines egress vs. ingress for each translator.</param>
    public AnimationReplicationModule(
        DdsParticipant participant,
        NetworkEntityMap entityMap,
        NodeRole role)
    {
        bool isBrain = (role & NodeRole.Brain) != 0;
        AllTranslators = BuildTranslators(participant, entityMap, isBrain);
    }

    private static IReadOnlyList<INetworkTranslator> BuildTranslators(
        DdsParticipant participant,
        NetworkEntityMap entityMap,
        bool isBrain)
    {
        var list = new List<INetworkTranslator>(15);

        if (isBrain)
        {
            // Brain egresses intent (Brain -> Muscle) and ingresses status/events (Muscle -> Brain).
            list.Add(new AnimationChannelIntentEgressTranslator(participant, entityMap));
            list.Add(new AnimationChannelStatusIngressTranslator(participant, entityMap));
            list.Add(new LookAtChannelIntentEgressTranslator(participant, entityMap));
            list.Add(new LookAtChannelStatusIngressTranslator(participant, entityMap));
            list.Add(new StanceIntentEgressTranslator(participant, entityMap));
            list.Add(new StanceStatusIngressTranslator(participant, entityMap));
            list.Add(new AnimationMontageQueueEgressTranslator(participant, entityMap));
            list.Add(new AnimationMontageQueueStateIngressTranslator(participant, entityMap));
            list.Add(new MontageStartedEventTranslator(participant, entityMap, TranslatorDirection.Ingress));
            list.Add(new MontageEndedEventTranslator(participant, entityMap, TranslatorDirection.Ingress));
            list.Add(new MontageSectionAdvancedEventTranslator(participant, entityMap, TranslatorDirection.Ingress));
            list.Add(new StanceChangedEventTranslator(participant, entityMap, TranslatorDirection.Ingress));
            list.Add(new HitWindowOpenedEventTranslator(participant, entityMap, TranslatorDirection.Ingress));
            list.Add(new HitWindowClosedEventTranslator(participant, entityMap, TranslatorDirection.Ingress));
            list.Add(new AnimNotifyEventTranslator(participant, entityMap, TranslatorDirection.Ingress));
        }
        else
        {
            // Muscle ingresses intent (Brain -> Muscle) and egresses status/events (Muscle -> Brain).
            list.Add(new AnimationChannelIntentIngressTranslator(participant, entityMap));
            list.Add(new AnimationChannelStatusEgressTranslator(participant, entityMap));
            list.Add(new LookAtChannelIntentIngressTranslator(participant, entityMap));
            list.Add(new LookAtChannelStatusEgressTranslator(participant, entityMap));
            list.Add(new StanceIntentIngressTranslator(participant, entityMap));
            list.Add(new StanceStatusEgressTranslator(participant, entityMap));
            list.Add(new AnimationMontageQueueIngressTranslator(participant, entityMap));
            list.Add(new AnimationMontageQueueStateEgressTranslator(participant, entityMap));
            list.Add(new MontageStartedEventTranslator(participant, entityMap, TranslatorDirection.Egress));
            list.Add(new MontageEndedEventTranslator(participant, entityMap, TranslatorDirection.Egress));
            list.Add(new MontageSectionAdvancedEventTranslator(participant, entityMap, TranslatorDirection.Egress));
            list.Add(new StanceChangedEventTranslator(participant, entityMap, TranslatorDirection.Egress));
            list.Add(new HitWindowOpenedEventTranslator(participant, entityMap, TranslatorDirection.Egress));
            list.Add(new HitWindowClosedEventTranslator(participant, entityMap, TranslatorDirection.Egress));
            list.Add(new AnimNotifyEventTranslator(participant, entityMap, TranslatorDirection.Egress));
        }

        return list.AsReadOnly();
    }

    private static IReadOnlyList<AnimTopicQosPolicy> BuildTopicQosPolicies()
    {
        // State-bearing topics (channels, descriptors, side-buffers) — DD-2 §6.
        static AnimTopicQosPolicy State(string name) => new()
        {
            TopicName = name,
            Reliability = DdsReliability.Reliable,
            Durability = DdsDurability.TransientLocal,
        };
        // Event topics — Volatile so late joiners do not replay historical events.
        static AnimTopicQosPolicy Evt(string name) => new()
        {
            TopicName = name,
            Reliability = DdsReliability.Reliable,
            Durability = DdsDurability.Volatile,
        };

        return new List<AnimTopicQosPolicy>
        {
            // Channel intent / status (4 topics)
            State("hrot/anim/intent/AnimationChannel"),
            State("hrot/anim/status/AnimationChannel"),
            State("hrot/anim/intent/LookAtChannel"),
            State("hrot/anim/status/LookAtChannel"),
            // Descriptor intent / status (2 topics)
            State("hrot/anim/StanceIntent"),
            State("hrot/anim/StanceStatus"),
            // Side-buffer (2 topics)
            State("hrot/anim/MontageQueue"),
            State("hrot/anim/MontageQueueState"),
            // Event topics (7 topics)
            Evt("hrot/anim/MontageStarted"),
            Evt("hrot/anim/MontageEnded"),
            Evt("hrot/anim/MontageSectionAdv"),
            Evt("hrot/anim/StanceChanged"),
            Evt("hrot/anim/HitWindowOpened"),
            Evt("hrot/anim/HitWindowClosed"),
            Evt("hrot/anim/AnimNotify"),
        }.AsReadOnly();
    }
}
