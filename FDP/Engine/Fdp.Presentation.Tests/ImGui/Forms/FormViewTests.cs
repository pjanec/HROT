using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Presentation.Forms;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Replication.Patching;
using Xunit;

namespace Fdp.Presentation.Tests.ImGui.Forms;

/// <summary>
/// ⭐ U-12 / D1 (docs/UX/DESIGN_Details_Forms.md §2, §4 D1) — a DECLARED form draws for a selection and commits through <see cref="Edits.Submit"/>: the rows common
/// to the selection, a differing value <i>mixed</i>, one Submit per entity with the attribute the field names, a value out of range refused with the reason,
/// and ⭐ 0 B per steady frame with the form open and no row in edit (CE-4285). 🔴 Red-proof (compiles): <c>Refresh</c> re-formatting every frame (dropping
/// the <c>Known</c> check) ⇒ the allocation rail fails.
/// </summary>
public sealed class FormViewTests : IDisposable
{
    private readonly EntityRepository _world;

    public FormViewTests()
    {
        ComponentTypeRegistry.Clear();
        _world = new EntityRepository();
        _world.RegisterComponent<EntityInfo>();
        _world.RegisterComponent<Health>();
    }

    public void Dispose() => _world.Dispose();

    private Entity Unit(string name, float hp)
    {
        var e = _world.CreateEntity();
        _world.AddComponent(e, new EntityInfo { Name = new FixedString64(name) });
        _world.AddComponent(e, new Health { Current = hp, Max = 100 });
        return e;
    }

    private static readonly FormDecl Form = new()
    {
        Id = "rail-unit", Title = "Unit", Requires = new[] { typeof(EntityInfo) },
        Fields = new[]
        {
            new FormField { Label = "Name", Kind = FieldKind.Text, Attribute = "Name", ReadText = (w, e) => w.GetComponentRO<EntityInfo>(e).Name },
            new FormField { Label = "Strength", Attribute = "Health.Current", Min = 0, Max = 100,
                            ReadNumber = (w, e) => w.GetComponentRO<Health>(e).Current, Has = (w, e) => w.HasComponent<Health>(e) },
            new FormField { Label = "Entity id", Expert = true, ReadNumber = (w, e) => e.Index },
        },
    };

    private void Frames(FormView view, IReadOnlyList<Entity> sel, int n, bool editable = true)
    {
        using var imgui = new ImGuiTestFixture();
        for (int f = 0; f < n; f++)
        {
            imgui.NewFrame();
            ImGuiNET.ImGui.Begin("D1");
            view.Draw(Form, _world, sel, editable);
            ImGuiNET.ImGui.End();
            imgui.Render();
        }
    }

    [Fact]
    public void D1_ASelection_ShowsTheCommonRows_ADifferingValueIsMixed()
    {
        var a = Unit("Alpha 1-1", 50); var b = Unit("Bravo 2", 50);
        var view = new FormView();
        Frames(view, new[] { a }, 2);
        Assert.Equal(new[] { "Name = Alpha 1-1", "Strength = 50", $"Entity id = {a.Index}" }, view.Rows);
        Frames(view, new[] { a, b }, 2);
        Assert.Equal("Name = mixed", view.Rows[0]);
        Assert.Equal("Strength = 50", view.Rows[1]);
        Assert.True(Form.OffersFor(_world, a));
    }

    [Fact]
    public void D1_ACommit_IsOneSubmitPerEntity_WithTheFieldsAttribute()
    {
        var a = Unit("Alpha 1-1", 50); var b = Unit("Bravo 2", 70);
        var sent = new List<EditOp>();
        var view = new FormView { Submit = (w, op) => { sent.Add(op); return new EditTicket(Guid.NewGuid(), EntityWriteRoute.Direct); } };
        var sel = new[] { a, b };
        Frames(view, sel, 1);
        view.CommitForTest(1, _world, sel, 40);
        Assert.Equal(2, sent.Count);
        Assert.Equal(new[] { a, b }, sent.Select(o => o.Target));
        Assert.All(sent, o =>
        {
            Assert.Equal(EditKind.Write, o.Kind);
            var c = Assert.Single(o.Changes!);
            Assert.Equal(AttributeIds.HealthCurrent, c.AttributeId);
            Assert.Equal(40.0, c.Value.DoubleValue);
            Assert.Equal("Set strength", o.Label);
        });
        Assert.NotNull(Form.Fields[1].RangeProblem(150));   // out of range: refused with the reason, never clamped (§2.3)
        Assert.Null(Form.Fields[1].RangeProblem(40));
        Assert.True(Form.Fields[2].ReadOnly);              // no attribute ⇒ read-only, never written
        view.CommitForTest(2, _world, sel, 7);
        Assert.Equal(2, sent.Count);
    }

    [Fact]
    public void D1_ASteadyFrame_WithTheFormOpen_AllocatesNothing()
    {
        var a = Unit("Alpha 1-1", 50); var b = Unit("Bravo 2", 50);
        var view = new FormView { ExpertOpen = true };
        var sel = new[] { a, b };
        using var imgui = new ImGuiTestFixture();
        long drawn = 0;
        for (int f = 0; f < 12; f++)
        {
            imgui.NewFrame();
            ImGuiNET.ImGui.Begin("D1");
            long before = GC.GetAllocatedBytesForCurrentThread();
            view.Draw(Form, _world, sel, editable: true);
            if (f >= 4) drawn += GC.GetAllocatedBytesForCurrentThread() - before;   // after the first frames (the cache, the tables)
            ImGuiNET.ImGui.End();
            imgui.Render();
        }
        Assert.Equal(0, drawn);
    }
}
