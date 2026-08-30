using System;
using System.Numerics;
using System.Reflection;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Replication.Components;
using Hrot.IG.Components;
using Hrot.ScenarioEditor.Gizmos;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>UXI-23</c> <c>S2</c> — the rails for the ONE entity presentation projector.</b>
    ///
    /// <para>📄 Design: <c>docs/UX/UX_Feature_Map_Parity.md</c> §3.9c (the three-way comparison) and §3.9j
    /// (the merge + the <c>R-137</c> capability ledger).</para>
    ///
    /// <para><b>These rails are RE-HOMED, not new.</b> The claims below were asserted by
    /// <c>Hrot.SimHost.Tests/Gizmos/SimHostEntityPresentationGizmoTests</c> (5) and
    /// <c>Hrot.IG.Tests/Gizmos/PresentationGizmoTests</c> (3) against the three host-private copies that
    /// <c>S2</c> merged. Every claim still holds — it now holds ONCE, over the shared projector — plus the
    /// four new ones that pin what the merge is FOR.</para>
    ///
    /// <para>⚠⚠ <b>One claim is deliberately INVERTED</b>, and it is the most important rail here:
    /// <c>SC_GZ057_5</c> used to assert that the query CONTAINS <c>CullingState</c>. It must now assert the
    /// opposite — see <see cref="TheQuery_MustNotRequireCullingState"/>.</para>
    /// </summary>
    public sealed class EntityPresentationGizmoTests : IDisposable
    {
        private const uint ConditionDamaged  = 1u << 0;
        private const uint ConditionImmobile = 1u << 1;

        private readonly EntityRepository _repo;

        public EntityPresentationGizmoTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<SimTransform>();
            _repo.RegisterComponent<NetworkIdentity>();
            _repo.RegisterComponent<CullingState>();
            _repo.RegisterComponent<IgHealthState>();
            _repo.RegisterComponent<VehicleParams>();
        }

        public void Dispose() => _repo.Dispose();

        private Entity Spawn(long networkId, Vector3 position)
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = position });
            _repo.AddComponent(entity, new NetworkIdentity(networkId));
            return entity;
        }

        // ── ① the query — the rail that keeps two hosts' maps from silently emptying ──────────

        /// <summary>
        /// ⭐ Re-homed from <c>SimHostEntityPresentationGizmoTests.SC_GZ057_1</c>.
        /// </summary>
        [Fact]
        public void TheQuery_RequiresSimTransformAndNetworkIdentity()
        {
            var attr = typeof(EntityPresentationGizmo).GetCustomAttribute<GizmoProjectorAttribute>();

            Assert.NotNull(attr);
            Assert.Contains(typeof(SimTransform),    attr!.RequiredComponents);
            Assert.Contains(typeof(NetworkIdentity), attr!.RequiredComponents);
        }

        /// <summary>
        /// 🔴🔴 <b>The INVERSION of IG's old <c>SC_GZ057_5</c>, and the highest-value rail in this file.</b>
        ///
        /// <para>IG's copy required <c>CullingState</c> in its <c>[GizmoProjector]</c>. A requirement is a
        /// HARD MASK FILTER, not an optional input: carrying it into the merged projector would make the
        /// rule match NOTHING on SimHost and CGF — neither produces <c>CullingState</c> — and their maps
        /// would go empty with no error, no log and no failing compile. That is acceptance 23.23.</para>
        ///
        /// <para>⭐ Culling is not lost, it moved: it is presence-decided inside <c>Draw</c>, so IG keeps it
        /// and every other host GAINS it the moment it produces the component (<c>R-137</c>).</para>
        /// </summary>
        [Fact]
        public void TheQuery_MustNotRequireCullingState()
        {
            var attr = typeof(EntityPresentationGizmo).GetCustomAttribute<GizmoProjectorAttribute>();

            Assert.NotNull(attr);
            Assert.DoesNotContain(typeof(CullingState), attr!.RequiredComponents);
            Assert.DoesNotContain(typeof(IgHealthState), attr!.RequiredComponents);
            Assert.Equal(2, attr!.RequiredComponents.Length);
        }

        // ── ② the emitted frame — re-homed from SimHost ────────────────────────────────────────

        /// <summary>⭐ Re-homed from <c>SC_GZ057_2</c>.</summary>
        [Fact]
        public void Draw_EmitsSpatialAnchorWithTheNetworkId()
        {
            var entity = Spawn(42L, new Vector3(100f, 200f, 5f));

            var buffer = new DebugPrimitiveBuffer();
            new EntityPresentationGizmo().Draw(_repo, entity, buffer);

            var frame = buffer.GetFrame();
            Assert.True(frame.Length >= 1);

            var anchor = frame[0];
            Assert.Equal(DebugPrimitiveShape.SpatialAnchor, anchor.Shape);
            Assert.Equal(42L,  anchor.NetworkId);
            Assert.Equal(100f, anchor.AnchorWorldX);
            Assert.Equal(200f, anchor.AnchorWorldY);
        }

        /// <summary>⭐ Re-homed from <c>SC_GZ057_3</c>. Frame order: [0] anchor, [1] pick box, [2] shape.</summary>
        [Fact]
        public void Draw_EmitsSemanticShapeAnchoredToTheNetworkId()
        {
            var entity = Spawn(99L, new Vector3(50f, 60f, 0f));

            var buffer = new DebugPrimitiveBuffer();
            new EntityPresentationGizmo().Draw(_repo, entity, buffer);

            var frame = buffer.GetFrame();
            Assert.True(frame.Length >= 3);

            var semantic = frame[2];
            Assert.Equal(DebugPrimitiveShape.SemanticShape, semantic.Shape);
            Assert.Equal(CoordinateSpace.EntityLocal, semantic.Space);
            Assert.Equal(99, semantic.AnchorIndex);
        }

        /// <summary>
        /// ⭐ Re-homed from <c>SC_GZ057_5</c> (SimHost's, not IG's): the regression where
        /// <c>entity.Index</c> was passed as the anchor index instead of <c>(int)networkId</c>, so the
        /// renderer's anchor cache — keyed by networkId — missed on every shape.
        /// </summary>
        [Fact]
        public void Draw_AnchorIndexIsTheNetworkId_NotTheEntityIndex()
        {
            var dummy = Spawn(0L, default);
            Assert.True(dummy.Index >= 0);

            const long networkId = 9999L;
            var entity = Spawn(networkId, new Vector3(10f, 20f, 0f));
            Assert.NotEqual((int)networkId, entity.Index);

            var buffer = new DebugPrimitiveBuffer();
            new EntityPresentationGizmo().Draw(_repo, entity, buffer);

            var frame = buffer.GetFrame();
            Assert.True(frame.Length >= 3);

            var semantic = frame[2];
            Assert.Equal(DebugPrimitiveShape.SemanticShape, semantic.Shape);
            Assert.Equal((int)networkId, semantic.AnchorIndex);
            Assert.NotEqual(entity.Index, semantic.AnchorIndex);

            var anchor = frame[0];
            Assert.Equal(networkId, anchor.NetworkId);
            Assert.Equal(anchor.NetworkId, (long)semantic.AnchorIndex);
        }

        /// <summary>⭐ Re-homed from <c>SC_GZ057_4</c>.</summary>
        [Fact]
        public void Draw_WithVehicleParams_EmitsItsDimensions()
        {
            var entity = Spawn(7L, Vector3.Zero);
            _repo.AddComponent(entity, new VehicleParams { Length = 8f, Width = 3f });

            var buffer = new DebugPrimitiveBuffer();
            new EntityPresentationGizmo().Draw(_repo, entity, buffer);

            var frame = buffer.GetFrame();
            Assert.True(frame.Length >= 3);

            var semantic = frame[2];
            Assert.Equal(DebugPrimitiveShape.SemanticShape, semantic.Shape);
            Assert.Equal(8f, semantic.LengthMeters);
            Assert.Equal(3f, semantic.WidthMeters);
        }

        // ── ③ culling — IG's capability, now presence-decided (R-137) ──────────────────────────

        /// <summary>
        /// A registry with <c>map.entity.cullOffscreen</c> turned on — what a host does when it wants
        /// IG's culling. The projector registers its own defaults in the constructor, so this only has to
        /// overwrite the one value.
        /// </summary>
        private static GizmoSettingsRegistry CullingEnabled()
        {
            var settings = new GizmoSettingsRegistry();
            EntityPresentationGizmoSettings.Register(settings);
            settings.Write(
                GizmoSettingsRegistry.ComputeHash(EntityPresentationGizmoSettings.CullOffscreen),
                GizmoSettingValue.From(true));
            return settings;
        }

        /// <summary>
        /// ⭐ Re-homed from IG's <c>SC_GZ057_6</c>: an invisible entity draws nothing —
        /// <b>when the host has asked for culling.</b>
        /// </summary>
        [Fact]
        public void Draw_WithCullingEnabled_SkipsTheEntityWhenCullingStateSaysHidden()
        {
            var entity = Spawn(1L, new Vector3(10f, 20f, 0f));
            _repo.AddComponent(entity, new CullingState { IsVisible = false });

            var buffer = new DebugPrimitiveBuffer();
            new EntityPresentationGizmo(CullingEnabled()).Draw(_repo, entity, buffer);

            Assert.Equal(0, buffer.GetFrame().Length);
        }

        /// <summary>⭐ The other half: a VISIBLE entity still draws — so the gate is a gate, not an off switch.</summary>
        [Fact]
        public void Draw_WithCullingEnabled_DrawsTheEntityWhenCullingStateSaysVisible()
        {
            var entity = Spawn(2L, new Vector3(10f, 20f, 0f));
            _repo.AddComponent(entity, new CullingState { IsVisible = true });

            var buffer = new DebugPrimitiveBuffer();
            new EntityPresentationGizmo(CullingEnabled()).Draw(_repo, entity, buffer);

            Assert.True(buffer.GetFrame().Length >= 3);
        }

        /// <summary>
        /// 🔴🔴 <b>The measured default (<c>R-137</c>).</b> Culling is OFF unless a host asks, so a
        /// <c>CullingState</c> the host does not actually maintain cannot blank its map.
        ///
        /// <para>📐 This is not a precaution — with culling on by default the IG perspective emitted ZERO
        /// anchors and shapes in a live <c>--mode all</c> run, because <c>MapCullingSystem</c> derives
        /// <c>IsVisible</c> from a screen-corner viewport that is degenerate without a real map view.
        /// Before the merge that was masked: IG's map was rendered by SimHost's and CGF's copies, which
        /// ignored culling. The culling INPUT is filed separately as <c>CE-131</c>.</para>
        /// </summary>
        [Fact]
        public void Draw_ByDefault_IgnoresCullingStateEntirely()
        {
            var entity = Spawn(4L, new Vector3(10f, 20f, 0f));
            _repo.AddComponent(entity, new CullingState { IsVisible = false });

            var buffer = new DebugPrimitiveBuffer();
            new EntityPresentationGizmo().Draw(_repo, entity, buffer);

            Assert.True(buffer.GetFrame().Length >= 3,
                "Culling must be OPT-IN. A host that carries CullingState but does not maintain it must "
              + "still get a map — measured live: culling on by default blanked the IG perspective.");
        }

        /// <summary>⭐ And the same when the settings registry exists but nobody turned culling on.</summary>
        [Fact]
        public void Draw_WithSettingsButCullingNotEnabled_IgnoresCullingState()
        {
            var entity = Spawn(9L, new Vector3(10f, 20f, 0f));
            _repo.AddComponent(entity, new CullingState { IsVisible = false });

            var settings = new GizmoSettingsRegistry();
            var buffer = new DebugPrimitiveBuffer();
            new EntityPresentationGizmo(settings).Draw(_repo, entity, buffer);

            Assert.True(buffer.GetFrame().Length >= 3);
        }

        /// <summary>
        /// ⭐⭐ <b>The <c>R-137</c> rail: the capability is REACHABLE.</b> A setting nobody can turn on is
        /// the same as a deleted feature, so this pins that the registry actually carries the three keys.
        /// </summary>
        [Fact]
        public void TheProjector_RegistersItsConfigurationSurface()
        {
            var settings = new GizmoSettingsRegistry();
            _ = new EntityPresentationGizmo(settings);

            // The two thresholds prove registration by their values — a key nobody registered reads back
            // as default(GizmoSettingValue), which is 0f, not 50f/90f.
            Assert.Equal(50f, settings.Read(
                GizmoSettingsRegistry.ComputeHash(EntityPresentationGizmoSettings.DamagedThreshold)).FloatValue);
            Assert.Equal(90f, settings.Read(
                GizmoSettingsRegistry.ComputeHash(EntityPresentationGizmoSettings.ImmobileThreshold)).FloatValue);

            // ⚠ The bool cannot be proven the same way: `false` is byte-identical to
            // default(GizmoSettingValue) (Type=Bool=0, payload=0), so reading it back says nothing about
            // whether it was registered. What R-137 actually cares about is that the capability is
            // REACHABLE — so assert the default is off AND that a host can turn it on and have the
            // projector obey, which is the whole point of making it a setting.
            uint cullKey = GizmoSettingsRegistry.ComputeHash(EntityPresentationGizmoSettings.CullOffscreen);
            Assert.False(settings.Read(cullKey).BoolValue);

            settings.Write(cullKey, GizmoSettingValue.From(true));
            Assert.True(settings.Read(cullKey).BoolValue);

            var hidden = _repo.CreateEntity();
            _repo.AddComponent(hidden, new SimTransform());
            _repo.AddComponent(hidden, new NetworkIdentity(77L));
            _repo.AddComponent(hidden, new CullingState { IsVisible = false });

            var buffer = new DebugPrimitiveBuffer();
            new EntityPresentationGizmo(settings).Draw(_repo, hidden, buffer);
            Assert.Equal(0, buffer.GetFrame().Length);
        }

        /// <summary>
        /// ⭐⭐ <b>The thresholds are configuration, not IG's literals.</b> Lowering the damaged threshold
        /// must change the mask — otherwise the setting is decorative.
        /// </summary>
        [Fact]
        public void TheDamageThresholds_AreHonouredFromSettings()
        {
            var entity = Spawn(10L, new Vector3(10f, 20f, 0f));
            _repo.AddComponent(entity, new IgHealthState { Damage = 20f });

            // Default (50/90): 20 damage is neither.
            var plain = new DebugPrimitiveBuffer();
            new EntityPresentationGizmo().Draw(_repo, entity, plain);
            Assert.Equal(0u, plain.GetFrame()[2].ConditionMask);

            // A host that wants a hair trigger sets 10/15 and gets both bits at the same damage.
            var settings = new GizmoSettingsRegistry();
            EntityPresentationGizmoSettings.Register(settings);
            settings.Write(GizmoSettingsRegistry.ComputeHash(EntityPresentationGizmoSettings.DamagedThreshold),
                GizmoSettingValue.From(10f));
            settings.Write(GizmoSettingsRegistry.ComputeHash(EntityPresentationGizmoSettings.ImmobileThreshold),
                GizmoSettingValue.From(15f));

            var tuned = new DebugPrimitiveBuffer();
            new EntityPresentationGizmo(settings).Draw(_repo, entity, tuned);

            var semantic = tuned.GetFrame()[2];
            Assert.NotEqual(0u, semantic.ConditionMask & ConditionDamaged);
            Assert.NotEqual(0u, semantic.ConditionMask & ConditionImmobile);
        }

        /// <summary>
        /// 🔴🔴 <b>NEW — the rail that proves SimHost and CGF still have a map.</b>
        ///
        /// <para>Neither host produces <c>CullingState</c>. If the merged projector treated an ABSENT
        /// culling component as "not visible" — the natural way to get this wrong — both hosts would go
        /// dark, which is the very symptom <c>S2</c> exists to fix. Absence must mean DRAW.</para>
        /// </summary>
        [Fact]
        public void Draw_WithNoCullingStateAtAll_StillDraws()
        {
            var entity = Spawn(3L, new Vector3(10f, 20f, 0f));
            Assert.False(_repo.HasComponent<CullingState>(entity));

            var buffer = new DebugPrimitiveBuffer();
            new EntityPresentationGizmo().Draw(_repo, entity, buffer);

            Assert.True(buffer.GetFrame().Length >= 3,
                "An entity with no CullingState must draw. SimHost and CGF produce none, so treating "
              + "absence as 'hidden' empties their maps — the exact CE-123 symptom S2 fixes.");
        }

        // ── ④ condition mask — IG's capability, now presence-decided (R-137) ───────────────────

        /// <summary>⭐ Re-homed from IG's <c>SC_GZ057_7</c>.</summary>
        [Fact]
        public void Draw_WithHighDamage_SetsTheDamagedConditionBit()
        {
            var entity = Spawn(5L, new Vector3(10f, 20f, 0f));
            _repo.AddComponent(entity, new IgHealthState { Damage = 75f });

            var buffer = new DebugPrimitiveBuffer();
            new EntityPresentationGizmo().Draw(_repo, entity, buffer);

            var frame = buffer.GetFrame();
            Assert.True(frame.Length >= 3);

            var semantic = frame[2];
            Assert.Equal(DebugPrimitiveShape.SemanticShape, semantic.Shape);
            Assert.NotEqual(0u, semantic.ConditionMask & ConditionDamaged);
            Assert.Equal(0u,    semantic.ConditionMask & ConditionImmobile);
        }

        /// <summary>⭐ NEW — the upper threshold, so both bits are pinned, not just one.</summary>
        [Fact]
        public void Draw_WithSevereDamage_SetsBothConditionBits()
        {
            var entity = Spawn(6L, new Vector3(10f, 20f, 0f));
            _repo.AddComponent(entity, new IgHealthState { Damage = 95f });

            var buffer = new DebugPrimitiveBuffer();
            new EntityPresentationGizmo().Draw(_repo, entity, buffer);

            var semantic = buffer.GetFrame()[2];
            Assert.NotEqual(0u, semantic.ConditionMask & ConditionDamaged);
            Assert.NotEqual(0u, semantic.ConditionMask & ConditionImmobile);
        }

        /// <summary>⭐ NEW — no health component means an UNDAMAGED mask, not a missing shape.</summary>
        [Fact]
        public void Draw_WithNoHealthState_EmitsAZeroConditionMask()
        {
            var entity = Spawn(8L, new Vector3(10f, 20f, 0f));
            Assert.False(_repo.HasComponent<IgHealthState>(entity));

            var buffer = new DebugPrimitiveBuffer();
            new EntityPresentationGizmo().Draw(_repo, entity, buffer);

            var frame = buffer.GetFrame();
            Assert.True(frame.Length >= 3);
            Assert.Equal(0u, frame[2].ConditionMask);
        }

        // ── ⑤ the two CGF defects the merge fixes — CE-126 ─────────────────────────────────────

        /// <summary>
        /// 🔴 <b><c>CE-126(b)</c>.</b> CGF's copy omitted <c>EmitPickBox</c> entirely, so CGF entities
        /// could not be picked at all. The merged projector emits it for every host.
        /// </summary>
        [Fact]
        public void Draw_AlwaysEmitsThePickBox()
        {
            var entity = Spawn(11L, new Vector3(30f, 40f, 0f));

            var buffer = new DebugPrimitiveBuffer();
            new EntityPresentationGizmo().Draw(_repo, entity, buffer);

            var frame = buffer.GetFrame();
            Assert.True(frame.Length >= 3);

            // ⚠ The network id lands in BoxAnchorId, NOT NetworkId: MakeBox2D routes its `networkId`
            // argument to `p.BoxAnchorId` (DebugPrimitive.cs:264,277), and on a Box2D the NetworkId field
            // is overlapped by the box geometry — reading it back yields the packed extents as garbage.
            // Together with AnchorIndex/AnchorGeneration this pairing is what resolves a click to an entity.
            var pick = frame[1];
            Assert.Equal(DebugPrimitiveShape.Box2D, pick.Shape);
            Assert.Equal(11L, pick.BoxAnchorId);
            Assert.Equal(entity.Index, pick.AnchorIndex);
            Assert.Equal((ushort)entity.Generation, pick.AnchorGeneration);
        }

        /// <summary>
        /// 🔴 <b><c>CE-126(a)</c>.</b> CGF's copy called the RAW builder, which starts from
        /// <c>default(DebugPrimitive)</c> and never sets <c>Color</c> — so its avatars were emitted at
        /// <c>(0,0,0,0)</c>, fully transparent and invisible. Going through the shared helper is what
        /// forces an opaque colour, and this rail is what keeps it that way.
        /// </summary>
        [Fact]
        public void Draw_EmitsAnOpaqueSemanticShape()
        {
            var entity = Spawn(12L, new Vector3(30f, 40f, 0f));

            var buffer = new DebugPrimitiveBuffer();
            new EntityPresentationGizmo().Draw(_repo, entity, buffer);

            var semantic = buffer.GetFrame()[2];
            Assert.Equal(DebugPrimitiveShape.SemanticShape, semantic.Shape);
            Assert.True(semantic.Color.A > 0,
                "The semantic shape must be opaque. DebugPrimitiveBuffer.DrawSemanticShape leaves Color "
              + "at (0,0,0,0); only EntityPresentationGizmoShared.DrawSemanticShape sets it. CGF's copy "
              + "called the raw builder and its avatars were invisible (CE-126a).");
        }

        // ── ⑥ the merge itself ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐⭐ <b>ONE projector, not three.</b> Reflection registers every <c>[GizmoProjector]</c> in the
        /// process, so under <c>--mode all</c> two projectors with the same query would BOTH match every
        /// entity and emit the shape twice. This rail is what keeps the merge merged.
        /// </summary>
        [Fact]
        public void ExactlyOneEntityPresentationProjectorExists()
        {
            var duplicates = System.Linq.Enumerable.ToArray(
                System.Linq.Enumerable.Where(
                    GizmoReflectionRegistrar.DiscoverProjectorTypes(),
                    t => t.Name.EndsWith("EntityPresentationGizmo", StringComparison.Ordinal)));

            Assert.Single(duplicates);
            Assert.Equal(typeof(EntityPresentationGizmo), duplicates[0]);
        }
    }
}
