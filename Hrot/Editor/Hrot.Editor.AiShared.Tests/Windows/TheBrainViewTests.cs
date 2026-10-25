using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Hrot.Editor.UiFrameRail;
using ImGuiNET;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐⭐ <c>CE-1248</c> — the Brain tab's reader and its place on every host. 📄 docs/DESIGN_Entity_Brain_View.md §1–§8.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed unsafe class TheBrainViewTests : IDisposable
{
    private const int Patrol = 11, Duck = 22;

    public TheBrainViewTests() { PanelSnapshot.Clear(); PanelSnapshot.CaptureEnabled = false; }

    public void Dispose() { PanelSnapshot.Clear(); PanelSnapshot.CaptureEnabled = false; }

    private static string? Names(int hash) => hash switch { Patrol => "Patrol", Duck => "Duck", _ => null };

    private static EntityRepository World(bool brainNode = true)
    {
        var w = new EntityRepository();
        w.RegisterComponent<BehaviorState>();
        if (brainNode)
        {
            w.RegisterComponent<ActionStatus>();
            w.RegisterComponent<BrainJournal>();
        }
        return w;
    }

    private static Entity Soldier(EntityRepository w, bool brainNode = true)
    {
        var e = w.CreateEntity();
        w.AddComponent(e, new BehaviorState
        {
            ActiveBehaviorHash = Duck, Origin = BehaviorOrigin.Reaction, Urgency = ReactionUrgency.UnderFire,
        });
        if (!brainNode) return e;
        var status = new ActionStatus();
        status.Weapon.ActionId = 7;
        status.Weapon.Reason = ActionReason.HoldNotSeen;
        w.AddComponent(e, status);
        w.AddComponent(e, new BrainJournal());
        BrainJournalOf.Write(w, e, BrainJournalKind.TaskStarted, a: Patrol, origin: (byte)BehaviorOrigin.Superior);
        BrainJournalOf.Write(w, e, BrainJournalKind.TaskPaused, a: Patrol, b: Duck);
        BrainJournalOf.Write(w, e, BrainJournalKind.TaskStarted, a: Duck, origin: (byte)BehaviorOrigin.Reaction,
            urgency: (byte)ReactionUrgency.UnderFire);
        return e;
    }

    /// <summary>⭐ Doing / Ordered by / Right now / Recent changes read from what the brain records, names resolved, newest change first.
    /// 🔴 Red-proof: list the journal oldest first in <c>BrainSnapshot.Read</c> ⇒ red.</summary>
    [Fact]
    public void CE1248_TheReader_TellsWhatItDoes_WhoOrderedIt_AndWhatChanged()
    {
        using var w = World();
        var b = BrainSnapshot.Read(w, Soldier(w), Names)!;

        Assert.True(b.OnBrainNode);
        Assert.Equal("Duck", b.Doing);
        Assert.Equal("Reaction", b.Origin);
        Assert.Equal("UnderFire", b.Urgency);
        var row = Assert.Single(b.RightNow);
        Assert.Equal("Weapon", row.Channel);
        Assert.True(row.IsHold);
        Assert.Equal(3u, b.JournalSeq);
        Assert.Equal(3, b.Changes.Count);
        Assert.StartsWith("started Duck (Reaction, UnderFire)", b.Changes[0].Text);
        Assert.Equal("paused Patrol for Duck", b.Changes[1].Text);
    }

    /// <summary>⭐ BV-6 — a node without the brain says so instead of showing empty rows; no BehaviorState = no brain.</summary>
    [Fact]
    public void CE1248_ANodeWithoutTheBrain_SaysSo()
    {
        using var w = World(brainNode: false);
        var b = BrainSnapshot.Read(w, Soldier(w, brainNode: false), Names)!;
        Assert.False(b.OnBrainNode);
        Assert.False(b.JournalOn);
        Assert.Equal("Duck", b.Doing);

        var bare = w.CreateEntity();
        Assert.Null(BrainSnapshot.Read(w, bare, Names));
    }

    /// <summary>⭐ BV-1 — the shared Details bundle offers the Brain tab for a brain unit (every host composes it), and the tab dumps what
    /// it shows. 🔴 Red-proof: drop the <c>EntityBrainViewDescriptor</c> line from <c>EntityDetailsBundle</c> ⇒ red.</summary>
    [Fact]
    public void CE1248_TheBundle_OffersTheBrainTab_ForABrainUnit_AndItDumps()
    {
        using var w = World();
        var e = Soldier(w);
        var windows = new WindowManager(new IconAtlas(nint.Zero, 1, 1, 16f));
        var bundle = new EntityDetailsBundle(new EntityDetailsHostServices(
            WindowId: "details_ce1248", Perspective: "IG", World: () => w,
            Inspector: () => null, Session: () => null, RunState: () => VariableRunState.Running, Host: () => null));
        Fdp.Toolkit.Runner.UiBundleHost.Compose(new Fdp.Toolkit.Runner.IUiBundle[] { bundle }, new Fdp.Toolkit.Runner.UiBundleContext(windows));

        var ctx = DetailsContext.Empty("IG") with { Entities = new[] { e } };
        var offered = bundle.Workspace.DetailsViews.OfferSet(ctx);
        Assert.Contains(offered, d => d.Id == EntityBrainViewDescriptor.ViewId);
        Assert.Equal(EntitySummaryViewDescriptor.ViewId, bundle.Workspace.DetailsViews.Default(ctx)?.Id);   // Summary stays the card

        var plain = w.CreateEntity();
        Assert.DoesNotContain(bundle.Workspace.DetailsViews.OfferSet(DetailsContext.Empty("IG") with { Entities = new[] { plain } }),
            d => d.Id == EntityBrainViewDescriptor.ViewId);

        PanelSnapshot.CaptureEnabled = true;
        var view = (EntityBrainView)Array.Find(System.Linq.Enumerable.ToArray(offered), d => d.Id == EntityBrainViewDescriptor.ViewId)!.Create();
        view.SimulateDraw(ctx, "details_ce1248");
        var dump = PanelSnapshot.TryGet($"details_ce1248/{EntityBrainViewDescriptor.ViewId}")!.Dump();
        Assert.Equal(3u, dump["brain"]!["journal"]!["seq"]!.GetValue<uint>());
    }

    /// <summary>⭐ A region entered from no state (0xFFFF — the machine's start) reads "entered", not <c>State_65535 → …</c> (measured live
    /// `2026-10-23`). 🔴 Red-proof: drop the 0xFFFF branch in <c>BrainJournalText.Describe</c> ⇒ red.</summary>
    [Fact]
    public void ARegionEnteredFromNoState_ReadsEntered()
    {
        var e = new BrainJournalEntry { Kind = BrainJournalKind.StateChanged, A = 0xFFFF | (3 << 16), B = 7 };
        Assert.Equal("M region 0: entered state 3", BrainJournalText.Describe(e, _ => "M"));
        var moved = new BrainJournalEntry { Kind = BrainJournalKind.StateChanged, A = 2 | (3 << 16), B = 7 };
        Assert.Equal("M region 0: state 2 → state 3", BrainJournalText.Describe(moved, _ => "M"));
    }

    /// <summary>⭐ <c>CE-1254</c> (BV-21) — the locator names the unit's RUNNING behaviour and its tier (what "Open running behaviour"
    /// opens); no behaviour, no answer. 🔴 Red-proof: read the SOP instead of <c>ActiveBehaviorHash</c> ⇒ red.</summary>
    [Fact]
    public void TheLocator_NamesTheRunningBehaviour_AndItsTier()
    {
        using var w = new EntityRepository();
        w.RegisterComponent<BehaviorState>();
        var unit = w.CreateEntity();
        w.AddComponent(unit, new BehaviorState { ActiveBehaviorHash = 5 });
        var def = new Fdp.Toolkit.Behavior.BehaviorDefinition { Name = "PlatoonDefend", BrainTier = Fdp.Toolkit.Behavior.BehaviorConstants.BrainTierHsm };

        var running = RunningBehaviourLocator.Resolve(w, unit, id => id == 5 ? "PlatoonDefend" : null, id => id == 5 ? def : null);
        Assert.Equal(new RunningRef(5, "PlatoonDefend", "HSM"), running);
        Assert.Null(RunningBehaviourLocator.Resolve(w, w.CreateEntity(), _ => "x", null));
    }

    // ── CE-1315 (Brain View §12b BV-42…BV-45): every running behaviour, through the ONE slot reader ──────────────────────────────

    [InlineArray(4)] public struct Shots4 { private int _e0; }
    public struct ShotList { public int Count; public Shots4 Items; }
    public struct FightParams { public Entity Target; public float CoverRadius; }
    public struct FightVars { public Vector3 CoverPoint; public ShotList Shots; public int Phase; }
    public struct FightBlock { public FightParams In; public FightVars St; }
    public struct ChatterParams { public float Interval; }
    public struct ChatterState { public Fdp.Toolkit.Blueprints.BlueprintLatentCursor Cursor; public ChatterParams Params; public int Messages; }

    private const int Fight = 0x5A11, Chatter = 0x0C4A7;

    /// <summary>A brain unit whose task block and one attached blueprint sit in a REAL occurrence store.</summary>
    private static (EntityRepository W, Entity Unit, Fdp.Toolkit.Behavior.BehaviorRegistry Reg, Fdp.Toolkit.Blueprints.BlueprintRegistry Bps)
        RunningWorld()
    {
        var w = World();
        Fdp.Toolkit.Blueprints.Partitioning.BlueprintTierTable.RegisterAll(w);
        var reg = new Fdp.Toolkit.Behavior.BehaviorRegistry();
        reg.Register(Fight, "FightFromCover", new Fdp.Toolkit.Behavior.BehaviorDefinition
        {
            Name = "FightFromCover", BrainTier = Fdp.Toolkit.Behavior.BehaviorConstants.BrainTierBTree, BlackboardLayoutType = typeof(FightBlock),
        });
        var bps = new Fdp.Toolkit.Blueprints.BlueprintRegistry();
        bps.RegisterInstance(Chatter, new Fdp.Toolkit.Blueprints.BlueprintDefinition
        {
            Name = "RadioChatter", Kind = Fdp.Toolkit.Blueprints.BlueprintDispatchKind.Instance, StructureHash = 7,
            StateSize = sizeof(ChatterState), StateClrType = typeof(ChatterState), ParamsOffset = 16, ParamsSize = sizeof(ChatterParams),
        });

        var e = w.CreateEntity();
        w.AddComponent(e, new BehaviorState { ActiveBehaviorHash = Fight });
        w.AddComponent(e, new BrainJournal());   // a brain node (BV-6)
        var spec = Fdp.Toolkit.Blueprints.Partitioning.BlueprintTierTable.Select(sizeof(FightBlock) + sizeof(ChatterState) + 64, requiredSlots: 2);
        spec.Add(w, e);
        Fdp.Toolkit.Blueprints.Partitioning.BlueprintBlackboardPartitions.Initialize(spec.Memory(w, e), spec.TotalSize, (byte)spec.MaxSlots);

        byte* block = Fdp.Toolkit.Behavior.RootParamsAccess.ResolveOrAttachRoot(w, e, Fight, sizeof(FightBlock),
            Fdp.Toolkit.Blueprints.Partitioning.OccurrenceKind.BTree, out _);
        var fight = new FightBlock { In = new FightParams { Target = new Entity(12, 3), CoverRadius = 25f } };
        fight.St.CoverPoint = new Vector3(412f, 3.1f, -88.5f);
        fight.St.Shots.Count = 3; fight.St.Shots.Items[0] = 11; fight.St.Shots.Items[1] = 12; fight.St.Shots.Items[2] = 13; fight.St.Shots.Items[3] = 99;
        fight.St.Phase = 2;
        *(FightBlock*)block = fight;

        Assert.True(Fdp.Toolkit.Blueprints.Partitioning.OccurrenceStoreAccess.TryAttachSlot(w, e, Chatter, sizeof(ChatterState), 7,
            Fdp.Toolkit.Blueprints.Partitioning.OccurrenceKind.Blueprint, out byte* bpBlock, out int bpOffset));
        *(ChatterState*)(bpBlock + bpOffset) = new ChatterState { Params = new ChatterParams { Interval = 4.5f }, Messages = 6 };
        return (w, e, reg, bps);
    }

    /// <summary>⭐ CE-1315 — the slot reader boxes each slot as its OWN struct (the exact managed image, an [InlineArray] included) and
    /// names an attached blueprint. 🔴 Red-proof: drop the blueprint arm in <c>OccurrenceSlotCatalog.Read</c> ⇒ no InstanceBlueprint row.</summary>
    [Fact]
    public void CE1315_TheSlotReader_BoxesEverySlot_AndNamesTheAttachedBlueprint()
    {
        var (w, e, reg, bps) = RunningWorld();
        using var _ = w;
        var slots = Fdp.Toolkit.Behavior.OccurrenceSlotCatalog.Read(w, e, reg, bps);

        var task = Assert.Single(slots, s => s.Owner == Fdp.Toolkit.Behavior.SlotOwnerKind.Task && s.Role == Fdp.Toolkit.Behavior.SlotRole.Params);
        var fight = Assert.IsType<FightBlock>(task.Boxed);
        Assert.Equal(13, fight.St.Shots.Items[2]);
        var bp = Assert.Single(slots, s => s.Owner == Fdp.Toolkit.Behavior.SlotOwnerKind.InstanceBlueprint);
        Assert.Equal("RadioChatter", bp.Behaviour);
        Assert.Equal(6, Assert.IsType<ChatterState>(bp.Boxed).Messages);
    }

    /// <summary>⭐ CE-1315 (BV-42) — task first, blueprint after; a block splits into parameters (<c>In</c>) and variables (<c>St</c>); a
    /// blueprint's State gives its <c>Params</c> and hides <c>Cursor</c> / <c>Params</c> from the variables. 🔴 Red-proof: return the
    /// block whole from <c>SplitBlock</c> ⇒ the variables are null.</summary>
    [Fact]
    public void CE1315_RunningBehaviours_SplitParamsFromVariables_ForTasksAndBlueprints()
    {
        var (w, e, reg, bps) = RunningWorld();
        using var _ = w;
        var running = BrainRunningBehaviours.From(Fdp.Toolkit.Behavior.OccurrenceSlotCatalog.Read(w, e, reg, bps));

        Assert.Equal(new[] { "Task · FightFromCover", "Blueprint · RadioChatter" }, running.Select(r => $"{r.OwnerLabel} · {r.Name}").ToArray());
        Assert.Equal(25f, Assert.IsType<FightParams>(running[0].Parameters).CoverRadius);
        Assert.Equal(2, Assert.IsType<FightVars>(running[0].Variables).Phase);
        Assert.Equal(4.5f, Assert.IsType<ChatterParams>(running[1].Parameters).Interval);
        Assert.Equal(new[] { "Cursor", "Params" }, running[1].HiddenVariableMembers.ToArray());
    }

    /// <summary>⭐ CE-1315 (BV-45) — the dump shows a fixed list's Count items, clamped, not its buffer. 🔴 Red-proof: read the capacity
    /// instead of Count in <c>FixedListItems.TryFrom</c> ⇒ 4 items.</summary>
    [Fact]
    public void CE1315_TheDump_ShowsAFixedListsCountItems()
    {
        var list = new ShotList { Count = 3 };
        list.Items[0] = 11; list.Items[1] = 12; list.Items[2] = 13; list.Items[3] = 99;
        Assert.True(Fdp.Presentation.Utils.ImGuiPropertyTree.FixedListItems.TryFrom(list, out var items));
        Assert.Equal(3, items.Count);
        Assert.Equal(4, items.Capacity);
        Assert.Equal(new object[] { 11, 12, 13 }, items.Cast<object>().ToArray());
        list.Count = 9;   // corrupt: clamped to the capacity (F2)
        Assert.True(Fdp.Presentation.Utils.ImGuiPropertyTree.FixedListItems.TryFrom(list, out var clamped));
        Assert.Equal(4, clamped.Count);
    }

    /// <summary>⭐ CE-1315 — the REAL draw of the Running behaviours section, photographed (HROT_RAIL_SHOTS).</summary>
    [SkippableFact]
    public void CE1315_TheRunningBehavioursSection_Draws()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-brain");
        Directory.CreateDirectory(shots);
        var (w, e, reg, bps) = RunningWorld();
        using var _ = w;
        BrainJournalOf.Write(w, e, BrainJournalKind.TaskStarted, a: Fight, origin: (byte)BehaviorOrigin.Superior);
        var view = new EntityBrainView(() => w, h => h == Fight ? "FightFromCover" : null, null, () => reg, () => bps);
        var ctx = DetailsContext.Empty("Scenario") with { Entities = new[] { e } };

        EntityBrainView.ExpandForRail = true;
        try
        {
            using var frame = UiFrameHarness.Begin(760, 900);
            frame.StepN(4, () =>
            {
                ImGui.SetNextWindowPos(new Vector2(8, 8));
                ImGui.SetNextWindowSize(new Vector2(740, 880));
                ImGui.Begin("Brain##ce1315");
                view.Draw(ctx, "details_ce1315");
                ImGui.End();
            });
            frame.Screenshot(Path.Combine(shots, "brain-running-behaviours.png"));
        }
        finally { EntityBrainView.ExpandForRail = false; }
        Assert.Equal(2, view.Running(w, e).Count);
    }

    // ── CE-1310 / CE-1311 / CE-1317 (Brain View §12b, §12c, B5): expanders, grouped settings, one map family per checkbox ───────────

    private static void Steering(EntityRepository w, Entity e)
    {
        if (!w.IsComponentTypeRegistered<CombatTemperament>()) w.RegisterComponent<CombatTemperament>();
        if (!w.IsComponentTypeRegistered<CombatProficiency>()) w.RegisterComponent<CombatProficiency>();
        w.AddComponent(e, new CombatTemperament
        {
            TypeRiskAcceptance = 0.5f, TypeObedience = 0.8f, TypeNerve = 0.5f, TypeResolve = 0.5f, TypeSet = 15,
            RiskAcceptance = 0.62f, Set = CombatTemperament.RiskAcceptanceSet,
        });
        w.AddComponent(e, new CombatProficiency { TypeDefault = 0.6f, Set = CombatProficiency.TypeDefaultSet });
    }

    /// <summary>⭐⭐ <c>CE-1311</c> (BV-41) — the settings come as the user's GROUPS (Temperament, Combat skill), never components; a slider
    /// write sets the AUTHORED bit (the reader honours it — a raw value alone would change nothing) and a reset clears it back to the
    /// type's value. 🔴 Red-proof: drop the <c>Set |=</c> in <c>CombatTemperamentSettings.Write</c> ⇒ the unit keeps its old bravery.</summary>
    [Fact]
    public void CE1311_Settings_AreGroups_AWriteIsAuthored_AndAResetGoesBackToTheType()
    {
        var (w, e, _, _) = RunningWorld();
        using var _w = w;
        Steering(w, e);

        var groups = BrainSettingRegistry.Default.For(w, e);
        Assert.Equal(new[] { "Temperament", "Combat skill" }, groups.Select(g => g.Group));
        var bravery = groups[0].Settings.Single(x => x.Setting.Key == "bravery");
        Assert.Equal((0.62f, 0.5f, true), (bravery.Setting.Value, bravery.Setting.TypeValue, bravery.Setting.IsAuthored));
        var skill = groups[1].Settings.Single().Setting;
        Assert.Equal((0.6f, false), (skill.Value, skill.IsAuthored));

        groups[0].Settings.Single(x => x.Setting.Key == "nerve").Provider.Write(w, e, "nerve", 0.9f);
        Assert.Equal(0.9f, TemperamentOf.Nerve(w, e));
        groups[1].Settings.Single().Provider.Write(w, e, "proficiency", ProficiencyOf.Elite);
        Assert.Equal(ProficiencyTier.Elite, ProficiencyOf.Tier(w, e));

        bravery.Provider.Reset(w, e, "bravery");
        Assert.Equal(0.5f, TemperamentOf.RiskAcceptance(w, e));                 // the type's value again
        Assert.False(BrainSettingRegistry.Default.For(w, e)[0].Settings.Single(x => x.Setting.Key == "bravery").Setting.IsAuthored);
    }

    /// <summary>⭐ <c>CE-1317</c> (BV-49, R-1038) — <i>Show on map</i> offers the brain-debug families, ⛔ never Path (the user's ruling),
    /// with the two commander families split out of Squad (BV-50).</summary>
    [Fact]
    public void CE1317_ShowOnMap_OffersOneCheckboxPerBrainFamily_NeverPath()
    {
        Assert.DoesNotContain(Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Path, EntityBrainView.MapFamilies);
        Assert.Contains(Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.CommandPicture, EntityBrainView.MapFamilies);
        Assert.Contains(Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.SquadContacts, EntityBrainView.MapFamilies);
        Assert.Equal(Fdp.Toolkit.Diagnostics.Gizmos.GizmoFamilies.All.Length - 1, EntityBrainView.MapFamilies.Length);
    }

    /// <summary>⭐ <c>CE-1310</c> (BV-34, BV-64) — the tab is EXPANDERS: photographed collapsed (as a fresh tab opens: nothing forced) and
    /// open (the rail opens each once). 📷 <c>HROT_RAIL_SHOTS</c>/<c>brain-expanders-*.png</c>.</summary>
    [SkippableFact]
    public void CE1310_TheTab_IsCollapsingSections_PhotographedClosedAndOpen()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-brain");
        Directory.CreateDirectory(shots);
        var (w, e, reg, bps) = RunningWorld();
        using var _ = w;
        Steering(w, e);
        BrainJournalOf.Write(w, e, BrainJournalKind.TaskStarted, a: Fight, origin: (byte)BehaviorOrigin.Superior);
        var view = new EntityBrainView(() => w, h => h == Fight ? "FightFromCover" : null, null, () => reg, () => bps);
        var ctx = DetailsContext.Empty("Scenario") with { Entities = new[] { e } };

        using var frame = UiFrameHarness.Begin(760, 1000);
        void Draw()
        {
            ImGui.SetNextWindowPos(new Vector2(8, 8));
            ImGui.SetNextWindowSize(new Vector2(740, 980));
            ImGui.Begin("Brain##ce1310");
            view.Draw(ctx, "details_ce1310");
            ImGui.End();
        }
        frame.StepN(3, Draw);
        frame.Screenshot(Path.Combine(shots, "brain-expanders-closed.png"));
        EntityBrainView.ExpandForRail = true;
        try
        {
            frame.StepN(4, Draw);
            frame.Screenshot(Path.Combine(shots, "brain-expanders-open.png"));
        }
        finally { EntityBrainView.ExpandForRail = false; }
        Assert.NotEmpty(BrainSettingRegistry.Default.For(w, e));
    }

    /// <summary>⭐⭐ <c>CE-1316</c> (BV-47, accepted by the user) — the tab applies to EVERY single entity a section applies to: a unit with
    /// no brain gets the tab for its blueprints, and the section's view is drawn there (one instance per tab). Without sections, only a
    /// unit with a brain (the hosts that hand none keep today's behaviour). 🔴 Red-proof: the embeds arm dropped from the predicate ⇒ red.</summary>
    [Fact]
    public void CE1316_ABrainlessEntity_GetsTheTab_ForItsSections_AndASectionIsDrawnThere()
    {
        var (w, brain, _, _) = RunningWorld();
        using var _w = w;
        var rock = w.CreateEntity();                                              // no BehaviorState
        int drawn = 0;
        var sections = new DetailsViewRegistry();
        sections.Add(new DetailsViewDescriptor("details.test.blueprints", "Blueprints", 1,
            DetailsViewPredicates.ExactlyOneEntity, () => new CountingView(() => drawn++)));
        DetailsContext One(Entity e) => DetailsContext.Empty("Scenario") with { Entities = new[] { e } };

        var bare = EntityBrainViewDescriptor.For(() => w, _ => null);
        Assert.False(bare.AppliesTo(One(rock)));
        Assert.True(bare.AppliesTo(One(brain)));
        var withSections = EntityBrainViewDescriptor.For(() => w, _ => null, embeds: () => sections);
        Assert.True(withSections.AppliesTo(One(rock)));

        var view = (EntityBrainView)withSections.Create();
        Assert.Same(view.EmbedInstance("details.test.blueprints"), view.EmbedInstance("details.test.blueprints"));
        Assert.Equal(new[] { "details.test.blueprints" }, view.Embeds.Select(d => d.Id));
    }

    private sealed class CountingView : IDetailsViewInstance
    {
        private readonly Action _onDraw;
        public CountingView(Action onDraw) => _onDraw = onDraw;
        public void Draw(DetailsContext context, string idScope) => _onDraw();
        public void Dispose() { }
    }

    // ── CE-1312 / CE-1318 (Brain View §12d, B7): the commander's sections, a pinned tab ──────────────────────────────────────

    /// <summary>⭐⭐ <c>CE-1312</c> (BV-56…BV-58) — a commander's tab reads his picture, his units' contacts and the report / order
    /// HISTORIES (CE-1319's recorded components), newest first, an order WITH its copied why; a soldier has none of it.
    /// 🔴 Red-proof: drop the Incoming arm in <c>BrainCommandView.Read</c> ⇒ the received order is missing.</summary>
    [Fact]
    public void CE1312_ACommandersTab_ReadsHisPicture_Contacts_AndBothHistories()
    {
        var (w, soldier, _, _) = RunningWorld();
        using var _w = w;
        w.RegisterComponent<Fdp.Toolkit.Squad.Command.CommandPicture>();
        w.RegisterComponent<Fdp.Toolkit.Squad.Command.CommandReportHistory>();
        w.RegisterComponent<Fdp.Toolkit.Squad.Command.CommandOrderHistory>();
        w.RegisterComponent<Fdp.Toolkit.Squad.SquadCognitiveState>();
        w.RegisterComponent<Fdp.Core.CommandHierarchy.UnitRoster>();
        var hq = w.CreateEntity();
        w.AddComponent(hq, new BehaviorState());
        var pic = default(Fdp.Toolkit.Squad.Command.CommandPicture);
        pic.Elements.Count = 1;
        pic.Elements.Items[0] = new Fdp.Toolkit.Squad.Command.ReportedElement { At = new Vector3(455, -90, 0), Count = 3, FromNetId = 1001 };
        w.AddComponent(hq, pic);
        var st = default(Fdp.Toolkit.Squad.SquadCognitiveState);
        st.Contacts.Count = 1;
        st.Contacts.Contacts[0] = new Fdp.Toolkit.Squad.SquadContact { PositionX = 470, PositionY = -102 };
        w.AddComponent(hq, st);
        w.AddComponent(hq, default(Fdp.Core.CommandHierarchy.UnitRoster));
        w.AddComponent(hq, default(Fdp.Toolkit.Squad.Command.CommandReportHistory));
        w.AddComponent(hq, default(Fdp.Toolkit.Squad.Command.CommandOrderHistory));
        var sqd = w.CreateEntity();
        Fdp.Toolkit.Squad.Command.CommandHistoryOf.Report(w, sqd, hq,
            new Fdp.Toolkit.Squad.Command.CommandReport { Kind = Fdp.Toolkit.Squad.Command.ReportKind.Progress, Subtype = (byte)Fdp.Toolkit.Squad.Command.ProgressKind.Set },
            10f, Fdp.Toolkit.Squad.Command.ReportFate.Folded);
        ref var oh = ref w.GetComponentRW<Fdp.Toolkit.Squad.Command.CommandOrderHistory>(hq);
        Fdp.Toolkit.Squad.Command.CommandHistoryOf.Push(ref oh, new Fdp.Toolkit.Squad.Command.CommandOrderEntry
        {
            At = 5f, Traffic = Fdp.Toolkit.Squad.Command.CommandTraffic.Incoming, CounterpartNetId = 900, Behaviour = Fight,
        });
        Fdp.Toolkit.Squad.Command.CommandHistoryOf.Push(ref oh, new Fdp.Toolkit.Squad.Command.CommandOrderEntry
        {
            At = 12f, Traffic = Fdp.Toolkit.Squad.Command.CommandTraffic.Outgoing, CounterpartNetId = 1001, Behaviour = Fight,
            CauseKind = BrainJournalKind.PlayPhase, Cause = (byte)Fdp.Toolkit.Squad.Primitives.PhaseEventKind.DefiladeReached,
        });

        var cmd = BrainCommandView.Read(w, hq, h => h == Fight ? "FightFromCover" : null, 20f)!;
        Assert.Contains("x3", Assert.Single(cmd.Picture));
        Assert.Contains("470", Assert.Single(cmd.Contacts));
        Assert.Contains("PROGRESS  Set", Assert.Single(cmd.Reports).Text);
        Assert.Contains("from #900  FightFromCover", Assert.Single(cmd.OrdersReceived).Text);
        Assert.Contains("because the play's phase changed (DefiladeReached)", Assert.Single(cmd.OrdersSent).Text);
        Assert.Null(BrainCommandView.Read(w, soldier, _ => null, 0f));                  // a soldier: no commander sections
    }

    /// <summary>📷 <c>CE-1312</c> — a commander's tab photographed with its sections open (<c>brain-commander.png</c>).</summary>
    [SkippableFact]
    public void CE1312_ACommandersTab_Draws()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-brain");
        Directory.CreateDirectory(shots);
        var (w, _, reg, bps) = RunningWorld();
        using var _w = w;
        var hq = Commander(w);
        var view = new EntityBrainView(() => w, h => h == Fight ? "FightFromCover" : null, null, () => reg, () => bps);
        var ctx = DetailsContext.Empty("Scenario") with { Entities = new[] { hq } };
        EntityBrainView.ExpandForRail = true;
        try
        {
            using var frame = UiFrameHarness.Begin(860, 900);
            frame.StepN(4, () =>
            {
                ImGui.SetNextWindowPos(new Vector2(8, 8));
                ImGui.SetNextWindowSize(new Vector2(840, 880));
                ImGui.Begin("Brain##ce1312");
                view.Draw(ctx, "details_ce1312");
                ImGui.End();
            });
            frame.Screenshot(Path.Combine(shots, "brain-commander.png"));
        }
        finally { EntityBrainView.ExpandForRail = false; }
    }

    /// <summary>A platoon HQ with a picture, his units' contacts, a report each way and an order each way.</summary>
    private static Entity Commander(EntityRepository w)
    {
        if (!w.IsComponentTypeRegistered<Fdp.Toolkit.Squad.Command.CommandPicture>()) w.RegisterComponent<Fdp.Toolkit.Squad.Command.CommandPicture>();
        if (!w.IsComponentTypeRegistered<Fdp.Toolkit.Squad.Command.CommandReportHistory>()) w.RegisterComponent<Fdp.Toolkit.Squad.Command.CommandReportHistory>();
        if (!w.IsComponentTypeRegistered<Fdp.Toolkit.Squad.Command.CommandOrderHistory>()) w.RegisterComponent<Fdp.Toolkit.Squad.Command.CommandOrderHistory>();
        if (!w.IsComponentTypeRegistered<Fdp.Toolkit.Squad.SquadCognitiveState>()) w.RegisterComponent<Fdp.Toolkit.Squad.SquadCognitiveState>();
        if (!w.IsComponentTypeRegistered<Fdp.Core.CommandHierarchy.UnitRoster>()) w.RegisterComponent<Fdp.Core.CommandHierarchy.UnitRoster>();
        var hq = w.CreateEntity();
        w.AddComponent(hq, new BehaviorState { ActiveBehaviorHash = Fight });
        w.AddComponent(hq, new BrainJournal());
        var pic = default(Fdp.Toolkit.Squad.Command.CommandPicture);
        pic.Elements.Count = 2;
        pic.Elements.Items[0] = new Fdp.Toolkit.Squad.Command.ReportedElement { At = new Vector3(455, -90, 0), Count = 3, FromNetId = 1001, Size = Fdp.Toolkit.Squad.Command.ElementSize.SquadSize };
        pic.Elements.Items[1] = new Fdp.Toolkit.Squad.Command.ReportedElement { At = new Vector3(470, -102, 0), Count = 1, FromNetId = 1002 };
        pic.Subordinates.Count = 2;
        pic.Subordinates.Items[0] = new Fdp.Toolkit.Squad.Command.SubordinateRow { UnitNetId = 1001, Progress = Fdp.Toolkit.Squad.Command.ProgressKind.Set };
        pic.Subordinates.Items[1] = new Fdp.Toolkit.Squad.Command.SubordinateRow { UnitNetId = 1002 };
        w.AddComponent(hq, pic);
        var st = default(Fdp.Toolkit.Squad.SquadCognitiveState);
        st.Contacts.Count = 1;
        st.Contacts.Contacts[0] = new Fdp.Toolkit.Squad.SquadContact { PositionX = 470, PositionY = -102 };
        w.AddComponent(hq, st);
        w.AddComponent(hq, default(Fdp.Core.CommandHierarchy.UnitRoster));
        w.AddComponent(hq, default(Fdp.Toolkit.Squad.Command.CommandReportHistory));
        w.AddComponent(hq, default(Fdp.Toolkit.Squad.Command.CommandOrderHistory));
        var sqd = w.CreateEntity();
        Fdp.Toolkit.Squad.Command.CommandHistoryOf.Report(w, sqd, hq,
            new Fdp.Toolkit.Squad.Command.CommandReport { Kind = Fdp.Toolkit.Squad.Command.ReportKind.Spot, Count = 3, At = new Vector3(455, -90, 0) },
            4f, Fdp.Toolkit.Squad.Command.ReportFate.Folded);
        Fdp.Toolkit.Squad.Command.CommandHistoryOf.Report(w, sqd, hq,
            new Fdp.Toolkit.Squad.Command.CommandReport { Kind = Fdp.Toolkit.Squad.Command.ReportKind.Progress, Subtype = (byte)Fdp.Toolkit.Squad.Command.ProgressKind.Set },
            10f, Fdp.Toolkit.Squad.Command.ReportFate.Folded);
        ref var oh = ref w.GetComponentRW<Fdp.Toolkit.Squad.Command.CommandOrderHistory>(hq);
        Fdp.Toolkit.Squad.Command.CommandHistoryOf.Push(ref oh, new Fdp.Toolkit.Squad.Command.CommandOrderEntry
            { At = 2f, Traffic = Fdp.Toolkit.Squad.Command.CommandTraffic.Incoming, CounterpartNetId = 900, Behaviour = Fight });
        Fdp.Toolkit.Squad.Command.CommandHistoryOf.Push(ref oh, new Fdp.Toolkit.Squad.Command.CommandOrderEntry
        {
            At = 12f, Traffic = Fdp.Toolkit.Squad.Command.CommandTraffic.Outgoing, CounterpartNetId = 1001, Behaviour = Fight,
            CauseKind = BrainJournalKind.PlayPhase, Cause = (byte)Fdp.Toolkit.Squad.Primitives.PhaseEventKind.DefiladeReached,
        });
        return hq;
    }

    /// <summary>⭐ <c>CE-1318</c> (BV-55) — the tab knows whether it is the docked one (follows the selection) or a PIN (opens a pinned
    /// graph): a pin's window id is <c>DetailsWindow.PinIdFor</c>'s.</summary>
    [Fact]
    public void CE1318_ThePinnedTab_IsToldApartFromTheDockedOne()
    {
        var ctx = DetailsContext.Empty("Scenario") with { Entities = new[] { new Entity(3, 1) } };
        Assert.True(EntityBrainView.IsPinnedScope(DetailsWindow.PinIdFor(ctx, EntityBrainViewDescriptor.ViewId)));
        Assert.False(EntityBrainView.IsPinnedScope("scenario_details"));
    }

    // ── CE-1320 (Brain View §12g BV-65): ONE live write for params and working state, no restart ──────────────────────────────

    /// <summary>A stager that applies at once (production stages through the breakpoint manager and the drain lands it next tick).</summary>
    private static Hrot.Editor.AiShared.Variables.FieldStager ApplyNow(EntityRepository w) => (e, t, off, bytes) =>
    {
        var spec = Fdp.Toolkit.Blueprints.Partitioning.BlueprintTierTable.Ascending.First(x => x.ComponentType == t);
        bytes.CopyTo(new Span<byte>(spec.Memory(w, e) + off, bytes.Length));
    };

    private static System.Text.Json.Nodes.JsonNode J(string json) => System.Text.Json.Nodes.JsonNode.Parse(json)!;

    /// <summary>⭐ CE-1320 — a field write of the task's params, its variables (into an inline array) and an attached blueprint's
    /// params and variables lands IN PLACE: the reader sees each new value and the behaviour is not restarted. 🔴 Red-proof: drop the
    /// "In"/"St" root offset in <c>ResolveField</c> ⇒ the write lands on the wrong half.</summary>
    [Fact]
    public void CE1320_TheOneWrite_LandsInPlace_ForTaskAndBlueprint_WithoutARestart()
    {
        var (w, e, reg, bps) = RunningWorld();
        using var _ = w;
        uint instance = w.GetComponentRO<BehaviorState>(e).InstanceId;
        var stage = ApplyNow(w);
        var P = Hrot.Editor.AiShared.Variables.OccurrenceSlotWriter.WriteField(w, e, reg, bps, stage, Fdp.Toolkit.Behavior.SlotOwnerKind.Task,
            null, null, Fdp.Toolkit.Behavior.OccurrenceSlotCatalog.SlotFieldRole.FieldParams, "CoverRadius", J("40"));
        Assert.Null(P.Error);
        Assert.Null(Hrot.Editor.AiShared.Variables.OccurrenceSlotWriter.WriteField(w, e, reg, bps, stage, Fdp.Toolkit.Behavior.SlotOwnerKind.Task,
            null, null, Fdp.Toolkit.Behavior.OccurrenceSlotCatalog.SlotFieldRole.FieldVariables, "Shots.Items[1]", J("77")).Error);
        Assert.Null(Hrot.Editor.AiShared.Variables.OccurrenceSlotWriter.WriteField(w, e, reg, bps, stage, Fdp.Toolkit.Behavior.SlotOwnerKind.InstanceBlueprint,
            "RadioChatter", null, Fdp.Toolkit.Behavior.OccurrenceSlotCatalog.SlotFieldRole.FieldParams, "Interval", J("2.5")).Error);
        Assert.Null(Hrot.Editor.AiShared.Variables.OccurrenceSlotWriter.WriteField(w, e, reg, bps, stage, Fdp.Toolkit.Behavior.SlotOwnerKind.InstanceBlueprint,
            "RadioChatter", null, Fdp.Toolkit.Behavior.OccurrenceSlotCatalog.SlotFieldRole.FieldVariables, "Messages", J("9")).Error);

        var running = BrainRunningBehaviours.From(Fdp.Toolkit.Behavior.OccurrenceSlotCatalog.Read(w, e, reg, bps));
        var fp = Assert.IsType<FightParams>(running[0].Parameters);
        var fv = Assert.IsType<FightVars>(running[0].Variables);
        Assert.Equal(40f, fp.CoverRadius);
        Assert.Equal(new Entity(12, 3), fp.Target);          // the neighbour untouched
        Assert.Equal(77, fv.Shots.Items[1]);
        Assert.Equal(3, fv.Shots.Count);
        Assert.Equal(2.5f, Assert.IsType<ChatterParams>(running[1].Parameters).Interval);
        Assert.Equal(9, Assert.IsType<ChatterState>(running[1].Variables).Messages);
        Assert.Equal(instance, w.GetComponentRO<BehaviorState>(e).InstanceId);   // ⛔ no restart
    }

    /// <summary>⭐ CE-1320 — what does not resolve is refused, with the reason: an unknown field, a blueprint's cursor as a variable, a
    /// params half for a node's working state. 🔴 Red-proof: drop the Cursor / Params guard ⇒ the cursor write is accepted.</summary>
    [Fact]
    public void CE1320_TheWrite_RefusesWhatDoesNotResolve_AndSaysWhy()
    {
        var (w, e, reg, bps) = RunningWorld();
        using var _ = w;
        var stage = ApplyNow(w);
        var bad = Hrot.Editor.AiShared.Variables.OccurrenceSlotWriter.WriteField(w, e, reg, bps, stage, Fdp.Toolkit.Behavior.SlotOwnerKind.Task,
            null, null, Fdp.Toolkit.Behavior.OccurrenceSlotCatalog.SlotFieldRole.FieldParams, "Nope", J("1"));
        Assert.Contains("no field 'Nope'", bad.Error);
        var cursor = Hrot.Editor.AiShared.Variables.OccurrenceSlotWriter.WriteField(w, e, reg, bps, stage, Fdp.Toolkit.Behavior.SlotOwnerKind.InstanceBlueprint,
            null, null, Fdp.Toolkit.Behavior.OccurrenceSlotCatalog.SlotFieldRole.FieldVariables, "Cursor", J("{}"));
        Assert.Contains("not variables", cursor.Error);
        var sop = Hrot.Editor.AiShared.Variables.OccurrenceSlotWriter.WriteField(w, e, reg, bps, stage, Fdp.Toolkit.Behavior.SlotOwnerKind.Sop,
            null, null, Fdp.Toolkit.Behavior.OccurrenceSlotCatalog.SlotFieldRole.FieldParams, "X", J("1"));
        Assert.Contains("No Sop slot", sop.Error);
    }

    /// <summary>⭐ CE-1320 — a task-params write keeps the start record true (the save and the "same order again?" checks read its TEXT):
    /// a field patch by name, and a whole JSON apply through the behaviour's OWN parser. 🔴 Red-proof: skip <c>TryPatchStartRecord</c> ⇒
    /// the record keeps 25.</summary>
    [Fact]
    public void CE1320_AParamsWrite_KeepsTheStartRecordTrue_AndAJsonApplyGoesThroughTheParser()
    {
        var (w, e, reg, bps) = RunningWorld();
        using var _ = w;
        w.RegisterComponent<BehaviorStartRecord>();
        w.SetManagedComponent(e, new BehaviorStartRecord { BehaviorName = "FightFromCover", JsonParams = "{\"coverRadius\":25}", InstanceId = 0 });
        var stage = ApplyNow(w);

        var r = Hrot.Editor.AiShared.Variables.OccurrenceSlotWriter.WriteField(w, e, reg, bps, stage, Fdp.Toolkit.Behavior.SlotOwnerKind.Task,
            null, null, Fdp.Toolkit.Behavior.OccurrenceSlotCatalog.SlotFieldRole.FieldParams, "CoverRadius", J("40"));
        Assert.True(r.StartRecordUpdated);
        Assert.Equal("{\"coverRadius\":40}", ((Fdp.ModuleHost.Abstractions.ISimulationView)w).GetManagedComponentRO<BehaviorStartRecord>(e)!.JsonParams);

        reg.TryGetDefinition(Fight, out var def);
        def!.ParseParams = (json, mem, cap, world, self) =>
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(json)!;
            ((FightParams*)mem)->CoverRadius = (float)parsed["CoverRadius"]!;
        };
        var applied = Hrot.Editor.AiShared.Variables.OccurrenceSlotWriter.WriteParamsJson(w, e, reg, bps, stage,
            Fdp.Toolkit.Behavior.SlotOwnerKind.Task, null, "{\"CoverRadius\":33}");
        Assert.Null(applied.Error);
        var running = BrainRunningBehaviours.From(Fdp.Toolkit.Behavior.OccurrenceSlotCatalog.Read(w, e, reg, bps));
        Assert.Equal(33f, Assert.IsType<FightParams>(running[0].Parameters).CoverRadius);
        Assert.Equal("{\"CoverRadius\":33}", ((Fdp.ModuleHost.Abstractions.ISimulationView)w).GetManagedComponentRO<BehaviorStartRecord>(e)!.JsonParams);
    }
}
