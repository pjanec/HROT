using System.Collections.Generic;
using Bagira.IG.Components;
using Bagira.IG.Systems;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Abstractions;

namespace Bagira.IG.Tests;

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
}
