using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Forms;
using Fdp.Toolkit.Replication.Edits;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Forms;
using ImGuiNET;
using NodeEditor.Core.Widgets;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐ CE-3385 (docs/DESIGN_Struct_Editor.md §6 S10 / §12) — the entity form's seat list is the kit's LIST block: its ⋮ menu's <i>Delete</i> runs the list's own action (here <i>Detach</i> from the commander) and that is ONE step of the editor's history
/// (the write goes through <c>Edits.Submit</c>, which brackets it). 🔴 Red-proof (compile): the Delete op not calling <c>list.Remove</c> ⇒ no step ⇒ red. (The write itself is Edits.Submit's, proved by its own rails; here the step bracket is the claim.)
/// </summary>
[Collection(Hrot.Presentation.Tests.ComponentTypeRegistryMutatorCollection.Name)]
public sealed class EntityFormListOpUndoFrameRail
{
    private sealed class Steps : IEditStepRecorder
    {
        public readonly List<string> Done = new(); private string? _label; private int _depth;
        public void Begin(EntityRepository repo, string label) { if (_depth++ == 0) _label = label; }
        public void End(EntityRepository repo) { if (--_depth == 0) Done.Add(_label!); }
        public void Cancel(EntityRepository repo) { _depth = 0; }
    }

    [SkippableFact]
    public void DeletingTheCommanderRow_ThroughTheListsMenu_IsOneHistoryStep()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        ComponentTypeRegistry.Clear();
        using var world = new EntityRepository();
        world.RegisterComponent<EntityInfo>(); world.RegisterComponent<Fdp.Core.CommandHierarchy.UnitSubordinate>(); world.RegisterComponent<Fdp.Core.CommandHierarchy.UnitRoster>();
        var platoon = world.CreateEntity();
        world.AddComponent(platoon, new EntityInfo { Name = new FixedString64("1st Platoon") });
        world.AddComponent(platoon, new Fdp.Core.CommandHierarchy.UnitRoster());
        var man = world.CreateEntity();
        world.AddComponent(man, new EntityInfo { Name = new FixedString64("Alpha 1-1") });
        world.AddComponent(man, new Fdp.Core.CommandHierarchy.UnitSubordinate { Commander = platoon });
        var steps = new Steps(); EditSteps.Install(world, steps);
        HrotForms.EnsureRegistered();

        var view = new FormView();
        using var frame = UiFrameHarness.Begin(700, 500);
        var sim = new WidgetKitFrameRail.Sim();
        var sel = new[] { man };
        void Draw()
        {
            sim.Feed();
            ImGui.SetNextWindowPos(new Vector2(10, 10)); ImGui.SetNextWindowSize(new Vector2(680, 480));
            ImGui.Begin("##unit", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings);
            view.Draw(HrotForms.Unit, world, sel, editable: true);
            ImGui.End();
        }
        for (int k = 0; k < 3; k++) { frame.StepN(10, Draw); System.Threading.Thread.Sleep(150); }
        Assert.Contains("1st Platoon", view.ListRows);
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } shot) frame.Screenshot(System.IO.Path.Combine(shot, "unit-form-seats.png"));
        // the Commander list is the one whose row is ["Commander", "1st Platoon"]: its ⋮ button is the only one drawn for a list row of a unit with no other seats
        Assert.True(KitProbe.TryGet(KitProbe.ListMenuButton, out var a, out var b), "the seat row draws the kit's ⋮ menu");
        sim.Click(frame, Draw, (a + b) / 2f);
        frame.StepN(3, Draw);
        Assert.True(KitProbe.TryGet(KitProbe.MenuRow + 8, out var da, out var db), "the menu lists Delete");
        sim.Click(frame, Draw, (da + db) / 2f);
        frame.StepN(3, Draw);
        Assert.Equal(new[] { "Detach from its commander" }, steps.Done);
    }
}
