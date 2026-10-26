using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Hrot.Core.Network;
using Hrot.IG.Components;
using Hrot.Map.Common.Dds;
using Hrot.Network.NED.CGF;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Fdp.Toolkit.NetworkSpawning.Events;

namespace Hrot.Map.Common.Tests.Replication.Egress;

/// <summary>
/// ⭐⭐ Rails for <see cref="NedEntityCreationRequestEgress"/> — the D1 forwarding half that replaces
/// <c>SpawnEntityCommandEgressTranslator</c>.
///
/// <para>📄 <c>docs/DESIGN_Entity_Creation_Unification.md</c> §3.4b, host (f).</para>
///
/// <para>⛔ <b>What these rails exist to catch</b>, in order of how badly each would fail silently:
/// <list type="number">
///   <item>the OWNER not travelling — the forwarded request would arrive untargeted and be serviced by
///     whichever node happens to be the default processor, which is the routing bug D1 fixes;</item>
///   <item>the TRANSIENT bit not travelling — the receiving node materialises an operator's sketch as an
///     ordinary saveable entity, i.e. D2's guarantee silently stopping at the node boundary;</item>
///   <item>the geometry being dropped — the R-137 regression the extraction was designed to prevent.</item>
/// </list></para>
/// </summary>
public class NedEntityCreationRequestEgressRails
{
    private sealed class CapturingWriter<T> : IDdsWriter<T>
    {
        public List<T> Publishes { get; } = new();
        public void Write(T sample) => Publishes.Add(sample);
        public void DisposeInstance(T key) { }
    }

    private static (NedEntityCreationRequestEgress Egress, CapturingWriter<CreateEntityRequest> Writer) NewEgress()
    {
        var writer = new CapturingWriter<CreateEntityRequest>();
        return (new NedEntityCreationRequestEgress(writer, geoTransform: null), writer);
    }

    /// <summary>The address must travel: a request targeted at node 7 arrives targeted at node 7.</summary>
    [Fact]
    public void Send_CarriesTheOwnerOntoTheWire()
    {
        var (egress, writer) = NewEgress();
        var requestId = Guid.NewGuid();

        egress.Send(new EntityCreationRequest
        {
            RequestId          = requestId,
            OwnerAppInstanceId = 7,
            TkbType            = 42L,
        });

        var sample = Assert.Single(writer.Publishes);
        Assert.Equal(requestId, sample.RequestId);
        Assert.Equal(7, sample.Owner.AppInstanceId);
        Assert.Equal(1, egress.SentSampleCount);
    }

    /// <summary>⭐ CE-4024 (AQ93 §15 K5, R-4029) — an id the author fixed travels in the master's EntityId: a SUMO pedestrian converted on the
    /// traffic node keeps his network id on the brain node. 📌 It was dropped, so the cluster gave him a new one.</summary>
    [Fact]
    public void Send_CarriesThePreAllocatedNetworkIdOntoTheWire()
    {
        var (egress, writer) = NewEgress();
        egress.Send(new EntityCreationRequest { RequestId = Guid.NewGuid(), TkbType = 1101L, PreAllocatedNetworkId = 1216 });
        egress.Send(new EntityCreationRequest { RequestId = Guid.NewGuid(), TkbType = 1101L });

        var master = writer.Publishes.Select(p => p.InitialDescriptors.Single(d => d._d == EDescriptorType.dtEntityMaster).EntityMaster).ToArray();
        Assert.Equal(1216, master[0].EntityId);
        Assert.Equal(0, master[1].EntityId);   // none fixed: the processor allocates, as before
    }

    /// <summary>
    /// ⭐⭐⭐ D2 across the node boundary. Without the flag bit the receiving node has no way to know the
    /// entity is a sketch, and <c>CollectSaveableEntities</c> writes it into the scenario.
    /// </summary>
    [Fact]
    public void Send_CarriesTheTransientFlagOntoTheWire()
    {
        var (egress, writer) = NewEgress();

        egress.Send(new EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 3,
            TkbType            = 42L,
            IsTransient        = true,
        });

        var sample = Assert.Single(writer.Publishes);
        Assert.Equal(EntityCreationRequestFlags.Transient,
                     sample.Flags & EntityCreationRequestFlags.Transient);
    }

    /// <summary>A non-transient request must NOT set the bit — the flag is a claim, not a default.</summary>
    [Fact]
    public void Send_LeavesTheTransientFlagClearForAnOrdinaryRequest()
    {
        var (egress, writer) = NewEgress();

        egress.Send(new EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 3,
            TkbType            = 42L,
        });

        var sample = Assert.Single(writer.Publishes);
        Assert.Equal(0L, sample.Flags & EntityCreationRequestFlags.Transient);
    }

    /// <summary>
    /// R-137: the geometry the retired translator knew how to encode must still reach the wire. A polyline
    /// in <c>InitialComponents</c> becomes a <c>dtMapVisualOverlay</c> descriptor, and the anchor is taken
    /// from the request's own <c>SimTransform</c> — the convention <c>CreateEntityRequestSystem</c> uses.
    /// </summary>
    [Fact]
    public void Send_PreservesGeometryAndAnchorFromInitialComponents()
    {
        var (egress, writer) = NewEgress();

        var polyline = new EditablePolyline
        {
            Points = new List<Vector2> { new(1f, 2f), new(3f, 4f) },
        };
        var transform = new SimTransform { Position = new Vector3(10f, 20f, 0f) };

        egress.Send(new EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 5,
            TkbType            = 42L,
            InitialComponents  = new List<object> { transform, polyline },
        });

        var sample = Assert.Single(writer.Publishes);

        // The anchor came from the SimTransform inside InitialComponents, not from a command field.
        var worldPos = sample.InitialDescriptors.Single(d => d._d == EDescriptorType.dtWorldPos);
        Assert.Equal(20.0, worldPos.WorldPos.Pos.Latitude,  3);
        Assert.Equal(10.0, worldPos.WorldPos.Pos.Longitude, 3);

        var overlay = sample.InitialDescriptors.Single(d => d._d == EDescriptorType.dtMapVisualOverlay);
        Assert.Equal(2, overlay.MapVisualOverlay.Points.Count);
    }

    /// <summary>
    /// ⭐⭐⭐ The wire ROUND TRIP for the transient claim. The egress encodes it and the NED ingress
    /// decodes it through the same pair, so a sketch forwarded to a persisting node arrives still marked
    /// unsaveable. ⛔ Without this the receiving node materialises it as an ordinary entity and
    /// CollectSaveableEntities writes an operator's sketch into the scenario (D2, R-140).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheTransientClaim_SurvivesTheWireRoundTrip(bool isTransient)
    {
        var (egress, writer) = NewEgress();

        egress.Send(new EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 9,
            TkbType            = 42L,
            IsTransient        = isTransient,
        });

        var sample = Assert.Single(writer.Publishes);

        // The decode the NED ingress performs when it rebuilds the request on the far node.
        Assert.Equal(isTransient, EntityCreationRequestFlags.IsTransient(sample.Flags));
    }

    /// <summary>
    /// ⭐ CE-3324 — the scenario-load claim survives the wire, independently of the transient one: a forwarded scenario load must
    /// not spawn the template's children a second time on the node that services it, and an ordinary request must not be read
    /// as one (the claim is no longer inferred from the pre-allocated id, which every author may now send).
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void CE3324_TheScenarioLoadClaim_SurvivesTheWireRoundTrip(bool isScenarioLoad, bool isTransient)
    {
        var (egress, writer) = NewEgress();

        egress.Send(new EntityCreationRequest
        {
            RequestId             = Guid.NewGuid(),
            OwnerAppInstanceId    = 9,
            TkbType               = 42L,
            PreAllocatedNetworkId = 7001L,   // a fixed id says nothing about either claim
            IsScenarioLoad        = isScenarioLoad,
            IsTransient           = isTransient,
        });

        var sample = Assert.Single(writer.Publishes);
        Assert.Equal(isScenarioLoad, EntityCreationRequestFlags.IsScenarioLoad(sample.Flags));
        Assert.Equal(isTransient, EntityCreationRequestFlags.IsTransient(sample.Flags));
    }

    /// <summary>The TKB type must survive — it is what the receiving node looks the template up by.</summary>
    [Fact]
    public void Send_CarriesTheTkbType()
    {
        var (egress, writer) = NewEgress();

        egress.Send(new EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 1,
            TkbType            = 4242L,
        });

        var sample = Assert.Single(writer.Publishes);
        var master = sample.InitialDescriptors.Single(d => d._d == EDescriptorType.dtEntityMaster);
        Assert.Equal(4242L, master.EntityMaster.TkbType);
    }

    /// <summary>
    /// ⭐ <c>CE-1017</c> S2 — the birth-height request survives the wire round trip: the egress writes the explicit
    /// <c>SpawnHeightMode</c>/<c>SpawnLevel</c> fields and the NED ingress rebuilds it with <c>SpawnHeight.FromWire</c>;
    /// absent ⇒ 0/0 ⇒ null (every sender before the fields). 📄 docs/DESIGN_Add_Entity_Picker.md §2e.
    /// </summary>
    [Theory]
    [InlineData((byte)1, (short)2)]
    [InlineData((byte)2, (short)-1)]
    [InlineData((byte)0, (short)0)]
    public void CE1017_TheSpawnHeight_SurvivesTheWireRoundTrip(byte mode, short level)
    {
        var (egress, writer) = NewEgress();
        Fdp.Toolkit.NetworkSpawning.SpawnHeight? height = mode == 0
            ? null
            : new Fdp.Toolkit.NetworkSpawning.SpawnHeight((Fdp.Toolkit.NetworkSpawning.SpawnHeightMode)mode, level);

        egress.Send(new EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 9,
            TkbType            = 42L,
            SpawnHeight        = height,
        });

        var sample = Assert.Single(writer.Publishes);
        Assert.Equal(mode, sample.SpawnHeightMode);
        Assert.Equal(level, sample.SpawnLevel);
        // The decode the NED ingress performs (NedCgfEntityLifecycleAdapters).
        Assert.Equal(height, Fdp.Toolkit.NetworkSpawning.SpawnHeight.FromWire(sample.SpawnHeightMode, sample.SpawnLevel));
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3228</c> — a gas / smoke cloud asked for on one node (owner 0) is created by the default processor on ANOTHER, so the
    /// forwarded request must carry the cloud: the egress writes a <c>dtAreaEffectCloud</c> descriptor and the NED ingress's mapper
    /// (<c>DescriptorMapper.MapToComponents</c>) rebuilds the component. ⛔ Without it the creator builds a bare type-7202 entity that no
    /// system ever ends (found by <c>EntityOrdersAcrossNodesTests.CE3219_…</c>: no cloud on any node).
    /// </summary>
    [Fact]
    public void CE3228_ACloud_SurvivesTheWireRoundTrip()
    {
        var (egress, writer) = NewEgress();
        var cloud = new Fdp.Toolkit.Combat.AreaEffectCloud
        {
            Kind = Fdp.Toolkit.Combat.CloudKind.TearGas, Radius = 6f, Height = 3f, SightTransmittance = 0.4f, IrritantSeconds = 2f,
            BornAtSimSeconds = 12.5, Seconds = 60f,
        };

        egress.Send(new EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 0,
            TkbType            = Fdp.Toolkit.Combat.AreaEffectClouds.TkbType,
            InitialComponents  = new List<object> { new SimTransform { Position = new Vector3(5f, 5f, 0f) }, cloud },
        });

        var sample = Assert.Single(writer.Publishes);
        Assert.Single(sample.InitialDescriptors, d => d._d == EDescriptorType.dtAreaEffectCloud);
        var back = Assert.Single(Hrot.Map.Common.Replication.Utils.DescriptorMapper
            .MapToComponents(sample.InitialDescriptors, geoTransform: null).OfType<Fdp.Toolkit.Combat.AreaEffectCloud>());
        Assert.Equal(cloud, back);
    }
}
