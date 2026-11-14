using System;
using System.Linq;
using System.Numerics;
using Fdp.Presentation.Editing;
using Fdp.Presentation.Editing.Types;
using StructEdit.Core;
using Xunit;

namespace Fdp.Presentation.Tests.ImGui.Editing;

/// <summary>
/// ⭐ S3 (📄 docs/DESIGN_Struct_Editor.md §3, CE-3378) — ONE registration per type: the standard catalogue carries every leaf the builders used to list AND the cells only two hosts
/// passed (Quaternion, WorldRotation, Guid, Type); a table built with NO drawer dictionary draws them through it; a host overlay answers first; a registered type is a leaf in every builder.
/// 🔴 Red-proved: <c>ComponentEditDrawer.DrawPrimitiveInput</c> skipping the catalogue lookup ⇒ the counters stay 0 ⇒ the two frame tests red.
/// </summary>
public class TypeCatalogueTests
{
    public struct Pose { public Quaternion Rot; public Guid Id; public float Speed; }

    private sealed class CountingGuid : TypeEditor<Guid>
    {
        public int Draws;
        public override bool CanDraw => true;
        public override bool Draw(in TypeDrawContext ctx, ref object value) { Draws++; Assert.NotNull(ctx.Session); return false; }
    }

    public readonly struct Marker { public readonly int V; }

    private sealed class MarkerLeaf : TypeEditor<Marker>
    {
        public override StructEdit.Core.Plugins.ICustomFieldEditor? Shape => new PickableLeafFieldEditor(typeof(Marker));
    }

    [Fact]
    public void TheStandardCatalogue_DrawsTheTypesOnlyTwoHostsUsedToPass()
    {
        foreach (var t in new[] { typeof(Quaternion), typeof(Fdp.Core.WorldRotation), typeof(Guid), typeof(Type) })
            Assert.True(TypeCatalogue.Standard.TryGet(t, out var e) && e.CanDraw, t.Name + ": a cell in the standard catalogue");
    }

    [Fact]
    public void TheStandardCatalogue_KeepsEveryLeafTheBuildersListed()
    {
        var leaves = TypeCatalogue.Standard.FieldEditors.Keys.ToHashSet();
        foreach (var t in new[]
        {
            typeof(Fdp.Core.FixedString32), typeof(Fdp.Core.FixedString64), typeof(Fdp.Core.FixedString128), typeof(Quaternion), typeof(Fdp.Core.WorldRotation),
            typeof(Fdp.Toolkit.Replication.EntityRef), typeof(Fdp.Toolkit.Replication.EntityLink), typeof(Fdp.Toolkit.Replication.PointLink), typeof(Fdp.Core.WorldPoint),
            typeof(Fdp.Core.Bearing), typeof(Fdp.Toolkit.Scenario.RefKey), typeof(Fdp.Toolkit.Replication.AreaLink), typeof(Fdp.Toolkit.Utility.UtilityDecisionRef),
            typeof(Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto), typeof(Type),
            typeof(System.Collections.Generic.List<Fdp.Toolkit.ScenarioRules.Actions.ActionStep>), typeof(Guid),
        })
            Assert.Contains(t, leaves);
    }

    [Fact]
    public void ATableWithNoDrawerDictionary_DrawsACellFromTheCatalogue_AndAnOverlayAnswersFirst()
    {
        var counting = new CountingGuid();
        var types = TypeCatalogue.Standard.With(counting);
        Assert.True(types.TryGet(typeof(Quaternion), out _));   // the parent still answers
        using var session = ComponentEditServices.CreateBuilder().Build().Open(new Pose { Id = Guid.NewGuid() }, typeof(Pose));
        using (var imgui = new ImGuiTestFixture())
        {
            imgui.NewFrame();
            ImGuiNET.ImGui.Begin("S3");
            if (ImGuiNET.ImGui.BeginTable("t", 2))
            {
                var drawer = new ComponentEditDrawer(session, pickerCtx: null, types: types);
                Assert.Same(types, drawer.Catalogue);
                drawer.DrawRoot(session.Document.Root);
                ImGuiNET.ImGui.EndTable();
            }
            ImGuiNET.ImGui.End();
            imgui.Render();
        }
        Assert.Equal(1, counting.Draws);
        Assert.Same(TypeCatalogue.Standard, new ComponentEditDrawer(session, null).Catalogue);
    }

    [AttributeUsage(AttributeTargets.Field)] public sealed class StatePickAttribute : Attribute { }
    public struct Picks { [StatePick] public string State; public string Plain; }

    private sealed class CountingString : TypeEditor<string>
    {
        public int Draws;
        public override bool CanDraw => true;
        public override bool Draw(in TypeDrawContext ctx, ref object value) { Draws++; return false; }
    }

    [Fact]
    public void AFieldRule_DrawsOnlyTheFieldsCarryingItsAttribute()
    {
        var rule = new CountingString();
        var types = TypeCatalogue.Standard.With().AddRule(typeof(StatePickAttribute), rule);
        using var session = ComponentEditServices.CreateBuilder().Build().Open(new Picks { State = "a", Plain = "b" }, typeof(Picks));
        var owner = ComponentEditDrawer.FieldsOwner(session.Document.Root)!;
        Assert.True(types.TryGetFor(owner.Children.First(c => c.Name == nameof(Picks.State)), out var e) && ReferenceEquals(e, rule));
        Assert.False(types.TryGetFor(owner.Children.First(c => c.Name == nameof(Picks.Plain)), out _));
        using (var imgui = new ImGuiTestFixture())
        {
            imgui.NewFrame();
            ImGuiNET.ImGui.Begin("rule");
            if (ImGuiNET.ImGui.BeginTable("t", 2))
            {
                new ComponentEditDrawer(session, pickerCtx: null, types: types).DrawRoot(session.Document.Root);
                ImGuiNET.ImGui.EndTable();
            }
            ImGuiNET.ImGui.End();
            imgui.Render();
        }
        Assert.Equal(1, rule.Draws);
    }

    [Fact]
    public void ARegisteredType_IsALeafInEveryBuilder()
    {
        TypeCatalogue.Register(new MarkerLeaf());
        Assert.True(TypeCatalogue.Standard.FieldEditors.ContainsKey(typeof(Marker)));
        Assert.True(ComponentEditServices.CreateBuilder().Build() is not null);
    }
}
