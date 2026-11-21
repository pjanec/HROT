using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Hrot.AiEditor.Persistence;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Windows;
using Hrot.Editor.UiFrameRail;
using ImGuiNET;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Blackboard.Frame;

/// <summary>
/// ⭐ CE-3401 (docs/DESIGN_Struct_Editor.md §12a) — the BTree / HSM blackboard panel (<see cref="VariablesPanelHost"/>) in a REAL frame: the ONE Define table over the declarations (no byte column, no memory line, D11), the Live column,
/// the node-owned allocations listed AROUND the table (never in it), the reserved Params row's fields as read-only sub-rows, the alias drop target in the table's trailing cell, Remove unused; a rename goes through the refactor service, a reorder is
/// translated to the source's own indices; 0 B on a steady frame. A skip is not a pass (Xvfb). Render (never in git): <c>HROT_RAIL_SHOTS=&lt;dir&gt;</c> ⇒ <c>blackboard-*.png</c>.
/// 🔴 Red-proofs (compile): <c>BTreeHsmSchemaSource.Update</c> bumping the revision every call (⇒ 0 B fails) · <c>BlackboardDeclarations</c> built without <c>hideAutoManaged</c> (⇒ the node-owned row is in the table) · <c>VariablesPanelHost.AcceptAlias</c>
/// without the type check · <c>BlackboardDeclarations.Rename</c> not calling the refactor service · <c>SchemaSourceDeclarations.Move</c> without the index translation.
/// </summary>
[Collection(Hrot.Editor.AiShared.Tests.PanelSnapshotTestCollection.Name)]
public sealed class TheBlackboardPanelRendersTests
{
    private const ImGuiWindowFlags Bare = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings;

    private sealed class RecordingAsset : TheParamsPanelRendersTests.ParamsAsset
    {
        public readonly List<string> Calls = new();
        public readonly List<(string Var, BlackboardAliasBinding Binding)> Aliases = new();
        public override void AddVariable(BlackboardVariableEntry entry) { Calls.Add("add:" + entry.Name + ":" + entry.FieldType.Name); base.AddVariable(entry); }
        public override void RenameVariable(string o, string n) { Calls.Add("rename:" + o + ">" + n); base.RenameVariable(o, n); }
        public override void MoveVariable(int src, int dst) { Calls.Add($"move:{src}>{dst}"); base.MoveVariable(src, dst); }
        public override void RemoveVariables(IReadOnlyList<string> names) { Calls.Add("remove:" + string.Join(",", names)); base.RemoveVariables(names); }
        public override void AddAlias(string v, BlackboardAliasBinding b) { Aliases.Add((v, b)); }
    }

    private sealed class RecordingRefactor : TheParamsPanelRendersTests.NoRefactor
    {
        public readonly List<(string From, string To)> Previews = new();
        public int Applied;
        public override RefactorPreview PreviewRename(string fromKey, string toKey, RefactorOptions options) { Previews.Add((fromKey, toKey)); return base.PreviewRename(fromKey, toKey, options); }
        public override RefactorResult ApplyRename(RefactorPreview preview) { Applied++; return base.ApplyRename(preview); }
    }

    private sealed class Rig : IDisposable
    {
        public readonly UiFrameSession Frame = UiFrameHarness.Begin(760, 560);
        public readonly RecordingAsset Asset = new();
        public readonly RecordingRefactor Refactor = new();
        public VariablesPanelHost Host = null!;
        public BlackboardWindowViewModel Vm = null!;
        public bool ReadOnly, Aliasing = true;
        public Dictionary<string, string>? Live;
        public long Bytes = -1;
        private Vector2 _pos = new(-100, -100);
        private bool _left;

        public Rig(IEnumerable<BlackboardVariableEntry> vars, IReadOnlyList<UnboundRequirementViewModel>? unbound = null, string? paramsDto = null)
        {
            new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
            ImGui.GetIO().MouseDoubleClickTime = 5f;
            Asset.ParamsDtoTypeId = paramsDto;
            Asset.CurrentParamsDefaults = paramsDto is null ? null : "{\"Rounds\":3}";
            Asset.SetBlackboardVariables(vars);
            Vm = BlackboardAuthoringWindow.BuildViewModel(Asset);
            if (unbound != null) Vm = Vm with { UnboundRequirements = unbound };
            Host = new VariablesPanelHost(Refactor, Asset, Vm.KnownTypeNames);
        }

        public void Draw()
        {
            var io = ImGui.GetIO();
            if (_pos.X >= 0) Raylib_cs.Raylib.SetMousePosition((int)_pos.X, (int)_pos.Y);   // rlImGui re-reads the REAL pointer every frame
            io.AddMousePosEvent(_pos.X, _pos.Y);
            io.AddMouseButtonEvent(0, _left);
            ImGui.SetNextWindowPos(new Vector2(10, 10)); ImGui.SetNextWindowSize(new Vector2(740, 540));
            ImGui.Begin("##blackboard", Bare);
            long before = GC.GetAllocatedBytesForCurrentThread();
            Host.Draw(Vm, ReadOnly, Aliasing, null, Live);
            Bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            ImGui.End();
        }

        /// <summary>Draws until STEADY (R-2084 is about optimised code: tier-0 JIT boxes — the sleeps let the runtime promote hot methods).</summary>
        public void Steady()
        {
            for (int k = 0; k < 3; k++) { Frame.StepN(15, Draw); System.Threading.Thread.Sleep(220); }
            Frame.StepN(5, Draw);
        }
        public void Frames(int n = 4) => Frame.StepN(n, Draw);
        public void Click(Vector2 p)
        {
            _pos = p; _left = false; Frame.StepN(2, Draw);
            _left = true; Frame.Step(Draw);
            _left = false; Frame.StepN(3, Draw);
        }
        public void Drag(Vector2 from, Vector2 to)
        {
            _pos = from; _left = false; Frame.StepN(2, Draw);
            _left = true; Frame.StepN(2, Draw);
            _pos = (from + to) * 0.5f; Frame.StepN(2, Draw);
            _pos = to; Frame.StepN(3, Draw);
            _left = false; Frame.StepN(3, Draw);
        }
        public static Vector2 Mid((Vector2 Min, Vector2 Max) r) => (r.Min + r.Max) / 2f;
        public void Shot(string name)
        {
            if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) Frame.Screenshot(Path.Combine(dir, name));
        }
        public void Dispose() => Frame.Dispose();
    }

    private static BlackboardVariableEntry[] Seed() => new[]
    {
        new BlackboardVariableEntry("Params", typeof(TheParamsPanelRendersTests.RenderedParams), null, DefaultValueJson: "{\"Rounds\":3}", IsReservedParams: true),
        new BlackboardVariableEntry("Counter", typeof(int), "a plain variable"),
        new BlackboardVariableEntry("Spare", typeof(float), null),
        new BlackboardVariableEntry("_auto_n1", typeof(float), null, IsAutoManaged: true),
    };

    /// <summary>The panel as the user sees it: Name · Type · Default · Live (no Bytes), the Live text per row, the node-owned row listed under the table and NOT in it, the Params row's fields as sub-rows, Remove unused offered; 0 B steady.</summary>
    [SkippableFact]
    public void CE3401_TheBlackboard_IsOneDefineTable_WithLive_NodeOwnedAround_AndParamsSubRows_AllocatingNothing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var r = new Rig(Seed());
        r.Live = new Dictionary<string, string> { ["Counter"] = "42" };
        r.Steady();

        Assert.Equal(new[] { "Name", "Type", "Default", "Live" }, r.Host.Table.ColumnHeaders);              // no Bytes (D11)
        Assert.Equal("42", r.Host.Table.LiveShown("Counter"));
        Assert.Null(r.Host.Table.LiveShown("Spare"));                                                       // no answer ⇒ "—"
        var names = r.Host.Table.NameCells.Select(c => c.Name).ToArray();
        Assert.Equal(new[] { "Params", "Counter", "Spare" }, names);                                        // the node-owned row is NOT a table row …
        Assert.Equal(1, r.Host.OwnedListed);                                                                // … it is listed around the table
        Assert.True(r.Host.Table.SubRowsOpen("Params"));                                                    // the DTO's fields, read-only, under the reserved row
        Assert.True(r.Host.RemoveUnusedDrawn);                                                              // Counter / Spare are used by nothing
        Assert.True(r.Bytes == 0, $"a steady frame allocates nothing ({r.Bytes} bytes)");
        r.Shot("blackboard-panel.png");
    }

    /// <summary>A behaviour that NAMES its params DTO: the chooser (preview text) and the defaults form under it draw in a steady frame without allocating — the ids and texts are built when the DTO changes, not every frame (R-2084).
    /// 🔴 Red-proof: <c>ParamsDtoChooser.Draw</c> building its combo id / preview text each frame ⇒ bytes.</summary>
    [SkippableFact]
    public void CE3401_TheParamsChooserAndDefaultsForm_AllocateNothingInASteadyFrame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var r = new Rig(Seed(), paramsDto: typeof(TheParamsPanelRendersTests.RenderedParams).FullName);
        r.Steady();
        Assert.True(r.Bytes == 0, $"a steady frame with the params chooser and defaults allocates nothing ({r.Bytes} bytes)");
        r.Shot("blackboard-params.png");
    }

    /// <summary>A read-only blackboard (a code-built graph): no Remove unused, and the table's Add does nothing; a writable one adds a row of the first type choice.</summary>
    [SkippableFact]
    public void CE3401_AddIsRefused_OnAReadOnlyBlackboard_AndWorksOnAWritableOne()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var r = new Rig(new[] { new BlackboardVariableEntry("Counter", typeof(int), null) });
        r.ReadOnly = true;
        r.Steady();
        Assert.False(r.Host.RemoveUnusedDrawn);
        r.Click(Rig.Mid(r.Host.Table.AddButton)); r.Frames(3);
        Assert.DoesNotContain(r.Asset.Calls, c => c.StartsWith("add:"));                                    // refused
        r.ReadOnly = false; r.Frames(4);
        r.Click(Rig.Mid(r.Host.Table.AddButton)); r.Frames(3);
        Assert.Contains(r.Asset.Calls, c => c.StartsWith("add:variable1:"));                                // a free name, the first choice's type
    }

    /// <summary>The alias drop target is the row's trailing cell: dragging an unbound requirement of the row's type onto it adds the alias; a requirement of another type, or a drop on the read-only Params row, does not.
    /// 🔴 Red-proof: <c>AcceptAlias</c> without <c>IsAliasDropAccepted</c> ⇒ the mismatched drop lands.</summary>
    [SkippableFact]
    public void CE3401_TheAliasDropTarget_IsTheRowsTrailingCell_AcceptsOnlyTheRowsType()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var good = new UnboundRequirementViewModel("Single", "Shoot_BT > Action#7", Guid.NewGuid(), Guid.NewGuid(), typeof(float), "Shoot_BT");
        var bad = new UnboundRequirementViewModel("Int32", "Move_BT > Action#2", Guid.NewGuid(), Guid.NewGuid(), typeof(double), "Move_BT");
        using var r = new Rig(Seed(), new[] { good, bad });
        r.Steady();
        Assert.Equal(2, r.Host.UnboundDrawn);
        var spare = Rig.Mid(r.Host.Table.TailCell("Spare")) + new Vector2(30f, 0f);

        r.Drag(Rig.Mid(r.Host.UnboundHandle(1)), spare);                                                    // a double onto a float row
        Assert.Empty(r.Asset.Aliases);
        r.Drag(Rig.Mid(r.Host.UnboundHandle(0)), Rig.Mid(r.Host.Table.TailCell("Params")) + new Vector2(30f, 0f));   // the read-only Params row
        Assert.Empty(r.Asset.Aliases);
        r.Drag(Rig.Mid(r.Host.UnboundHandle(0)), spare);                                                    // a float onto a float row
        var (var, binding) = Assert.Single(r.Asset.Aliases);
        Assert.Equal("Spare", var);
        Assert.Equal(good.RequiringAssetId, binding.RequiringAssetId);
        r.Shot("blackboard-alias.png");
    }

    /// <summary>A rename goes through the refactor service FIRST (references in other assets move with it), then the asset; a refused name never reaches either.
    /// 🔴 Red-proof: <c>BlackboardDeclarations.Rename</c> without the refactor call ⇒ the first assertion fails.</summary>
    [Fact]
    public void CE3401_ARename_GoesThroughTheRefactorService_AndARefusedNameNeitherDoes()
    {
        var r = new RenameRig();
        Assert.True(r.Decls.Rename("Counter", "Renamed"));
        Assert.Single(r.Refactor.Previews);
        Assert.EndsWith("::Counter", r.Refactor.Previews[0].From);
        Assert.EndsWith("::Renamed", r.Refactor.Previews[0].To);
        Assert.Equal(1, r.Refactor.Applied);
        Assert.Contains("rename:Counter>Renamed", r.Asset.Calls);

        r.Asset.Calls.Clear(); r.Refactor.Previews.Clear();
        Assert.False(r.Decls.Rename("Spare", "class"));                                                     // a keyword
        Assert.Empty(r.Refactor.Previews);
        Assert.DoesNotContain(r.Asset.Calls, c => c.StartsWith("rename:"));
        Assert.False(string.IsNullOrEmpty(r.Decls.LastRefusal));
    }

    /// <summary>The table lists the hand-authored rows only, so its reorder indices are translated to the blackboard's own (a node-owned row may sit between them).
    /// 🔴 Red-proof: <c>SchemaSourceDeclarations.Move</c> passing the table's indices through ⇒ <c>move:0&gt;1</c>.</summary>
    [Fact]
    public void CE3401_AReorder_IsTranslatedToTheBlackboardsIndices()
    {
        var r = new RenameRig(new[]
        {
            new BlackboardVariableEntry("_auto_n0", typeof(float), null, IsAutoManaged: true),
            new BlackboardVariableEntry("A", typeof(int), null),
            new BlackboardVariableEntry("_auto_n1", typeof(float), null, IsAutoManaged: true),
            new BlackboardVariableEntry("B", typeof(int), null),
        });
        Assert.Equal(new[] { "A", "B" }, r.Decls.Rows.Select(x => x.Name).ToArray());
        Assert.True(r.Decls.Move(0, 1));
        Assert.Contains("move:1>3", r.Asset.Calls);
    }

    private sealed class RenameRig
    {
        public readonly RecordingAsset Asset = new();
        public readonly RecordingRefactor Refactor = new();
        public readonly BlackboardDeclarations Decls;
        public RenameRig(BlackboardVariableEntry[]? vars = null)
        {
            Asset.SetBlackboardVariables(vars ?? new[] { new BlackboardVariableEntry("Counter", typeof(int), null), new BlackboardVariableEntry("Spare", typeof(float), null) });
            var vm = BlackboardAuthoringWindow.BuildViewModel(Asset);
            var src = new BTreeHsmSchemaSource(Asset, vm, false);
            src.Update(vm, false);
            Decls = new BlackboardDeclarations(src, Refactor, Asset, vm.KnownTypeNames);
        }
    }
}
