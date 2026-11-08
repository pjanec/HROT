using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Fdp.Presentation.Editing;
using Fdp.Presentation.Fonts;
using Fdp.Presentation.Input;
using Fdp.Toolkit.Input;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Edits;
using Hrot.Presentation.Tests.Map3D;
using ImGuiNET;
using NodeEditor.Core.Widgets;
using NodeEditor.Primitives;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐ N-6 (CE-3383, docs/DESIGN_Struct_Editor.md §11a) — the DEFINE TABLE (<see cref="FieldTable"/>) in a REAL ImGui frame (real fonts, Xvfb; a skip is not a pass), over an in-memory <see cref="IFieldDeclarations"/> with the scenario-parameters
/// settings: Add (a free name, the first choice's type) · inline rename (a refusal draws its reason under the row and KEEPS the typed text) · retype through the ONE picker (the structure group greyed with its reason) · a default edited in the
/// value cell lands as JSON through <c>SetValueJson</c> and one <see cref="EditHistory"/> step undoes it · Delete refused while used · F2 / Del / Alt+↓ through the keymap · drag ⋮⋮ reorder · 0 B on a steady frame.
/// Render (never in git): <c>HROT_RAIL_SHOTS=&lt;dir&gt;</c> ⇒ <c>fieldtable-*.png</c>.
/// 🔴 Red-proofs (compile): <c>FieldTable.Exec</c> Op.Rename success path not clearing <c>_renameIdx</c> / the refusal path dropping <c>_renameFocus</c>; <c>FieldTable.Draw</c> not setting <c>_builtVersion</c> after an own write.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class FieldTableFrameRail
{
    private const ImGuiWindowFlags Bare = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings;

    public enum Weather { Clear, Rain, Fog }

    /// <summary>An in-memory declaration list with ONE undo history — what a real source (the scenario header) is, minus the world.</summary>
    internal sealed class Decls : IFieldDeclarations
    {
        public readonly List<FieldRow> List = new();
        public readonly Dictionary<string, int> Used = new();
        public readonly EditHistory History = new();
        public int Ver;
        public string? Refusal;
        public bool Ro;
        public int Calls;

        private static readonly (string Id, Type Type)[] Known = { ("int", typeof(int)), ("float", typeof(float)), ("bool", typeof(bool)), ("string", typeof(string)), ("Weather", typeof(Weather)) };
        public static Type? Resolve(string id) { foreach (var (i, t) in Known) if (i == id) return t; return null; }
        public static TypeChoice[] Choices() => Known.Select(k => new TypeChoice(k.Id, k.Type, k.Type.IsEnum ? TypeGroup.Enums : TypeGroup.Values)).ToArray();

        public IReadOnlyList<FieldRow> Rows => List.ToArray();
        public int Version => Ver;
        public bool IsReadOnly => Ro;
        public string? LastRefusal => Refusal;
        public int UsedBy(string name) => Used.TryGetValue(name, out var n) ? n : 0;

        private void Replace(List<FieldRow> next) { List.Clear(); List.AddRange(next); Ver++; }
        private void Do(string label, List<FieldRow> after) { var before = List.ToList(); History.Do(label, () => Replace(after), () => Replace(before)); }

        private string? NameProblem(string name)
        {
            if (!Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$")) return "Name must contain only letters, digits, or underscores.";
            if (List.Any(r => r.Name == name)) return $"A variable named '{name}' already exists.";
            return null;
        }

        public bool Add(string name, string typeId, string? valueJson)
        {
            Calls++; Refusal = null;
            if (NameProblem(name) is { } why) { Refusal = why; return false; }
            var t = Resolve(typeId); if (t is null) { Refusal = "unknown type"; return false; }
            Do("Add " + name, List.Append(new FieldRow(name, typeId, t, valueJson, null, false)).ToList());
            return true;
        }

        public bool Rename(string name, string newName)
        {
            Calls++; Refusal = null;
            if (NameProblem(newName) is { } why) { Refusal = "Rename refused: " + why; return false; }
            Do("Rename " + name, List.Select(r => r.Name == name ? r with { Name = newName } : r).ToList());
            return true;
        }

        public bool Retype(string name, string typeId)
        {
            Calls++; Refusal = null;
            var t = Resolve(typeId); if (t is null) { Refusal = "unknown type"; return false; }
            Do("Retype " + name, List.Select(r => r.Name == name ? r with { TypeId = typeId, ClrType = t, ValueJson = null } : r).ToList());
            return true;
        }

        public bool Move(int from, int to)
        {
            Calls++; Refusal = null;
            var l = List.ToList(); var item = l[from]; l.RemoveAt(from); l.Insert(to, item);
            Do("Move " + item.Name, l);
            return true;
        }

        public bool Remove(string name)
        {
            Calls++; Refusal = null;
            if (UsedBy(name) is > 0 and var n) { Refusal = $"'{name}' is still bound by {n} field(s). Unbind them first."; return false; }
            Do("Delete " + name, List.Where(r => r.Name != name).ToList());
            return true;
        }

        public void SetValueJson(string name, string? valueJson) { Calls++; Refusal = null; Do("Edit " + name, List.Select(r => r.Name == name ? r with { ValueJson = valueJson } : r).ToList()); }
        public string Names() => string.Join(",", List.Select(r => r.Name));
    }

    internal sealed class Rig : IDisposable
    {
        public readonly UiFrameSession Frame;
        public readonly WidgetKitFrameRail.Sim Sim = new();
        public readonly Decls D = new();
        public readonly FieldTable Table = new();
        public FieldTableSettings Settings;
        public readonly ListFormsFrameRail.Pressing Input = new();
        public readonly KeyRouter Router;
        public readonly ListFormsFrameRail.Keyed Window = new();
        public bool Route;
        public long Bytes = -1;

        public Rig(bool structsOff = true)
        {
            Frame = UiFrameHarness.Begin(760, 520);
            new EditorFontService().Initialize(1f, 1f);
            Router = new KeyRouter(Input);
            ImGui.GetIO().MouseDoubleClickTime = 5f;
            Settings = new FieldTableSettings
            {
                AddLabel = "Add parameter", NewNameStem = "param", ValueHeader = "Default", UsedBy = true,
                AllowStructs = !structsOff, AllowLists = !structsOff, StructsOffReason = "structures and lists are off for scenario parameters", ListsOffReason = "structures and lists are off for scenario parameters",
                TypeChoices = Decls.Choices(),
            };
        }

        public void Draw()
        {
            Sim.Feed();
            ImGui.SetNextWindowPos(new Vector2(10, 10)); ImGui.SetNextWindowSize(new Vector2(740, 480));
            ImGui.Begin("##fieldtable", Bare);
            long before = GC.GetAllocatedBytesForCurrentThread();
            Table.Draw(D, Settings, null);
            Bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            if (Route) Router.Dispatch(new KeyFrame(false, false, Window));
            ImGui.End();
        }

        /// <summary>Draws until STEADY (R-2084 is about optimised code: tier-0 JIT boxes — the warm-up sleeps let the runtime promote hot methods).</summary>
        public void Steady()
        {
            for (int k = 0; k < 3; k++) { Frame.StepN(15, Draw); System.Threading.Thread.Sleep(220); }
            Frame.StepN(5, Draw);
        }
        public void Frames(int n = 4) => Frame.StepN(n, Draw);
        public void Click(Vector2 p, bool right = false) => Sim.Click(Frame, Draw, p, right);
        public void DoubleClick(Vector2 p) { Click(p); Click(p); }
        public void Type(string text) { ImGui.GetIO().AddInputCharactersUTF8(text); Frame.StepN(4, Draw); }
        public void Chord(ImGuiKey mod, ImGuiKey key)
        {
            Frame.Step(() => { ImGui.GetIO().AddKeyEvent(mod, true); ImGui.GetIO().AddKeyEvent(key, true); Draw(); });
            Frame.Step(() => { ImGui.GetIO().AddKeyEvent(key, false); ImGui.GetIO().AddKeyEvent(mod, false); Draw(); });
            Frames(3);
        }
        public void Key(ImGuiKey key)
        {
            Frame.Step(() => { ImGui.GetIO().AddKeyEvent(key, true); Draw(); });
            Frame.Step(() => { ImGui.GetIO().AddKeyEvent(key, false); Draw(); });
            Frames(3);
        }
        public void Press(EditorKey key, KeyModifiers mods)
        {
            Input.Key = key; Input.Modifiers = mods; Route = true;
            Frame.Step(Draw);
            Input.Key = EditorKey.F12; Input.Modifiers = KeyModifiers.None;
            Frame.StepN(4, Draw);
            Route = false;
        }
        public Vector2 NameCenter(string name) { var c = Table.NameCells.Single(x => x.Name == name); return (c.Min + c.Max) / 2f; }
        public void Rename(string name, string typed)
        {
            DoubleClick(NameCenter(name));
            Frames(3);
            Chord(ImGuiKey.ModCtrl, ImGuiKey.A);
            Type(typed);
            Key(ImGuiKey.Enter);
        }
        public void Dispose() => Frame.Dispose();
    }

    private static void Shot(Rig r, string name)
    {
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) r.Frame.Screenshot(System.IO.Path.Combine(dir, name));
    }

    private static Rig Seeded(bool structsOff = true)
    {
        var r = new Rig(structsOff);
        r.D.Add("enemyStrength", "int", "3"); r.D.Add("weather", "Weather", "\"Rain\""); r.D.Add("callsign", "string", null);
        r.D.History.Clear();
        r.D.Used["enemyStrength"] = 4; r.D.Used["weather"] = 2;
        return r;
    }

    [SkippableFact]
    public void Add_MakesAFreeNameOfTheFirstChoicesType_TheRowAppears_AndASteadyFrameAllocatesNothing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var r = Seeded();
        r.Steady();
        Shot(r, "fieldtable-c.png");
        Assert.Equal(new[] { "Name", "Type", "Default", "Used by" }, r.Table.ColumnHeaders);
        Assert.Equal(4, r.Table.UsedByDrawn["enemyStrength"]);
        Assert.True(r.Bytes == 0, $"a steady Define-table frame allocates nothing ({r.Bytes} bytes)");
        r.Click((r.Table.AddButton.Min + r.Table.AddButton.Max) / 2f);
        r.Frames(4);
        Assert.Equal("enemyStrength,weather,callsign,param1", r.D.Names());
        Assert.Equal("int", r.D.List[3].TypeId);                                  // the first choice
        Assert.Contains(r.Table.NameCells, c => c.Name == "param1");               // and the table shows it
        r.Click((r.Table.AddButton.Min + r.Table.AddButton.Max) / 2f); r.Frames(4);
        Assert.Equal("param2", r.D.List[4].Name);                                  // free: never a name that is taken
        r.Steady();
        Assert.True(r.Bytes == 0, $"still nothing per frame with five rows ({r.Bytes} bytes)");
    }

    [SkippableFact]
    public void ARefusedRename_DrawsItsReasonUnderTheRow_KeepsTheTypedText_AndAValidOneThenSticks()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var r = Seeded();
        r.Steady();
        r.Rename("weather", "my side");
        Assert.Equal("enemyStrength,weather,callsign", r.D.Names());               // nothing renamed
        var err = r.Table.Error;
        Assert.NotNull(err);
        Assert.Equal("weather", err!.Value.Row);
        Assert.Contains("letters, digits", err.Value.Text);
        Assert.Equal("weather", r.Table.RenamingName);                             // the box is still open …
        Assert.Equal("my side", r.Table.RenameText);                               // … with what was typed
        Shot(r, "fieldtable-a-error.png");
        r.Frames(3);
        r.Chord(ImGuiKey.ModCtrl, ImGuiKey.A);
        r.Type("climate");
        r.Key(ImGuiKey.Enter);
        Assert.Equal("enemyStrength,climate,callsign", r.D.Names());
        Assert.Null(r.Table.Error);
        Assert.Null(r.Table.RenamingName);
        Assert.True(r.D.History.CanUndo);
        r.D.History.Undo(); r.Frames(3);
        Assert.Equal("enemyStrength,weather,callsign", r.D.Names());               // ONE undo step
    }

    [SkippableFact]
    public void ADuplicateNameIsRefusedToo()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var r = Seeded();
        r.Steady();
        r.Rename("weather", "callsign");
        Assert.Contains("already exists", r.Table.Error!.Value.Text);
        Assert.Equal("enemyStrength,weather,callsign", r.D.Names());
    }

    [SkippableFact]
    public void F2StartsARename_AndEscapeKeepsTheNameWithoutAskingTheSource()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        {
        using var r2 = Seeded();
        r2.Steady();
        int calls = r2.D.Calls;
        r2.Click(r2.NameCenter("callsign")); r2.Frames(2);
        r2.Press(EditorKey.F2, KeyModifiers.None);                                 // the keymap's rename command
        Assert.Equal("callsign", r2.Table.RenamingName);
        r2.Key(ImGuiKey.Escape);
        Assert.Null(r2.Table.RenamingName);
        Assert.Equal(calls, r2.D.Calls);
        Assert.Null(r2.Table.Error);
        }
    }

    [SkippableFact]
    public void TheTypePicker_Retypes_AndTheStructureGroupIsGreyedWithItsReasonForTheParametersSettings()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var r = Seeded();
        r.Steady();
        var t = r.Table.TypeCell("enemyStrength");
        r.Click((t.Min + t.Max) / 2f); r.Frames(4);
        Assert.Equal("enemyStrength", r.Table.PickerRow);
        var (cell, reason) = r.Table.PickerGroup(TypeGroup.Structures);
        Assert.Equal("structures and lists are off for scenario parameters", reason);
        Assert.NotEqual(default, cell);                                            // the group is listed — greyed, not hidden
        Assert.Equal("structures and lists are off for scenario parameters", r.Table.PickerGroup(TypeGroup.Lists).Reason);
        Shot(r, "fieldtable-picker.png");
        r.Click((cell.Min + cell.Max) / 2f); r.Frames(3);                          // a disabled row refuses the click
        Assert.Equal("int", r.D.List[0].TypeId);
        var f = r.Table.PickerChoice("float");
        Assert.NotEqual(default, f);
        r.Click((f.Min + f.Max) / 2f); r.Frames(4);
        Assert.Equal("float", r.D.List[0].TypeId);
        Assert.Null(r.D.List[0].ValueJson);                                        // a value of the old type is dropped
        Assert.Null(r.Table.PickerRow);                                            // the picker closed
        r.D.History.Undo(); r.Frames(3);
        Assert.Equal("int", r.D.List[0].TypeId);
        Assert.Equal("3", r.D.List[0].ValueJson);
    }

    [SkippableFact]
    public void ADefaultEditedInTheValueCell_LandsAsJsonThroughSetValueJson_AndOneUndoStepRestoresIt()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var r = Seeded();
        r.Steady();
        Assert.Equal("3", r.D.List[0].ValueJson);
        var first = r.Table.ValueNode("enemyStrength");
        Assert.NotNull(first);
        Assert.Equal(typeof(int), first!.ClrType);
        var name = r.Table.NameCells.Single(c => c.Name == "enemyStrength");
        var valueAt = new Vector2(r.Table.TypeCell("enemyStrength").Max.X + 60f, (name.Min.Y + name.Max.Y) / 2f);   // inside the value column of that row
        r.Click(valueAt); r.Frames(3);
        r.Chord(ImGuiKey.ModCtrl, ImGuiKey.A);
        r.Type("7");
        r.Key(ImGuiKey.Enter);
        Assert.Equal("7", r.D.List[0].ValueJson);                                  // written as the member's JSON
        int calls = r.D.Calls;
        r.Frames(6);
        Assert.Equal(calls, r.D.Calls);                                            // a steady frame asks the source for nothing
        Shot(r, "fieldtable-c-edit.png");
        Assert.True(r.D.History.CanUndo);
        r.D.History.Undo(); r.Frames(4);
        Assert.Equal("3", r.D.List[0].ValueJson);                                  // ONE undo step back
        Assert.Equal(3, Convert.ToInt32(r.Table.ValueNode("enemyStrength")!.Binding!.GetBoxed()));   // and the cell shows it again (the table rebuilt on the Version)
    }

    [SkippableFact]
    public void DeleteIsRefusedWhileUsed_NamingItUnderTheRow_AndRunsWhenFree_AndTheKeysMoveAndDuplicate()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var r = Seeded();
        r.Steady();
        r.Click(r.NameCenter("weather")); r.Frames(2);                              // select (the window takes the focus)
        r.Press(EditorKey.Delete, KeyModifiers.None);
        Assert.Equal("enemyStrength,weather,callsign", r.D.Names());                // used by 2 ⇒ refused …
        Assert.Equal("weather", r.Table.Error!.Value.Row);                          // … and says why under its row
        Assert.Equal("'weather' is still bound by 2 field(s). Unbind them first.", r.Table.Error!.Value.Text);
        Shot(r, "fieldtable-delete-refused.png");
        r.Click(r.NameCenter("callsign")); r.Frames(2);
        r.Press(EditorKey.Down, KeyModifiers.Alt);                                  // Alt+↓ on the last row: nothing to move
        Assert.Equal("enemyStrength,weather,callsign", r.D.Names());
        r.Click(r.NameCenter("weather")); r.Frames(2);
        r.Press(EditorKey.Down, KeyModifiers.Alt);
        Assert.Equal("enemyStrength,callsign,weather", r.D.Names());
        r.Press(EditorKey.D, KeyModifiers.Ctrl);                                    // Ctrl+D duplicates the selected row (free name, same type and value)
        Assert.Equal("enemyStrength,callsign,weather,weather1", r.D.Names());
        Assert.Equal("\"Rain\"", r.D.List[3].ValueJson);
        r.Click(r.NameCenter("callsign")); r.Frames(2);
        r.Press(EditorKey.Delete, KeyModifiers.None);                               // free ⇒ gone
        Assert.Equal("enemyStrength,weather,weather1", r.D.Names());
        Assert.Null(r.Table.Error);
    }

    [SkippableFact]
    public void DraggingTheGripReorders_AndAReadOnlySourceDrawsButEditsNothing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var r = Seeded();
        r.Steady();
        var a = r.Table.NameCells.Single(c => c.Name == "enemyStrength"); var b = r.Table.NameCells.Single(c => c.Name == "callsign");
        var grip = new Vector2(a.Min.X - 8f, (a.Min.Y + a.Max.Y) / 2f);
        r.Sim.Drag(r.Frame, r.Draw, grip, (b.Min + b.Max) / 2f);
        r.Frames(3);
        Assert.Equal("weather,callsign,enemyStrength", r.D.Names());                // the dragged row lands on the target's place
        r.D.Ro = true; r.Frames(3);
        int calls = r.D.Calls;
        r.DoubleClick(r.NameCenter("weather")); r.Frames(3);
        Assert.Null(r.Table.RenamingName);
        r.Click((r.Table.AddButton.Min + r.Table.AddButton.Max) / 2f); r.Frames(3);
        Assert.Equal(calls, r.D.Calls);
    }

    /// <summary>⭐ CE-3384 — FILL mode (the macro args, a run setup): names and types are fixed — no grip, no ⋮, no Add, no rename; the value column carries the defaults (grey = default, amber + ↺ = overridden); ↺ writes the default; 0 B.</summary>
    [SkippableFact]
    public void Fill_HasNoStructureControls_ShowsDefaultsAndOverrides_AndAllocatesNothing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var r = new Rig();
        r.Settings = new FieldTableSettings { Fill = true, ShowDefaults = true, ValueHeader = "Value" };
        r.D.List.Add(new FieldRow("speed", "int", typeof(int), "9", null, false, "3"));       // overridden
        r.D.List.Add(new FieldRow("limit", "int", typeof(int), "5", null, false, "5"));       // at its default
        r.D.Ver++;
        r.Steady();
        Assert.True(r.Bytes == 0, $"a steady Fill frame allocates nothing ({r.Bytes} bytes)");
        Assert.Equal(default, r.Table.AddButton);                                              // no Add
        Assert.Equal(default, r.Table.MenuButton("speed"));                                    // no ⋮
        Assert.True(r.Table.Drawer!.IsChanged(r.Table.ValueNode("speed")!)); Assert.False(r.Table.Drawer.IsChanged(r.Table.ValueNode("limit")!));
        r.DoubleClick(r.NameCenter("speed")); r.Frames(3);
        Assert.Null(r.Table.RenamingName);                                                      // names are fixed
        Assert.True(KitProbe.TryGet(KitProbe.ResetMark, out var a, out var b));
        r.Click((a + b) / 2f); r.Frames(3);
        Assert.Equal("3", r.D.List[0].ValueJson);                                               // ↺ wrote the default through SetValueJson
        Shot(r, "fieldtable-fill.png");
    }
}
