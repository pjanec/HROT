using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Hrot.AiEditor.Persistence;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐⭐ <b>Batch 90 — the Details Value column goes live *(<c>BP-334</c>)*.</b>
///
/// <para>🔴🔴 <b>The defect.</b> 📐 <c>grep -rn "readRaw:" --include=*.cs Hrot/ | grep -v Tests</c> →
/// <b>NOTHING</b>. Three production sites build the Details row sources and <b>not one passed a
/// reader</b>, so both sources spelled the same honest output: <c>HasEverBeenWritten: false</c> ⇒
/// <b>every Value cell read <c>(pending)</c></b>. ⚠ <c>88a</c> made the standalone <i>Blackboard
/// Variables</i> window live on Blueprint and Details stayed dark, because they are <b>TWO
/// SEAMS</b>.</para>
///
/// <para>⭐⭐⭐ <b>What these rails ASK — the CELL TEXT the control would draw.</b> 📌 Batch 88's own
/// words: <i>"a rail on the provider's return value proves NOTHING."</i> ⇒ ⛔ nothing below asserts a
/// dictionary; every assertion runs the row through <see cref="VariableValueFormatter"/>, which is the
/// object the control calls. ⭐ <b>And the TOOLTIP is asserted beside the cell</b> — a Value cell that
/// is live while its tooltip says <c>(pending)</c> is worse than neither being live.</para>
///
/// <para>⚠ <b>What they cannot see</b>, stated: that ImGui paints the string. 📌 <c>R-21</c>/<c>R-62</c>
/// — no visual checks. They prove the text is produced.</para>
/// </summary>
public sealed class TheValueColumnGoesLiveTests
{
    private static readonly Guid AssetId = Guid.NewGuid();

    private static VariableValueFormatter Formatter() => new(RawValueDecoder.Instance);

    private struct Pair { public int A; public int B; }

    // ══ 90a — the OBJECT arm, through the formatter ══════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE rail.</b> A row carrying an already-decoded value renders that value —
    /// 🔴 RED before this batch, when no object arm existed and the cell read <c>(pending)</c>.
    /// </summary>
    [Fact]
    public void AnObjectArmRowRendersItsLiveValue()
    {
        var row = Row("Health", typeof(int), live: 7);

        Assert.Equal("7", Formatter().Cell(row));
    }

    /// <summary>
    /// ⭐⭐ <b>The formatter keeps NOTATION.</b> 📌 This is why the arm carries an <c>object</c> and not
    /// a <c>string</c>: a struct renders in the formatter's own <c>{A=1, B=2}</c> shape, ⛔ not in
    /// whatever shape a provider chose. A string arm would be two notations for one value —
    /// <c>BP-01</c>/<c>C8</c>.
    /// </summary>
    [Fact]
    public void AStructThroughTheObjectArmUsesTheFormattersNotation()
    {
        var row = Row("Wave", typeof(Pair), live: new Pair { A = 1, B = 2 });

        Assert.Equal("{A=1, B=2}", Formatter().Cell(row));
    }

    /// <summary>
    /// ⭐⭐ <b>The TOOLTIP agrees with the cell</b> — same value, richer layout. ⛔ Asserted because both
    /// funnel through ONE <c>Decode</c>: if a future change added a second entry point for the arm,
    /// this is what would catch it.
    /// </summary>
    [Fact]
    public void TheTooltipAgreesWithTheCell()
    {
        var row = Row("Wave", typeof(Pair), live: new Pair { A = 1, B = 2 });
        var f   = Formatter();

        Assert.Equal("{A=1, B=2}", f.Cell(row));
        Assert.Equal("A = 1\nB = 2", f.Tooltip(row).Replace("\r\n", "\n"));
    }

    /// <summary>
    /// ⭐⭐ <b>The object arm needs no <c>ClrType</c>.</b> An object carries its own type; only the byte
    /// path needs a declared one to decode. ⇒ a blueprint row whose declared type could not be resolved
    /// still shows its live value rather than <c>&lt;unreadable&gt;</c>.
    /// </summary>
    [Fact]
    public void TheObjectArmWorksWithoutADeclaredClrType()
    {
        var row = Row("Health", clrType: null, live: 7);

        Assert.Equal("7", Formatter().Cell(row));
    }

    /// <summary>
    /// ⭐ <b>The object arm WINS over bytes when both are present.</b> ⚠ Production never sets both, but
    /// the precedence must be stated somewhere other than in a comment.
    /// </summary>
    [Fact]
    public void TheObjectArmIsPreferredOverBytes()
    {
        var row = Row("Health", typeof(int), live: 7) with
        {
            ReadValue = () => BitConverter.GetBytes(999),
        };

        Assert.Equal("7", Formatter().Cell(row));
    }

    /// <summary>⛔ A row whose object arm returns <c>null</c> is <c>&lt;unreadable&gt;</c>, ⭐ NOT
    /// <c>(pending)</c> — <i>"the run did not write this"</i> and <i>"the value would not render"</i>
    /// are different facts, and only the first is <c>(pending)</c>.</summary>
    [Fact]
    public void ANullObjectIsUnreadableNotPending()
    {
        var row = Row("Health", typeof(int), live: null, written: true);

        Assert.Equal(VariableValueFormatter.Unreadable, Formatter().Cell(row));
    }

    /// <summary>⛔ A throwing arm never takes the window down.</summary>
    [Fact]
    public void AThrowingObjectArmIsUnreadable()
    {
        var row = BaseRow("Health", typeof(int)) with
        {
            HasEverBeenWritten = true,
            ReadValueObject    = () => throw new InvalidOperationException("boom"),
        };

        Assert.Equal(VariableValueFormatter.Unreadable, Formatter().Cell(row));
    }

    // ══ 90a — (pending) STAYS HONEST. Guide row C9 ═══════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The row that decides whether this batch is a fix or a regression.</b>
    /// 📌 Guide <c>C9</c>: <i>"a variable declared but never written by the run reads
    /// <c>(pending)</c>."</i> ⇒ ⛔ a live map that EXISTS but does not contain this name must not
    /// produce a zero.
    /// </summary>
    [Fact]
    public void AVariableAbsentFromALiveMapStillReadsPending()
    {
        var source = SectionSource(
            names: new[] { "Health", "Ammo" },
            live:  new Dictionary<string, object> { ["Health"] = 7 });   // ⛔ Ammo absent, on purpose

        var f    = Formatter();
        var rows = source.GetRows().ToDictionary(r => r.ShortName);

        Assert.Equal("7", f.Cell(rows["Health"]));
        Assert.Equal(VariableValueFormatter.PendingFirstWrite, f.Cell(rows["Ammo"]));
    }

    /// <summary>⭐ No provider at all ⇒ every cell is <c>(pending)</c> — the authoring case, unchanged
    /// from before this batch and the reason nothing looked broken.</summary>
    [Fact]
    public void WithNoProviderEveryCellIsPending()
    {
        var source = SectionSource(new[] { "Health", "Ammo" },
                                   live: (Func<IReadOnlyDictionary<string, object>?>?)null);
        var f      = Formatter();

        Assert.All(source.GetRows(),
            r => Assert.Equal(VariableValueFormatter.PendingFirstWrite, f.Cell(r)));
    }

    /// <summary>⭐ A provider that CAN project but has nothing live ⇒ still <c>(pending)</c> everywhere.
    /// ⛔ An empty map is not a licence to show zeros.</summary>
    [Fact]
    public void AnEmptyLiveMapIsStillPending()
    {
        var source = SectionSource(new[] { "Health" }, live: new Dictionary<string, object>());

        Assert.Equal(VariableValueFormatter.PendingFirstWrite,
                     Formatter().Cell(Assert.Single(source.GetRows())));
    }

    /// <summary>
    /// ⭐⭐ <b>The map is re-read EVERY frame.</b> <c>GetRows()</c> is called per frame by
    /// <c>VariableTableModel.Build()</c> ⇒ a value that appears mid-run must appear in the cell.
    /// ⛔ Without this the arm could capture the first frame's map and look correct forever.
    /// </summary>
    [Fact]
    public void TheLiveMapIsReReadOnEveryGetRows()
    {
        var live   = new Dictionary<string, object>();
        var source = SectionSource(new[] { "Health" }, () => live);
        var f      = Formatter();

        Assert.Equal(VariableValueFormatter.PendingFirstWrite, f.Cell(source.GetRows()[0]));

        live["Health"] = 42;

        Assert.Equal("42", f.Cell(source.GetRows()[0]));
    }

    // ══ 90c — the BYTE arm's honesty, on the AI source ═══════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b><c>90c</c>'s half of <c>C9</c>, and it was a live regression risk.</b>
    /// 🔴 <c>BlackboardSectionRowSource</c> said <c>HasEverBeenWritten: reader != null</c> — i.e. the
    /// moment ANY reader was supplied, <b>every row claimed to have been written</b>. ⇒ wiring the
    /// BTree/HSM provider would have shown a decoded ZERO where <c>(pending)</c> belongs.
    /// ⭐ Presence is now MEASURED per name: an empty read is the absence signal.
    /// </summary>
    [Fact]
    public void AnAiRowWithNoBytesForItsNameStillReadsPending()
    {
        var asset = FakeAsset.With(Var("Health"), Var("Ammo"));
        var live  = new Dictionary<string, byte[]> { ["Health"] = BitConverter.GetBytes(7f) };

        var source = new BlackboardSectionRowSource(
            () => asset, asset.AssetId, BlackboardMyBlueprintModel.SectionInputs,
            readRaw: n => live.TryGetValue(n, out var b) ? b : Array.Empty<byte>());

        var f    = Formatter();
        var rows = source.GetRows().ToDictionary(r => r.ShortName);

        Assert.Equal("7", f.Cell(rows["Health"]));
        Assert.Equal(VariableValueFormatter.PendingFirstWrite, f.Cell(rows["Ammo"]));
    }

    /// <summary>⭐ And with no reader at all, unchanged: <c>(pending)</c>, never
    /// <c>&lt;unreadable&gt;</c>.</summary>
    [Fact]
    public void AnAiRowWithNoReaderReadsPending()
    {
        var asset  = FakeAsset.With(Var("Health"));
        var source = new BlackboardSectionRowSource(
            () => asset, asset.AssetId, BlackboardMyBlueprintModel.SectionInputs);

        Assert.Equal(VariableValueFormatter.PendingFirstWrite,
                     Formatter().Cell(Assert.Single(source.GetRows())));
    }

    /// <summary>
    /// ⚠ <b>THE HONEST COST, asserted rather than only documented.</b> §4a's change highlight diffs
    /// BYTES ⇒ a row on the object arm has none, so its highlight is <b>INERT</b>. ⭐ That is the safe
    /// direction this codebase already chose for <c>ReadAssetTick</c> — ⛔ never a WRONG highlight.
    /// </summary>
    [Fact]
    public void AnObjectArmRowHasNoBytesAndThereforeAnInertHighlight()
    {
        var row = Row("Health", typeof(int), live: 7);

        Assert.True(row.ReadValue().IsEmpty);
        Assert.Null(row.AssetTick);
    }

    /// <summary>⭐ The byte arm keeps its bytes, so BTree/HSM keep a LIVE highlight — the reason
    /// <c>90c</c> does not route through the object arm.</summary>
    [Fact]
    public void AByteArmRowStillCarriesItsBytes()
    {
        var asset  = FakeAsset.With(Var("Health"));
        var source = new BlackboardSectionRowSource(
            () => asset, asset.AssetId, BlackboardMyBlueprintModel.SectionInputs,
            readRaw: _ => BitConverter.GetBytes(7f));

        Assert.Equal(4, Assert.Single(source.GetRows()).ReadValue().Length);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static VariableRow BaseRow(string name, Type? clrType)
        => new(
            Origin:    new VariableRowOrigin(AssetId, default, "s", name, "Alpha"),
            ShortName: name,
            TypeText:  clrType?.Name ?? "?",
            ClrType:   clrType,
            ReadValue: () => Array.Empty<byte>());

    private static VariableRow Row(string name, Type? clrType, object? live, bool written = true)
        => BaseRow(name, clrType) with
        {
            HasEverBeenWritten = written,
            ReadValueObject    = () => live,
        };

    private static SectionVariableRowSource SectionSource(
        string[] names, IReadOnlyDictionary<string, object>? live)
        => SectionSource(names, live is null ? null : () => live);

    private static SectionVariableRowSource SectionSource(
        string[] names, Func<IReadOnlyDictionary<string, object>?>? live)
        => new(
            assetId: AssetId, assetName: "Alpha", entity: default, section: "s",
            schema:  new StubSchema(names),
            liveObjects: live);

    /// <summary>⭐ A schema source of N int variables. ⚠ The interface has 15 members and only
    /// <c>Variables</c> matters here — the rest are inert by design, not by omission.</summary>
    private sealed class StubSchema : IVariablesSchemaSource
    {
        public StubSchema(string[] names)
            => Variables = names
                .Select(n => new VariableViewModel(
                    Name: n, TypeName: "int", ByteSize: 4, FieldType: typeof(int),
                    Comment: null,
                    AliasedBy: Array.Empty<(string, Guid, Guid)>(),
                    IsUnused: false))
                .ToList();

        public IReadOnlyList<VariableViewModel> Variables { get; }

        public bool IsReadOnly => false;
        public bool SupportsRoleScopeEditing => false;
        public string? GetRefactorKey(string variableName) => null;
        public void AddVariable(BlackboardVariableEntry entry) { }
        public void RemoveVariable(string name) { }
        public void RemoveVariables(IReadOnlyList<string> names) { }
        public void RenameVariable(string oldName, string newName) { }
        // ⭐ 98a — the interface has NO default body on purpose (U-5/BP-230: "a default body is
        //   the interface volunteering to lie on an implementer's behalf"), so every double must
        //   answer. ⚠ These doubles do not exercise the write, so they RECORD rather than no-op —
        //   a silent { } here would be the very shape the rule exists to stop.
        public System.Collections.Generic.List<(string Name, string? Json)> DefaultWrites { get; } = new();
        public void UpdateVariableDefaultValueJson(string name, string? defaultValueJson)
            => DefaultWrites.Add((name, defaultValueJson));
        public void MoveVariable(int sourceIndex, int destIndex) { }
        public int CountNodesReferencingVariable(string name) => 0;
        public IReadOnlyList<UnboundRequirementViewModel> UnboundRequirements
            => Array.Empty<UnboundRequirementViewModel>();
        public void AddAlias(string name, BlackboardAliasBinding binding) { }
        public void RemoveAlias(string name, Guid requirementAssetId, Guid requirementElementId) { }
        public IReadOnlyDictionary<Guid, int>? GetParallelRegionMap() => null;
    }

    private static BlackboardVariableEntry Var(string n) => new(n, typeof(float), null);

    private sealed class FakeAsset : IEditableAsset, IBlackboardManagedAsset
    {
        private readonly List<BlackboardVariableEntry> _vars;
        private FakeAsset(IEnumerable<BlackboardVariableEntry> vars) => _vars = vars.ToList();
        public static FakeAsset With(params BlackboardVariableEntry[] vars) => new(vars);

        public Guid AssetId { get; } = Guid.NewGuid();
        public string Name => "Alpha";
        public AssetKind Kind => AssetKind.BTree;
        public string SourceFilePath => "/fake.btree.json";
        public bool IsDirty => false;
        public bool IsEditorOwned => true;
        public event Action? Changed { add { } remove { } }

        public bool IsBlackboardEditorManaged => true;
        public void SetBlackboardEditorManaged(bool managed) { }
        public IReadOnlyList<BlackboardVariableEntry> BlackboardVariables => _vars;
        public void AddVariable(BlackboardVariableEntry entry) => _vars.Add(entry);
        public void RemoveVariable(string name) => _vars.RemoveAll(v => v.Name == name);
        public void UpdateVariableComment(string name, string? comment) { }
        public void UpdateVariableDefaultValueJson(string name, string? json) { }
        public void MoveVariable(int sourceIndex, int destIndex) { }
        public void RenameVariable(string oldName, string newName) { }
        public int CountNodesReferencingVariable(string name) => 0;
        public IReadOnlyList<BlackboardAliasBinding> GetAliasesFor(string variableName)
            => Array.Empty<BlackboardAliasBinding>();
        public void AddAlias(string variableName, BlackboardAliasBinding binding) { }
        public void RemoveAlias(string variableName, Guid requiringAssetId, Guid requiringElementId) { }
        public void RemoveVariables(IReadOnlyList<string> names) { }
    }
}
