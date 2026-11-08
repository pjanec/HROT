using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using Fdp.Core;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Hrot.Common.Actions;
using Hrot.Common.Editing;
using Hrot.Common.Infrastructure;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Hrot.Map.Common.Components;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ B-11 A7 (📄 docs/UX/DESIGN_Action_Catalogue.md §4 A7, Editing UX §3.2 / D2) — a Details tab carries <c>Requires</c> / <c>Modes</c> / <c>Level</c> (the
/// <c>ActionDecl</c> model: <c>ActionAvailability</c>, <c>EditingMode</c> from <c>StationMode</c>), registered ONCE in <c>EntityDetailsBundle</c>.
/// <para>The GOLDEN (<c>Goldens/details-tabs.golden.txt</c>): the tab offer — id and Offered / Greyed(reason) — per entity kind × host × mode, on the PRODUCTION
/// bundle. Hosts differ only in the services they pass (what they CAN do); the golden shows that difference is a greyed Orders tab, never a different
/// registration. Capture: <c>DETAILS_TABS_GOLDEN_CAPTURE=1 dotnet test … --filter TheDetailsTabsDeclareWhatTheyNeedTests</c>.</para>
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class TheDetailsTabsDeclareWhatTheyNeedTests
{
    private const ulong GroundUnitDis = (3UL << 56) | (1UL << 48) | (225UL << 32) | (1UL << 24);   // a unit with orders offered
    private const ulong MunitionDis = 2UL << 56;                                                    // no class of a munition has an order behaviour

    private static string GoldenPath([CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "Goldens", "details-tabs.golden.txt");

    // ── the world: one entity per kind ─────────────────────────────────────────────────────────────────────────────

    private static EntityRepository World()
    {
        var w = new EntityRepository();
        w.RegisterComponent<NetworkIdentity>();
        w.RegisterComponent<SimTransform>();
        w.RegisterComponent<EntityInfo>();
        w.RegisterComponent<Health>();
        w.RegisterComponent<BehaviorState>();
        w.RegisterComponent<PassengerBuffer>();
        return w;
    }

    private static Entity Unit(EntityRepository w, int net, ulong dis, bool info = true)
    {
        var e = w.CreateEntity();
        w.AddComponent(e, new NetworkIdentity(net));
        w.SetDisType(e, new DISEntityType { Value = dis });
        w.AddComponent(e, new SimTransform { Position = new Vector3(1, 2, 3), Rotation = Quaternion.Identity });
        if (info) w.AddComponent(e, new EntityInfo { Name = new FixedString64("u" + net), ForceId = ForceId.Friend });
        return e;
    }

    private sealed class Kinds
    {
        public Entity Bare, Munition, Unit, Brain, Carrier, Other;
    }

    private static Kinds MakeKinds(EntityRepository w)
    {
        var k = new Kinds { Bare = w.CreateEntity() };
        k.Munition = Unit(w, 2, MunitionDis, info: false);
        k.Unit = Unit(w, 3, GroundUnitDis);
        k.Brain = Unit(w, 4, GroundUnitDis); w.AddComponent(k.Brain, new BehaviorState { ActiveBehaviorHash = 5 });
        k.Carrier = Unit(w, 5, GroundUnitDis); w.AddComponent(k.Carrier, default(PassengerBuffer));
        k.Other = Unit(w, 6, GroundUnitDis);
        return k;
    }

    private static IEnumerable<(string Name, Entity[] Selection)> Selections(Kinds k)
    {
        yield return ("nothing selected", Array.Empty<Entity>());
        yield return ("a bare entity", new[] { k.Bare });
        yield return ("a munition (identity, no orders)", new[] { k.Munition });
        yield return ("a unit", new[] { k.Unit });
        yield return ("a unit with a brain", new[] { k.Brain });
        yield return ("a carrier (passengers)", new[] { k.Carrier });
        yield return ("two units", new[] { k.Unit, k.Other });
        yield return ("a unit and a carrier", new[] { k.Unit, k.Carrier });
    }

    // ── the hosts: the same bundle, the services each real host passes ─────────────────────────────────────────────

    private sealed class FakeMissions : Hrot.UI.Common.Facades.IMissionEditorService
    {
        public IReadOnlyList<string> GetAvailableBehaviors(int entityId) => Array.Empty<string>();
        public (Hrot.Core.Mission.MissionPlan? Plan, long Version) GetMissionSnapshot(int entityId) => (null, 1);
        public System.Threading.Tasks.Task<Hrot.UI.Common.Models.MissionCommitResult> CommitMissionAsync(int entityId, Hrot.Core.Mission.MissionPlan plan, long baseVersion)
            => System.Threading.Tasks.Task.FromResult(new Hrot.UI.Common.Models.MissionCommitResult(true, baseVersion + 1));
        public System.Threading.Tasks.Task<Hrot.UI.Common.Models.MissionCommitResult> SendControlCommandAsync(int entityId, Hrot.Core.Mission.eMissionCommandType type, Guid taskId)
            => System.Threading.Tasks.Task.FromResult(new Hrot.UI.Common.Models.MissionCommitResult(true, 1));
    }

    private sealed class NoRulesHost : Hrot.UI.Common.Rules.IRuleDetailsHost
    {
        public bool Offers(Entity e) => false;
        public void DrawDetails(Entity e) { }
        public void AttachToDetailsPanel() { }
    }

    /// <summary>The services a real host passes (EditorSubsystem · CgfSubsystem · ReplayBrowserSubsystem · SimHostSubsystem / PlayerSubsystem · StrideNodeShell).</summary>
    private static readonly (string Name, bool Missions, bool Rules, bool EventFocus, bool Pick)[] Hosts =
    {
        ("editor / cgf (every service)", true, true, true, true),
        ("replay browser (rules, event focus)", false, true, true, false),
        ("simhost / player (pick only)", false, false, false, true),
        ("stride (nothing)", false, false, false, false),
    };

    private static EntityDetailsBundle Bundle(EntityRepository w, (string Name, bool Missions, bool Rules, bool EventFocus, bool Pick) host)
    {
        var inspector = new Fdp.Presentation.Panels.EntityInspectorPanel();
        var session = new Fdp.Presentation.Adapters.RepositoryAdapter(w);
        var windows = new WindowManager(new IconAtlas(nint.Zero, 1, 1, 16f));
        var services = new EntityDetailsHostServices(
            WindowId: "details_a7", Perspective: "Scenario", World: () => w,
            Inspector: () => inspector, Session: () => session, RunState: () => VariableRunState.Running, Host: () => null,
            EventFocus: host.EventFocus ? new TimelineEventFocus() : null,
            Rules: host.Rules ? () => new NoRulesHost() : null,
            Missions: host.Missions ? () => new FakeMissions() : null);
        var bundle = new EntityDetailsBundle(services);
        Fdp.Toolkit.Runner.UiBundleHost.Compose(new Fdp.Toolkit.Runner.IUiBundle[] { bundle }, new Fdp.Toolkit.Runner.UiBundleContext(windows));
        return bundle;
    }

    private static void Station(EntityRepository w, EditingMode mode, Seat seat) => StationMode.Install(w, () => mode, () => seat);

    private static string Offer(EntityDetailsBundle bundle, EntityRepository w, Entity[] selection)
    {
        var ctx = bundle.Workspace.BuildContext() with { Entities = selection };
        var offer = bundle.Workspace.DetailsViews.Evaluate(ctx);
        var sb = new StringBuilder();
        foreach (var d in offer.Offered) sb.Append(sb.Length == 0 ? "" : " · ").Append(d.Id.Replace("details.", ""));
        foreach (var g in offer.Greyed) sb.Append(sb.Length == 0 ? "" : " · ").Append(g.View.Id.Replace("details.", "")).Append(" [greyed: ").Append(g.Reason).Append(']');
        return sb.Length == 0 ? "(none)" : sb.ToString();
    }

    private static string Render()
    {
        var sb = new StringBuilder();
        sb.Append("# Details tab offer — id (Rank order), [greyed: reason] last. Hosts × modes at the Author seat; the seat rows at the end.\n");
        foreach (var host in Hosts)
        {
            using var w = World();
            var k = MakeKinds(w);
            var bundle = Bundle(w, host);
            sb.Append("\n== host: ").Append(host.Name).Append(" ==\n");
            foreach (var mode in new[] { EditingMode.Edit, EditingMode.Preview, EditingMode.Live, EditingMode.Review })
            {
                Station(w, mode, Seat.Author);
                foreach (var (name, sel) in Selections(k))
                    sb.Append(mode).Append(" | ").Append(name).Append(" | ").Append(Offer(bundle, w, sel)).Append('\n');
            }
        }
        sb.Append("\n== seats (editor / cgf host, Live, a unit) ==\n");
        {
            using var w = World();
            var k = MakeKinds(w);
            var bundle = Bundle(w, Hosts[0]);
            foreach (var seat in new[] { Seat.Author, Seat.Instructor, Seat.SideController, Seat.Observer, Seat.Player })
            {
                Station(w, EditingMode.Live, seat);
                sb.Append(seat).Append(" | a unit | ").Append(Offer(bundle, w, new[] { k.Unit })).Append('\n');
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// ⭐⭐ THE GOLDEN. 🔴 Red-proof (compiles): drop <c>Requires</c> on the Crew form's tab (the <c>PureRequires</c> arm of <c>FormDetailsViewDescriptor</c>) ⇒ the
    /// carrier rows lose their Crew tab and a unit gains one ⇒ red; <c>Level</c> of Orders changed to <c>None</c> ⇒ the seat rows ⇒ red.
    /// </summary>
    [Fact]
    public void A7_TheTabOfferPerEntityKindHostAndMode_IsTheGolden()
    {
        var actual = Render();
        var path = GoldenPath();
        if (Environment.GetEnvironmentVariable("DETAILS_TABS_GOLDEN_CAPTURE") == "1") { File.WriteAllText(path, actual); return; }
        Assert.True(File.Exists(path), $"no golden at {path} — capture it with DETAILS_TABS_GOLDEN_CAPTURE=1");
        var expected = File.ReadAllText(path).Replace("\r\n", "\n");
        if (expected == actual) return;
        var a = expected.Split('\n'); var b = actual.Split('\n');
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : "<end>"; var y = i < b.Length ? b[i] : "<end>";
            if (x != y) Assert.Fail($"details tab golden differs at line {i + 1}:\n  expected: {x}\n  actual:   {y}");
        }
    }

    /// <summary>
    /// ⭐⭐⭐ "Identical across hosts" BY CONSTRUCTION: every host registers the SAME tab ids in the SAME order (the shared bundle is the only registrar), and for
    /// every kind × mode the hosts agree except where a host lacks a service — then the tab is greyed "Not available on this station", never absent.
    /// 🔴 Red-proof (compiles): the bundle guarding the Orders registration with <c>if (host.Missions != null)</c> again ⇒ the id lists differ ⇒ red.
    /// </summary>
    [Fact]
    public void A7_EveryHostRegistersTheSameTabs_AndTheOffersDifferOnlyByAGreyedCapability()
    {
        var ids = new List<List<string>>();
        var offers = new List<string[]>();
        foreach (var host in Hosts)
        {
            using var w = World();
            var k = MakeKinds(w);
            var bundle = Bundle(w, host);
            Station(w, EditingMode.Live, Seat.Author);
            ids.Add(new List<string>(System.Linq.Enumerable.Select(bundle.Workspace.DetailsViews.All, d => d.Id)));
            var row = new List<string>();
            foreach (var (_, sel) in Selections(k))
            {
                var ctx = bundle.Workspace.BuildContext() with { Entities = sel };
                var offer = bundle.Workspace.DetailsViews.Evaluate(ctx);
                foreach (var d in offer.Offered) row.Add(d.Id);
                foreach (var g in offer.Greyed) { Assert.Equal(DetailsViewDescriptor.NotBoundReason, g.Reason); Assert.Equal(OrdersTabDescriptor.Id, g.View.Id); }
            }
            offers.Add(row.ToArray());
        }
        for (int h = 1; h < ids.Count; h++) Assert.Equal(ids[0], ids[h]);   // one registration list, whatever the host passes
        // the hosts without a mission facade offer exactly the editor's tabs minus Orders (which they show greyed)
        for (int h = 1; h < offers.Count; h++)
        {
            var expect = new List<string>(offers[0]); expect.RemoveAll(i => i == OrdersTabDescriptor.Id);
            var got = new List<string>(offers[h]);
            got.RemoveAll(i => i == "details.rule" || i == EventDetailsViewDescriptor.ViewId);   // rules / event never apply with nothing to ask
            expect.RemoveAll(i => i == "details.rule" || i == EventDetailsViewDescriptor.ViewId);
            Assert.Equal(expect, got);
        }
    }

    // ── the mechanism, on small registries ─────────────────────────────────────────────────────────────────────────

    private sealed class Dummy : IDetailsViewInstance { public void Draw(DetailsContext c, string s) { } public void Dispose() { } }

    private static DetailsViewDescriptor Tab(string id, Func<DetailsContext, bool>? applies = null, Type[]? requires = null, ActionAvailability? availability = null, Func<bool>? bound = null, int rank = 10)
        => new(id, id, rank, applies ?? (_ => true), () => new Dummy())
        { Requires = requires ?? Array.Empty<Type>(), Availability = availability ?? ActionAvailability.Always, Bound = bound };

    /// <summary>⭐ <c>Requires</c> is AND over the selection (AQ26): every selected entity must carry every component; an empty selection carries nothing; a registry with no probe cannot ask.</summary>
    [Fact]
    public void A7_RequiresIsAndOverTheSelection()
    {
        using var w = World();
        var both = w.CreateEntity(); w.AddComponent(both, new Health { Current = 1, Max = 1 }); w.AddComponent(both, new SimTransform());
        var onlyHealth = w.CreateEntity(); w.AddComponent(onlyHealth, new Health { Current = 1, Max = 1 });
        var reg = new DetailsViewRegistry { ComponentProbe = DetailsViewRegistry.ProbeFor(() => w) };
        reg.Add(Tab("t.health", requires: new[] { typeof(Health) }));
        reg.Add(Tab("t.both", requires: new[] { typeof(Health), typeof(SimTransform) }));
        IReadOnlyList<string> Ids(params Entity[] e) { var l = new List<string>(); foreach (var d in reg.OfferSet(DetailsContext.Empty("x") with { Entities = e })) l.Add(d.Id); return l; }

        Assert.Equal(new[] { "t.health", "t.both" }, Ids(both));
        Assert.Equal(new[] { "t.health" }, Ids(onlyHealth));
        Assert.Equal(new[] { "t.health" }, Ids(both, onlyHealth));          // AND over the selection: `both` alone has Transform, the pair does not
        Assert.Empty(Ids());                                                // nothing selected carries nothing
        var blind = new DetailsViewRegistry(); blind.Add(Tab("t.health", requires: new[] { typeof(Health) }));
        Assert.Empty(blind.OfferSet(DetailsContext.Empty("x") with { Entities = new[] { both } }));   // no probe ⇒ cannot claim yes
    }

    /// <summary>⭐ A managed component is asked the same way (the Macro tab requires <c>MacroInstance</c>, a managed class).</summary>
    [Fact]
    public void A7_RequiresAManagedComponent_IsAskedTheSameWay()
    {
        using var w = World();
        w.RegisterManagedComponent<Fdp.Toolkit.Scenario.MacroInstance>();
        var e = w.CreateEntity(); w.SetManagedComponent(e, new Fdp.Toolkit.Scenario.MacroInstance { Macro = "m" });
        var plain = w.CreateEntity();
        var probe = DetailsViewRegistry.ProbeFor(() => w);
        Assert.True(probe(e, typeof(Fdp.Toolkit.Scenario.MacroInstance)));
        Assert.False(probe(plain, typeof(Fdp.Toolkit.Scenario.MacroInstance)));
        Assert.False(probe(e, typeof(Fdp.Toolkit.Scenario.MacroInstance)) && probe(plain, typeof(Fdp.Toolkit.Scenario.MacroInstance)));
    }

    /// <summary>⭐ <c>Modes</c> per <c>EditingMode</c>: Offered · Greyed with its reason · Hidden — the SAME verdict an <c>ActionDecl</c> gets (<c>ActionAvailability</c>).</summary>
    [Fact]
    public void A7_ModesDecideOfferedGreyedHidden_PerEditingMode()
    {
        var a = new ActionAvailability(ActionModes.InEdit | ActionModes.InReview, Greyed: ActionModes.InLive, GreyReason: "not while the exercise runs");
        Assert.Equal(new AvailabilityVerdict(ActionOffer.Offered, null), a.Evaluate(EditingMode.Edit, EditingLevel.Full));
        Assert.Equal(new AvailabilityVerdict(ActionOffer.Greyed, "not while the exercise runs"), a.Evaluate(EditingMode.Live, EditingLevel.Full));
        Assert.Equal(new AvailabilityVerdict(ActionOffer.Hidden, null), a.Evaluate(EditingMode.Preview, EditingLevel.Full));

        var reg = new DetailsViewRegistry();
        reg.Add(Tab("t.a", availability: a));
        IReadOnlyList<DetailsViewRegistry.GreyedTab> Greyed(EditingMode m) => reg.Evaluate(DetailsContext.Empty("x") with { Station = m }).Greyed;
        Assert.Single(reg.OfferSet(DetailsContext.Empty("x") with { Station = EditingMode.Edit }));
        Assert.Empty(reg.OfferSet(DetailsContext.Empty("x") with { Station = EditingMode.Live }));
        Assert.Equal("not while the exercise runs", Assert.Single(Greyed(EditingMode.Live)).Reason);
        Assert.Empty(Greyed(EditingMode.Preview));   // hidden: in neither list
    }

    /// <summary>⭐ <c>Level</c>: a station below the tab's level sees it GREYED with the seat's reason (the action's rule — one function); at or above it, offered.</summary>
    [Fact]
    public void A7_ALevelBelowTheTabsLevel_GreysItWithTheSeatsReason()
    {
        var reg = new DetailsViewRegistry();
        reg.Add(Tab("t.orders", availability: new ActionAvailability(ActionModes.Always, Level: EditingLevel.Controller)));
        DetailsViewRegistry.Offer At(EditingLevel l) => reg.Evaluate(DetailsContext.Empty("x") with { Level = l });

        Assert.Single(At(EditingLevel.Full).Offered);
        Assert.Single(At(EditingLevel.Instructor).Offered);
        Assert.Single(At(EditingLevel.Controller).Offered);
        Assert.Equal("this station is an observer", Assert.Single(At(EditingLevel.Observer).Greyed).Reason);
        Assert.Equal(ActionContext.SeatText(EditingLevel.None), Assert.Single(At(EditingLevel.None).Greyed).Reason);
        Assert.Empty(At(EditingLevel.Observer).Offered);
        // the SAME rule an action uses: ActionDecl.Availability and ActionContext.SeatRefusal both go through LevelRefusal
        Assert.Equal(ActionAvailability.LevelRefusal(EditingLevel.Controller, EditingLevel.Observer), ActionContext.SeatText(EditingLevel.Observer));
        Assert.Equal(new ActionAvailability(ActionModes.Always, 0, EditingLevel.Instructor), new ActionDecl(1, "x", ActionGroup.Edit, 0, ActionTarget.Unit).Availability with { Offered = ActionModes.Always });
    }

    /// <summary>⭐ A tab the host does not bind (<c>Bound</c> false) that otherwise applies is GREYED "Not available on this station"; one that does not apply is simply absent.</summary>
    [Fact]
    public void A7_AnUnboundTab_IsGreyedNotMissing_OnlyWhereItApplies()
    {
        bool bound = false;
        var reg = new DetailsViewRegistry();
        reg.Add(Tab("t.applies", bound: () => bound));
        reg.Add(Tab("t.never", applies: _ => false, bound: () => bound));
        var ctx = DetailsContext.Empty("x");

        var off = reg.Evaluate(ctx);
        Assert.Empty(off.Offered);
        var g = Assert.Single(off.Greyed);
        Assert.Equal("t.applies", g.View.Id);
        Assert.Equal("Not available on this station", g.Reason);
        bound = true;
        Assert.Single(reg.Evaluate(ctx).Offered);
        Assert.Empty(reg.Evaluate(ctx).Greyed);
    }

    /// <summary>⭐ The offer stays <c>Rank</c>-ordered and STABLE for equal ranks (registration order), greyed tabs included.</summary>
    [Fact]
    public void A7_TheOfferIsRankOrdered_AndStable()
    {
        var reg = new DetailsViewRegistry();
        reg.Add(Tab("a", rank: 5)); reg.Add(Tab("b", rank: 9)); reg.Add(Tab("c", rank: 5)); reg.Add(Tab("d", rank: 9)); reg.Add(Tab("e", rank: 1));
        var ids = new List<string>(); foreach (var d in reg.OfferSet(DetailsContext.Empty("x"))) ids.Add(d.Id);
        Assert.Equal(new[] { "b", "d", "a", "c", "e" }, ids);
    }

    /// <summary>⭐ The hot path (R-2084): the evaluation of an offer — tabs with a requirement, a mode set, a level and a binding, nothing greyed — allocates only its
    /// result list. 🔴 Red-proof (compiles): a LINQ call or a boxed enumerator in <c>Evaluate</c>'s loop ⇒ the per-tab share rises ⇒ red.</summary>
    [Fact]
    public void A7_TheAvailabilityCheck_AllocatesNothingPerTab()
    {
        using var w = World();
        var e = w.CreateEntity(); w.AddComponent(e, new Health { Current = 1, Max = 1 });
        var reg = new DetailsViewRegistry { ComponentProbe = DetailsViewRegistry.ProbeFor(() => w) };
        for (int i = 0; i < 40; i++)
            reg.Add(Tab("t" + i, requires: new[] { typeof(Health) }, availability: new ActionAvailability(ActionModes.Always, 0, EditingLevel.Controller), bound: () => true, rank: i));
        var ctx = DetailsContext.Empty("x") with { Entities = new[] { e }, Station = EditingMode.Live, Level = EditingLevel.Instructor };
        for (int i = 0; i < 3; i++) reg.Evaluate(ctx);   // warm

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) reg.Evaluate(ctx);
        long perCall = (GC.GetAllocatedBytesForCurrentThread() - before) / 100;
        // one List<DetailsViewDescriptor> sized for 40 refs (40 × 8 B + headers ≈ 380) is the result; ⛔ nothing per tab beyond it
        Assert.True(perCall < 450, $"{perCall} B per offer of 40 tabs");
    }
}
