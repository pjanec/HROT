using Fdp.Toolkit.Combat.Components;
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
            _repo.RegisterComponent<Health>();
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
        /// <para>⭐ Culling is not lost, it moved — <c>S4</c> put it in
        /// <c>CullingStateVisibilityPolicy</c>, which <c>StatelessGizmoSystem</c> consults per entity
        /// BEFORE calling <c>Draw</c>. IG keeps it; every host can now attach it to ANY projector
        /// (<c>R-137</c>).</para>
        /// </summary>
        [Fact]
        public void TheQuery_MustNotRequireCullingState()
        {
            var attr = typeof(EntityPresentationGizmo).GetCustomAttribute<GizmoProjectorAttribute>();

            Assert.NotNull(attr);
            Assert.DoesNotContain(typeof(CullingState), attr!.RequiredComponents);
            Assert.DoesNotContain(typeof(Health), attr!.RequiredComponents);
            Assert.Equal(2, attr!.RequiredComponents.Length);
        }

        // ── CE-1058 — a carried passenger ───────────────────────────────────────────────────────

        /// <summary>⭐ <c>CE-1058</c> — a passenger inside an ENCLOSED vehicle draws nothing on the 2-D map (no anchor, no pick box, no
        /// symbol, no card); an exposed rider (a motorcycle, a bike, an open-door helicopter) draws as anyone. The engine's one rule:
        /// <c>PhysicalPresence.IsEnclosedPassenger</c> (R-271).</summary>
        [Fact]
        public void CE1058_AnEnclosedPassenger_DrawsNothing_AnExposedRiderDraws()
        {
            if (!_repo.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>())
                _repo.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
            var vehicle = Spawn(10L, new Vector3(50f, 50f, 0f));
            var inside = Spawn(11L, new Vector3(50f, 50f, 0f));
            _repo.AddComponent(inside, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = vehicle, Exposed = 0 });
            var rider = Spawn(12L, new Vector3(50f, 50f, 0f));
            _repo.AddComponent(rider, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = vehicle, Exposed = 1 });

            var hidden = new DebugPrimitiveBuffer();
            new EntityPresentationGizmo().Draw(_repo, inside, hidden);
            new EntityEditorLabelGizmo().Draw(_repo, inside, hidden);
            Assert.Equal(0, hidden.GetFrame().Length);

            var shown = new DebugPrimitiveBuffer();
            new EntityPresentationGizmo().Draw(_repo, rider, shown);
            Assert.Contains(shown.GetFrame().ToArray(), p => p.Shape == DebugPrimitiveShape.SpatialAnchor && p.NetworkId == 12L);
        }

        /// <summary>⭐ <c>CE-1068</c> — a TERRAIN OBJECT (a door) keeps its anchor and pick box (it is selectable — the door commands
        /// hang off it) but draws NO symbol: it used to draw the unknown-type fallback (magenta) for every door. 📄
        /// DESIGN_Building_Interiors.md §3j 5b: "no symbol, no palette entry".</summary>
        [Fact]
        public void CE1068_ADoor_HasAnAnchorAndAPickBox_ButNoSymbol()
        {
            var door = Spawn(70L, new Vector3(5f, 5f, 0f));
            _repo.RegisterManagedComponent<Fdp.Toolkit.Terrain.TerrainObjectKey>();   // a fresh repository per test (xUnit)
            _repo.AddComponent(door, new Fdp.Toolkit.Terrain.TerrainObjectKey { Key = "t/b/d1" });

            var buffer = new DebugPrimitiveBuffer();
            new EntityPresentationGizmo().Draw(_repo, door, buffer);
            var frame = buffer.GetFrame().ToArray();
            Assert.Contains(frame, p => p.Shape == DebugPrimitiveShape.SpatialAnchor && p.NetworkId == 70L);
            Assert.Contains(frame, p => p.Shape == DebugPrimitiveShape.Box2D);   // the pick box
            Assert.DoesNotContain(frame, p => p.Shape == DebugPrimitiveShape.SemanticShape);
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

        // ── ③ culling — MOVED to CullingStateVisibilityPolicy by S4 ───────────────────────────
        //
        // The culling rails re-homed to MapCullingPolicyTests when S4 moved the logic out of Draw and into
        // IGizmoVisibilityPolicy (ruling 9: one implementation of "should this entity draw?"). What stays
        // here is the claim that belongs to the PROJECTOR: it draws every entity it is handed, and never
        // second-guesses visibility itself.

        /// <summary>
        /// 🔴 <b>The projector does not filter.</b> Handed an entity whose <c>CullingState</c> says hidden,
        /// it still draws — because deciding that is the POLICY's job now, and
        /// <c>StatelessGizmoSystem</c> applies the policy before ever calling <c>Draw</c>.
        ///
        /// <para>⚠ This is the inverse of what this file asserted before <c>S4</c>, deliberately. A
        /// projector that also filtered would be the second mechanism ruling 9 forbids.</para>
        /// </summary>
        [Fact]
        public void TheProjector_DrawsWhateverItIsHanded_AndNeverFiltersOnVisibility()
        {
            var entity = Spawn(1L, new Vector3(10f, 20f, 0f));
            _repo.AddComponent(entity, new CullingState { IsVisible = false });

            var buffer = new DebugPrimitiveBuffer();
            new EntityPresentationGizmo().Draw(_repo, entity, buffer);

            Assert.True(buffer.GetFrame().Length >= 3,
                "The projector must draw whatever it is handed. Visibility is decided by "
              + "CullingStateVisibilityPolicy, which StatelessGizmoSystem consults BEFORE Draw — a "
              + "projector that filtered too would be a second mechanism for one concept (ruling 9).");
        }

        /// <summary>⭐ And with no CullingState at all, unchanged — it simply draws.</summary>
        [Fact]
        public void TheProjector_DrawsWhenThereIsNoCullingStateAtAll()
        {
            var entity = Spawn(3L, new Vector3(10f, 20f, 0f));
            Assert.False(_repo.HasComponent<CullingState>(entity));

            var buffer = new DebugPrimitiveBuffer();
            new EntityPresentationGizmo().Draw(_repo, entity, buffer);

            Assert.True(buffer.GetFrame().Length >= 3);
        }

        // ── ④ condition mask — IG's capability, now presence-decided (R-137) ───────────────────

        /// <summary>⭐ Re-homed from IG's <c>SC_GZ057_7</c>.</summary>
        [Fact]
        public void Draw_WithHighDamage_SetsTheDamagedConditionBit()
        {
            var entity = Spawn(5L, new Vector3(10f, 20f, 0f));
            _repo.AddComponent(entity, new Health { Current = 25f, Max = 100f });

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
            _repo.AddComponent(entity, new Health { Current = 5f, Max = 100f });

            var buffer = new DebugPrimitiveBuffer();
            new EntityPresentationGizmo().Draw(_repo, entity, buffer);

            var semantic = buffer.GetFrame()[2];
            Assert.NotEqual(0u, semantic.ConditionMask & ConditionDamaged);
            Assert.NotEqual(0u, semantic.ConditionMask & ConditionImmobile);
        }

        /// <summary>⭐ W2 (docs/DESIGN_Visual_Effects.md §7 VE-O) — a wreck (Health ≤ 0, still in the world) carries the Destroyed bit
        /// the renderer draws as the MIL-STD-2525 X; a badly hurt but living unit does not.</summary>
        [Fact]
        public void Draw_WhenDestroyed_SetsTheDestroyedBit_TheMilStdX()
        {
            var wreck = Spawn(7L, new Vector3(10f, 20f, 0f));
            _repo.AddComponent(wreck, new Health { Current = 0f, Max = 100f });
            var hurt = Spawn(8L, new Vector3(30f, 20f, 0f));
            _repo.AddComponent(hurt, new Health { Current = 1f, Max = 100f });

            var buffer = new DebugPrimitiveBuffer();
            new EntityPresentationGizmo().Draw(_repo, wreck, buffer);
            Assert.Equal((uint)GizmoMap.Presentation.Shapes.EntityShapeCondition.Destroyed, buffer.GetFrame()[2].ConditionMask & 4u);

            buffer = new DebugPrimitiveBuffer();
            new EntityPresentationGizmo().Draw(_repo, hurt, buffer);
            Assert.Equal(0u, buffer.GetFrame()[2].ConditionMask & 4u);
        }

        /// <summary>⭐ NEW — no health component means an UNDAMAGED mask, not a missing shape.</summary>
        [Fact]
        public void Draw_WithNoHealthState_EmitsAZeroConditionMask()
        {
            var entity = Spawn(8L, new Vector3(10f, 20f, 0f));
            Assert.False(_repo.HasComponent<Health>(entity));

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

            // ⚠ The network id lands in BoxAnchorId, NOT NetworkId: MakeBox2D routes its `anchorId`
            // argument to `p.BoxAnchorId` (DebugPrimitive.cs:264,277), and on a Box2D the NetworkId field
            // is overlapped by the box geometry — reading it back yields the packed extents as garbage.
            // ⭐⭐⭐ §6.7 — AND THAT FIELD IS NOW THE WHOLE PAIRING. This rail used to also assert
            //   `pick.AnchorIndex == entity.Index` and `pick.AnchorGeneration == entity.Generation`,
            //   saying "together with AnchorIndex/AnchorGeneration this pairing is what resolves a click
            //   to an entity". ⛔ It no longer is: the ECS handle is not emitted, and a click resolves
            //   BoxAnchorId against the world. ⇒ the rail now pins that offsets 8/12 are LEFT CLEAN,
            //   which is the property that replaced it.
            // ⛔ RED-PROOF SHAPE: re-add the two stamps in EmitPickBox and the two zero asserts redden.
            // 📄 docs/DESIGN_Gizmo_Anchor_Identity.md §6.7.
            var pick = frame[1];
            Assert.Equal(DebugPrimitiveShape.Box2D, pick.Shape);
            Assert.Equal(11L, pick.BoxAnchorId);
            Assert.Equal(0, pick.AnchorIndex);
            Assert.Equal((ushort)0, pick.AnchorGeneration);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>§6.8 — AN ENTITY WHOSE NETWORK ID IS 0 EMITS NOTHING.</b>
        /// 📄 <c>docs/DESIGN_Gizmo_Anchor_Identity.md</c> §6.8.
        ///
        /// <para>⛔ The <c>[GizmoProjector(SimTransform, NetworkIdentity)]</c> attribute guarantees the
        /// component is PRESENT, never that its value is usable. With <c>0</c> this gizmo used to emit
        /// THREE broken primitives from one unset field: a <c>SpatialAnchor</c> nothing can reference, a
        /// pick box that wins the hit-test and resolves to nothing, and an <c>EntityLocal</c>
        /// <c>SemanticShape</c> that resolves against no anchor.</para>
        ///
        /// <para>⚠ An entity whose id has not been allocated yet is a NORMAL transient state — it gets
        /// its avatar on the frame the id exists. ⛔ RED-PROOF SHAPE: delete the
        /// <c>if (networkId == 0) return;</c> guard and this reddens on a non-empty frame.</para>
        /// </summary>
        [Fact]
        public void Draw_EmitsNothing_WhenTheNetworkIdIsZero()
        {
            var entity = Spawn(0L, new Vector3(30f, 40f, 0f));

            var buffer = new DebugPrimitiveBuffer();
            new EntityPresentationGizmo().Draw(_repo, entity, buffer);

            Assert.Empty(buffer.GetFrame().ToArray());
        }

        /// <summary>
        /// ⭐⭐ <b>The counter-case, so the guard cannot over-refuse.</b> A real id still emits the full
        /// set. ⛔ Without this pairing the rail above would also pass if the gizmo drew nothing ever.
        /// </summary>
        [Fact]
        public void Draw_StillEmits_WhenTheNetworkIdIsReal()
        {
            var entity = Spawn(11L, new Vector3(30f, 40f, 0f));

            var buffer = new DebugPrimitiveBuffer();
            new EntityPresentationGizmo().Draw(_repo, entity, buffer);

            Assert.True(buffer.GetFrame().Length >= 3);
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

        // ── ⑦ CE-1163 (civilian U-2; UX_Feature_Entity_Symbology §3.1 — CE-125 defect A) — the side's colour ─────────────

        private Rgba32 OutlineColour(Entity entity)
        {
            var buffer = new DebugPrimitiveBuffer();
            new EntityPresentationGizmo().Draw(_repo, entity, buffer);
            foreach (var p in buffer.GetFrame())
                if (p.Shape == DebugPrimitiveShape.SemanticShape) return p.Color;
            throw new InvalidOperationException("no semantic shape drawn");
        }

        /// <summary>
        /// ⭐⭐ <b>CE-1163</b> — the outline is the entity's LIVE side, from the one palette, every frame: a neutral civilian draws green and,
        /// the moment its side flips (R-3013 CV-4: a hostile act), red — no reload. An entity whose side is not known keeps the old cyan,
        /// now only the fallback. 🔒 Before: every entity on every host was the same cyan (<c>EntityPresentationGizmoShared</c>).
        /// </summary>
        [Fact]
        public void CE1163_TheOutline_IsTheLiveSide_AndRedrawsTheMomentItFlips_AnUnknownSideKeepsTheCyan()
        {
            _repo.RegisterComponent<EntityInfo>();
            var civilian = Spawn(21L, new Vector3(10f, 10f, 0f));
            _repo.AddComponent(civilian, new EntityInfo { ForceId = ForceId.Neutral });
            Assert.Equal(new Rgba32(ResolvedStyleConstants.NeutralTintR, ResolvedStyleConstants.NeutralTintG, ResolvedStyleConstants.NeutralTintB, 255),
                OutlineColour(civilian));

            _repo.SetComponent(civilian, new EntityInfo { ForceId = ForceId.Hostile });
            Assert.Equal(new Rgba32(ResolvedStyleConstants.HostileTintR, ResolvedStyleConstants.HostileTintG, ResolvedStyleConstants.HostileTintB, 255),
                OutlineColour(civilian));

            var unknown = Spawn(22L, new Vector3(20f, 10f, 0f));
            Assert.Equal(Hrot.Common.Diagnostics.Gizmos.AffiliationColors.NoSide, OutlineColour(unknown));
        }

        /// <summary>⭐ CE-1163 — where a host resolved a style (the IG's <see cref="ResolvedStyle"/>: an operator's symbol override, an authored
        /// colour), its tint wins over the side — the IG's capability is kept (R-137), not flattened to the side's colour.</summary>
        [Fact]
        public void CE1163_AResolvedTint_WinsOverTheSide()
        {
            _repo.RegisterComponent<EntityInfo>();
            _repo.RegisterComponent<ResolvedStyle>();
            var unit = Spawn(23L, new Vector3(10f, 10f, 0f));
            _repo.AddComponent(unit, new EntityInfo { ForceId = ForceId.Hostile });
            var style = ResolvedStyle.CreateDefault();
            style.Tint = new Color32 { R = 10, G = 200, B = 30, A = 255 };
            _repo.AddComponent(unit, style);
            Assert.Equal(new Rgba32(10, 200, 30, 255), OutlineColour(unit));
        }

        /// <summary>⭐ CE-1163 (Symbology §3.2) — ONE side palette: the entity card's <c>SidePalette</c> (it had its own) and the outline read the
        /// same table, so a card frame and its symbol never disagree.</summary>
        [Fact]
        public void CE1163_TheCardAndTheOutline_ReadOnePalette()
        {
            foreach (var side in new[] { ForceId.Neutral, ForceId.Friend, ForceId.Hostile })
                Assert.Equal(Hrot.Common.Diagnostics.Gizmos.AffiliationColors.Of(side), Hrot.Common.Diagnostics.Gizmos.SidePalette.Of(side));
            Assert.Equal(new Rgba32(ResolvedStyleConstants.FriendTintR, ResolvedStyleConstants.FriendTintG, ResolvedStyleConstants.FriendTintB, 255),
                Hrot.Common.Diagnostics.Gizmos.SidePalette.Friend);
        }

        /// <summary>
        /// ⭐ CE-1232 (docs/DESIGN_Visual_Effects.md §11 CH-2, R-1016) — a PLACED charge (an IED) is a side-coloured diamond reading "IED", a
        /// fixed size on screen and 2-D only, with a pick box of that size; a CARRIED one (a worn vest — AW-12) is never the marker.
        /// 🔒 User: <i>"visualization of the OCD itself - i sam just the entity label"</i>. 🔴 Red-proof: it drew a ground-vehicle outline at its
        /// 30 cm size — sub-pixel.
        /// </summary>
        [Fact]
        public void CE1232_APlacedCharge_IsTheIedMarker_AWornOneIsNot()
        {
            _repo.RegisterComponent<TkbIdentity>();
            _repo.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
            _repo.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(Hrot.Map.Common.HrotEnvironment.CreateTkb());
            var ied = Spawn(4401, new Vector3(50f, 60f, 0f));
            _repo.AddComponent(ied, new TkbIdentity { TkbType = Hrot.Map.Common.TkbEntityTypes.WeaponSystem_ExplosiveCharge });

            var draw = new DebugPrimitiveBuffer();
            new EntityPresentationGizmo().Draw(_repo, ied, draw);
            var frame = draw.GetFrame().ToArray();
            Assert.DoesNotContain(frame, p => p.Shape == DebugPrimitiveShape.SemanticShape);
            var marker = Assert.Single(frame, p => p.Shape == DebugPrimitiveShape.Box2D && !DebugPrimitiveBuffer.IsPickTarget(in p));
            Assert.Equal(SizeMode.ScreenPixels, marker.SizeMode);
            Assert.True((marker.TargetView & PipelineTarget.FlatOnly) != 0, "2-D only: the 3-D map draws the charge itself");
            Assert.Contains(frame, p => p.Shape == DebugPrimitiveShape.Text);
            var pick = Assert.Single(frame, p => DebugPrimitiveBuffer.IsPickTarget(in p));
            Assert.Equal(SizeMode.ScreenPixels, pick.SizeMode);   // clickable at every zoom, like the marker

            // worn (carried by a man): not the marker
            _repo.AddComponent(ied, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { Exposed = 1 });
            var worn = new DebugPrimitiveBuffer();
            new EntityPresentationGizmo().Draw(_repo, ied, worn);
            Assert.DoesNotContain(worn.GetFrame().ToArray(), p => p.Shape == DebugPrimitiveShape.Text);
        }
    }
}
