using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Editing;
using Fdp.Presentation.Fonts;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.ReplayBrowser.Search.Conditions;
using Fdp.Toolkit.Scenario;
using Hrot.Editor.UiFrameRail;
using ImGuiNET;
using NodeEditor.Core.Widgets;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐ CE-3397 (docs/DESIGN_Struct_Editor.md §11) — the condition editor's <i>who</i> is the SHARED entity field (<see cref="ReferenceLeafDrawers.DrawEntityLinkField"/>), not its own buttons: in a real frame the Who chip opens its popup,
/// the field's type-ahead lists the named entities, and picking a row sets <c>Who.Kind = One</c> with that entity. 🔴 Red-proof (compile): the Who popup's field not writing <c>Kind = One</c> ⇒ red.
/// </summary>
[Collection(Hrot.Presentation.Tests.ComponentTypeRegistryMutatorCollection.Name)]
public sealed class ConditionWhoFieldFrameRail : IDisposable
{
    private readonly EntityRepository _repo;

    private sealed class Ctx : IMapPickContext
    {
        public LiveRefScope? Scope;
        public bool IsPickPendingFor(string jsonPath) => false;
        public void RequestEntityPick(string jsonPath, string[]? filterPresets) { }
        public void RequestLocationPick(string jsonPath) { }
        public bool TryConsumeEntityPick(string jsonPath, out EntityRef picked) { picked = default; return false; }
        public bool TryConsumeLocationPick(string jsonPath, out PointLink location) { location = default; return false; }
        public LiveRefScope? References => Scope;
    }

    public ConditionWhoFieldFrameRail()
    {
        ComponentTypeRegistry.Clear();
        _repo = new EntityRepository();
        _repo.RegisterComponent<NetworkIdentity>();
        _repo.RegisterComponent<EntityInfo>();
        _repo.RegisterComponent<SimTransform>();
    }

    public void Dispose() => _repo.Dispose();

    private EntityRef Spawn(int id, string name)
    {
        var e = _repo.CreateEntity();
        _repo.AddComponent(e, new NetworkIdentity(id));
        _repo.AddComponent(e, new EntityInfo { Name = new FixedString64(name), ForceId = ForceId.Friend });
        _repo.AddComponent(e, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
        return new EntityRef(id);
    }

    [SkippableFact]
    public void PickingARowInTheWhoField_SetsWhoToThatOneThing_AndASteadyFrameAllocatesNothing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        Spawn(41, "Alpha 1-1"); var bravo = Spawn(42, "Bravo 2-3");
        var ctx = new Ctx { Scope = new LiveRefScope(_repo, () => 0) };
        var editor = new ConditionEditor();
        SearchPredicateDto? value = RowProjection.ToDto(new[] { new ConditionRow(Subject.Thing(EntityLink.None), "inside") });
        using var frame = UiFrameHarness.Begin(900, 500);
        new EditorFontService().Initialize(1f, 1f);
        var sim = new WidgetKitFrameRail.Sim();
        long bytes = -1; bool changed = false;
        void Draw()
        {
            sim.Feed();
            ImGui.SetNextWindowPos(new Vector2(10, 10)); ImGui.SetNextWindowSize(new Vector2(880, 460));
            ImGui.Begin("##cond", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings);
            long before = GC.GetAllocatedBytesForCurrentThread();
            if (editor.Draw("c", ref value, ctx)) changed = true;
            bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            ImGui.End();
        }
        for (int k = 0; k < 3; k++) { frame.StepN(15, Draw); System.Threading.Thread.Sleep(220); }
        frame.StepN(5, Draw);
        Assert.True(bytes == 0, $"a steady condition editor allocates nothing ({bytes} bytes)");

        var (a, b) = editor.WhoChipOf(0);
        sim.Click(frame, Draw, (a + b) / 2f);                                  // the Who chip: its popup
        frame.StepN(3, Draw);
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d0) frame.Screenshot(System.IO.Path.Combine(d0, "condition-who-popup.png"));
        Assert.True(KitProbe.TryGet(KitProbe.EntityChip, out var ca, out var cb), "the popup draws the shared entity field");
        sim.Click(frame, Draw, (ca + cb) / 2f);                                // the field's chip: its type-ahead
        frame.StepN(3, Draw);
        var shot = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS");
        if (!string.IsNullOrEmpty(shot)) frame.Screenshot(System.IO.Path.Combine(shot, "condition-who-field.png"));
        Assert.True(KitProbe.TryGet(KitProbe.CandidateRow + 1, out var ra, out var rb), "the picker lists the named entities");
        sim.Click(frame, Draw, (ra + rb) / 2f);                                // Bravo 2-3 (rows are by name)
        frame.StepN(3, Draw);
        Assert.True(changed);
        var who = editor.SubjectOf(0)!;
        Assert.Equal(SubjectKind.One, who.Kind);
        Assert.Equal(bravo, who.One.Ref);
    }
}
