using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication.Attributes;

namespace Hrot.SimHost.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>Q59-E</c> — a minimal <see cref="IDescriptorTranslator"/> so a unit test can supply the
/// component→descriptor knowledge the NETWORK layer supplies in production.</b>
///
/// <para>📄 <c>docs/blueprints/Architect_Question_59_…md</c> §7.3 · §9.3.</para>
///
/// <para>⭐⭐⭐ <b>Why the rails NEED this now, and why that makes them stronger.</b> Before <c>Q59-E</c> the
/// dirty-descriptor ordinal came from the JSON routing table — FDP code naming a NED grouping — so a unit
/// test got marks "for free" and never exercised the real mechanism. ⭐ Now the ordinal comes from the world's
/// <c>DescriptorOwnershipMap</c>, fed from translators. ⇒ ⛔ a test that contributes none would assert
/// <c>{} == {}</c> and prove nothing, while a test that contributes one exercises the WHOLE chain: applier →
/// component id → map → <c>SmartEgressUtil.MarkDirty</c>.</para>
///
/// <para>⚠ <b>The ordinals come from the DDS enum on purpose.</b> FDP no longer has a descriptor vocabulary —
/// that is the point of <c>Q59-E</c> — so a test that needs a concrete ordinal must name the network one,
/// exactly as a real translator does.</para>
/// </summary>
internal sealed class FakeDescriptorTranslator : IDescriptorTranslator
{
    private readonly IReadOnlyList<int> _targets;

    public FakeDescriptorTranslator(long ordinal, params int[] targetComponentIds)
    {
        DescriptorOrdinal = ordinal;
        _targets = targetComponentIds;
    }

    public long DescriptorOrdinal { get; }
    public IReadOnlyList<int> TargetComponentIds => _targets;

    public string TopicName => $"fake-{DescriptorOrdinal}";
    public TranslatorDirection Direction => TranslatorDirection.Egress;
    public long ReceivedSampleCount => 0;
    public long SentSampleCount => 0;

    public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    public void Dispose(long networkEntityId) { }

    // ⭐ Never exercised: this fake exists only to DECLARE a descriptor↔component pairing.
    public void PollIngress(Fdp.Interfaces.IEntityCommandBuffer ecb, Fdp.ModuleHost.Abstractions.ISimulationView view) { }
    public void ScanAndPublish(Fdp.ModuleHost.Abstractions.ISimulationView view) { }

    /// <summary>
    /// ⭐⭐ Contributes the production pairings for the attribute vocabulary's components, mirroring what
    /// <c>EntityInfoEgressTranslator</c> and <c>GeoSpatialEgressTranslator</c> declare.
    /// </summary>
    internal static void ContributeProductionPairings(EntityRepository repo)
        => AttributeInterpreterProvider.ContributeTranslators(repo, new[]
        {
            new FakeDescriptorTranslator(
                (long)Hrot.NED.Descriptors.EDescriptorType.dtEntityInfo,
                GlobalComponentIds.EntityInfo),
            new FakeDescriptorTranslator(
                (long)Hrot.NED.Descriptors.EDescriptorType.dtWorldPos,
                GlobalComponentIds.SimTransform,
                GlobalComponentIds.NetworkTransform,
                GlobalComponentIds.NetworkVelocity),
        });
}
