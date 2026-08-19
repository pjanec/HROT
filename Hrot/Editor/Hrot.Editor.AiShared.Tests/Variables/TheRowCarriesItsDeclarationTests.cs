using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐⭐ <b>Batch 95 (<c>95a</c>) — the other half of the dialog rail: the ROW CARRIES ITS DECLARATION.</b>
///
/// <para>📌 <c>TheDialogOpensOnEveryHostTests</c> drives the real composition root and asserts a session
/// opens; it builds its rows by hand, so ⛔ <b>the row sources are the layer that rail cannot see</b>.
/// ⭐ This one covers exactly that layer, on <b>both</b> production sources.</para>
///
/// <para>⭐⭐ <b>Why the row and not the composition root</b>, measured: Blueprint's
/// <c>Local Variables</c> section resolves against <c>_currentGraph()</c> AT CALL TIME inside a window
/// registered as an EXTRA, long after <c>CreateRegistrar</c> returned ⇒ a resolver supplied at the
/// composition root could answer the two asset-scoped sections and not the graph-scoped one. ⭐ The
/// source that built the row already holds the schema that declares it.</para>
///
/// <para>📐 <b>And the measurement that made this shape legal at all:</b>
/// <c>VariableEditLauncher.Open</c> → <c>DefaultValueAuthoring.OpenSession</c> reads exactly
/// <c>FieldType</c> and <c>DefaultValueJson</c> and nothing else — asserted below rather than asserted
/// in prose.</para>
/// </summary>
public sealed class TheRowCarriesItsDeclarationTests
{
    private static readonly Guid AssetId = new("aaaaaaaa-0000-0000-0000-00000000000a");

    // ══ the schema-backed source (Blueprint's three sections) ════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>A <see cref="SectionVariableRowSource"/> row names its own declaration</b> — and it does
    /// so with NO asset, NO store and NO host in the picture, which is the whole reason the Blueprint
    /// perspective can now open the dialog.
    /// </summary>
    [Fact]
    public void ASchemaBackedRowCarriesTheDeclaration()
    {
        var source = new SectionVariableRowSource(
            assetId: AssetId, assetName: "Alpha", entity: default, section: "s",
            schema:  new Schema(new VariableViewModel(
                Name: "Health", TypeName: "int", ByteSize: 4, FieldType: typeof(int),
                Comment: "hit points", AliasedBy: Array.Empty<(string, Guid, Guid)>(),
                IsUnused: false, DefaultValueJson: "7")));

        var decl = source.GetRows().Single().ReadDeclaration?.Invoke();

        Assert.NotNull(decl);
        Assert.Equal("Health",     decl!.Name);
        Assert.Equal(typeof(int),  decl.FieldType);
        Assert.Equal("7",          decl.DefaultValueJson);
        Assert.Equal("hit points", decl.Comment);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE two members the consumer actually reads</b> — 📐 measured off
    /// <c>DefaultValueAuthoring.OpenSession</c>, which does
    /// <c>Hydrate(varEntry.FieldType, varEntry.DefaultValueJson)</c> and then
    /// <c>editService.Open(instance, varEntry.FieldType, scope)</c>.
    ///
    /// <para>⛔ This is what makes a SYNTHESISED entry substitutable for an authored one. ⚠ If the
    /// opener ever starts reading a third member, this rail is where that has to be argued.</para>
    /// </summary>
    [Fact]
    public void TheOpenerReadsOnlyFieldTypeAndDefaultValueJson()
    {
        var src = RepoFiles.Read(
            "Hrot/Editor/Hrot.Editor.AiShared/Inspector/DefaultValueAuthoring.cs");

        int at = src.IndexOf("public static IEditSession OpenSession", StringComparison.Ordinal);
        Assert.True(at >= 0, "DefaultValueAuthoring.OpenSession moved — re-measure before trusting 95a.");

        int end = src.IndexOf('}', src.IndexOf('{', at));
        var body = src[at..end];

        foreach (var member in new[] { "Name", "IsAutoManaged", "Role", "Scope" })
            Assert.DoesNotContain("varEntry." + member, body, StringComparison.Ordinal);

        Assert.Contains("varEntry.FieldType",        body, StringComparison.Ordinal);
        Assert.Contains("varEntry.DefaultValueJson", body, StringComparison.Ordinal);
    }

    // ══ the blackboard source (BTree / HSM) ══════════════════════════════════

    /// <summary>
    /// ⭐⭐ <b>The AI half carries the authored entry ITSELF</b> — ⛔ both sources or neither: leaving
    /// BTree/HSM on the store lookup would keep two rules for one question (ruling 9), and the AI hosts
    /// would silently keep depending on a type test the Blueprint host does not satisfy.
    /// </summary>
    [Fact]
    public void ABlackboardRowCarriesTheAuthoredEntryItself()
    {
        var entry = new BlackboardVariableEntry(
            "Health", typeof(int), Comment: "hit points", DefaultValueJson: "7");
        var asset = new FakeAsset(entry);

        var source = new BlackboardSectionRowSource(
            asset:   () => asset,
            assetId: AssetId,
            section: BlackboardMyBlueprintModel.SectionOf(entry));

        Assert.Same(entry, source.GetRows().Single().ReadDeclaration?.Invoke());
    }

    // ══ the arm survives the two transforms a row goes through ═══════════════

    /// <summary>
    /// ⭐⭐ <b>The sampler must not drop it.</b> 📌 Batch 94's <see cref="VariableRowSampler"/> returns
    /// <c>row with { … }</c> per pulse; ⛔ a rewrite that reconstructed the record instead would strip
    /// the declaration and put the dialog straight back where 95a found it — silently, because every
    /// other cell would still render.
    /// </summary>
    [Fact]
    public void TheDeclarationSurvivesSamplingAndPinning()
    {
        var source = new SectionVariableRowSource(
            assetId: AssetId, assetName: "Alpha", entity: default, section: "s",
            schema:  new Schema(new VariableViewModel(
                Name: "Health", TypeName: "int", ByteSize: 4, FieldType: typeof(int),
                Comment: null, AliasedBy: Array.Empty<(string, Guid, Guid)>(),
                IsUnused: false, DefaultValueJson: "7")),
            liveObjects: () => new Dictionary<string, object> { ["Health"] = 10 });

        var sampled = new VariableRowSampler()
            .Sample(source.GetRows(), VariableRunState.Paused).Single();
        Assert.NotNull(sampled.ReadDeclaration?.Invoke());

        var pinned = new PinnedVariableRowSource();
        pinned.Pin(sampled);
        Assert.NotNull(pinned.GetRows().Single().ReadDeclaration?.Invoke());
    }

    // ── fixtures ─────────────────────────────────────────────────────────────

    private sealed class FakeAsset : IEditableAsset, IBlackboardManagedAsset
    {
        private readonly List<BlackboardVariableEntry> _vars;
        public FakeAsset(params BlackboardVariableEntry[] vars) => _vars = vars.ToList();

        public Guid AssetId => TheRowCarriesItsDeclarationTests.AssetId;
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
        public void RemoveVariables(IReadOnlyList<string> names) { }
        public void UpdateVariableComment(string name, string? comment) { }
        public void UpdateVariableDefaultValueJson(string name, string? json) { }
        public void MoveVariable(int sourceIndex, int destIndex) { }
        public void RenameVariable(string oldName, string newName) { }
        public int CountNodesReferencingVariable(string name) => 0;
        public IReadOnlyList<BlackboardAliasBinding> GetAliasesFor(string variableName)
            => Array.Empty<BlackboardAliasBinding>();
        public void AddAlias(string variableName, BlackboardAliasBinding binding) { }
        public void RemoveAlias(string variableName, Guid requiringAssetId, Guid requiringElementId) { }
    }

    private sealed class Schema : IVariablesSchemaSource
    {
        public Schema(params VariableViewModel[] vars) => Variables = vars;
        public IReadOnlyList<VariableViewModel> Variables { get; }
        public bool IsReadOnly => false;
        public bool SupportsRoleScopeEditing => false;
        public string? GetRefactorKey(string variableName) => null;
        public void AddVariable(BlackboardVariableEntry entry) { }
        public void RemoveVariable(string name) { }
        public void RemoveVariables(IReadOnlyList<string> names) { }
        public void RenameVariable(string oldName, string newName) { }
        public void MoveVariable(int sourceIndex, int destIndex) { }
        public int CountNodesReferencingVariable(string name) => 0;
        public IReadOnlyList<UnboundRequirementViewModel> UnboundRequirements
            => Array.Empty<UnboundRequirementViewModel>();
        public void AddAlias(string name, BlackboardAliasBinding binding) { }
        public void RemoveAlias(string name, Guid requirementAssetId, Guid requirementElementId) { }
        public IReadOnlyDictionary<Guid, int>? GetParallelRegionMap() => null;
    }
}
