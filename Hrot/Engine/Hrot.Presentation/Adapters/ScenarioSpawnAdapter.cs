using System;
using System.Numerics;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis2D.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
// ⭐ `Hrot.Player.Components` is a NAMESPACE IN Hrot.Core, ⛔ not in the Hrot.Player assembly — measured
//   2026-08-27 after I wrongly called this using stale and the compiler corrected me
//   (EditablePolyline + MapOverlayStyle are declared under it in Hrot.Core/Components/Map).
//   ⇒ it carries NO Hrot.Player reference, which is why this adapter can live here at all.
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using Hrot.UI.Common.Facades;
using Hrot.ScenarioEditor.Gizmos;
using Fdp.Toolkit.Replication;
using Hrot.Core.Network;
using Hrot.Common.EntityCreation;

namespace Hrot.UI.Common.Adapters
{
    /// <summary>
    /// Implements <see cref="ISpawnController"/> for the offline editor.
    /// Translates spawn requests into <see cref="MapCanvas"/> tool activations:
    /// <list type="bullet">
    ///   <item>Entity placement → <see cref="GlobalGizmoManager"/> (wrapping <see cref="EntityPlacementGizmo"/>).</item>
    ///   <item>Area authoring → <see cref="PointSequenceGizmo"/> registered with <see cref="GlobalGizmoManager"/>.</item>
    ///   <item>Route authoring → <see cref="PointSequenceGizmo"/> registered with <see cref="GlobalGizmoManager"/>.</item>
    /// </list>
    /// No DDS or CycloneDDS references; all dispatch is done through the in-process
    /// <see cref="FdpEventBus"/>.
    /// </summary>
    public sealed class ScenarioSpawnAdapter : ISpawnController, Hrot.UI.Common.AddEntity.ITerrainDrawSource
    {
        private readonly EntityCreation     _creation;
        private readonly Func<string>?      _nameResolver;
        /// <summary>⭐ CE-4312 — the numbered names handed out by recent placements (a placement's entities are created a frame later).</summary>
        private readonly System.Collections.Generic.List<string> _handedOut = new();
        private readonly JsonAttributeCompiler? _jsonCompiler;
        private readonly ITkbDatabase?      _tkbDb;
        private readonly GlobalGizmoManager? _globalGizmoManager;
        private readonly Hrot.ScenarioEditor.Tools.ToolController? _tools;
        private long?                        _activeSequenceId;
        // ⭐ Terrain authoring follow-up 2 — what the user chose for the terrain feature being drawn (the drawing panel edits it, the commit reads it)
        private Hrot.UI.Common.AddEntity.TerrainDrawOptions? _terrainDraw;
        Hrot.UI.Common.AddEntity.TerrainDrawOptions? Hrot.UI.Common.AddEntity.ITerrainDrawSource.Options => _terrainDraw;
        IReadOnlyList<System.Numerics.Vector2> Hrot.UI.Common.AddEntity.ITerrainDrawSource.Points => (IReadOnlyList<System.Numerics.Vector2>?)_areaArm?.ActiveGizmo?.Session.Points ?? System.Array.Empty<System.Numerics.Vector2>();
        Hrot.UI.Common.AddEntity.TerrainGhostContext? Hrot.UI.Common.AddEntity.ITerrainDrawSource.Context => TerrainContext?.Invoke();   // ⭐ CE-4592 — the ghost's context: the ground read-outs
        void Hrot.UI.Common.AddEntity.ITerrainDrawSource.Finish() => _areaArm?.ActiveGizmo?.OnKeyEvent(Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapKeyboardKey.Enter, true);
        void Hrot.UI.Common.AddEntity.ITerrainDrawSource.Cancel() => _areaArm?.ActiveGizmo?.OnKeyEvent(Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapKeyboardKey.Escape, true);
        /// <summary>⭐ Terrain authoring follow-up 2 — the scatter recipes the drawing panel offers (the host sets them from the recipes that carry a <c>scatter</c> section); empty ⇒ none.</summary>
        public System.Collections.Generic.IReadOnlyList<(string Id, string Name, float Density)> ScatterRecipes { get; set; } = System.Array.Empty<(string, string, float)>();

        /// <summary>
        /// ⭐ <c>E5</c> — the ONE area-authoring mechanism, shared with IG. Created lazily because it
        /// needs the gizmo manager, which is optional on this adapter.
        /// </summary>
        private Hrot.ScenarioEditor.Tools.AreaAuthoringArm? _areaArm;

        // ⚠⚠ The per-invocation parameters, held between Activate() and the arm body — the same shape
        //    EditorZoneAdapter and MapCommandController use, because ToolActivation takes only an Entity.
        // ⭐⭐ _pendingPropertiesJson is CONSUMED by the arm (read-then-clear), and that is load-bearing:
        //    it is what keeps the toolbar's behaviour IDENTICAL to before this split. 📐 Before, the
        //    toolbar path was StartPlacementModeWithLastType() => StartPlacementMode(last, json: null),
        //    i.e. the toolbar always cleared the properties. If the field merely PERSISTED, a toolbar
        //    press after an ORBAT create would silently re-apply that unit's affiliation JSON.
        private string? _pendingPropertiesJson;
        private string  _pendingStyleJson = string.Empty;
        // ⭐ CE-1161 (Weather and Time §7 E-4) — the TKB type the next area tool activation births (a Weather Region, 8811); the plain
        //   StartAreaAuthoringMode resets it to the tactical area.
        private TkbTypeId    _pendingAreaTkbType = (Fdp.Core.TkbTypeId)(TkbEntityTypes.TacGraphic_Area);

        /// <summary>
        /// The TKB entity type most recently passed to <see cref="StartPlacementMode"/>.
        /// Used by <see cref="StartPlacementModeWithLastType"/> to re-activate placement
        /// without needing a new type selection.
        /// </summary>
        public TkbTypeId LastSelectedTkbType { get; private set; } = (Fdp.Core.TkbTypeId)(TkbEntityTypes.Tank_M1Abrams);

        /// <param name="creation">⭐ <c>CE-259ax</c> — the host's creation pack: every affordance (placement, area, zone, route) ends in ONE call,
        /// <see cref="EntityCreation.RequestEntityCreation"/> (Entity Authoring Surface G1 / acceptance ⑧, §4). ⛔ This adapter no longer maps a
        /// request itself — that was a second copy of what the affordance does.</param>
        /// <param name="nameResolver">⭐ <c>G2</c> — optional: supplies the NAME of each placed entity (a session-sequential generator).
        /// It replaces the TKB-default name; an explicit <c>name</c> in the properties JSON still wins, being compiled on top.</param>
        /// <param name="jsonCompiler">
        /// The JSON->ECS attribute compiler (from <c>AttributeCompilerFactory.Build</c>).
        /// Used to parse <c>InitialAttributesJson</c> into concrete ECS component objects
        /// so the offline spawning pipeline attaches <see cref="EntityInfo"/> (name,
        /// affiliation) without going through the DDS pipeline.
        /// </param>
        /// <param name="tkbDb">
        /// Optional TKB database used to look up the default entity name for the
        /// baseline <see cref="EntityInfo"/> component. When <c>null</c>, the
        /// name defaults to <c>"New Unit"</c>.
        /// </param>
        /// <param name="tools">
        /// 🔒 <b>The host's arbiter</b> — <c>MapInteraction.Tools</c>. ⛔ Optional so tests and
        /// unconverted hosts still work, but a production caller that HAS it must PASS it.
        ///
        /// <para>⚠⚠ <b>This adapter registers only <c>PlaceArea</c> and <c>PlaceRoute</c> — NOT
        /// <c>Spawn</c>.</b> 📐 <c>Spawn</c> is registered by <c>MapInteractionPack.Build</c> (its arm calls
        /// the pack's <c>StartPlacementMode</c> delegate), and the duplicate-id guard is strict, so
        /// registering it here too would throw at composition. ⇒ placement REACHES the arbiter by
        /// <c>Activate(Spawn)</c>, while area/route bring their own ids.</para>
        /// </param>
        public ScenarioSpawnAdapter(
            EntityCreation creation,
            JsonAttributeCompiler? jsonCompiler = null,
            ITkbDatabase? tkbDb = null,
            GlobalGizmoManager? globalGizmoManager = null,
            Hrot.ScenarioEditor.Tools.ToolController? tools = null,
            Func<string>? nameResolver = null)
        {
            _creation     = creation ?? throw new ArgumentNullException(nameof(creation));
            _nameResolver = nameResolver;
            _jsonCompiler = jsonCompiler;
            _tkbDb        = tkbDb;
            _globalGizmoManager = globalGizmoManager;
            _tools        = tools;

            // ⭐ Registered ONCE, here — ⛔ not per activation (the G4 duplicate-id lesson).
            _tools?.Register(
                new Hrot.ScenarioEditor.Tools.ToolDescriptor(
                    Hrot.ScenarioEditor.Tools.ScenarioToolIds.PlaceArea,
                    "Draw Area",
                    Hrot.ScenarioEditor.Tools.ToolModality.Modal,
                    Hrot.ScenarioEditor.Tools.ToolArbiter.Global,
                    Hint: Hrot.ScenarioEditor.Tools.DrawingMenu.AreaHint, Glyph: Fdp.Presentation.Fonts.IconsFontAwesome6.DrawPolygon),   // ⭐ B-17 P2 — the D11 words, stated once
                _ => ArmAreaAuthoring(_pendingAreaTkbType));

            _tools?.Register(
                new Hrot.ScenarioEditor.Tools.ToolDescriptor(
                    Hrot.ScenarioEditor.Tools.ScenarioToolIds.PlaceRoute,
                    "Draw Route",
                    Hrot.ScenarioEditor.Tools.ToolModality.Modal,
                    Hrot.ScenarioEditor.Tools.ToolArbiter.Global,
                    Hint: Hrot.ScenarioEditor.Tools.DrawingMenu.RouteHint, Glyph: Fdp.Presentation.Fonts.IconsFontAwesome6.Route),   // ⭐ B-17 P2 — the D11 words, stated once
                _ => ArmRouteAuthoring());

            // ⭐⭐ E5 — a terrain zone is drawn by the SAME mechanism with a different TkbType, so the
            //   tool costs one registration. ⛔ Without it `TerrainZone` (B1) would be a type nothing
            //   can author — R-133's "a capability reported present that silently no-ops" in its
            //   other direction: a whole stage of zone surfaces (E1 gizmo, E2 menu, E3 view) with no
            //   way to make a zone in the first place.
            _tools?.Register(
                new Hrot.ScenarioEditor.Tools.ToolDescriptor(
                    Hrot.ScenarioEditor.Tools.ScenarioToolIds.PlaceZone,
                    "Draw Zone",
                    Hrot.ScenarioEditor.Tools.ToolModality.Modal,
                    Hrot.ScenarioEditor.Tools.ToolArbiter.Global,
                    Hint: Hrot.ScenarioEditor.Tools.DrawingMenu.AreaHint, Glyph: Fdp.Presentation.Fonts.IconsFontAwesome6.DrawPolygon),   // ⭐ B-17 P2 — the D11 words, stated once
                _ => ArmZoneAuthoring());
        }

        /// <summary>⭐ Terrain authoring U-2 — the ground a block's ghost fits itself to (the editor's base terrain and its folder); null on a host with none (the ghost then draws nothing — the click still places).
        /// Set by <c>EntityAuthoring</c> from its inputs.</summary>
        public Func<Hrot.UI.Common.AddEntity.TerrainGhostContext?>? TerrainContext { get; set; }

        /// <inheritdoc/>
        /// <remarks>
        /// Creates an <see cref="EntityPlacementGizmo"/> registered with <see cref="GlobalGizmoManager"/>.
        /// The gizmo delegate seeds a baseline <see cref="EntityInfo"/> (so the entity always appears
        /// in the ORBAT tree) then uses the shared <see cref="JsonAttributeCompiler"/> to compile
        /// <c>InitialAttributesJson</c> overrides on top, then publishes the completed
        /// <see cref="SpawnEntityCommand"/> onto the local bus.
        /// </remarks>
        public void StartPlacementMode(long tkbType, string? initialPropertiesJson = null)
        {
            LastSelectedTkbType    = (Fdp.Core.TkbTypeId)(tkbType);
            _pendingPropertiesJson = initialPropertiesJson;

            // ⭐⭐⭐ UXI-07 step 4a — ACTIVATE through the arbiter instead of arming directly.
            //   🔴 This used to register an EntityPlacementGizmo straight on GlobalGizmoManager, which is
            //   §4.8's bypass: the gizmo took focus while ToolController still believed a tool held it.
            //   📐 Reached from ScenarioOrbatAdapter.CreateUnit and SpawnerPanel.HandleActivatePlacementTool
            //   — both shared surfaces, so both were bypassing on every host that composes this adapter.
            if (_tools != null)
            {
                _tools.Activate(Hrot.ScenarioEditor.Tools.ScenarioToolIds.Spawn);
                return;
            }

            // ⚠ No arbiter wired ⇒ arm anyway and SAY SO. 🔒 R-137: refusing would cost a capability on a
            //   host that simply has not been converted; silence would hide the bypass this step removes.
            Hrot.ScenarioEditor.Tools.ToolReport.Say(null,
                "entity placement armed WITHOUT an arbiter — ScenarioSpawnAdapter was constructed with no "
              + "ToolController, so this modal cannot displace another (UXI-07 step 4a).");
            ArmPlacement();
        }

        /// <summary>
        /// ⭐⭐⭐ <b>The arm body — and splitting it out is what BREAKS THE RECURSION.</b>
        ///
        /// <para>📐 The loop this avoids, measured: the pack's <c>Spawn</c> arm calls its
        /// <c>StartPlacementMode</c> delegate; the hosts used to point that delegate at
        /// <see cref="StartPlacementModeWithLastType"/>. ⇒ had <see cref="StartPlacementMode"/> simply
        /// called <c>Activate(Spawn)</c>, the chain would be
        /// <c>Activate → arm → StartPlacementModeWithLastType → StartPlacementMode → Activate → …</c>.
        /// ⭐ The hosts now pass THIS body instead, so the cycle cannot form.</para>
        ///
        /// <para>⚠ Reached either through <c>ToolController.Activate</c> (production) or directly when no
        /// arbiter was wired. It CONSUMES <c>_pendingPropertiesJson</c> — see the field's remarks.</para>
        /// </summary>
        public Hrot.ScenarioEditor.Tools.ToolActivationOutcome ArmPlacement()
        {
            if (_globalGizmoManager == null)
                return Hrot.ScenarioEditor.Tools.ToolActivationOutcome.Unserviceable;

            var tkbType = LastSelectedTkbType;

            // ⭐⭐ CONSUMED, not merely read — the toolbar path must see null, exactly as it did before.
            // ⭐ CE-4210 MA-7 — a macro placement rides its path in the JSON; split it out here, in the ONE point commit every host passes
            var (initialPropertiesJson, macroPath) = Hrot.UI.Common.AddEntity.MacroBirth.Split(_pendingPropertiesJson);
            // ⭐ Terrain authoring U-2 — a block / house placement rides its spec the same way; its placement state (the seed the next click uses, R's turn, the cached ghost) lives for this armed tool
            (initialPropertiesJson, var terrainSpec) = Hrot.UI.Common.AddEntity.TerrainBirth.Split(initialPropertiesJson);
            var terrainPick = Hrot.UI.Common.AddEntity.TerrainBirth.Parse(terrainSpec);
            var terrainPlacement = terrainPick is { } tp ? new Hrot.UI.Common.AddEntity.TerrainPlacement(tp.Kind, tp.Id, TerrainContext) : null;
            _pendingPropertiesJson    = null;

            var id = GlobalGizmoManager.NewId();
            Hrot.Common.Macros.MacroPreviewResult? previewCache = null;
            var gizmo = new EntityPlacementGizmo(
                onEntityCreated: cmd =>
                {
                    cmd.InitialComponents ??= new System.Collections.Generic.List<object>();
                    Hrot.UI.Common.AddEntity.MacroBirth.BornWith(cmd.TkbType, macroPath, cmd.InitialComponents);   // ⭐ CE-4210 MA-7 — the instance is born with its MacroInstance
                    Hrot.UI.Common.AddEntity.GameplayBirth.BornWith(cmd.TkbType, cmd.InitialComponents);          // ⭐ Player Join S2-a — a Deploy Role is born with its DeployRole
                    if (terrainPlacement is not null)                                                              // ⭐ Terrain authoring U-2 — a block / house is born with its TerrainBlock / TerrainFeature: the seed its ghost showed, the heading the drag and R gave
                        Hrot.UI.Common.AddEntity.TerrainBirth.BornWith(cmd.TkbType, terrainSpec, cmd.InitialTransform?.Rotation ?? Hrot.ScenarioEditor.Gizmos.EntityPlacementGizmo.FacingNorth, cmd.InitialComponents, terrainPlacement.Seed, terrainPlacement.RotationDeg);

                    // Seed a baseline EntityInfo so the entity is guaranteed to appear
                    // in the ORBAT tree even when no Name property is supplied in JSON.
                    string defaultName = "New Unit";
                    if (_tkbDb != null
                     && _tkbDb.TryGetByType(cmd.TkbType, out var template)
                     && !string.IsNullOrWhiteSpace(template.Name))
                    {
                        defaultName = template.Name;
                    }

                    // ⭐ G2 — a resolver-supplied name (the gizmo put an EntityInfo on the command) replaces the TKB default.
                    if (!cmd.InitialComponents.Exists(c => c is EntityInfo))
                        cmd.InitialComponents.Add(new EntityInfo
                        {
                            Name    = new Fdp.Core.FixedString64(defaultName),
                            ForceId = ForceId.Neutral,
                        });

                    // Apply JSON overrides (e.g. Affiliation) on top of the baseline.
                    if (!string.IsNullOrEmpty(cmd.InitialAttributesJson) && _jsonCompiler != null)
                    {
                        var ctx = new ListPatchContext(cmd.InitialComponents);
                        _jsonCompiler.Compile(cmd.InitialAttributesJson, ctx);
                        cmd.InitialComponents = ctx.FlushComponents();
                    }

                    // ⭐⭐ CE-4312 (lifecycle LC-2) — a unit still carrying its TYPE's bare name is NUMBERED, unique in the scenario ("Rifleman 3");
                    //   a resolver's or the JSON's own name is kept. The names handed out this placement count until they are in the world.
                    int infoAt = cmd.InitialComponents.FindIndex(c => c is EntityInfo);
                    if (infoAt >= 0 && cmd.InitialComponents[infoAt] is EntityInfo info && info.Name.ToString() == defaultName)
                    {
                        string numbered = Hrot.Common.EntityCreation.EntityNames.Next(_creation.World, defaultName, _handedOut);
                        _handedOut.Add(numbered);
                        if (_handedOut.Count > 256) _handedOut.RemoveAt(0);
                        info.Name = new Fdp.Core.FixedString64(numbered);
                        cmd.InitialComponents[infoAt] = info;
                    }

                    Request(cmd);
                },
                tkbType:               tkbType,
                initialPropertiesJson: initialPropertiesJson,
                // ⭐ CE-1017 S2 (D5 rev 3, approved): ALWAYS MULTI for physical entities — each click places,
                //   right-click / Esc ends. (Areas, routes and zones keep their own single-shot tools.)
                autoPopOnPlace:        false,
                nameResolver:          _nameResolver,
                onRemove:              () => _globalGizmoManager!.Unregister(id),
                displayName:           _tkbDb != null && _tkbDb.TryGetByType(tkbType, out var shown)
                                            && !string.IsNullOrWhiteSpace(shown.Name) ? shown.Name : null,
                // ⭐ CE-4244 (MA-10 MP-2 a) — a macro placement shows its ghosts at the cursor: expanded ONCE (at the origin), moved with the cursor
                macroPreview:          macroPath is null ? null : (() => previewCache ??= PreviewOf(macroPath, "{}")),
                terrain:               terrainPlacement);
            _globalGizmoManager!.Register(id, gizmo);
            return Hrot.ScenarioEditor.Tools.ToolActivationOutcome.Armed;
        }

        /// <summary>⭐ CE-4244 — the preview of <paramref name="macroPath"/> (anchored at <paramref name="anchor"/>, default the origin), from THIS node's one expander (<c>EntityCreation.MacroExpansionSystem.Expander</c>). Pure: nothing is created.</summary>
        public Hrot.Common.Macros.MacroPreviewResult PreviewOf(string macroPath, string? argsJson, Fdp.Core.SimTransform? anchor = null)
            => Hrot.Common.Macros.MacroPreview.Of(_creation.MacroExpansionSystem.Expander, macroPath, argsJson, anchor ?? new Fdp.Core.SimTransform { Rotation = System.Numerics.Quaternion.Identity },
                                                         Fdp.Toolkit.Scenario.LiveRefScope.Of(_creation.World));

        private long _previewGizmo;

        /// <summary>⭐ CE-4244 — this node's one macro expander (null on a host that cannot expand).</summary>
        public Hrot.Common.Macros.MacroExpander? ExpanderOrNull => _creation.MacroExpansionSystem.Expander;

        /// <summary>⭐ A5 — the ids this node's expansions take (a paste expands through the same expander with them).</summary>
        public Fdp.Toolkit.NetworkSpawning.INetworkIdAllocator IdAllocator => _creation.MacroExpansionSystem.IdAllocator;

        /// <summary>⭐ A5 (📄 docs/UX/DESIGN_Entity_Copy_Paste.md §3) — arms the paste tool over <paramref name="clipboard"/> (the placement gesture, the copy's
        /// ghosts); false with no gizmo manager or nothing copied.</summary>
        public bool ArmPaste(Hrot.Common.Macros.EntityClipboard clipboard)
        {
            if (_globalGizmoManager == null || !clipboard.HasCopy) return false;
            var id = GlobalGizmoManager.NewId();
            _globalGizmoManager.Register(id, new Hrot.ScenarioEditor.Gizmos.PasteGizmo(clipboard, _creation.World, () => _globalGizmoManager!.Unregister(id)));
            return true;
        }

        /// <summary>⭐ CE-4244 (MP-2 b) — shows <paramref name="preview"/> at <paramref name="anchor"/> as a gizmo (replacing one already shown); false when this host has no gizmo manager.</summary>
        public bool ShowPreview(Hrot.Common.Macros.MacroPreviewResult preview, System.Numerics.Vector3 anchor)
        {
            if (_globalGizmoManager == null) return false;
            ClosePreview();
            _previewGizmo = GlobalGizmoManager.NewId();
            _globalGizmoManager.Register(_previewGizmo, new Hrot.ScenarioEditor.Gizmos.MacroPreviewGizmo(preview, anchor));
            return true;
        }

        /// <summary>⭐ CE-4244 — removes the preview gizmo; nothing else exists to clean up.</summary>
        public void ClosePreview()
        {
            if (_previewGizmo != 0) _globalGizmoManager?.Unregister(_previewGizmo);
            _previewGizmo = 0;
        }

        /// <summary>
        /// Activates entity placement using the last entity type that was passed to
        /// <see cref="StartPlacementMode"/>.  Useful when the toolbar's "Place Entity"
        /// button fires without an explicit type selection.
        /// </summary>
        /// <remarks>
        /// ⚠⚠ <b>Hosts must NOT pass this as the pack's <c>StartPlacementMode</c> delegate</b> — pass
        /// <see cref="ArmPlacement"/> instead, or the cycle described there re-forms.
        /// </remarks>
        public void StartPlacementModeWithLastType()
            => StartPlacementMode(LastSelectedTkbType);

        /// <inheritdoc/>
        /// <remarks>
        /// Registers a <see cref="PointSequenceGizmo"/> requiring >= 3 points with
        /// <see cref="GlobalGizmoManager"/>. On completion, emits a
        /// <see cref="SpawnEntityCommand"/> carrying an <see cref="EditablePolyline"/>
        /// and optional <see cref="MapOverlayStyle"/>.
        /// </remarks>
        public void StartAreaAuthoringMode(string styleOverrideJson = "")
            => StartAreaAuthoringMode(TkbEntityTypes.TacGraphic_Area, styleOverrideJson);

        /// <inheritdoc/>
        public void StartAreaAuthoringMode(long tkbType, string styleOverrideJson = "")
        {
            _pendingStyleJson = styleOverrideJson;
            _pendingAreaTkbType = (Fdp.Core.TkbTypeId)(tkbType == 0 ? TkbEntityTypes.TacGraphic_Area : tkbType);

            // ⭐⭐⭐ UXI-07 step 4a — see StartPlacementMode. ⭐ Area/route bring their OWN tool ids
            //   (PlaceArea/PlaceRoute) because nothing else registers them, and §4.9 ④ measured that
            //   neither has the recursion Spawn has: no host wires them into the pack.
            if (_tools != null)
            {
                _tools.Activate(Hrot.ScenarioEditor.Tools.ScenarioToolIds.PlaceArea);
                return;
            }

            ArmAreaAuthoring(_pendingAreaTkbType);
        }

        /// <summary>
        /// The area arm body — reached through <c>Activate</c>, or directly with no arbiter.
        ///
        /// <para>⭐⭐⭐ <c>E5</c> — 🔴 this method used to carry the gizmo lifecycle, the centroid
        /// arithmetic, the relative-point loop and the <c>SpawnEntityCommand</c> shape VERBATIM, and
        /// <c>IgApplication.ActivateAreaAuthoringTool</c> carried the same mechanism again with a
        /// geodetic centroid. ⇒ moved into <see cref="Hrot.ScenarioEditor.Tools.AreaAuthoringArm"/>
        /// (ruling 9, and the <c>U6</c> ruling *"nothing of it should be IG host only"*). ⭐ The only
        /// thing left here is this host's DIFFERENCE: the commit goes on the local bus.</para>
        ///
        /// <para>⚠ The <c>null</c>-manager pre-check stays: the arm itself tolerates a null manager
        /// (IG's rails need that), so returning <c>Unserviceable</c> is a CALLER rule this host keeps.
        /// </para>
        /// </summary>
        private static readonly System.Numerics.Quaternion FacingNorth = Hrot.ScenarioEditor.Gizmos.EntityPlacementGizmo.FacingNorth;

        public Hrot.ScenarioEditor.Tools.ToolActivationOutcome ArmAreaAuthoring()
            => ArmAreaAuthoring(TkbEntityTypes.TacGraphic_Area);

        /// <summary>
        /// ⭐ The arm, parameterised by the TKB type the shape is born as — <c>TacGraphic_Area</c> for a
        /// tactical area, <c>TerrainZone</c> for a zone (<c>B1</c>). 📄 design §2.1: <c>TkbType</c> is
        /// THE discriminator, so one authoring mechanism serves every drawn kind.
        /// </summary>
        public Hrot.ScenarioEditor.Tools.ToolActivationOutcome ArmAreaAuthoring(long tkbType)
        {
            if (_globalGizmoManager == null)
                return Hrot.ScenarioEditor.Tools.ToolActivationOutcome.Unserviceable;

            var arm = _areaArm ??= new Hrot.ScenarioEditor.Tools.AreaAuthoringArm(_globalGizmoManager, tools: _tools);

            if (_activeSequenceId.HasValue)
            {
                _globalGizmoManager!.Unregister(_activeSequenceId.Value);
                _activeSequenceId = null;
            }

            // ⭐ Terrain authoring U-7 — a LINE / AREA terrain feature rides its spec in the style JSON: a line needs 2 points and is open, an area 3 and closed; the entity is born a TerrainFeature (and loses the area fill style)
            var (styleJson, terrainSpec) = Hrot.UI.Common.AddEntity.TerrainBirth.Split(_pendingStyleJson);
            var feature = tkbType == TkbEntityTypes.TerrainFeature ? Hrot.UI.Common.AddEntity.TerrainBirth.FeatureOf(terrainSpec) : null;
            var drawOptions = feature is null ? null : Hrot.UI.Common.AddEntity.TerrainDrawOptions.From(terrainSpec);
            if (drawOptions is not null)
            {
                drawOptions.ScatterRecipes = ScatterRecipes;
                // a scatter with no recipe chosen takes the first one the host offers
                if (drawOptions.IsScatter && drawOptions.Template.Length == 0 && ScatterRecipes.Count > 0) drawOptions.SetTemplate(ScatterRecipes[0].Id);
            }
            var outcome = arm.Arm(new Hrot.ScenarioEditor.Tools.AreaAuthoringRequest(
                TkbType:    (Fdp.Core.TkbTypeId)(tkbType),
                StyleJson:  feature is null ? _pendingStyleJson : styleJson ?? "",
                OnCommit:   Request,
                MinPoints:  feature is { Shape: Hrot.UI.Common.AddEntity.TerrainShape.Line } ? 2 : 3,
                Closed:     feature is not { Shape: Hrot.UI.Common.AddEntity.TerrainShape.Line },
                // ⭐ the commit reads what the drawing panel holds (kind, height / width, recipe, density, seed): ONE object, so the panel and the entity cannot disagree
                Born:       drawOptions is null ? null : drawOptions.Born,
                // ⭐ Clears this adapter's mirror when the tool tears itself down — exactly what the
                //   prior inline `onRemove` did. ⛔ Dropping it would leave `_activeSequenceId` stale and
                //   make the next arm unregister a gizmo that is already gone.
                OnDisarmed: () => { _activeSequenceId = null; if (ReferenceEquals(_terrainDraw, drawOptions)) _terrainDraw = null; }));

            _activeSequenceId = arm.ActiveGizmoId;
            _terrainDraw = drawOptions;   // ⭐ after the arm: arming first disarms the previous tool, whose OnDisarmed clears the field
            return outcome;
        }

        /// <inheritdoc/>
        public void StartZoneAuthoringMode(string styleOverrideJson = "")
        {
            _pendingStyleJson = styleOverrideJson;

            if (_tools != null)
            {
                _tools.Activate(Hrot.ScenarioEditor.Tools.ScenarioToolIds.PlaceZone);
                return;
            }

            ArmZoneAuthoring();
        }

        /// <summary>
        /// ⭐ <c>E5</c> — the zone arm. ⛔ Not a second mechanism: it is
        /// <see cref="ArmAreaAuthoring(long)"/> with <c>TkbEntityTypes.TerrainZone</c> (<c>B1</c>), which
        /// is what makes <c>E1</c>'s zone gizmo, <c>E2</c>'s "Load zone" menu item and <c>E3</c>'s zones
        /// view reachable from something an operator can actually create.
        /// </summary>
        public Hrot.ScenarioEditor.Tools.ToolActivationOutcome ArmZoneAuthoring()
            => ArmAreaAuthoring(TkbEntityTypes.TerrainZone);

        /// <remarks>
        /// Registers a <see cref="PointSequenceGizmo"/> requiring >= 2 points with
        /// <see cref="GlobalGizmoManager"/>. On completion, emits a
        /// <see cref="SpawnEntityCommand"/> carrying a <see cref="RoutePlan"/>.
        /// </remarks>
        public void StartRouteAuthoringMode()
        {
            // ⭐⭐⭐ UXI-07 step 4a — see StartPlacementMode.
            if (_tools != null)
            {
                _tools.Activate(Hrot.ScenarioEditor.Tools.ScenarioToolIds.PlaceRoute);
                return;
            }

            ArmRouteAuthoring();
        }

        /// <summary>The route arm body — reached through <c>Activate</c>, or directly with no arbiter.</summary>
        public Hrot.ScenarioEditor.Tools.ToolActivationOutcome ArmRouteAuthoring()
        {
            if (_globalGizmoManager == null)
                return Hrot.ScenarioEditor.Tools.ToolActivationOutcome.Unserviceable;

            if (_activeSequenceId.HasValue)
            {
                _globalGizmoManager!.Unregister(_activeSequenceId.Value);
                _activeSequenceId = null;
            }

            var routeId = GlobalGizmoManager.NewId();
            var gizmo = new PointSequenceGizmo(
                onFinish: points =>
                {
                    // ⭐ B-17 P2 — the gizmo's session refuses a finish under 2 points (reason shown, tool stays armed); it never gets here short.
                    var routePlan = new RoutePlan { IsLoop = false }.WithWaypoints(wps =>
                    {
                        for (int i = 0; i < points.Length; i++)
                        {
                            wps.Add(new RouteWaypoint
                            {
                                Position    = new System.Numerics.Vector3(points[i].X, points[i].Y, 0f),
                                TargetSpeed = 0f,
                            });
                        }
                    });

                    var anchor = routePlan.Waypoints.Count > 0
                        ? routePlan.Waypoints[0].Position
                        : default;

                    var cmd = new SpawnEntityCommand
                    {
                        NetworkId         = 0,
                        TkbType           = (Fdp.Core.TkbTypeId)(TkbEntityTypes.TacGraphic_Route),
                        OwnerNodeId       = (Fdp.Core.NodeId)(0),
                        InitType          = ReliableInitType.AllPeers,
                        RequestId         = System.Guid.NewGuid(),
                        InitialTransform  = new SimTransform { Position = anchor },
                        InitialComponents = new System.Collections.Generic.List<object> { routePlan },
                    };

                    Request(cmd);
                },
                onRemove: () => { _globalGizmoManager!.Unregister(routeId); _activeSequenceId = null; },
                minPoints: 2, closed: false, noun: "a route");

            Hrot.ScenarioEditor.Tools.DrawingMenu.Attach(gizmo.Session, _tools);   // ⭐ B-17 P2 — right-click = the drawing menu
            _activeSequenceId = routeId;
            _globalGizmoManager!.Register(routeId, gizmo);
            return Hrot.ScenarioEditor.Tools.ToolActivationOutcome.Armed;
        }

        /// <summary>
        /// ⭐⭐⭐ <c>CE-259ax</c> — THE one tail, for every affordance (placement, area, zone, route): an AUTHOR calls
        /// <see cref="EntityCreation.RequestEntityCreation"/> (R-145). ⛔ No <c>SpawnEntityCommand</c> is published and no request is built here:
        /// the command is only the SHAPE the tools hand over. Owner 0 ⇒ the system picks (the default processor). The velocity rides in the
        /// component list; the transform is the affordance's own parameter.
        /// 📄 docs/DESIGN_Entity_Authoring_Surface.md §4, §9.
        /// </summary>
        public void Request(SpawnEntityCommand cmd)
        {
            var components = cmd.InitialComponents;
            if (cmd.InitialVelocity.HasValue)
                (components ??= new System.Collections.Generic.List<object>()).Add(cmd.InitialVelocity.Value);

            // ⭐ Edit framework F2 (CE-4278) — the creation is a journalled edit: Submit mints the id, the affordance sends the request under it, and the
            //   processing node's answer (Pending → Applied with the new id, or Refused with why) lands in the journal. A caller's own id (cmd.RequestId)
            //   is kept when it names one.
            void Send(System.Guid id) => _creation.RequestEntityCreation(
                tkbType:               cmd.TkbType,
                transform:             cmd.InitialTransform,
                initialComponents:     components,
                owner:                 cmd.OwnerNodeId,
                initType:              cmd.InitType,
                initialAttributesJson: cmd.InitialAttributesJson,
                isTransient:           cmd.IsTransient,
                disType:               cmd.DisType,
                requestId:             id,
                reliableInitTimeout:   cmd.ReliableInitTimeout,
                spawnHeight:           cmd.SpawnHeight);
            if (cmd.RequestId != System.Guid.Empty) { Send(cmd.RequestId); return; }
            Fdp.Toolkit.Replication.Edits.Edits.Submit(_creation.World, Fdp.Toolkit.Replication.Edits.EditOp.Create(Send, "Place an entity"));
        }
    }
}

