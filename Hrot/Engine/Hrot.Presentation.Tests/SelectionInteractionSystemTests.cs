using System.Collections.Generic;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.ScenarioEditor.Systems;
using Xunit;

namespace Hrot.ScenarioEditor.Tests;

/// <summary>
/// Unit tests for <see cref="SelectionInteractionSystem"/> (SIS-001..SIS-008).
/// </summary>
public class SelectionInteractionSystemTests
{
    private readonly EntityRepository _world;
    private readonly SelectionInteractionSystem _system;

    public SelectionInteractionSystemTests()
    {
        _world = new EntityRepository();
        HrotSharedComponentRegistry.RegisterAll(_world);
        _world.RegisterComponent<SelectionState>();
        _world.RegisterComponent<VehicleState>();
        _system = new SelectionInteractionSystem(_world, _world.Bus);
        _requests = new SelectionRequestSystem(
            () => new Hrot.ScenarioEditor.Selection.EcsSelectionState(_world));
    }

    private readonly SelectionRequestSystem _requests;

    /// <summary>
    /// ⭐⭐⭐ <b><c>UXI-11</c> <c>S-4</c> — a map gesture is a REQUEST now, so serving it takes a frame.</b>
    ///
    /// <para>📄 §2.7.3 rule 1: <c>SelectionRequestSystem</c> is the only writer. ⛔ This system used to
    /// write the component itself and announce nothing, which is exactly the defect <c>S-4</c> closes —
    /// the hosts' inspector context stopped following map clicks when <c>S-3</c> replaced their
    /// hand-syncs with the notification.</para>
    ///
    /// <para>⭐⭐ <b>These rails are STRONGER for it.</b> Before, they proved this system wrote two
    /// booleans. Now they prove the whole chain — gesture → request → the one writer → the component —
    /// which is the chain that actually has to work on five hosts.</para>
    /// </summary>
    private void ServeRequests()
    {
        _world.Bus.SwapBuffers();
        _requests.Execute(_world, 0f);
    }

    /// <summary>⭐⭐ §6.7 — ids must be UNIQUE now. Selection resolves the token's anchor id against the
    /// world, so two entities sharing <c>NetworkIdentity.Value = 1</c> (which is what this used to do)
    /// would make the second one unaddressable — the resolve answers with the first match.
    /// 📄 docs/DESIGN_Gizmo_Anchor_Identity.md §6.7.</summary>
    private long _nextNetId = 7041L;

    private Entity CreateSelectableEntity()
    {
        var e = _world.CreateEntity();
        _world.AddComponent(e, default(SimTransform));
        _world.AddComponent(e, new NetworkIdentity { Value = _nextNetId++ });
        _world.AddComponent(e, new SelectionState());
        return e;
    }

    /// <summary>⭐ §6.7 — the token carries the target's NETWORK id; <c>Entity.Null</c> maps to 0,
    /// which is exactly the "empty canvas" signal the rubber-band arm reads.</summary>
    private void PublishStartedEvent(Entity target, Vector3 worldPos = default,
                                     MapMouseButton button = MapMouseButton.Left)
    {
        _world.Bus.Publish(new GizmoInteractionStartedEvent
        {
            Token    = new PickToken { AnchorId = AnchorIdOf(target) },
            WorldPos = worldPos,
            Button   = button,
        });
        _world.Bus.SwapBuffers();
    }

    /// <summary>⭐ <c>S-4b</c> — the right-press form, for §2.3's rows.</summary>
    private void PublishRightStartedEvent(Entity target, Vector3 worldPos = default)
        => PublishStartedEvent(target, worldPos, MapMouseButton.Right);

    private long AnchorIdOf(Entity target)
        => Fdp.Toolkit.Replication.Services.NetworkIdResolver.RuntimeNetworkIdOf(_world, target);

    private void PublishKeyEvent(MapKeyboardKey key, bool isPressed)
    {
        _world.Bus.Publish(new GizmoKeyEvent
        {
            Key       = key,
            IsPressed = isPressed,
        });
        _world.Bus.SwapBuffers();
    }

    // SIS-001: GizmoInteractionStartedEvent with valid entity selects it.
    [Fact]
    public void GizmoInteractionStartedEvent_WithValidEntity_SelectsIt()
    {
        var entity = CreateSelectableEntity();
        PublishStartedEvent(entity);

        _system.Tick(0f);
        ServeRequests();

        var state = _world.GetComponent<SelectionState>(entity);
        Assert.True(state.IsSelected);
        Assert.True(state.IsPrimarySelection);
    }

    // SIS-002: After null-entity GizmoInteractionStartedEvent, selection is NOT cleared immediately.
    // A tiny-drag commit (GizmoInteractionCommitEvent without intervening GizmoDragUpdateEvent) clears all.
    [Fact]
    public void GizmoInteractionStartedEvent_WithNullEntity_StartsRubberBand_NotImmediateClear()
    {
        var entity = CreateSelectableEntity();
        _world.SetComponent(entity, new SelectionState { IsSelected = true, IsPrimarySelection = true });

        // Step 1: null entity click -> rubber-band starts, selection NOT yet cleared
        PublishStartedEvent(Entity.Null);
        _system.Tick(0f);
        ServeRequests();
        // Still selected (rubber-band in progress, no commit yet)
        Assert.True(_world.GetComponent<SelectionState>(entity).IsSelected);

        // Step 2: commit without any drag event -> tiny drag path -> clears selection
        _world.Bus.Publish(new GizmoInteractionCommitEvent { Token = default });
        _world.Bus.SwapBuffers();
        _system.Tick(0f);
        ServeRequests();

        var state = _world.GetComponent<SelectionState>(entity);
        Assert.False(state.IsSelected);
    }

    // SIS-003: Second click clears previous selection (single-select).
    [Fact]
    public void SecondClick_ClearsPreviousSelection()
    {
        var entity1 = CreateSelectableEntity();
        var entity2 = _world.CreateEntity();
        _world.AddComponent(entity2, default(SimTransform));
        _world.AddComponent(entity2, new NetworkIdentity { Value = 2L });
        _world.AddComponent(entity2, new SelectionState());

        PublishStartedEvent(entity1);
        _system.Tick(0f);
        ServeRequests();
        Assert.True(_world.GetComponent<SelectionState>(entity1).IsSelected);

        PublishStartedEvent(entity2);
        _system.Tick(0f);
        ServeRequests();

        Assert.False(_world.GetComponent<SelectionState>(entity1).IsSelected);
        Assert.True(_world.GetComponent<SelectionState>(entity2).IsSelected);
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>UXI-11</c> <c>S-4</c> — A MAP CLICK REACHES THE INSPECTOR CONTEXT. This is the rail
    /// the regression it fixes never had.</b>
    ///
    /// <para>🔴 <b>What broke, and how it hid.</b> Every host used to hand-sync
    /// <c>IInspectorContext.SelectedEntity</c> off <c>OnSelectionChanged</c>. <c>S-3</c> deleted those
    /// in favour of <c>SelectionChangedNotification</c> — correctly, because they fired for a MAP click
    /// and nothing else. ⛔ But this system wrote the component DIRECTLY and published no notification,
    /// so the replacement never fired for a map click either. ⇒ the details pane stopped following the
    /// map on the editor, IG, SimHost and ReplayBrowser, and **the whole ~8 000-rail suite stayed
    /// green** — because the entity-inspector PANEL projects from <c>ISelectionState</c> each draw and
    /// so kept working, which is exactly the kind of partial symptom that reads as "fine".</para>
    ///
    /// <para>⭐ Now the gesture is a request, the request system announces, and the notification system
    /// points the context. This asserts that chain end to end.</para>
    /// </summary>
    [Fact]
    public void AMapClickReachesTheInspectorContext()
    {
        var entity    = CreateSelectableEntity();
        var inspector = new Fdp.Presentation.Abstractions.InspectorState();
        var notify    = new SelectionNotificationSystem(() => inspector);

        PublishStartedEvent(entity);
        _system.Tick(0f);          // the gesture publishes a request
        ServeRequests();           // the one writer applies it AND announces
        _world.Bus.SwapBuffers();
        notify.Execute(_world, 0f);

        Assert.Equal(entity, inspector.SelectedEntity);
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-300</c> — the AI editors' entity cell follows the ANNOUNCEMENT, so EVERY cause
    /// moves it.</b> 📄 <c>docs/blueprints/DESIGN_Editor_Entity_Selection_Source.md</c> §3.1.
    ///
    /// <para>🔴 <b>The defect this pins.</b> The cell's only production writer used to be
    /// <c>CallbackSelectionBridge</c>, hung off <c>SelectionInteractionSystem.OnSelectionChanged</c> —
    /// <b>a map gesture</b>. ⇒ an entity-inspector click, an orbat select, a context-menu <i>Select</i>
    /// or a remote <c>CMD_SET_SELECTION</c> left every Watch/Details live-value row projecting the
    /// PREVIOUS entity.</para>
    ///
    /// <para>⭐⭐ <b>The request here is deliberately NOT a map gesture</b> — it is published directly,
    /// the way the inspector publishes one. ⛔ Driving this through <c>PublishStartedEvent</c> would
    /// assert the defect away: the old bridge passed that case too, and only that case.
    /// ⛔ <b>Red-proof:</b> restrict the sink to <c>Map.</c> reasons and this reddens while
    /// <see cref="AMapClickReachesTheInspectorContext"/> stays green.</para>
    /// </summary>
    [Fact]
    public void ASelectionFromANonMapCause_ReachesTheAiEditorsEntityCell()
    {
        var entity = CreateSelectableEntity();
        Entity? aiCell = null;
        var notify = new SelectionNotificationSystem(
            static () => null, null, null, e => aiCell = e);

        _world.Bus.PublishManaged(
            Fdp.Toolkit.Vis2D.Abstractions.SelectionChangeRequest
                .ReplaceWith(entity, "Inspector.RowClick"));     // ⭐ NOT a map gesture
        ServeRequests();
        _world.Bus.SwapBuffers();
        notify.Execute(_world, 0f);

        Assert.Equal(entity, aiCell);
    }

    /// <summary>
    /// ⚠ <b><c>CE-300</c> — a CLEARED selection hands the cell <c>null</c>, never
    /// <see cref="Entity.Null"/>.</b>
    ///
    /// <para>⭐ The cell's <c>null</c> is a REAL state its readers gate on — <c>LiveBlackboardValue
    /// Provider</c>'s second line is <c>if (entity == null) return false</c>, so the row honestly reads
    /// <c>(pending)</c>. ⛔ Handing them <c>Entity.Null</c> instead would make every provider ask the
    /// world about entity 0.</para>
    ///
    /// <para>⛔ <b>Asserted as "assigned, and assigned null"</b>, not merely "still null" — ⚠ an
    /// unchanged initial value would pass a test that never ran the system at all.</para>
    /// </summary>
    [Fact]
    public void ClearingTheSelection_HandsTheAiCellNull_NotEntityNull()
    {
        var entity = CreateSelectableEntity();
        var writes = new System.Collections.Generic.List<Entity?>();
        var notify = new SelectionNotificationSystem(
            static () => null, null, null, e => writes.Add(e));

        _world.Bus.PublishManaged(
            Fdp.Toolkit.Vis2D.Abstractions.SelectionChangeRequest.ReplaceWith(entity, "Map.Click"));
        ServeRequests();
        _world.Bus.SwapBuffers();
        notify.Execute(_world, 0f);

        _world.Bus.PublishManaged(
            Fdp.Toolkit.Vis2D.Abstractions.SelectionChangeRequest.ClearAll("Map.RightClick.EmptySpace"));
        ServeRequests();
        _world.Bus.SwapBuffers();
        notify.Execute(_world, 0f);

        Assert.Equal(new Entity?[] { entity, null }, writes);
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>UXI-11</c> <c>S-5</c> — ruling ②, END TO END: selecting another entity cancels the edit
    /// on the one that lost the selection.</b>
    /// 🔒 <i>"if entity becomes unselected, it should cancel any editing on the entity losing the
    /// selection"</i> (user, <c>2026-09-10</c>). 📄 §2.6 ② / §2.7.15.
    ///
    /// <para>⭐ This is the first real EDGE consumer of <c>SelectionChangedNotification</c> — the thing
    /// §2.7.8 deviation ② said the notification existed for while the panels projected instead.</para>
    ///
    /// <para>⛔ <b>Red-proof:</b> stop passing the controller to <c>SelectionNotificationSystem</c> and the
    /// gizmo stays armed on the deselected entity.</para>
    /// </summary>
    [Fact]
    public void SelectingAnotherEntity_CancelsTheEditOnTheOneThatLostTheSelection()
    {
        var a = CreateSelectableEntity();
        var b = CreateSelectableEntity();

        var buffer     = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
        var dataDriven = new Fdp.Toolkit.Diagnostics.Gizmos.Systems.DataDrivenGizmoSystem(
            new Fdp.Toolkit.Diagnostics.Gizmos.GizmoRegistry(), buffer, interactionBus: _world.Bus);
        var tools = new Hrot.ScenarioEditor.Tools.ToolController(() => null, () => dataDriven);
        tools.Register(
            new Hrot.ScenarioEditor.Tools.ToolDescriptor(
                "edit", "Edit Shape",
                Hrot.ScenarioEditor.Tools.ToolModality.Modal,
                Hrot.ScenarioEditor.Tools.ToolArbiter.EntityScoped),
            target =>
            {
                dataDriven.ActivateGizmo(target, new S5ProbeGizmo());
                return Hrot.ScenarioEditor.Tools.ToolActivationOutcome.Armed;
            });

        var selection = new Hrot.ScenarioEditor.Selection.EcsSelectionState(_world);
        var notify    = new SelectionNotificationSystem(
            () => new Fdp.Presentation.Abstractions.InspectorState(), tools, selection);

        // ① select A, then arm an edit ON A.
        _world.Bus.PublishManaged(Fdp.Toolkit.Vis2D.Abstractions.SelectionChangeRequest
            .ReplaceWith(a, "test.setup"));
        ServeRequests();
        Assert.True(tools.Activate("edit", a));
        Assert.True(dataDriven.HasInjectedGizmo(a));        // ⛔ anti-vacuity: it really is armed

        // ② now select B. A loses the selection.
        _world.Bus.PublishManaged(Fdp.Toolkit.Vis2D.Abstractions.SelectionChangeRequest
            .ReplaceWith(b, "test.reselect"));
        ServeRequests();                                     // the one writer applies AND announces
        _world.Bus.SwapBuffers();
        notify.Execute(_world, 0f);

        Assert.Null(tools.ActiveModal);                      // ⭐ the edit is over
        Assert.False(dataDriven.HasInjectedGizmo(a));        // ⭐ and the gizmo is gone, not merely forgotten
    }

    /// <summary>
    /// ⭐⭐ <b>The other half of ruling ②, and the one a sweep would break:</b> an entity that KEEPS the
    /// selection keeps its edit. ⛔ If this ever reddens, something replaced the per-entity predicate with
    /// a "selection changed ⇒ cancel everything" sweep, which §4.14 forbids by name.
    /// </summary>
    [Fact]
    public void AnEntityThatKeepsTheSelection_KeepsItsEdit()
    {
        var a = CreateSelectableEntity();
        var b = CreateSelectableEntity();

        var buffer     = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
        var dataDriven = new Fdp.Toolkit.Diagnostics.Gizmos.Systems.DataDrivenGizmoSystem(
            new Fdp.Toolkit.Diagnostics.Gizmos.GizmoRegistry(), buffer, interactionBus: _world.Bus);
        var tools = new Hrot.ScenarioEditor.Tools.ToolController(() => null, () => dataDriven);
        tools.Register(
            new Hrot.ScenarioEditor.Tools.ToolDescriptor(
                "edit", "Edit Shape",
                Hrot.ScenarioEditor.Tools.ToolModality.Modal,
                Hrot.ScenarioEditor.Tools.ToolArbiter.EntityScoped),
            target =>
            {
                dataDriven.ActivateGizmo(target, new S5ProbeGizmo());
                return Hrot.ScenarioEditor.Tools.ToolActivationOutcome.Armed;
            });

        var selection = new Hrot.ScenarioEditor.Selection.EcsSelectionState(_world);
        var notify    = new SelectionNotificationSystem(
            () => new Fdp.Presentation.Abstractions.InspectorState(), tools, selection);

        // A is armed, and the selection GROWS to {A, B} — A never loses it.
        _world.Bus.PublishManaged(Fdp.Toolkit.Vis2D.Abstractions.SelectionChangeRequest
            .ReplaceWith(a, "test.setup"));
        ServeRequests();
        tools.Activate("edit", a);

        _world.Bus.PublishManaged(Fdp.Toolkit.Vis2D.Abstractions.SelectionChangeRequest
            .ReplaceWith(new[] { a, b }, "test.grow"));
        ServeRequests();
        _world.Bus.SwapBuffers();
        notify.Execute(_world, 0f);

        Assert.NotNull(tools.ActiveModal);                   // ⭐ still editing A
        Assert.True(dataDriven.HasInjectedGizmo(a));
    }

    /// <summary>
    /// ⭐⭐ A rubber band is ONE request carrying the set — not a clear plus N writes.
    /// ⚠ That is what makes the ring, the panels and the announcement all see the finished selection
    /// instead of N intermediate ones.
    /// </summary>
    [Fact]
    public void ARubberBandSelectsTheWholeBoxInOneRequest()
    {
        var a = CreateSelectableEntity();
        var b = CreateSelectableEntity();
        _world.SetComponent(a, default(SimTransform));
        _world.SetComponent(b, new SimTransform { Position = new System.Numerics.Vector3(5f, 5f, 0f) });

        PublishStartedEvent(Entity.Null, new System.Numerics.Vector3(-10f, -10f, 0f));
        _system.Tick(0f);

        _world.Bus.Publish(new GizmoDragUpdateEvent
        {
            Token = default, WorldPos = new System.Numerics.Vector3(20f, 20f, 0f),
        });
        _world.Bus.SwapBuffers();
        _system.Tick(0f);

        _world.Bus.Publish(new GizmoInteractionCommitEvent { Token = default });
        _world.Bus.SwapBuffers();
        _system.Tick(0f);
        ServeRequests();

        Assert.True(_world.GetComponent<SelectionState>(a).IsSelected);
        Assert.True(_world.GetComponent<SelectionState>(b).IsSelected);
    }

    // SIS-004: GizmoKeyEvent(Delete, isPressed=false) on selected entity publishes DestroyEntityCommand.
    [Fact]
    public void GizmoKeyEvent_Delete_Released_OnSelectedEntity_PublishesDestroyCommand()
    {
        var entity = CreateSelectableEntity();
        _world.SetComponent(entity, new SelectionState { IsSelected = true, IsPrimarySelection = true });

        PublishKeyEvent(MapKeyboardKey.Delete, isPressed: false);
        _system.Tick(0f);
        _world.Bus.SwapBuffers(); // make commands published during Tick readable

        var commands = new List<DestroyEntityCommand>();
        foreach (var cmd in _world.Bus.ReadManaged<DestroyEntityCommand>())
            commands.Add(cmd);
        Assert.Single(commands);
        // ⭐ §6.7 — CreateSelectableEntity now allocates a UNIQUE network id per entity (it used to
        //   hard-code 1 for every one of them, which the id-based resolve makes unusable).
        Assert.Equal(
            Fdp.Toolkit.Replication.Services.NetworkIdResolver.RuntimeNetworkIdOf(_world, entity),
            commands[0].NetworkId);
    }

    // SIS-005: GizmoKeyEvent(Delete, isPressed=true) is ignored.
    [Fact]
    public void GizmoKeyEvent_Delete_Pressed_IsIgnored()
    {
        var entity = CreateSelectableEntity();
        _world.SetComponent(entity, new SelectionState { IsSelected = true, IsPrimarySelection = true });

        PublishKeyEvent(MapKeyboardKey.Delete, isPressed: true);
        _system.Tick(0f);

        // Entity should still be alive and selected.
        Assert.True(_world.IsAlive(entity));
        Assert.True(_world.GetComponent<SelectionState>(entity).IsSelected);
    }

    // SIS-006: ClearAllSelections() deselects all live entities.
    [Fact]
    public void ClearAllSelections_DeselectsAllLiveEntities()
    {
        var entity1 = CreateSelectableEntity();
        var entity2 = CreateSelectableEntity();
        _world.SetComponent(entity1, new SelectionState { IsSelected = true, IsPrimarySelection = true });
        _world.SetComponent(entity2, new SelectionState { IsSelected = true, IsPrimarySelection = false });

        _system.ClearAllSelections();

        Assert.False(_world.GetComponent<SelectionState>(entity1).IsSelected);
        Assert.False(_world.GetComponent<SelectionState>(entity2).IsSelected);
    }

    // SIS-007: OnSelectionChanged callback fires on entity click.
    [Fact]
    public void OnSelectionChanged_FiresOnEntityClick()
    {
        var entity = CreateSelectableEntity();
        Entity? callbackEntity = null;
        _system.OnSelectionChanged += (e, _) => callbackEntity = e;

        PublishStartedEvent(entity);
        _system.Tick(0f);

        Assert.Equal(entity, callbackEntity);
    }

    // SIS-008: OnSelectionChanged fires with Entity.Null on tiny-drag commit (empty-space rubber-band commit).
    [Fact]
    public void OnSelectionChanged_FiresWithNull_AfterTinyDragCommit()
    {
        Entity? callbackEntity = null;
        _system.OnSelectionChanged += (e, _) => callbackEntity = e;

        // Start rubber-band on empty space (null entity)
        PublishStartedEvent(Entity.Null);
        _system.Tick(0f);
        Assert.Null(callbackEntity); // not yet fired

        // Commit without drag = tiny drag = deselect all
        _world.Bus.Publish(new GizmoInteractionCommitEvent { Token = default });
        _world.Bus.SwapBuffers();
        _system.Tick(0f);

        Assert.Equal(Entity.Null, callbackEntity);
    }

    // ══ UXI-11 S-4b — §2.3'S BUTTON-SPECIFIC ROWS ON THE MAP ═══════════════════
    // 📄 docs/UX/UX_Feature_Selection.md §2.3 (ruled 2026-08-12) and §2.7.14 (the as-built).
    //
    // 🔴 Until the terminal tagged the button, a right-release and a left-press arrived here as the
    //    SAME event, so every press took the left branch and a right-click collapsed a multi-selection.
    //    These four rails are what that defect could not satisfy.

    /// <summary>
    /// ⭐⭐⭐ <b>§2.3 row 1 — a right-click INSIDE the selection leaves the whole selection alone.</b>
    /// 🔒 Load-bearing, not cosmetic: the <c>2026-09-10</c> fan-out ruling (<i>"a menu opened on a
    /// selection affects all selected"</i>) is impossible if the gesture that opens the menu destroys
    /// the multi-selection.
    ///
    /// <para>⛔ <b>Red-proof:</b> drop the <c>isRight &amp;&amp; IsSelected</c> guard and the second
    /// entity loses its selection — the exact collapse the operator sees today.</para>
    /// </summary>
    [Fact]
    public void RightClickingAnEntityInsideTheSelection_LeavesTheWholeSelectionAlone()
    {
        var a = CreateSelectableEntity();
        var b = CreateSelectableEntity();

        // Select both, through the real writer.
        _world.Bus.PublishManaged(Fdp.Toolkit.Vis2D.Abstractions.SelectionChangeRequest
            .ReplaceWith(new[] { a, b }, "test.setup"));
        ServeRequests();
        Assert.True(_world.GetComponent<SelectionState>(a).IsSelected);   // ⛔ anti-vacuity
        Assert.True(_world.GetComponent<SelectionState>(b).IsSelected);

        PublishRightStartedEvent(b);
        _system.Tick(0f);
        ServeRequests();

        Assert.True(_world.GetComponent<SelectionState>(a).IsSelected);   // ⭐ the group SURVIVES
        Assert.True(_world.GetComponent<SelectionState>(b).IsSelected);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>§2.3 row 2 — a right-click OUTSIDE the selection still replaces it.</b> ⚠ Right-click
    /// does not stop selecting; it deselects the others exactly as a left-click would. Only the
    /// inside-the-group case is exempt.
    /// </summary>
    [Fact]
    public void RightClickingAnEntityOutsideTheSelection_ReplacesTheSelectionWithIt()
    {
        var a = CreateSelectableEntity();
        var b = CreateSelectableEntity();

        _world.Bus.PublishManaged(Fdp.Toolkit.Vis2D.Abstractions.SelectionChangeRequest
            .ReplaceWith(a, "test.setup"));
        ServeRequests();

        PublishRightStartedEvent(b);
        _system.Tick(0f);
        ServeRequests();

        Assert.False(_world.GetComponent<SelectionState>(a).IsSelected);
        Assert.True(_world.GetComponent<SelectionState>(b).IsSelected);
        Assert.True(_world.GetComponent<SelectionState>(b).IsPrimarySelection);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The asymmetry that made the button necessary.</b> A LEFT-click inside a multi-selection
    /// must still narrow it to that one entity — that is how an operator drills down. ⛔ If the §2.3
    /// guard were applied to both buttons (which is what a button-less fix would have done), this rail
    /// goes red and nobody would have noticed until an operator tried it.
    /// </summary>
    [Fact]
    public void LeftClickingAnEntityInsideTheSelection_StillNarrowsTheSelectionToIt()
    {
        var a = CreateSelectableEntity();
        var b = CreateSelectableEntity();

        _world.Bus.PublishManaged(Fdp.Toolkit.Vis2D.Abstractions.SelectionChangeRequest
            .ReplaceWith(new[] { a, b }, "test.setup"));
        ServeRequests();

        PublishStartedEvent(b);                     // ⭐ LEFT, the default
        _system.Tick(0f);
        ServeRequests();

        Assert.False(_world.GetComponent<SelectionState>(a).IsSelected);
        Assert.True(_world.GetComponent<SelectionState>(b).IsSelected);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>§2.3 row 3 — right-click on EMPTY SPACE clears.</b> 🔴 This could not be implemented at
    /// all before <c>S-4b</c>: the terminal emitted nothing for a canvas right-click, so no event
    /// reached this system.
    ///
    /// <para>⛔ And it must NOT arm a rubber band — that is a left-drag gesture, and arming one on a
    /// right-release leaves <c>_isBoxSelecting</c> set with no matching commit. The second half of this
    /// rail pins that: a later commit must not then re-clear as a tiny drag.</para>
    /// </summary>
    [Fact]
    public void RightClickingEmptySpace_ClearsTheSelection_AndDoesNotArmARubberBand()
    {
        var a = CreateSelectableEntity();
        _world.Bus.PublishManaged(Fdp.Toolkit.Vis2D.Abstractions.SelectionChangeRequest
            .ReplaceWith(a, "test.setup"));
        ServeRequests();
        Assert.True(_world.GetComponent<SelectionState>(a).IsSelected);   // ⛔ anti-vacuity

        PublishRightStartedEvent(Entity.Null);
        _system.Tick(0f);
        ServeRequests();

        Assert.False(_world.GetComponent<SelectionState>(a).IsSelected);

        // ⭐ No band was armed, so this commit is not a tiny-drag deselect and nothing further happens.
        Entity? callbackEntity = null;
        _system.OnSelectionChanged += (e, _) => callbackEntity = e;
        _world.Bus.Publish(new GizmoInteractionCommitEvent { Token = default });
        _world.Bus.SwapBuffers();
        _system.Tick(0f);
        Assert.Null(callbackEntity);
    }

    /// <summary>
    /// ⚠ <b>The modifier trap, pinned.</b> <c>MapMouseButton</c> is <c>[Flags]</c> and packs
    /// Shift/Ctrl/Alt into bits 28-30, so a naive <c>Button == Right</c> is FALSE for a
    /// shift-right-click. ⛔ That bug would appear only once someone held a modifier.
    /// </summary>
    [Fact]
    public void AShiftRightClickInsideTheSelection_IsStillARightClick()
    {
        var a = CreateSelectableEntity();
        var b = CreateSelectableEntity();

        _world.Bus.PublishManaged(Fdp.Toolkit.Vis2D.Abstractions.SelectionChangeRequest
            .ReplaceWith(new[] { a, b }, "test.setup"));
        ServeRequests();

        PublishStartedEvent(b, default, MapMouseButton.Right | MapMouseButton.ShiftMask);
        _system.Tick(0f);
        ServeRequests();

        Assert.True(_world.GetComponent<SelectionState>(a).IsSelected);
        Assert.True(_world.GetComponent<SelectionState>(b).IsSelected);
    }
    /// <summary>⭐ A minimal modal gizmo for the <c>S-5</c> rails. ⚠ Local on purpose:
    /// <c>ToolControllerTests.ProbeGizmo</c> is a private nested type and sharing it would mean widening
    /// another suite's surface for this one's convenience.</summary>
    private sealed class S5ProbeGizmo : Fdp.Toolkit.Diagnostics.Gizmos.IEntityStatefulGizmo
    {
        public bool RequiresExclusiveFocus => true;
        public bool IsFocused { get; private set; }
        public bool Disposed  { get; private set; }
        public void SetFocus(bool f) { IsFocused = f; }
        public void UpdateAndDraw(Fdp.ModuleHost.Abstractions.ISimulationView view, float dt,
                                  Fdp.Toolkit.Diagnostics.Gizmos.IDebugDrawBuilder b) { }
        public void OnInteractionStarted(GizmoPickToken t, Vector3 w) { }
        public void OnDragUpdate(Vector3 pos) { }
        public void OnCommit(Vector3 w) { }
        public void OnMenuAction(int id) { }
        public void OnMouseEvent(MapMouseButton b, bool p, Vector3 w) { }
        public void OnKeyEvent(MapKeyboardKey k, bool p) { }
        public void OnCancel() { }
        public void Dispose() { Disposed = true; }
    }

}
