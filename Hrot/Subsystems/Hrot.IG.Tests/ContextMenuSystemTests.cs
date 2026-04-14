using System;
using System.Collections.Generic;
using Hrot.IG.Components;
using Hrot.IG.Systems;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.IG.Tests;

/// <summary>
/// Unit tests for Task IG.4.3: <see cref="ContextMenuSystem"/>.
///
/// Validates:
/// <list type="bullet">
///   <item><see cref="ContextMenuSystem.TestHook_TriggerContextMenu"/> followed by
///   <see cref="ContextMenuSystem.Execute"/> marks the entity's
///   <see cref="ContextMenuState"/> as open with the correct screen position.</item>
///   <item><see cref="ContextMenuSystem.TestHook_CloseContextMenu"/> followed by
///   Execute marks the entity as closed.</item>
///   <item><see cref="ContextMenuSystem.ActiveMenuEntity"/> tracks the currently-open
///   entity correctly.</item>
///   <item>A <see cref="ContextActionsUpdate"/> managed event populates the entity's
///   action list without disturbing the open flag.</item>
///   <item>Opening a menu for one entity does not alter any other entity's state.</item>
/// </list>
///
/// No DDS or Raylib window context required.
/// </summary>
public class ContextMenuSystemTests
{
    // ── Test constants (§CODE-STANDARDS §1) ───────────────────────────────────

    private const float ScreenX = 400f;
    private const float ScreenY = 300f;

    // ── World factory ─────────────────────────────────────────────────────────

    private static EntityRepository CreateRepo()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<NetworkIdentity>();
        repo.RegisterManagedComponent<ContextMenuState>();
        return repo;
    }

    private static void RunSystem(EntityRepository repo, ContextMenuSystem system)
    {
        repo.Bus.SwapBuffers();
        system.Execute(repo, 0f);
        var cb = (EntityCommandBuffer)((ISimulationView)repo).GetCommandBuffer();
        cb.Playback(repo);
    }

    /// <summary>
    /// Runs only the Execute phase without flushing the command buffer.
    /// Mirrors the real runtime where ContextMenuSystem (PostSimulation) calls Execute
    /// and queues SetManagedComponent calls, but the buffer is flushed later in
    /// BeforeSync — AFTER Draw() is already called.
    /// </summary>
    private static void RunExecuteOnly(EntityRepository repo, ContextMenuSystem system)
    {
        repo.Bus.SwapBuffers();
        system.Execute(repo, 0f);
    }

    /// <summary>Flushes the ECS command buffer (simulates the BeforeSync flush).</summary>
    private static void FlushCommandBuffer(EntityRepository repo)
    {
        var cb = (EntityCommandBuffer)((ISimulationView)repo).GetCommandBuffer();
        cb.Playback(repo);
    }

    private static ContextMenuState? TryGetMenuState(EntityRepository repo, Entity entity)
    {
        var view = (ISimulationView)repo;
        return view.HasManagedComponent<ContextMenuState>(entity)
            ? view.GetManagedComponentRO<ContextMenuState>(entity)
            : null;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Open / close lifecycle
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// After TestHook_TriggerContextMenu + Execute, the entity must have a
    /// <see cref="ContextMenuState"/> with IsOpen = true.
    /// </summary>
    [Fact]
    public void TriggerContextMenu_EntityGetsOpenState()
    {
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        var system = new ContextMenuSystem();

        system.TestHook_TriggerContextMenu(entity, ScreenX, ScreenY);
        RunSystem(repo, system);

        var state = TryGetMenuState(repo, entity);
        Assert.NotNull(state);
        Assert.True(state!.IsOpen);
    }

    /// <summary>
    /// The screen position stored on the component must match the coordinates
    /// supplied to TestHook_TriggerContextMenu.
    /// </summary>
    [Fact]
    public void TriggerContextMenu_EntityGetsCorrectScreenPosition()
    {
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        var system = new ContextMenuSystem();

        system.TestHook_TriggerContextMenu(entity, ScreenX, ScreenY);
        RunSystem(repo, system);

        var state = TryGetMenuState(repo, entity);
        Assert.NotNull(state);
        Assert.Equal(ScreenX, state!.ScreenX);
        Assert.Equal(ScreenY, state!.ScreenY);
    }

    /// <summary>
    /// After opening then closing, the entity's ContextMenuState must have
    /// IsOpen = false.
    /// </summary>
    [Fact]
    public void CloseContextMenu_EntityGetsClosedState()
    {
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        var system = new ContextMenuSystem();

        // Open first.
        system.TestHook_TriggerContextMenu(entity, ScreenX, ScreenY);
        RunSystem(repo, system);

        // Now close.
        system.TestHook_CloseContextMenu(entity);
        RunSystem(repo, system);

        var state = TryGetMenuState(repo, entity);
        Assert.NotNull(state);
        Assert.False(state!.IsOpen);
    }

    /// <summary>
    /// <see cref="ContextMenuSystem.ActiveMenuEntity"/> must equal the triggered
    /// entity after Execute.
    /// </summary>
    [Fact]
    public void TriggerContextMenu_ActiveMenuEntityTracksOpenEntity()
    {
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        var system = new ContextMenuSystem();

        system.TestHook_TriggerContextMenu(entity, ScreenX, ScreenY);
        RunSystem(repo, system);

        Assert.Equal(entity, system.ActiveMenuEntity);
    }

    /// <summary>
    /// After closing the menu, <see cref="ContextMenuSystem.ActiveMenuEntity"/>
    /// must revert to <see cref="Entity.Null"/>.
    /// </summary>
    [Fact]
    public void CloseContextMenu_ActiveMenuEntityBecomesNull()
    {
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        var system = new ContextMenuSystem();

        system.TestHook_TriggerContextMenu(entity, ScreenX, ScreenY);
        RunSystem(repo, system);

        system.TestHook_CloseContextMenu(entity);
        RunSystem(repo, system);

        Assert.Equal(Entity.Null, system.ActiveMenuEntity);
    }

    /// <summary>
    /// Opening a menu for entity A must not modify entity B's state.
    /// </summary>
    [Fact]
    public void TriggerContextMenu_DoesNotAffectOtherEntities()
    {
        var repo    = CreateRepo();
        var entityA = repo.CreateEntity();
        var entityB = repo.CreateEntity();
        var system  = new ContextMenuSystem();

        // Pre-seed entityB with a closed ContextMenuState so we can assert it's untouched.
        repo.SetManagedComponent(entityB, new ContextMenuState { IsOpen = false });

        system.TestHook_TriggerContextMenu(entityA, ScreenX, ScreenY);
        RunSystem(repo, system);

        var stateB = TryGetMenuState(repo, entityB);
        Assert.NotNull(stateB);
        Assert.False(stateB!.IsOpen);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ContextActionsUpdate managed-event processing
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A <see cref="ContextActionsUpdate"/> event must populate the entity's action
    /// list without changing the IsOpen flag.
    /// </summary>
    [Fact]
    public void ContextActionsUpdate_PopulatesActionList()
    {
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        var system = new ContextMenuSystem();

        repo.AddComponent(entity, new NetworkIdentity(42));
        // Pre-seed entity with an open ContextMenuState so the update path applies.
        repo.SetManagedComponent(entity, new ContextMenuState { IsOpen = true });

        var actions = new List<ContextAction>
        {
            new ContextAction { Label = "Engage", ActionName = "IG_Engage" },
            new ContextAction { Label = "Move To", ActionName = "MoveTo" },
        };

        repo.Bus.PublishManaged(new ContextActionsUpdate
        {
            EntityNetworkId = 42,
            Actions         = actions,
        });

        RunSystem(repo, system);

        var state = TryGetMenuState(repo, entity);
        Assert.NotNull(state);
        Assert.Equal(2, state!.Actions.Count);
        Assert.Equal("Engage",  state.Actions[0].Label);
        Assert.Equal("Move To", state.Actions[1].Label);
    }

    /// <summary>
    /// A <see cref="ContextActionsUpdate"/> must not change the IsOpen flag of the
    /// target entity.
    /// </summary>
    [Fact]
    public void ContextActionsUpdate_PreservesIsOpenFlag()
    {
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        var system = new ContextMenuSystem();

        repo.AddComponent(entity, new NetworkIdentity(1));
        repo.SetManagedComponent(entity, new ContextMenuState { IsOpen = true });

        repo.Bus.PublishManaged(new ContextActionsUpdate
        {
            EntityNetworkId = 1,
            Actions         = new List<ContextAction> { new ContextAction { Label = "Attack", ActionName = "attack" } },
        });

        RunSystem(repo, system);

        var state = TryGetMenuState(repo, entity);
        Assert.NotNull(state);
        Assert.True(state!.IsOpen, "ContextActionsUpdate must not close an open menu.");
    }

    /// <summary>
    /// Before any trigger or update, no entity should have a ContextMenuState and
    /// ActiveMenuEntity must be Entity.Null.
    /// </summary>
    [Fact]
    public void InitialState_NoMenuIsOpen()
    {
        var system = new ContextMenuSystem();

        Assert.Equal(Entity.Null, system.ActiveMenuEntity);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Task-4 regression: same-frame close + open ordering
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Regression test for Task-4 (IG.4.4 bug fix).
    ///
    /// If a close request and an open request for the <em>same</em> entity are both
    /// queued before a single <see cref="ContextMenuSystem.Execute"/> call the open
    /// must win — i.e. the same-frame sequence {close, open} should leave the menu open.
    ///
    /// Before the fix, close was applied AFTER open inside Execute, so the menu was
    /// left closed even though a fresh open was requested in the same frame.  This
    /// manifested as "context menu appears only once; subsequent right-clicks on the
    /// same entity do nothing".
    /// </summary>
    [Fact]
    public void SameFrame_CloseAndOpenRequest_OpenWins()
    {
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        var system = new ContextMenuSystem();

        // Frame 1 — open the menu successfully so we have a known baseline.
        system.TestHook_TriggerContextMenu(entity, ScreenX, ScreenY);
        RunSystem(repo, system);
        Assert.Equal(entity, system.ActiveMenuEntity);

        // Frame 2 — queue BOTH a close (from UI layer dismissing the popup)
        // and a re-open (from the input layer handling another right-click)
        // BEFORE Execute runs.  The open must win.
        system.TestHook_CloseContextMenu(entity);
        system.TestHook_TriggerContextMenu(entity, ScreenX + 1f, ScreenY + 1f);
        RunSystem(repo, system);

        var state = TryGetMenuState(repo, entity);
        Assert.NotNull(state);
        Assert.True(state!.IsOpen,
            "Open request must win when close and open are both queued in the same frame.");
        Assert.Equal(entity, system.ActiveMenuEntity);
        Assert.Equal(ScreenX + 1f, state.ScreenX);
        Assert.Equal(ScreenY + 1f, state.ScreenY);
    }

    /// <summary>
    /// Verifies that a stand-alone close request (without a same-frame open) correctly
    /// closes the menu, so the Task-4 fix does not inadvertently break normal close
    /// behaviour.
    /// </summary>
    [Fact]
    public void CloseRequest_WithoutOpen_ClosesMenu()
    {
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        var system = new ContextMenuSystem();

        system.TestHook_TriggerContextMenu(entity, ScreenX, ScreenY);
        RunSystem(repo, system);
        Assert.True(system.ActiveMenuEntity == entity);

        system.TestHook_CloseContextMenu(entity);
        RunSystem(repo, system);

        var state = TryGetMenuState(repo, entity);
        Assert.NotNull(state);
        Assert.False(state!.IsOpen, "A stand-alone close request must close the menu.");
        Assert.Equal(Entity.Null, system.ActiveMenuEntity);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SimTransform guard — "Center on Entity" injection
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When an entity has a <see cref="SimTransform"/> the system must inject a
    /// "Center on Entity" action at position 0 if none is already present.
    /// </summary>
    [Fact]
    public void OpenMenu_EntityWithSimTransform_InjectsCenterOnEntity()
    {
        var repo   = CreateRepo();
        repo.RegisterComponent<SimTransform>();
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SimTransform());
        var system = new ContextMenuSystem();

        // Seed the entity with an EMPTY action list so the injection path is exercised.
        repo.SetManagedComponent(entity, new ContextMenuState
        {
            IsOpen  = false,
            Actions = new List<ContextAction>()
        });

        system.TestHook_TriggerContextMenu(entity, ScreenX, ScreenY);
        RunSystem(repo, system);

        var state = TryGetMenuState(repo, entity);
        Assert.NotNull(state);
        var hasCenter = state!.Actions.Exists(
            a => a.ActionName == "IG_CenterOnEntity" || a.ActionName == "IG_Center");
        Assert.True(hasCenter, "SimTransform entity must get 'Center on Entity' injected.");
    }

    /// <summary>
    /// When an entity does NOT have a <see cref="SimTransform"/> (e.g. the
    /// <c>_mapContextEntity</c> used for empty-space clicks) the system must NOT
    /// inject the spatial "Center on Entity" action.
    /// </summary>
    [Fact]
    public void OpenMenu_EntityWithoutSimTransform_DoesNotInjectCenterOnEntity()
    {
        var repo   = CreateRepo();
        repo.RegisterComponent<SimTransform>();
        var entity = repo.CreateEntity(); // deliberately no SimTransform
        var system = new ContextMenuSystem();

        system.TestHook_TriggerContextMenu(entity, ScreenX, ScreenY);
        RunSystem(repo, system);

        var state = TryGetMenuState(repo, entity);
        Assert.NotNull(state);
        var hasCenter = state!.Actions.Exists(
            a => a.ActionName == "IG_CenterOnEntity" || a.ActionName == "IG_Center");
        Assert.False(hasCenter,
            "Entity without SimTransform must NOT receive 'Center on Entity'.");
    }

    /// <summary>
    /// When ExCon provides a list that already contains "IG_CenterOnEntity", the system
    /// must not add a duplicate.
    /// </summary>
    [Fact]
    public void OpenMenu_AlreadyHasCenterAction_DoesNotDuplicate()
    {
        var repo   = CreateRepo();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterComponent<NetworkIdentity>();
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SimTransform());
        repo.AddComponent(entity, new NetworkIdentity(99));

        // Pre-populate via a ContextActionsUpdate that includes the center action.
        repo.SetManagedComponent(entity, new ContextMenuState
        {
            IsOpen  = false,
            Actions = new List<ContextAction>
            {
                new ContextAction { Label = "Center on Entity", ActionName = "IG_CenterOnEntity" },
                new ContextAction { Label = "Properties...",    ActionName = "2" }
            }
        });

        var system = new ContextMenuSystem();
        system.TestHook_TriggerContextMenu(entity, ScreenX, ScreenY);
        RunSystem(repo, system);

        var state = TryGetMenuState(repo, entity);
        Assert.NotNull(state);
        var centerCount = state!.Actions.Count(
            a => a.ActionName == "IG_CenterOnEntity" || a.ActionName == "IG_Center");
        Assert.Equal(1, centerCount);
    }

    /// <summary>
    /// Verifies that re-opening a menu for the same entity does not permanently
    /// mutate the action list stored from a previous open cycle.  Specifically,
    /// the second open must still see the original ExCon-provided actions.
    /// </summary>
    [Fact]
    public void Reopen_ActionListIsCloned_NotSharedWithPreviousTick()
    {
        var repo   = CreateRepo();
        repo.RegisterComponent<SimTransform>();
        var entity = repo.CreateEntity();
        // No SimTransform — prevents "Center on Entity" injection noise.

        repo.SetManagedComponent(entity, new ContextMenuState
        {
            IsOpen  = false,
            Actions = new List<ContextAction>
            {
                new ContextAction { Label = "Action A", ActionName = "a" }
            }
        });

        var system = new ContextMenuSystem();

        // First open.
        system.TestHook_TriggerContextMenu(entity, ScreenX, ScreenY);
        RunSystem(repo, system);
        var state1 = TryGetMenuState(repo, entity);
        Assert.Single(state1!.Actions);

        // Close it (so the state goes back to a closed state).
        system.TestHook_CloseContextMenu(entity);
        RunSystem(repo, system);

        // Second open — must get the same 1-action list, not an inflated one.
        system.TestHook_TriggerContextMenu(entity, ScreenX, ScreenY);
        RunSystem(repo, system);
        var state2 = TryGetMenuState(repo, entity);
        Assert.Single(state2!.Actions);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Regression: context menu must not be a one-shot feature
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Regression test for the "one-shot context menu" bug.
    ///
    /// Root cause: <see cref="ContextMenuSystem"/> runs in <c>PostSimulation</c> and
    /// issues its state changes via <c>cmd.SetManagedComponent</c>.  The command
    /// buffer is flushed in <c>BeforeSync</c> — which happens AFTER the UI Draw call.
    /// When the operator right-clicks a second time, Execute processes the open and
    /// (synchronously) increments <see cref="ContextMenuSystem.OpenSequence"/> and sets
    /// <see cref="ContextMenuSystem.ActiveMenuEntity"/>.  However the
    /// <see cref="ContextMenuState.IsOpen"/> flag is still <c>false</c> in the ECS
    /// view because the command buffer hasn't been flushed yet.
    ///
    /// A naive Draw() that sees <c>IsOpen=false</c> while <c>ActiveMenuEntity != Null</c>
    /// would call <c>RequestClose</c>, which in the next Execute would overwrite the
    /// pending open with a close — leaving the menu permanently broken after the first
    /// use.  The correct guard is: if <c>OpenSequence != _lastOpenSequence</c> (a fresh
    /// open is in-flight), skip <c>RequestClose</c> this frame.
    ///
    /// This test verifies the two observable facts the panel relies on:
    /// 1. After <c>RunExecuteOnly</c> (no playback), <c>OpenSequence</c> is already
    ///    incremented — the panel can detect the in-flight open.
    /// 2. After <c>FlushCommandBuffer</c>, <c>IsOpen</c> becomes <c>true</c>.
    /// </summary>
    [Fact]
    public void Reopen_OpenSequenceAdvancesBeforePlayback_PreventsFalseClose()
    {
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        var system = new ContextMenuSystem();

        // Frame 1: open.
        system.TestHook_TriggerContextMenu(entity, ScreenX, ScreenY);
        RunSystem(repo, system);
        Assert.True(TryGetMenuState(repo, entity)!.IsOpen);
        Assert.Equal(1, system.OpenSequence);

        // Frame 2: close.
        system.TestHook_CloseContextMenu(entity);
        RunSystem(repo, system);
        Assert.False(TryGetMenuState(repo, entity)!.IsOpen);
        Assert.Equal(Entity.Null, system.ActiveMenuEntity);

        // Frame 3 — Execute only (no cmd playback yet).  This mirrors the real
        // runtime where Draw() fires between Execute and BeforeSync flush.
        system.TestHook_TriggerContextMenu(entity, ScreenX + 10f, ScreenY + 10f);
        RunExecuteOnly(repo, system);

        // These two fields are updated synchronously inside Execute (no cmd needed).
        // The panel's "freshOpen" guard relies on OpenSequence being already advanced
        // here so it knows NOT to call RequestClose on the stale IsOpen=false.
        Assert.True(system.OpenSequence == 2,
            "OpenSequence must be incremented synchronously in Execute before cmd playback.");
        Assert.True(system.ActiveMenuEntity == entity,
            "ActiveMenuEntity must be set synchronously in Execute before cmd playback.");

        // The ECS state is still stale (cmd not flushed).
        var staleState = TryGetMenuState(repo, entity);
        Assert.NotNull(staleState);
        Assert.False(staleState!.IsOpen,
            "ECS IsOpen must still be false before cmd playback — this is the stale state " +
            "that was causing the false RequestClose.");

        // Simulate BeforeSync flush.
        FlushCommandBuffer(repo);

        // After flush, IsOpen must be true — the reopen is complete.
        var freshState = TryGetMenuState(repo, entity);
        Assert.NotNull(freshState);
        Assert.True(freshState!.IsOpen,
            "After cmd playback IsOpen must be true — the menu reopen must succeed.");
        Assert.Equal(ScreenX + 10f, freshState.ScreenX);
        Assert.Equal(ScreenY + 10f, freshState.ScreenY);
    }

    /// <summary>
    /// Verifies the full open → close → open lifecycle across multiple sequential
    /// cycles using the normal (execute-then-playback) path.
    /// </summary>
    [Fact]
    public void MultipleOpenCloseCycles_AllSucceed()
    {
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        var system = new ContextMenuSystem();

        for (int i = 1; i <= 3; i++)
        {
            system.TestHook_TriggerContextMenu(entity, ScreenX, ScreenY);
            RunSystem(repo, system);

            Assert.True(TryGetMenuState(repo, entity)!.IsOpen,
                $"Cycle {i}: IsOpen must be true after open.");
            Assert.True(system.ActiveMenuEntity == entity,
                $"Cycle {i}: ActiveMenuEntity must be the entity.");
            Assert.True(system.OpenSequence == i,
                $"Cycle {i}: OpenSequence must equal cycle index.");

            system.TestHook_CloseContextMenu(entity);
            RunSystem(repo, system);

            Assert.False(TryGetMenuState(repo, entity)!.IsOpen,
                $"Cycle {i}: IsOpen must be false after close.");
            Assert.True(system.ActiveMenuEntity == Entity.Null,
                $"Cycle {i}: ActiveMenuEntity must be Null after close.");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ContextMenuRequest cache-miss fallback (right-click without prior selection)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Records every cache-miss callback invocation (requestId, mapId, forSelection).</summary>
    private sealed class FakeCacheMissCallback
    {
        public List<(Guid RequestId, int MapId, IReadOnlyList<int> ForSelection)> Written { get; } = new();
        public Action<Guid, int, IReadOnlyList<int>> Callback =>
            (reqId, mapId, sel) => Written.Add((reqId, mapId, sel));
    }

    private static ContextMenuSystem CreateSystemWithWriter(
        FakeCacheMissCallback writer, int mapId = 1)
    {
        var system = new ContextMenuSystem();
        system.SetCacheMissWriter(writer.Callback, mapId);
        return system;
    }

    /// <summary>
    /// When the operator right-clicks an entity that has NO cached ExCon actions
    /// (fresh right-click without prior selection), the system must publish a
    /// <see cref="ContextMenuRequest"/> so the ExCon can push back the menu definition.
    /// </summary>
    [Fact]
    public void OpenMenu_CacheMiss_EmitsContextMenuRequest()
    {
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new NetworkIdentity(42));

        var writer = new FakeCacheMissCallback();
        var system = CreateSystemWithWriter(writer, mapId: 7);

        system.TestHook_TriggerContextMenu(entity, ScreenX, ScreenY);
        RunSystem(repo, system);

        Assert.Single(writer.Written);
        var req = writer.Written[0];
        Assert.Equal(7, req.MapId);
        Assert.NotEqual(Guid.Empty, req.RequestId);
        Assert.NotNull(req.ForSelection);
        Assert.Single(req.ForSelection);
        Assert.Equal(42, req.ForSelection[0]);
    }

    /// <summary>
    /// When the entity already has ExCon-provided actions cached (the normal push-model
    /// happy path — entity was previously selected), no <see cref="ContextMenuRequest"/>
    /// should be emitted on right-click.
    /// </summary>
    [Fact]
    public void OpenMenu_CacheHit_DoesNotEmitContextMenuRequest()
    {
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new NetworkIdentity(10));

        // Pre-populate with an ExCon-provided action list (the "cache hit" scenario).
        repo.SetManagedComponent(entity, new ContextMenuState
        {
            IsOpen  = false,
            Actions = new List<ContextAction>
            {
                new ContextAction { Label = "Attack",  ActionName = "attack" },
                new ContextAction { Label = "Move To", ActionName = "moveTo" },
            }
        });

        var writer = new FakeCacheMissCallback();
        var system = CreateSystemWithWriter(writer);

        system.TestHook_TriggerContextMenu(entity, ScreenX, ScreenY);
        RunSystem(repo, system);

        Assert.Empty(writer.Written);
    }

    /// <summary>
    /// The map-background entity (<c>_mapContextEntity</c>, NetworkIdentity = 0)
    /// represents empty-space right-clicks.  The ExCon has no concept of a
    /// zero-ID entity, so the system must NOT emit a <see cref="ContextMenuRequest"/>
    /// for it even when its action list is empty.
    /// </summary>
    [Fact]
    public void OpenMenu_MapContextEntity_DoesNotEmitContextMenuRequest()
    {
        var repo        = CreateRepo();
        var mapContext  = repo.CreateEntity();
        repo.AddComponent(mapContext, new NetworkIdentity(0));   // the background entity

        var writer = new FakeCacheMissCallback();
        var system = CreateSystemWithWriter(writer);

        system.TestHook_TriggerContextMenu(mapContext, ScreenX, ScreenY);
        RunSystem(repo, system);

        Assert.Empty(writer.Written);
    }

    /// <summary>
    /// If the cached state contains ONLY IG-local defaults (e.g. <c>IG_CenterOnEntity</c>
    /// was injected on a previous open but the ExCon never responded), the entity still has
    /// no ExCon-provided actions — so a <see cref="ContextMenuRequest"/> must be emitted
    /// on the next right-click.
    /// </summary>
    [Fact]
    public void OpenMenu_OnlyIgLocalActionsInCache_EmitsContextMenuRequest()
    {
        var repo   = CreateRepo();
        repo.RegisterComponent<SimTransform>();
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new NetworkIdentity(99));
        repo.AddComponent(entity, new SimTransform());

        // Seed state as if a previous open injected IG_CenterOnEntity but ExCon never responded.
        repo.SetManagedComponent(entity, new ContextMenuState
        {
            IsOpen  = false,
            Actions = new List<ContextAction>
            {
                new ContextAction { Label = "Center on Entity", ActionName = "IG_CenterOnEntity" }
            }
        });

        var writer = new FakeCacheMissCallback();
        var system = CreateSystemWithWriter(writer);

        system.TestHook_TriggerContextMenu(entity, ScreenX, ScreenY);
        RunSystem(repo, system);

        Assert.Single(writer.Written);
        Assert.Equal(99, writer.Written[0].ForSelection[0]);
    }

    /// <summary>
    /// When no writer is configured (<c>SetCacheMissWriter</c> not called), the system
    /// must open the menu correctly and not throw — verifying offline / test-mode safety.
    /// All existing tests implicitly cover this, but this test makes the contract explicit.
    /// </summary>
    [Fact]
    public void OpenMenu_NoWriterConfigured_MenuOpensWithoutError()
    {
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new NetworkIdentity(5));

        var system = new ContextMenuSystem();   // no writer wired

        system.TestHook_TriggerContextMenu(entity, ScreenX, ScreenY);
        RunSystem(repo, system);

        var state = TryGetMenuState(repo, entity);
        Assert.NotNull(state);
        Assert.True(state!.IsOpen);
    }
}
