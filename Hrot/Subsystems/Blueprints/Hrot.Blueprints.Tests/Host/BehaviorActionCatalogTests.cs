using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor.ActionCatalog;
using Hrot.Editor.AiShared.Blackboard;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

// ---------------------------------------------------------------------------
// Fake source catalogs
// ---------------------------------------------------------------------------

/// <summary>
/// Minimal fake <see cref="IChannelCommandCatalog"/> whose entries can be replaced per test.
/// </summary>
internal sealed class FakeChannelCommandCatalog : IChannelCommandCatalog
{
    private List<ChannelCommandCatalogEntry> _entries;

    public FakeChannelCommandCatalog(params ChannelCommandCatalogEntry[] entries)
        => _entries = new List<ChannelCommandCatalogEntry>(entries);

    public void SetEntries(params ChannelCommandCatalogEntry[] entries)
        => _entries = new List<ChannelCommandCatalogEntry>(entries);

    public IReadOnlyList<ChannelCommandCatalogEntry> GetEntries() => _entries;
}

/// <summary>
/// Minimal fake <see cref="IActionSchemaExporter"/> whose entries can be replaced per test.
/// </summary>
internal sealed class FakeActionSchemaExporter : IActionSchemaExporter
{
    private Dictionary<string, ActionSchemaEntry> _entries = new();

    public IReadOnlyDictionary<string, ActionSchemaEntry> All => _entries;

    public ActionSchemaEntry? Lookup(string fqn)
        => _entries.TryGetValue(fqn, out var e) ? e : null;

    public void Rebuild() => Changed?.Invoke();

    public event Action? Changed;

    public void SetEntries(params ActionSchemaEntry[] entries)
    {
        _entries = entries.ToDictionary(e => e.Fqn, e => e);
    }

    /// <summary>Triggers the Changed event without modifying entries.</summary>
    public void FireChanged() => Changed?.Invoke();
}

// ---------------------------------------------------------------------------
// Test class
// ---------------------------------------------------------------------------

/// <summary>
/// Headless tests for AN3 — <see cref="BehaviorActionCatalog"/> facade.
/// </summary>
public sealed class BehaviorActionCatalogTests
{
    // ── Shared fixture types ─────────────────────────────────────────────────
    private const string LocoChannel = "Fdp.Toolkit.Behavior.Components.LocomotionChannel";
    private const string MoveToParams = "Fdp.Toolkit.Navigation.MoveToParams";

    private static readonly ChannelCommandCatalogEntry CcMoveTo =
        new("MoveTo", LocoChannel, 1, MoveToParams);

    private struct FakeBTreeDto { public int Value; }
    private struct FakeHsmDto   { public float X; }

    private static ActionSchemaEntry MakeBTreeEntry(string fqn) =>
        new(fqn, typeof(FakeBTreeDto), ActionHosting.BTree, BlackboardAccess.Unknown, null);

    private static ActionSchemaEntry MakeHsmEntry(string fqn) =>
        new(fqn, typeof(FakeHsmDto), ActionHosting.Hsm, BlackboardAccess.Unknown, null);

    private static ActionSchemaEntry MakeSharedEntry(string fqn) =>
        new(fqn, typeof(FakeBTreeDto),
            ActionHosting.BTree | ActionHosting.Hsm | ActionHosting.Shared,
            BlackboardAccess.Unknown, null);

    // ── 1. Channel-command entries ───────────────────────────────────────────

    [Fact]
    public void GetActions_ChannelCommandEntry_IsPresentInSnapshot()
    {
        var cc  = new FakeChannelCommandCatalog(CcMoveTo);
        var ase = new FakeActionSchemaExporter();

        using var catalog = new BehaviorActionCatalog(cc, ase);
        var actions = catalog.GetActions();

        Assert.Contains(actions, e => e.DisplayName == "MoveTo");
    }

    [Fact]
    public void GetActions_ChannelCommandEntry_SourceIsChannelCommand()
    {
        var cc  = new FakeChannelCommandCatalog(CcMoveTo);
        var ase = new FakeActionSchemaExporter();

        using var catalog = new BehaviorActionCatalog(cc, ase);
        var entry = catalog.GetActions().Single(e => e.DisplayName == "MoveTo");

        Assert.Equal(BehaviorActionSource.ChannelCommand, entry.Source);
    }

    [Fact]
    public void GetActions_ChannelCommandEntry_ValidHostsIncludesBlueprint()
    {
        var cc  = new FakeChannelCommandCatalog(CcMoveTo);
        var ase = new FakeActionSchemaExporter();

        using var catalog = new BehaviorActionCatalog(cc, ase);
        var entry = catalog.GetActions().Single(e => e.DisplayName == "MoveTo");

        Assert.True(entry.ValidHosts.HasFlag(BehaviorActionHosts.Blueprint));
    }

    [Fact]
    public void GetActions_ChannelCommandEntry_ValidHostsDoesNotIncludeBTreeOrHsm()
    {
        var cc  = new FakeChannelCommandCatalog(CcMoveTo);
        var ase = new FakeActionSchemaExporter();

        using var catalog = new BehaviorActionCatalog(cc, ase);
        var entry = catalog.GetActions().Single(e => e.DisplayName == "MoveTo");

        Assert.False(entry.ValidHosts.HasFlag(BehaviorActionHosts.BTree));
        Assert.False(entry.ValidHosts.HasFlag(BehaviorActionHosts.Hsm));
    }

    [Fact]
    public void GetActions_ChannelCommandEntry_ChannelTypeFqnMatches()
    {
        var cc  = new FakeChannelCommandCatalog(CcMoveTo);
        var ase = new FakeActionSchemaExporter();

        using var catalog = new BehaviorActionCatalog(cc, ase);
        var entry = catalog.GetActions().Single(e => e.DisplayName == "MoveTo");

        Assert.Equal(LocoChannel, entry.ChannelTypeFqn);
    }

    [Fact]
    public void GetActions_ChannelCommandEntry_ActionIdMatches()
    {
        var cc  = new FakeChannelCommandCatalog(CcMoveTo);
        var ase = new FakeActionSchemaExporter();

        using var catalog = new BehaviorActionCatalog(cc, ase);
        var entry = catalog.GetActions().Single(e => e.DisplayName == "MoveTo");

        Assert.Equal((ushort)1, entry.ActionId);
    }

    [Fact]
    public void GetActions_ChannelCommandEntry_ParamsTypeFqnMatches()
    {
        var cc  = new FakeChannelCommandCatalog(CcMoveTo);
        var ase = new FakeActionSchemaExporter();

        using var catalog = new BehaviorActionCatalog(cc, ase);
        var entry = catalog.GetActions().Single(e => e.DisplayName == "MoveTo");

        Assert.Equal(MoveToParams, entry.ParamsTypeFqn);
    }

    [Fact]
    public void GetActions_ChannelCommandEntry_IdIsChannelTypePlusSeparatorPlusActionId()
    {
        var cc  = new FakeChannelCommandCatalog(CcMoveTo);
        var ase = new FakeActionSchemaExporter();

        using var catalog = new BehaviorActionCatalog(cc, ase);
        var entry = catalog.GetActions().Single(e => e.DisplayName == "MoveTo");

        Assert.Equal($"{LocoChannel}::1", entry.Id);
    }

    // ── 2. Schema (Hardcoded) entries ────────────────────────────────────────

    [Fact]
    public void GetActions_HardcodedBTreeEntry_SourceIsHardcoded()
    {
        var cc  = new FakeChannelCommandCatalog();
        var ase = new FakeActionSchemaExporter();
        ase.SetEntries(MakeBTreeEntry("Foo.Bar.BTreeAction1"));

        using var catalog = new BehaviorActionCatalog(cc, ase);
        var entry = catalog.GetActions().Single(e => e.Id == "Foo.Bar.BTreeAction1");

        Assert.Equal(BehaviorActionSource.Hardcoded, entry.Source);
    }

    [Fact]
    public void GetActions_HardcodedBTreeEntry_ValidHostsIncludesBTree()
    {
        var cc  = new FakeChannelCommandCatalog();
        var ase = new FakeActionSchemaExporter();
        ase.SetEntries(MakeBTreeEntry("Foo.Bar.BTreeAction1"));

        using var catalog = new BehaviorActionCatalog(cc, ase);
        var entry = catalog.GetActions().Single(e => e.Id == "Foo.Bar.BTreeAction1");

        Assert.True(entry.ValidHosts.HasFlag(BehaviorActionHosts.BTree));
    }

    [Fact]
    public void GetActions_HardcodedBTreeEntry_ValidHostsDoesNotIncludeBlueprint()
    {
        var cc  = new FakeChannelCommandCatalog();
        var ase = new FakeActionSchemaExporter();
        ase.SetEntries(MakeBTreeEntry("Foo.Bar.BTreeAction1"));

        using var catalog = new BehaviorActionCatalog(cc, ase);
        var entry = catalog.GetActions().Single(e => e.Id == "Foo.Bar.BTreeAction1");

        Assert.False(entry.ValidHosts.HasFlag(BehaviorActionHosts.Blueprint));
    }

    [Fact]
    public void GetActions_HardcodedHsmEntry_ValidHostsIncludesHsm()
    {
        var cc  = new FakeChannelCommandCatalog();
        var ase = new FakeActionSchemaExporter();
        ase.SetEntries(MakeHsmEntry("Foo.Baz.HsmAction1"));

        using var catalog = new BehaviorActionCatalog(cc, ase);
        var entry = catalog.GetActions().Single(e => e.Id == "Foo.Baz.HsmAction1");

        Assert.True(entry.ValidHosts.HasFlag(BehaviorActionHosts.Hsm));
        Assert.False(entry.ValidHosts.HasFlag(BehaviorActionHosts.BTree));
    }

    [Fact]
    public void GetActions_SharedEntry_ValidHostsIncludesBTreeAndHsm()
    {
        var cc  = new FakeChannelCommandCatalog();
        var ase = new FakeActionSchemaExporter();
        ase.SetEntries(MakeSharedEntry("Foo.Shared.SharedAction1"));

        using var catalog = new BehaviorActionCatalog(cc, ase);
        var entry = catalog.GetActions().Single(e => e.Id == "Foo.Shared.SharedAction1");

        Assert.True(entry.ValidHosts.HasFlag(BehaviorActionHosts.BTree));
        Assert.True(entry.ValidHosts.HasFlag(BehaviorActionHosts.Hsm));
    }

    [Fact]
    public void GetActions_SchemaEntry_IdIsFqn()
    {
        const string fqn = "My.Namespace.MyNodes.DoThing";
        var cc  = new FakeChannelCommandCatalog();
        var ase = new FakeActionSchemaExporter();
        ase.SetEntries(MakeBTreeEntry(fqn));

        using var catalog = new BehaviorActionCatalog(cc, ase);
        var entry = catalog.GetActions().Single(e => e.Id == fqn);

        Assert.Equal(fqn, entry.Id);
    }

    [Fact]
    public void GetActions_SchemaEntry_ParamsTypeFqnIsDoTypeName()
    {
        var cc  = new FakeChannelCommandCatalog();
        var ase = new FakeActionSchemaExporter();
        ase.SetEntries(MakeBTreeEntry("Foo.Bar.BTreeAction1"));

        using var catalog = new BehaviorActionCatalog(cc, ase);
        var entry = catalog.GetActions().Single(e => e.Id == "Foo.Bar.BTreeAction1");

        Assert.Equal(typeof(FakeBTreeDto).FullName, entry.ParamsTypeFqn);
    }

    [Fact]
    public void GetActions_SchemaEntry_ChannelTypeFqnIsNull()
    {
        var cc  = new FakeChannelCommandCatalog();
        var ase = new FakeActionSchemaExporter();
        ase.SetEntries(MakeBTreeEntry("Foo.Bar.BTreeAction1"));

        using var catalog = new BehaviorActionCatalog(cc, ase);
        var entry = catalog.GetActions().Single(e => e.Id == "Foo.Bar.BTreeAction1");

        Assert.Null(entry.ChannelTypeFqn);
    }

    // ── 3. Composite: both sources together ─────────────────────────────────

    [Fact]
    public void GetActions_BothSources_AllEntriesPresent()
    {
        var cc  = new FakeChannelCommandCatalog(CcMoveTo);
        var ase = new FakeActionSchemaExporter();
        ase.SetEntries(MakeBTreeEntry("Foo.Bar.BTreeAction1"));

        using var catalog = new BehaviorActionCatalog(cc, ase);
        var actions = catalog.GetActions();

        Assert.Equal(2, actions.Count);
        Assert.Contains(actions, e => e.Source == BehaviorActionSource.ChannelCommand);
        Assert.Contains(actions, e => e.Source == BehaviorActionSource.Hardcoded);
    }

    [Fact]
    public void GetActions_EmptyCatalogs_ReturnsEmptyList()
    {
        var cc  = new FakeChannelCommandCatalog();
        var ase = new FakeActionSchemaExporter();

        using var catalog = new BehaviorActionCatalog(cc, ase);

        Assert.Empty(catalog.GetActions());
    }

    // ── 4. Host filtering ───────────────────────────────────────────────────

    [Fact]
    public void GetActionsByHost_Blueprint_ReturnsOnlyChannelCommands()
    {
        var cc  = new FakeChannelCommandCatalog(CcMoveTo);
        var ase = new FakeActionSchemaExporter();
        ase.SetEntries(MakeBTreeEntry("Foo.Bar.BTreeAction1"));

        using var catalog = new BehaviorActionCatalog(cc, ase);
        var blueprintActions = catalog.GetActions(BehaviorActionHosts.Blueprint);

        Assert.Single(blueprintActions);
        Assert.Equal(BehaviorActionSource.ChannelCommand, blueprintActions[0].Source);
    }

    [Fact]
    public void GetActionsByHost_BTree_ReturnsOnlyBTreeActions()
    {
        var cc  = new FakeChannelCommandCatalog(CcMoveTo);
        var ase = new FakeActionSchemaExporter();
        ase.SetEntries(MakeBTreeEntry("Foo.Bar.BTreeAction1"));

        using var catalog = new BehaviorActionCatalog(cc, ase);
        var btreeActions = catalog.GetActions(BehaviorActionHosts.BTree);

        Assert.Single(btreeActions);
        Assert.Equal("Foo.Bar.BTreeAction1", btreeActions[0].Id);
    }

    [Fact]
    public void GetActionsByHost_Hsm_ReturnsOnlyHsmActions()
    {
        var cc  = new FakeChannelCommandCatalog(CcMoveTo);
        var ase = new FakeActionSchemaExporter();
        ase.SetEntries(MakeHsmEntry("Foo.Baz.HsmAction1"));

        using var catalog = new BehaviorActionCatalog(cc, ase);
        var hsmActions = catalog.GetActions(BehaviorActionHosts.Hsm);

        Assert.Single(hsmActions);
        Assert.Equal("Foo.Baz.HsmAction1", hsmActions[0].Id);
    }

    [Fact]
    public void GetActionsByHost_BTreeOrHsm_DoesNotIncludeChannelCommands()
    {
        var cc  = new FakeChannelCommandCatalog(CcMoveTo);
        var ase = new FakeActionSchemaExporter();
        ase.SetEntries(MakeSharedEntry("Foo.Shared.SharedAction1"));

        using var catalog = new BehaviorActionCatalog(cc, ase);

        var btreeActions = catalog.GetActions(BehaviorActionHosts.BTree);
        var hsmActions   = catalog.GetActions(BehaviorActionHosts.Hsm);

        Assert.DoesNotContain(btreeActions,   e => e.Source == BehaviorActionSource.ChannelCommand);
        Assert.DoesNotContain(hsmActions,     e => e.Source == BehaviorActionSource.ChannelCommand);
    }

    [Fact]
    public void GetActionsByHost_SharedEntry_AppearsInBothBTreeAndHsm()
    {
        var cc  = new FakeChannelCommandCatalog();
        var ase = new FakeActionSchemaExporter();
        ase.SetEntries(MakeSharedEntry("Foo.Shared.SharedAction1"));

        using var catalog = new BehaviorActionCatalog(cc, ase);

        var btreeActions = catalog.GetActions(BehaviorActionHosts.BTree);
        var hsmActions   = catalog.GetActions(BehaviorActionHosts.Hsm);

        Assert.Single(btreeActions);
        Assert.Single(hsmActions);
        Assert.Equal("Foo.Shared.SharedAction1", btreeActions[0].Id);
        Assert.Equal("Foo.Shared.SharedAction1", hsmActions[0].Id);
    }

    // ── 5. Rebuild / Changed event ───────────────────────────────────────────

    [Fact]
    public void Changed_FiredAfterInitialRebuild()
    {
        var cc  = new FakeChannelCommandCatalog(CcMoveTo);
        var ase = new FakeActionSchemaExporter();

        int changedCount = 0;

        // Subscribe before construction.
        // Note: Changed fires during Rebuild() in constructor, but we attach after.
        // So we fire manually via FireChanged to verify the wiring.
        using var catalog = new BehaviorActionCatalog(cc, ase);
        catalog.Changed += () => changedCount++;

        ase.FireChanged();

        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void SchemaChanged_TriggersRebuild_UpdatesSnapshot()
    {
        var cc  = new FakeChannelCommandCatalog(CcMoveTo);
        var ase = new FakeActionSchemaExporter();

        using var catalog = new BehaviorActionCatalog(cc, ase);
        Assert.Single(catalog.GetActions()); // only channel command initially

        // Now add a BTree entry and fire Changed.
        ase.SetEntries(MakeBTreeEntry("Foo.Bar.BTreeAction1"));
        ase.FireChanged();

        Assert.Equal(2, catalog.GetActions().Count);
    }

    [Fact]
    public void SchemaChanged_Twice_SnapshotIsLatest()
    {
        var cc  = new FakeChannelCommandCatalog();
        var ase = new FakeActionSchemaExporter();

        using var catalog = new BehaviorActionCatalog(cc, ase);

        ase.SetEntries(MakeBTreeEntry("Foo.Bar.Action1"));
        ase.FireChanged();
        Assert.Single(catalog.GetActions());

        ase.SetEntries(
            MakeBTreeEntry("Foo.Bar.Action1"),
            MakeBTreeEntry("Foo.Bar.Action2"));
        ase.FireChanged();
        Assert.Equal(2, catalog.GetActions().Count);
    }

    [Fact]
    public void Changed_RaisedAfterSchemaRebuild()
    {
        var cc  = new FakeChannelCommandCatalog();
        var ase = new FakeActionSchemaExporter();

        using var catalog = new BehaviorActionCatalog(cc, ase);

        int count = 0;
        catalog.Changed += () => count++;

        ase.SetEntries(MakeBTreeEntry("Foo.Bar.Action1"));
        ase.FireChanged();

        Assert.Equal(1, count);
    }

    [Fact]
    public void AfterDispose_SchemaChanged_DoesNotUpdateSnapshot()
    {
        var cc  = new FakeChannelCommandCatalog();
        var ase = new FakeActionSchemaExporter();

        var catalog = new BehaviorActionCatalog(cc, ase);
        catalog.Dispose();

        // Modifying entries after dispose should not affect the snapshot.
        ase.SetEntries(MakeBTreeEntry("Foo.Bar.Action1"));
        ase.FireChanged();

        // Snapshot should remain empty (as it was at construction with no entries).
        Assert.Empty(catalog.GetActions());
    }

    // ── 6. Multiple channel commands ────────────────────────────────────────

    [Fact]
    public void GetActions_MultipleChannelCommands_AllPresent()
    {
        var weaponChannel = "Fdp.Toolkit.Behavior.Components.WeaponChannel";
        var entries = new[]
        {
            new ChannelCommandCatalogEntry("MoveTo",     LocoChannel,    1, MoveToParams),
            new ChannelCommandCatalogEntry("AimAndFire", weaponChannel,  1, "Fdp.Toolkit.Combat.Executors.AimAndFireParams"),
        };
        var cc  = new FakeChannelCommandCatalog(entries);
        var ase = new FakeActionSchemaExporter();

        using var catalog = new BehaviorActionCatalog(cc, ase);
        var actions = catalog.GetActions(BehaviorActionHosts.Blueprint);

        Assert.Equal(2, actions.Count);
        Assert.Contains(actions, e => e.DisplayName == "MoveTo");
        Assert.Contains(actions, e => e.DisplayName == "AimAndFire");
    }

    [Fact]
    public void GetActions_MultipleChannelCommandsOnSameChannel_HaveDistinctIds()
    {
        var entries = new[]
        {
            new ChannelCommandCatalogEntry("MoveTo",      LocoChannel, 1, MoveToParams),
            new ChannelCommandCatalogEntry("FollowRoute", LocoChannel, 3, "Fdp.Toolkit.Navigation.FollowRouteParams"),
        };
        var cc  = new FakeChannelCommandCatalog(entries);
        var ase = new FakeActionSchemaExporter();

        using var catalog = new BehaviorActionCatalog(cc, ase);
        var actions = catalog.GetActions(BehaviorActionHosts.Blueprint);

        var ids = actions.Select(e => e.Id).ToList();
        Assert.Equal(ids.Distinct().Count(), ids.Count);
    }
}
