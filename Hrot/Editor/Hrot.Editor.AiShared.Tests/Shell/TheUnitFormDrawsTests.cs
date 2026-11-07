using System;
using System.Linq;
using Fdp.Core;
using Fdp.Presentation.Forms;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Forms;
using ImGuiNET;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐ U-23 / D3 (docs/UX/DESIGN_Details_Forms.md §3) — the <b>Unit</b> form in a real frame: name, side, strength read from the world, the expert row under
/// <i>Details for experts</i>; ⭐ §2.4 — ONE editable rule: a recording (the run state <c>Replay</c>) is read-only. The bundle registers the declared forms.
/// 🔴 Red-proof (compiles): the Unit form's Side reading the wrong index map ⇒ <c>Side = Red</c> ⇒ red. Renders: <c>HROT_RAIL_SHOTS</c> ⇒ <c>unit-form.png</c>.
/// </summary>
[Collection(Hrot.Editor.AiShared.Tests.PanelSnapshotTestCollection.Name)]
public sealed class TheUnitFormDrawsTests
{
    [SkippableFact]
    public void U23_TheUnitForm_ReadsTheWorld_AndARecordingIsReadOnly()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        ComponentTypeRegistry.Clear();
        using var world = new EntityRepository();
        world.RegisterComponent<EntityInfo>(); world.RegisterComponent<Health>(); world.RegisterComponent<NetworkIdentity>();
        var e = world.CreateEntity();
        world.AddComponent(e, new EntityInfo { Name = new FixedString64("Alpha 1-1"), ForceId = ForceId.Friend });
        world.AddComponent(e, new Health { Current = 80, Max = 100 });
        world.AddComponent(e, new NetworkIdentity(42));

        HrotForms.EnsureRegistered();
        Assert.Contains(FormCatalogue.All, f => f.Id == HrotForms.Unit.Id);   // the bundle's one registration
        Assert.True(HrotForms.Unit.OffersFor(world, e));
        Assert.True(FormDetailsView.Editable(VariableRunState.Planning, world));
        Assert.False(FormDetailsView.Editable(VariableRunState.Replay, world));   // §2.4: a recording is read-only, one rule

        var view = new FormDetailsView(HrotForms.Unit, () => world);
        view.View.ExpertOpen = true;
        using var frame = UiFrameHarness.Begin(700, 420);
        frame.StepN(4, () =>
        {
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10));
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(460, 300));
            ImGui.Begin("Details##unit-form");
            view.View.Draw(HrotForms.Unit, world, new[] { e }, editable: true);
            ImGui.End();
        });
        Assert.Equal(new[] { "Name = Alpha 1-1", "Side = Blue", "Strength = 80 / 100", "Entity id = 42" }, view.View.Rows);
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) frame.Screenshot(System.IO.Path.Combine(dir, "unit-form.png"));
    }

    /// <summary>⭐ U-23 / D5 — the <b>Shape</b> form is offered for a spawn area OR a weather region (an any-of), and a spawn area's rows read the world.
    /// 🔴 Red-proof (compiles): <c>FormDecl.OffersFor</c> ignoring <c>Offers</c> ⇒ not offered (no Requires) ⇒ red.</summary>
    [Fact]
    public void U23_TheShapeForm_IsOfferedForASpawnArea_AndReadsIt()
    {
        ComponentTypeRegistry.Clear();
        using var world = new EntityRepository();
        world.RegisterComponent<EntityInfo>(); world.RegisterComponent<Hrot.Common.SpawnArea>(); world.RegisterComponent<Fdp.Toolkit.Weather.WeatherConditions>();
        var spawn = world.CreateEntity();
        world.AddComponent(spawn, new Hrot.Common.SpawnArea { Side = ForceId.Hostile, FacingDeg = 90 });
        var unit = world.CreateEntity();
        world.AddComponent(unit, new EntityInfo { Name = new FixedString64("Alpha") });
        HrotForms.EnsureRegistered();
        Assert.True(HrotForms.Shape.OffersFor(world, spawn));
        Assert.False(HrotForms.Shape.OffersFor(world, unit));
        Assert.Equal(1.0, HrotForms.Shape.Fields[0].ReadNumber!(world, spawn));   // Red
        Assert.Equal(90.0, HrotForms.Shape.Fields[1].ReadNumber!(world, spawn));
        Assert.Equal("90 °", FormView.Format(HrotForms.Shape.Fields[1], 90, default));
        Assert.All(HrotForms.Shape.Fields, f => Assert.False(f.Attribute != null && f.Definition == null));   // every field names a vocabulary attribute
    }

    /// <summary>⭐ U-24 / S5 (DESIGN_Initial_State §3) — the Unit form's start-state rows in a real frame: <i>Weapon</i> (posture) and <i>In hand</i> read the
    /// saved <c>WeaponPosture</c> (B-5 S2), <i>Aboard</i> lists the vehicle and the seat's role. 🔴 Red-proof (compiles): <c>FillAboard</c> returning nothing ⇒ red.</summary>
    [SkippableFact]
    public void U24_S5_TheUnitForm_ShowsTheWeaponStateAndWhereHeRides()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        ComponentTypeRegistry.Clear();
        using var world = new EntityRepository();
        world.RegisterComponent<EntityInfo>(); world.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponPosture>();
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(); world.RegisterComponent<Fdp.Toolkit.Behavior.Components.CarrierSeats>();
        world.RegisterComponent<NetworkIdentity>();
        var enemy = world.CreateEntity();
        world.AddComponent(enemy, new EntityInfo { Name = new FixedString64("Enemy 1") });
        world.AddComponent(enemy, new NetworkIdentity(9));
        var bmp = world.CreateEntity();
        world.AddComponent(bmp, new EntityInfo { Name = new FixedString64("BMP 1") });
        var seats = new Fdp.Toolkit.Behavior.Components.CarrierSeats { SeatCount = 4 };
        seats.Roles[0] = Fdp.Toolkit.Behavior.Components.SeatRole.Driver;
        world.AddComponent(bmp, seats);
        var man = world.CreateEntity();
        world.AddComponent(man, new EntityInfo { Name = new FixedString64("Alpha 1-1"), ForceId = ForceId.Friend });
        world.AddComponent(man, new Fdp.Toolkit.Combat.Components.WeaponPosture { Kind = Fdp.Toolkit.Combat.Components.WeaponPostureKind.Aimed, Target = enemy, HeldItem = Fdp.Toolkit.Combat.Components.HeldItemKind.Binoculars });
        world.AddComponent(man, new NetworkIdentity(7));
        world.AddComponent(man, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = bmp, Seat = 0 });

        HrotForms.EnsureRegistered();
        var view = new FormView { Pick = new OnePick(new Fdp.Toolkit.Replication.EntityRef(7), "form.unit.list1") };   // he picks HIMSELF to aim at
        using var frame = UiFrameHarness.Begin(700, 420);
        frame.StepN(3, () =>
        {
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10)); ImGui.SetNextWindowSize(new System.Numerics.Vector2(460, 320));
            ImGui.Begin("Details##unit-s5");
            view.Draw(HrotForms.Unit, world, new[] { man }, editable: true);
            ImGui.End();
        });
        Assert.Contains("Weapon = Aimed", view.Rows);
        Assert.Contains("In hand = Binoculars", view.Rows);
        Assert.Equal(new[] { "Driver", "BMP 1", "Target", "Enemy 1" }, view.ListRows);
        Assert.Equal("he cannot aim at himself", view.ListProblem(1));   // the Aim at… pick came back through the one context and was refused with the reason
        Assert.NotNull(HrotForms.Unit.Fields.First(f => f.Label == "Weapon").Definition);   // the row writes a vocabulary attribute (Weapon.Posture)
        Assert.NotNull(HrotForms.Unit.Fields.First(f => f.Label == "In hand").Definition);
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) frame.Screenshot(System.IO.Path.Combine(dir, "unit-form-s5.png"));
    }

    /// <summary>⭐ U-24 / S5 (DESIGN_Initial_State §3) — the Unit form's <b>Knows about</b> list in a real frame: one row per remembered target by name, after the other lists; <i>Add…</i> picks through the one context and seeds the target
    /// through <c>KnowledgeOrders.SeedTarget</c> (a <c>SeedTargetCommand</c> on the bus); he cannot be asked to know of himself; with no <c>TargetMemory</c> (a station that does not run his brain) the footer says where it is known.
    /// 🔴 Red-proof (compiles): <c>FillKnows</c> adding nothing ⇒ no row ⇒ red.</summary>
    [SkippableFact]
    public void U24_S5_KnowsAbout_ListsTheRememberedTargets_AndAddSeedsThroughTheOneOrder()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        ComponentTypeRegistry.Clear();
        using var world = new EntityRepository();
        world.RegisterComponent<EntityInfo>(); world.RegisterComponent<NetworkIdentity>(); world.RegisterComponent<Fdp.Toolkit.Perception.Components.TargetMemory>();
        world.RegisterEvent<Fdp.Toolkit.Perception.Events.SeedTargetCommand>();
        var enemy = world.CreateEntity();
        world.AddComponent(enemy, new EntityInfo { Name = new FixedString64("Enemy 1") });
        world.AddComponent(enemy, new NetworkIdentity(9));
        var man = world.CreateEntity();
        world.AddComponent(man, new EntityInfo { Name = new FixedString64("Alpha 1-1"), ForceId = ForceId.Friend });
        world.AddComponent(man, new NetworkIdentity(7));
        var mem = new Fdp.Toolkit.Perception.Components.TargetMemory();
        Fdp.Toolkit.Perception.Components.TargetMemory.AddOrUpdateTarget(ref mem, (long)enemy.PackedValue, 10f, 20f, 1f, 1);
        world.AddComponent(man, mem);
        var bare = world.CreateEntity();   // a unit whose brain is elsewhere: no TargetMemory here
        world.AddComponent(bare, new EntityInfo { Name = new FixedString64("Bravo 2") });

        HrotForms.EnsureRegistered();
        int list = 2;
        Assert.Equal("Knows about", HrotForms.Unit.Lists[list].Label);
        Assert.Null(HrotForms.Unit.Lists[list].Remove);   // nothing in the engine forgets a target on request
        var pick = new OnePick(new Fdp.Toolkit.Replication.EntityRef(7), "form.unit.list" + list);   // he is picked as HIS OWN known unit
        var view = new FormView { Pick = pick };
        using var frame = UiFrameHarness.Begin(700, 420);
        void Draw(Entity who) => frame.StepN(3, () =>
        {
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10)); ImGui.SetNextWindowSize(new System.Numerics.Vector2(460, 360));
            ImGui.Begin("Details##unit-knows");
            view.Draw(HrotForms.Unit, world, new[] { who }, editable: true);
            ImGui.End();
        });
        Draw(man);
        Assert.Equal(new[] { "Unit", "Enemy 1" }, view.ListRows.Skip(view.ListRows.Count - 2).ToArray());   // after the other lists' rows
        Assert.Equal("he cannot know of himself", view.ListProblem(list));
        HrotForms.Unit.Lists[list].Add!(world, man, enemy);   // what the picked enemy sends
        world.Bus.SwapBuffers();
        var sent = world.Bus.Read<Fdp.Toolkit.Perception.Events.SeedTargetCommand>();
        Assert.Equal(1, sent.Length);
        Assert.Equal(man, sent[0].Perceiver); Assert.Equal(enemy, sent[0].Target);
        view = new FormView();
        Draw(bare);
        Assert.Empty(view.ListRows);                       // no memory here: no rows ...
        Assert.Equal("(known only where his brain runs)", HrotForms.Unit.Lists[list].Footer!(world, bare));   // ... and the footer says why
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) { view = new FormView(); Draw(man); frame.Screenshot(System.IO.Path.Combine(dir, "unit-form-knows.png")); }
    }

    /// <summary>⭐ U-23 part 5 (DESIGN_Details_Forms §4.0a) — the Summary tab retired into the Unit form: <i>Strength</i> reads <c>80 / 100</c> (the write stays <c>Health.Current</c>), the expert rows <i>Position · Kind · DIS type · Shown as</i> read the world through the ONE
    /// <c>EntityFacts</c>; a STATIC unit's frame allocates nothing; Summary is no longer offered where the form is, and still is for an entity with no <c>EntityInfo</c>.
    /// 🔴 Red-proof (compiles): <c>FormView.Refresh</c> dropping the max from <c>Shown</c> ⇒ "80" ⇒ red. Renders: <c>unit-form.png</c>.</summary>
    [SkippableFact]
    public void U23_P5_TheUnitForm_AbsorbsTheSummary_StrengthOverMax_ExpertFacts_0B_AndTheTabIsNotOffered()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        ComponentTypeRegistry.Clear();
        using var world = new EntityRepository();
        world.RegisterComponent<EntityInfo>(); world.RegisterComponent<Health>(); world.RegisterComponent<NetworkIdentity>();
        world.RegisterComponent<SimTransform>(); world.RegisterComponent<Hrot.Map.Common.Components.ResolvedStyle>();
        var e = world.CreateEntity();
        world.AddComponent(e, new EntityInfo { Name = new FixedString64("Alpha 1-1"), ForceId = ForceId.Friend });
        world.AddComponent(e, new Health { Current = 80, Max = 100 });
        world.AddComponent(e, new NetworkIdentity(42));
        world.AddComponent(e, new SimTransform { Position = new System.Numerics.Vector3(12.34f, 5f, 0f), Rotation = System.Numerics.Quaternion.Identity });
        var style = Hrot.Map.Common.Components.ResolvedStyle.CreateDefault(); style.Affiliation = ForceId.Hostile;   // drawn as Red though he is Blue
        world.AddComponent(e, style);
        var dis = new DISEntityType { Kind = 1, Domain = 1, Country = 225, Category = 1 };
        world.SetDisType(e, dis);

        HrotForms.EnsureRegistered();
        var view = new FormView { ExpertOpen = true };
        using var frame = UiFrameHarness.Begin(700, 420);
        Action draw = () =>
        {
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10)); ImGui.SetNextWindowSize(new System.Numerics.Vector2(460, 360));
            ImGui.Begin("Details##unit-p5");
            view.Draw(HrotForms.Unit, world, new[] { e }, editable: true);
            ImGui.End();
        };
        var sel = new[] { e };
        draw = () =>
        {
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10)); ImGui.SetNextWindowSize(new System.Numerics.Vector2(460, 360));
            ImGui.Begin("Details##unit-p5");
            view.Draw(HrotForms.Unit, world, sel, editable: true);
            ImGui.End();
        };
        for (int i = 0; i < 5; i++) frame.Step(draw);
        Assert.Contains("Strength = 80 / 100", view.Rows);
        Assert.Contains("Position = 12.3, 5.0, 0.0 m", view.Rows);
        Assert.Contains("DIS type = " + Hrot.Presentation.Forms.EntityFacts.DisText(dis), view.Rows);
        Assert.Contains("Shown as = Hostile", view.Rows);
        Assert.Contains(view.Rows, r => r.StartsWith("Kind = "));
        Assert.Equal("Health.Current", HrotForms.Unit.Fields.First(f => f.Label == "Strength").Attribute);   // reads over a max, writes the current value
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) frame.Screenshot(System.IO.Path.Combine(dir, "unit-form.png"));

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 3; i++) frame.Step(draw);
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);   // a static unit: nothing is rebuilt, nothing allocated
        world.SetComponent(e, new SimTransform { Position = new System.Numerics.Vector3(12.36f, 5f, 0f), Rotation = System.Numerics.Quaternion.Identity });   // moved 2 cm: the text stays
        frame.Step(draw);
        Assert.Contains("Position = 12.3, 5.0, 0.0 m", view.Rows);
        world.SetComponent(e, new SimTransform { Position = new System.Numerics.Vector3(20f, 5f, 0f), Rotation = System.Numerics.Quaternion.Identity });   // moved 8 m: it follows
        frame.Step(draw);
        Assert.Contains("Position = 20.0, 5.0, 0.0 m", view.Rows);

        // the Summary tab is not offered where the Unit form is — and still is for an entity the form is not offered for
        var summary = Hrot.Editor.AiShared.Shell.EntitySummaryViewDescriptor.For(() => world);
        Assert.False(summary.AppliesTo(DetailsContext.Empty("Scenario") with { Entities = new[] { e } }));
        var bare = world.CreateEntity();
        Assert.True(summary.AppliesTo(DetailsContext.Empty("Scenario") with { Entities = new[] { bare } }));
    }

    /// <summary>⭐ U-24 / S5 (DESIGN_Initial_State §3) — the Unit form's <b>Carried by</b> list in a real frame: a man a SOLDIER drags reads <c>["Dragged", "&lt;carrier&gt;"]</c> and <i>Aboard</i> (vehicles) shows nothing for him; a pick of something that is no
    /// soldier is refused with the reason; the 4-argument <c>EmbarkOrders.Embark</c> publishes the command with the carry mode. ⏳ Changing the mode of a man already aboard is not offered: <c>Embarkation.TryEmbark</c> answers
    /// <i>AlreadyAboard</i> without touching the mode (set at boarding only), so a "Carried as" field would need a backend op — recorded, not built. 🔴 Red-proof (compiles): <c>FillAboard</c> not skipping a person carrier ⇒ a second row ⇒ red.</summary>
    [SkippableFact]
    public void U24_S5_CarriedBy_ReadsTheCarrierAndMode_AndAboardLeavesHimAlone()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        ComponentTypeRegistry.Clear();
        using var world = new EntityRepository();
        world.RegisterComponent<EntityInfo>(); world.RegisterComponent<NetworkIdentity>(); world.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.PassengerBuffer>(); world.RegisterComponent<Fdp.Toolkit.Behavior.Components.PersonCarrier>();
        world.RegisterEvent<Fdp.Toolkit.Behavior.Events.EmbarkEntityCommand>();
        var thing = world.CreateEntity();   // not a soldier
        world.AddComponent(thing, new EntityInfo { Name = new FixedString64("Crate") });
        world.AddComponent(thing, new NetworkIdentity(9));
        var soldier = world.CreateEntity();
        world.AddComponent(soldier, new EntityInfo { Name = new FixedString64("Bravo 2") });
        world.AddComponent(soldier, new Fdp.Toolkit.Behavior.Components.PersonCarrier());
        var man = world.CreateEntity();
        world.AddComponent(man, new EntityInfo { Name = new FixedString64("Alpha 1-1"), ForceId = ForceId.Friend });
        world.AddComponent(man, new NetworkIdentity(7));
        var buf = new Fdp.Toolkit.Behavior.Components.PassengerBuffer { Count = 1 };
        buf.Passengers[0] = man;
        buf.SetMode(0, Fdp.Toolkit.Behavior.Components.CarryMode.Dragged);
        world.AddComponent(soldier, buf);
        world.AddComponent(man, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = soldier, Seat = 0 });

        HrotForms.EnsureRegistered();
        const int list = 3;
        Assert.Equal("Carried by", HrotForms.Unit.Lists[list].Label);
        var view = new FormView { Pick = new OnePick(new Fdp.Toolkit.Replication.EntityRef(9), "form.unit.list" + list) };   // he is told to be carried by a crate
        using var frame = UiFrameHarness.Begin(700, 420);
        frame.StepN(3, () =>
        {
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10)); ImGui.SetNextWindowSize(new System.Numerics.Vector2(460, 360));
            ImGui.Begin("Details##unit-carried");
            view.Draw(HrotForms.Unit, world, new[] { man }, editable: true);
            ImGui.End();
        });
        Assert.Equal(new[] { "Dragged", "Bravo 2" }, view.ListRows);   // the Aboard list (vehicles) draws nothing for him
        Assert.Equal("only a soldier carries a man", view.ListProblem(list));
        Assert.Equal("he cannot carry himself", HrotForms.Unit.Lists[list].Add!(world, man, man));
        Assert.Equal("already aboard — put him down first", HrotForms.Unit.Lists[list].Add!(world, man, soldier));

        var other = world.CreateEntity();
        Assert.True(Hrot.Common.Orders.EmbarkOrders.Embark(world, other, soldier, Fdp.Toolkit.Behavior.Components.CarryMode.Dragged));
        world.Bus.SwapBuffers();
        var sent = world.Bus.Read<Fdp.Toolkit.Behavior.Events.EmbarkEntityCommand>();
        Assert.Equal(1, sent.Length);
        Assert.Equal(Fdp.Toolkit.Behavior.Components.CarryMode.Dragged, sent[0].Mode);   // the order carries how he rides
        Assert.Equal(other, sent[0].Passenger); Assert.Equal(soldier, sent[0].Vehicle);
    }

    /// <summary>⭐ U-24 / S6 (DESIGN_Initial_State §4.2) — the Unit form's <b>Character</b> section in a real frame: Skill and Bravery / Obedience / Nerve / Resolve read the EFFECTIVE values; an authored value that differs from the type's
    /// carries the blue reset arrow (<c>FormMark.DiffersFromType</c>, tooltip <i>type 0.5 — reset</i>), one that does not carries none; the reset writes a NEGATIVE value to the attribute (81) through the one <c>Submit</c> seam.
    /// Skill is a SLIDER row (not the tier word — the optional part of the S6 row). 🔴 Red-proof (compiles): <c>FormView.RefreshType</c> never setting <c>Differs</c> ⇒ no mark ⇒ red. Renders: <c>unit-form-s5.png</c>.</summary>
    [SkippableFact]
    public void U24_S6_TheCharacterSection_ReadsTheEffectiveValues_MarksAnAuthoredOverTheType_AndResetWritesNegative()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        ComponentTypeRegistry.Clear();
        using var world = new EntityRepository();
        world.RegisterComponent<EntityInfo>(); world.RegisterComponent<Health>(); world.RegisterComponent<NetworkIdentity>();
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.CombatProficiency>(); world.RegisterComponent<Fdp.Toolkit.Behavior.Components.CombatTemperament>();
        var e = world.CreateEntity();
        world.AddComponent(e, new EntityInfo { Name = new FixedString64("Alpha 1-1"), ForceId = ForceId.Friend });
        world.AddComponent(e, new Health { Current = 80, Max = 100 });
        world.AddComponent(e, new NetworkIdentity(42));
        world.AddComponent(e, new Fdp.Toolkit.Behavior.Components.CombatProficiency { Authored = 0.8f, TypeDefault = 0.5f, Set = (byte)(Fdp.Toolkit.Behavior.Components.CombatProficiency.AuthoredSet | Fdp.Toolkit.Behavior.Components.CombatProficiency.TypeDefaultSet) });
        world.AddComponent(e, new Fdp.Toolkit.Behavior.Components.CombatTemperament { RiskAcceptance = 0.8f, Set = Fdp.Toolkit.Behavior.Components.CombatTemperament.RiskAcceptanceSet, TypeRiskAcceptance = 0.5f, TypeSet = Fdp.Toolkit.Behavior.Components.CombatTemperament.RiskAcceptanceSet });

        HrotForms.EnsureRegistered();
        int Idx(string label) { for (int i = 0; i < HrotForms.Unit.Fields.Count; i++) if (HrotForms.Unit.Fields[i].Label == label) return i; return -1; }
        int skill = Idx("Skill"), bravery = Idx("Bravery"), obedience = Idx("Obedience");
        Assert.True(skill >= 0 && bravery >= 0 && obedience >= 0 && Idx("Nerve") >= 0 && Idx("Resolve") >= 0);
        Assert.Equal("Character", HrotForms.Unit.Fields[skill].Section);

        var sent = new System.Collections.Generic.List<Fdp.Toolkit.Replication.Edits.EditOp>();
        var view = new FormView { Submit = (w, op) => { sent.Add(op); return new Fdp.Toolkit.Replication.Edits.EditTicket(Guid.NewGuid(), Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Direct); } };
        using var frame = UiFrameHarness.Begin(700, 420);
        frame.StepN(4, () =>
        {
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10));
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(460, 360));
            ImGui.Begin("Details##unit-s6");
            view.Draw(HrotForms.Unit, world, new[] { e }, editable: true);
            ImGui.End();
        });
        Assert.Contains("Skill = 0.8", view.Rows); Assert.Contains("Bravery = 0.8", view.Rows);
        Assert.Contains("Obedience = 0.8", view.Rows);   // not authored: the default, no mark
        Assert.Contains("Nerve = 0.5", view.Rows); Assert.Contains("Resolve = 0.5", view.Rows);
        Assert.Equal(FormMark.DiffersFromType, view.MarkOf(skill));
        Assert.Equal(FormMark.DiffersFromType, view.MarkOf(bravery));
        Assert.Equal(FormMark.None, view.MarkOf(obedience));
        Assert.Equal("type 0.5 — reset", view.TypeTipOf(bravery));
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) frame.Screenshot(System.IO.Path.Combine(dir, "unit-form-s5.png"));

        view.CommitForTest(HrotForms.Unit, bravery, world, new[] { e }, HrotForms.Unit.Fields[bravery].ResetValue);   // what a click on the arrow commits
        var op = Assert.Single(sent);
        var change = Assert.Single(op.Changes!);
        Assert.Equal(Fdp.Toolkit.Replication.Patching.AttributeIds.TemperamentRiskAcceptance, change.AttributeId);
        Assert.Equal(-1.0, change.Value.DoubleValue);
        Assert.Null(HrotForms.Unit.Fields[bravery].RangeProblem(0.5));   // a typed value is range-checked; the reset is not a typed value
    }

    /// <summary>⭐ U-23 part 5 — the Unit form's <b>Playable</b> toggle is the ONE make-playable op (it replaced the Playable tab where the form is offered):
    /// on adds the component with the unit's side and name, off removes it. 🔴 Red-proof (compiles): the field's <c>Apply</c> removed ⇒ read-only ⇒ nothing written ⇒ red.</summary>
    [Fact]
    public void U23_P5_TheUnitFormsPlayableToggle_AddsAndRemovesTheComponent()
    {
        ComponentTypeRegistry.Clear();
        using var world = new EntityRepository();
        world.RegisterComponent<EntityInfo>(); world.RegisterComponent<Hrot.Common.Playable>();
        var e = world.CreateEntity();
        world.AddComponent(e, new EntityInfo { Name = new FixedString64("Alpha 1"), ForceId = ForceId.Friend });
        HrotForms.EnsureRegistered();
        int toggle = -1;
        for (int i = 0; i < HrotForms.Unit.Fields.Count; i++) if (HrotForms.Unit.Fields[i].Label == "Playable") toggle = i;
        Assert.True(toggle >= 0);
        Assert.False(HrotForms.Unit.Fields[toggle].ReadOnly);
        var view = new FormView();
        view.CommitForTest(HrotForms.Unit, toggle, world, new[] { e }, 1);
        Assert.True(world.HasComponent<Hrot.Common.Playable>(e));
        Assert.Equal("Alpha 1", world.GetComponentRO<Hrot.Common.Playable>(e).SlotName.ToString());
        view.CommitForTest(HrotForms.Unit, toggle, world, new[] { e }, 0);
        Assert.False(world.HasComponent<Hrot.Common.Playable>(e));
    }

    /// <summary>⭐ U-23 / D4 — the <b>Crew &amp; cargo</b> form in a real frame: offered for a carrier, not for a man; one row per OCCUPIED seat (role, who), the free
    /// seats as a count; <i>Put aboard…</i> picks through the one pick context and a vehicle picking itself is refused with the reason.
    /// 🔴 Red-proof (compiles): <c>FillSeats</c> skipping the seat-role lookup (always Passenger) ⇒ "Driver" missing ⇒ red.</summary>
    [SkippableFact]
    public void U23_D4_TheCrewAndCargoForm_ListsTheSeats_AndPutAboardPicksThroughTheOnePickContext()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        ComponentTypeRegistry.Clear();
        using var world = new EntityRepository();
        world.RegisterComponent<EntityInfo>(); world.RegisterComponent<NetworkIdentity>();
        world.RegisterComponent<Fdp.Toolkit.Behavior.Components.PassengerBuffer>(); world.RegisterComponent<Fdp.Toolkit.Behavior.Components.CarrierSeats>();
        var bmp = world.CreateEntity();
        world.AddComponent(bmp, new EntityInfo { Name = new FixedString64("BMP 1") });
        world.AddComponent(bmp, new NetworkIdentity(50));
        var seats = new Fdp.Toolkit.Behavior.Components.CarrierSeats { SeatCount = 4 };
        seats.Roles[0] = Fdp.Toolkit.Behavior.Components.SeatRole.Driver;
        world.AddComponent(bmp, seats);
        var driver = world.CreateEntity();
        world.AddComponent(driver, new EntityInfo { Name = new FixedString64("Alpha 1-1") });
        var buf = new Fdp.Toolkit.Behavior.Components.PassengerBuffer { Count = 1 };
        buf.Passengers[0] = driver;
        world.AddComponent(bmp, buf);

        HrotForms.EnsureRegistered();
        Assert.True(HrotForms.CrewAndCargo.OffersFor(world, bmp));
        Assert.False(HrotForms.CrewAndCargo.OffersFor(world, driver));

        var pick = new OnePick(new Fdp.Toolkit.Replication.EntityRef(50));   // the person picks the vehicle itself
        var view = new FormView { Pick = pick };
        using var frame = UiFrameHarness.Begin(700, 420);
        frame.StepN(3, () =>
        {
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10)); ImGui.SetNextWindowSize(new System.Numerics.Vector2(460, 300));
            ImGui.Begin("Details##crew-form");
            view.Draw(HrotForms.CrewAndCargo, world, new[] { bmp }, editable: true);
            ImGui.End();
        });
        Assert.Equal(new[] { "Driver", "Alpha 1-1" }, view.ListRows);
        Assert.Equal("a vehicle cannot board itself", view.ListProblem(0));   // the pick came back through the one context and was refused with the reason
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) frame.Screenshot(System.IO.Path.Combine(dir, "crew-form.png"));
    }

    /// <summary>A pick context that hands back one entity at once (the person picked it).</summary>
    private sealed class OnePick : Fdp.Presentation.Editing.IMapPickContext
    {
        private Fdp.Toolkit.Replication.EntityRef _next;
        private readonly string? _path;
        public OnePick(Fdp.Toolkit.Replication.EntityRef next, string? path = null) { _next = next; _path = path; }
        public bool IsPickPendingFor(string jsonPath) => false;
        public void RequestEntityPick(string jsonPath, string[]? filterPresets) { }
        public void RequestLocationPick(string jsonPath) { }
        public bool TryConsumeEntityPick(string jsonPath, out Fdp.Toolkit.Replication.EntityRef picked) { picked = default; if (_path != null && jsonPath != _path) return false; picked = _next; bool had = !_next.IsNone; _next = default; return had; }
        public bool TryConsumeLocationPick(string jsonPath, out Fdp.Toolkit.Replication.PointLink location) { location = default; return false; }
    }

    /// <summary>⭐ U-23 / D5 — the <b>Scenario</b> form is the WORLD form: its target is the World Environment (the clock + the global weather); the start reads as a
    /// date, the global weather through the SAME rows as a region's. 🔴 Red-proof (compiles): <c>WorldTarget</c> returning <c>Entity.Null</c> ⇒ red.</summary>
    [Fact]
    public void U23_TheScenarioForm_TargetsTheWorldEnvironment()
    {
        ComponentTypeRegistry.Clear();
        using var world = new EntityRepository();
        world.RegisterComponent<Fdp.Toolkit.Weather.ScenarioClock>(); world.RegisterComponent<Fdp.Toolkit.Weather.WeatherConditions>();
        var env = world.CreateEntity();
        world.AddComponent(env, new Fdp.Toolkit.Weather.ScenarioClock { StartUtcTicks = new DateTime(2026, 6, 1, 6, 0, 0, DateTimeKind.Utc).Ticks, DisplayUtcOffsetMinutes = 120 });
        world.AddComponent(env, new Fdp.Toolkit.Weather.WeatherConditions { GroundTemperatureC = 21, VisibilityM = 8000 });
        HrotForms.EnsureRegistered();
        Assert.Equal(env, HrotForms.Scenario.WorldTarget!(world));
        var f = HrotForms.Scenario.Fields;
        Assert.Equal("2026-06-01 06:00 UTC", f[0].ReadText!(world, env).ToString());
        Assert.Equal(120.0, f[1].ReadNumber!(world, env));
        Assert.Contains(f, x => x.Label == "Temperature" && x.ReadNumber!(world, env) == 21);
        Assert.Contains(HrotForms.Shape.Fields, x => x.Label == "Temperature");   // the ONE weather row set, on both forms
    }
}
