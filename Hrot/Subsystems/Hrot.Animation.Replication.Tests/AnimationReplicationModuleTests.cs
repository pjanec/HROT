using System;
using System.Linq;
using CycloneDDS.Schema;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication.Services;
using Hrot.Common;
using Xunit;

namespace Hrot.Animation.Replication.Tests;

/// <summary>
/// Tests for AnimationReplicationModule — verifies exactly 15 translators are created
/// with the correct topic names and directions per node role.
/// </summary>
public sealed class AnimationReplicationModuleTests
{
    // ── Topic table constants matching spec ───────────────────────────────────

    private static readonly string[] BrainEgressTopics =
    {
        "hrot/anim/intent/AnimationChannel",
        "hrot/anim/intent/LookAtChannel",
        "hrot/anim/StanceIntent",
        "hrot/anim/MontageQueue",
    };

    private static readonly string[] BrainIngressTopics =
    {
        "hrot/anim/status/AnimationChannel",
        "hrot/anim/status/LookAtChannel",
        "hrot/anim/StanceStatus",
        "hrot/anim/MontageQueueState",
        "hrot/anim/MontageStarted",
        "hrot/anim/MontageEnded",
        "hrot/anim/MontageSectionAdv",
        "hrot/anim/StanceChanged",
        "hrot/anim/HitWindowOpened",
        "hrot/anim/HitWindowClosed",
        "hrot/anim/AnimNotify",
    };

    // ── SC-1: Brain module has exactly 15 translators ─────────────────────────

    [Fact]
    public void BrainModule_Has_Exactly15Translators()
    {
        var map = new NetworkEntityMap();
        var module = new AnimationReplicationModule(participant: null, map, NodeRole.Brain);

        Assert.Equal(15, module.AllTranslators.Count);
    }

    // ── SC-2: Brain egress topics are intent + queue topics ───────────────────

    [Fact]
    public void BrainModule_HasCorrectEgressTopics()
    {
        var map = new NetworkEntityMap();
        var module = new AnimationReplicationModule(participant: null, map, NodeRole.Brain);

        var egressTopics = module.AllTranslators
            .Where(t => t.Direction == TranslatorDirection.Egress)
            .Select(t => t.TopicName)
            .ToHashSet();

        foreach (var topic in BrainEgressTopics)
        {
            Assert.Contains(topic, egressTopics);
        }
        Assert.Equal(BrainEgressTopics.Length, egressTopics.Count);
    }

    // ── SC-3: Brain ingress topics are status + event topics ──────────────────

    [Fact]
    public void BrainModule_HasCorrectIngressTopics()
    {
        var map = new NetworkEntityMap();
        var module = new AnimationReplicationModule(participant: null, map, NodeRole.Brain);

        var ingressTopics = module.AllTranslators
            .Where(t => t.Direction == TranslatorDirection.Ingress)
            .Select(t => t.TopicName)
            .ToHashSet();

        foreach (var topic in BrainIngressTopics)
        {
            Assert.Contains(topic, ingressTopics);
        }
        Assert.Equal(BrainIngressTopics.Length, ingressTopics.Count);
    }

    // ── SC-4: Muscle module has exactly 15 translators ────────────────────────

    [Fact]
    public void MuscleModule_Has_Exactly15Translators()
    {
        var map = new NetworkEntityMap();
        var module = new AnimationReplicationModule(participant: null, map, NodeRole.MuscleGround);

        Assert.Equal(15, module.AllTranslators.Count);
    }

    // ── SC-5: Muscle has opposite directions to Brain ─────────────────────────

    [Fact]
    public void MuscleModule_HasOppositeDirectionsFromBrain()
    {
        var map = new NetworkEntityMap();
        var brainModule = new AnimationReplicationModule(participant: null, map, NodeRole.Brain);
        var muscleModule = new AnimationReplicationModule(participant: null, map, NodeRole.MuscleGround);

        // Muscle egress = Brain ingress topics (status/events)
        var muscleEgressTopics = muscleModule.AllTranslators
            .Where(t => t.Direction == TranslatorDirection.Egress)
            .Select(t => t.TopicName)
            .ToHashSet();

        var brainIngressTopics = brainModule.AllTranslators
            .Where(t => t.Direction == TranslatorDirection.Ingress)
            .Select(t => t.TopicName)
            .ToHashSet();

        Assert.Equal(brainIngressTopics, muscleEgressTopics);
    }

    // ── SC-6: No duplicate topic names in Brain module ────────────────────────

    [Fact]
    public void BrainModule_HasNoDuplicateTopicNames()
    {
        var map = new NetworkEntityMap();
        var module = new AnimationReplicationModule(participant: null, map, NodeRole.Brain);

        var topics = module.AllTranslators.Select(t => t.TopicName).ToList();
        var unique = topics.Distinct().ToList();

        Assert.Equal(unique.Count, topics.Count);
    }

    // ── SC-7: Module constructs without throwing (null participant) ───────────

    [Fact]
    public void Module_ConstructsWithoutThrow_WithNullParticipant()
    {
        var map = new NetworkEntityMap();
        var ex = Record.Exception(
            () => new AnimationReplicationModule(participant: null, map, NodeRole.Brain));

        Assert.Null(ex);
    }

    // ── SC-8: QoS table has exactly 15 entries ────────────────────────────────

    [Fact]
    public void TopicQosPolicies_HasExactly15Entries()
    {
        Assert.Equal(15, AnimationReplicationModule.TopicQosPolicies.Count);
    }

    // ── SC-9: State-bearing topics are Reliable + TransientLocal ──────────────

    [Fact]
    public void TopicQosPolicies_StateBearingTopics_AreReliableTransientLocal()
    {
        var stateBearingTopics = new[]
        {
            "hrot/anim/intent/AnimationChannel",
            "hrot/anim/status/AnimationChannel",
            "hrot/anim/intent/LookAtChannel",
            "hrot/anim/status/LookAtChannel",
            "hrot/anim/StanceIntent",
            "hrot/anim/StanceStatus",
            "hrot/anim/MontageQueue",
            "hrot/anim/MontageQueueState",
        };

        var policyMap = AnimationReplicationModule.TopicQosPolicies
            .ToDictionary(p => p.TopicName);

        foreach (var topic in stateBearingTopics)
        {
            Assert.True(policyMap.ContainsKey(topic), $"Missing QoS entry for topic: {topic}");
            var policy = policyMap[topic];
            Assert.Equal(DdsReliability.Reliable, policy.Reliability);
            Assert.Equal(DdsDurability.TransientLocal, policy.Durability);
        }
    }

    // ── SC-10: Event topics are Reliable + Volatile ───────────────────────────

    [Fact]
    public void TopicQosPolicies_EventTopics_AreReliableVolatile()
    {
        var eventTopics = new[]
        {
            "hrot/anim/MontageStarted",
            "hrot/anim/MontageEnded",
            "hrot/anim/MontageSectionAdv",
            "hrot/anim/StanceChanged",
            "hrot/anim/HitWindowOpened",
            "hrot/anim/HitWindowClosed",
            "hrot/anim/AnimNotify",
        };

        var policyMap = AnimationReplicationModule.TopicQosPolicies
            .ToDictionary(p => p.TopicName);

        foreach (var topic in eventTopics)
        {
            Assert.True(policyMap.ContainsKey(topic), $"Missing QoS entry for topic: {topic}");
            var policy = policyMap[topic];
            Assert.Equal(DdsReliability.Reliable, policy.Reliability);
            Assert.Equal(DdsDurability.Volatile, policy.Durability);
        }
    }

    // ── SC-11: No FootstepEvent translator present ────────────────────────────

    [Fact]
    public void BrainModule_HasNoFootstepEventTopic()
    {
        var map = new NetworkEntityMap();
        var brainModule = new AnimationReplicationModule(participant: null, map, NodeRole.Brain);
        var muscleModule = new AnimationReplicationModule(participant: null, map, NodeRole.MuscleGround);

        var allTopics = brainModule.AllTranslators
            .Concat(muscleModule.AllTranslators)
            .Select(t => t.TopicName)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("hrot/anim/FootstepEvent", allTopics);
        Assert.DoesNotContain("hrot/anim/Footstep", allTopics);
        Assert.DoesNotContain("hrot/anim/FootstepEvent", AnimationReplicationModule.TopicQosPolicies.Select(p => p.TopicName));
    }
}
