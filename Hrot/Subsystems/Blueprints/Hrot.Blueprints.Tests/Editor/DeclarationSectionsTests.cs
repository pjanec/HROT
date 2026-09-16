using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.Windows;
using NodeEditor.Core.Action;
using Hrot.Editor.AiShared;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b><c>C-sections</c> — sections ARE the classification</b> (user ruling <c>2026-08-16</c>,
/// 📄 <c>DESIGN_Variable_Details_And_Editing.md</c> §1c).
///
/// <para>
/// 🔴🔴 <b>What this closes.</b> <c>BuildVariableItems()</c> listed only
/// <c>DeclarationKind.Variable</c> ⇒ <b>Parameters and WorkingState were not in My Blueprint at
/// all</b>. ⚠ <b>32 of the shipped assets are <c>(Parameter, WorkingState)</c> AiPrimitives</b>, so
/// for most of the corpus the panel showed an empty Variables section and offered no way to see,
/// rename or delete anything the asset actually declares.
/// </para>
///
/// <para>
/// ⛔ <b>No <c>Role</c>/<c>Scope</c> control is introduced by any of this.</b> The ruling deletes that
/// concept rather than unifying two: where a declaration was created IS its classification.
/// </para>
///
/// <para>
/// ⚠⚠ <b>What these tests CANNOT see, stated rather than implied:</b> the sections DRAWING — that a
/// designer sees three headers in the right visual order, that an empty one renders as a header
/// rather than a gap, and that the per-section "+" appears where expected. 📌 The visual check is
/// suspended, so the projection and the command wiring are checked and the pixels are not.
/// </para>
/// </summary>
public sealed class DeclarationSectionsTests
{
    /// <summary>Minimal <see cref="IEditableAsset"/> — the model only reads the id.</summary>
    private sealed class SectionsFakeAsset : IEditableAsset
    {
        public Guid      AssetId        { get; }
        public string    Name           => "";
        public AssetKind Kind           => AssetKind.Blueprint;
        public string    SourceFilePath => "";
        public bool      IsDirty        => false;
        public bool      IsEditorOwned  => false;
        public event Action? Changed;
        public SectionsFakeAsset(Guid id) { AssetId = id; _ = Changed; }
    }

    private static BlueprintAsset AssetWithOneOfEachKind()
    {
        var asset = Builders.BlueprintAssetBuilder.Instance("SectionsHost").Build();
        asset.Declarations.Add(BlueprintDeclaration.Create(
            DeclarationKind.Parameter, Guid.NewGuid(), "Speed",
            new BlueprintTypeRef { TypeId = "System.Single" }));
        asset.Declarations.Add(BlueprintDeclaration.Create(
            DeclarationKind.Variable, Guid.NewGuid(), "Health",
            new BlueprintTypeRef { TypeId = "System.Int32" }));
        // ⭐ Batch 86 — RESTATED. "Phase" was a WorkingState; R-01 makes it a second Variable. ⛔ It is
        //   KEPT rather than dropped: it is what makes "and in NO other section" a real claim — with
        //   one declaration per section a mis-routed projection would still look right.
        asset.Declarations.Add(BlueprintDeclaration.Create(
            DeclarationKind.Variable, Guid.NewGuid(), "Phase",
            new BlueprintTypeRef { TypeId = "System.Byte" }));
        return asset;
    }

    private static BlueprintMyBlueprintModel MakeModel(BlueprintAsset asset)
    {
        var model = new BlueprintMyBlueprintModel();
        model.Retarget(new SectionsFakeAsset(asset.AssetId), asset);
        return model;
    }

    // ── the projection ───────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>Each kind lands in its own section, and in NO other.</b> ⚠ The "and no other" half is
    /// the one that matters: the failure this replaces was not a missing section but every kind
    /// collapsed onto one projection, so a test that only checked presence would have passed while
    /// every declaration appeared under "Variables".
    ///
    /// <para>⭐⭐ <b>Batch 86 — RESTATED.</b> "Phase" is a <c>Variable</c> now *(<c>R-01</c>)</c>, so it
    /// belongs in Variables <b>with</b> Health, and the retired Working State section must hold nothing
    /// at all. ⛔ The "no other" half is asserted MORE strongly than before: the parameter must not
    /// leak into the state section, and the state declarations must not leak into Inputs.</para>
    /// </summary>
    [Fact]
    public void EachDeclarationKind_AppearsInItsOwnSection_AndNoOther()
    {
        var model = MakeModel(AssetWithOneOfEachKind());

        var inputs    = model.GetItems(BlueprintMyBlueprintModel.SectionParameters);
        var variables = model.GetItems(BlueprintMyBlueprintModel.SectionVariables);

        Assert.Equal("Speed", Assert.Single(inputs).DisplayName);
        Assert.Equal(new[] { "Health", "Phase" }, variables.Select(i => i.DisplayName));

        Assert.Equal(BlueprintMyBlueprintModel.SectionParameters, inputs[0].SectionId);
        Assert.All(variables, i => Assert.Equal(BlueprintMyBlueprintModel.SectionVariables, i.SectionId));

        // ⛔ And the retired section projects NOTHING — R-01/U-16: one concept, one surface.
        Assert.Empty(model.GetItems(BlueprintMyBlueprintModel.SectionWorkingState));
        Assert.DoesNotContain(model.Sections, s => s.Id == BlueprintMyBlueprintModel.SectionWorkingState);
    }

    /// <summary>
    /// ⚠ <b>Empty rather than absent</b> — the subtlety <c>SectionLocalVariables</c> already records:
    /// <i>"a section that appears and disappears reads as a broken feature."</i>
    ///
    /// <para>⭐⭐ <b>Batch 86 — RESTATED onto a LIVE section.</b> It used to assert this of Working
    /// State, which <c>R-01</c> retires — and a retired section is <b>absent on purpose</b>, which is
    /// the opposite claim. ⛔ Deleting the test would have taken the rule down with the section, so it
    /// moves to <c>Inputs</c>: an Instance asset declaring no parameters must still show the section.
    /// </para>
    /// </summary>
    [Fact]
    public void ASectionWithNoDeclarations_IsEmptyNotAbsent()
    {
        var asset = Builders.BlueprintAssetBuilder.Instance("NoParameters").Build();
        asset.Declarations.Add(BlueprintDeclaration.Create(
            DeclarationKind.Variable, Guid.NewGuid(), "OnlyThis", new BlueprintTypeRef { TypeId = "int" }));

        var model = MakeModel(asset);

        Assert.Contains(model.Sections, s => s.Id == BlueprintMyBlueprintModel.SectionParameters);
        Assert.Empty(model.GetItems(BlueprintMyBlueprintModel.SectionParameters));
    }

    /// <summary>⭐ Every new section declares a create command — the ruling's "each with its own
    /// create command". ⛔ Whether that id is HANDLED is the next test; a descriptor alone is
    /// <c>BP-12c</c>'s inert button.
    ///
    /// <para>⭐ <b>Batch 86 — RESTATED</b>: the Working State half becomes the Variables section, which
    /// is where <c>R-01</c> puts the state kind's "+".</para>
    /// </summary>
    [Fact]
    public void TheNewSectionsDeclareTheirOwnCreateCommands()
    {
        var model = MakeModel(AssetWithOneOfEachKind());

        var inputs = model.Sections.Single(s => s.Id == BlueprintMyBlueprintModel.SectionParameters);
        var state  = model.Sections.Single(s => s.Id == BlueprintMyBlueprintModel.SectionVariables);

        Assert.True(inputs.CanCreateItems);
        Assert.True(state.CanCreateItems);
        Assert.Equal(BlueprintMyBlueprintModel.CommandCreateParameter, inputs.CreateCommandId);
        Assert.Equal("editor.create-variable",                         state.CreateCommandId);
    }

    // ── the wiring — BP-12c ──────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>Creating in a section produces a declaration OF THAT KIND</b> — the handoff's rail,
    /// and the one that catches the inert button.
    ///
    /// <para>
    /// ⛔ Asserted by INVOKING the command and inspecting the asset, not by reading the descriptor.
    /// <c>BP-12c</c> shipped twice as a section whose "+" was declared and unhandled; a test over the
    /// descriptor would have passed both times.
    /// </para>
    /// </summary>
    /// <remarks>
    /// ⭐⭐ <b>Batch 86 — RESTATED to ONE case, and the reason is a design fact rather than a
    /// convenience.</b> The theory ran (Inputs, Working State) over the quick-add registrar. <c>R-01</c>
    /// retires the Working State section, and its surviving counterpart — the Variables "+" — is
    /// <b>not</b> registered by this method: <c>editor.create-variable</c> has its own owner
    /// (<c>RegisterCreateVariableCommand</c>), and registering it here too would be the duplicate
    /// implementation ruling 9 forbids. ⇒ ⭐ the second half of the claim did not vanish, it moved:
    /// <c>TheCreateCommandsAreRegisteredByTheProductionRetarget</c> below covers that id end-to-end,
    /// which is the STRONGER of the two gates anyway.
    /// </remarks>
    [Theory]
    [InlineData(BlueprintMyBlueprintModel.CommandCreateParameter, DeclarationKind.Parameter)]
    public void InvokingASectionsCreateCommand_AddsADeclarationOfThatKind(string commandId, DeclarationKind kind)
    {
        var asset = Builders.BlueprintAssetBuilder.Instance("CreateHost").Build();
        int before = asset.Declarations.CountIn(kind);

        var commands = new EditorCommandsImpl();
        bool dirtied = false;
        BlueprintDocumentFactory.RegisterCreateDeclarationCommands(commands, asset, () => dirtied = true);

        Assert.True(commands.Invoke(commandId).Success,
            $"'{commandId}' is declared by a section but nothing handles it — an inert '+' button.");

        Assert.Equal(before + 1, asset.Declarations.CountIn(kind));
        Assert.True(dirtied, "the document was not marked dirty, so the new declaration would not be saved.");
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>BP-12c</c> for real: the PRODUCTION construction site registers them.</b>
    ///
    /// <para>
    /// 🔴 <b>Found by a revert probe, not by reading.</b> Deleting the registration call from
    /// <c>BlueprintMyBlueprintWindow.Retarget</c> left the invoke test above GREEN — it registers the
    /// commands itself, so it proves the handler works and says nothing about whether anything calls
    /// it. ⇒ that is precisely the <i>"verified it EXISTS without verifying anything USES it"</i>
    /// shape, and it needed its own test. ⭐ Mirrors
    /// <c>LocalVariableSectionTests.TheCreateCommandIsRegisteredByTheProductionRetarget</c>, including
    /// the pre-assertion that the command is absent beforehand — otherwise a globally-registered id
    /// would satisfy it.
    /// </para>
    /// </summary>
    [Theory]
    // ⭐ Batch 86 — RESTATED: the Working State "+" is retired (R-01), and the row it leaves behind is
    //   the state kind's surviving "+". ⛔ Kept as a SECOND row rather than dropped — one row would
    //   stop proving that Retarget wires more than a single command.
    [InlineData(BlueprintMyBlueprintModel.CommandCreateParameter)]
    [InlineData(NodeEditor.Core.CommandCatalog.CreateVariable)]
    public void TheCreateCommandsAreRegisteredByTheProductionRetarget(string commandId)
    {
        var asset    = Builders.BlueprintAssetBuilder.Instance("RetargetHost").Build();
        var window   = new BlueprintMyBlueprintWindow();
        var commands = new EditorCommandsImpl();

        Assert.False(commands.Invoke(commandId).Success);

        window.Retarget(null, asset, null, commands, null, () => Guid.Empty);

        Assert.True(commands.Invoke(commandId).Success,
            $"'{commandId}' is declared by a section but the production Retarget never registers it.");
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The production "+" OPENS A DIALOG — it does not quick-add.</b>
    ///
    /// <para>📌 <b>User ruling, <c>2026-08-17</c>, verbatim:</b> <i>"working state [+] opening no
    /// dialog is <b>wrong, inconsistent</b>. Must open new variable dialog same as any other variable
    /// section."</i></para>
    ///
    /// <para>⛔ <b>Note what this asserts and what
    /// <see cref="TheCreateCommandsAreRegisteredByTheProductionRetarget"/> cannot.</b> That rail only
    /// asks whether the invoke SUCCEEDS, which both wirings do. ⭐ The observable difference is the
    /// asset: the quick-add appends a declaration on the spot, the dialog appends nothing until the
    /// designer confirms a name and a type.</para>
    /// </summary>
    [Theory]
    // ⭐ Batch 86 — RESTATED onto the surviving state section (see above).
    [InlineData(BlueprintMyBlueprintModel.CommandCreateParameter, DeclarationKind.Parameter)]
    [InlineData("editor.create-variable",                         DeclarationKind.Variable)]
    public void TheProductionCreateCommand_OpensADialog_RatherThanQuickAdding(
        string commandId, DeclarationKind kind)
    {
        var asset    = Builders.BlueprintAssetBuilder.Instance("DialogHost").Build();
        var window   = new BlueprintMyBlueprintWindow();
        var commands = new EditorCommandsImpl();
        window.Retarget(null, asset, null, commands, null, () => Guid.Empty);

        int before = asset.Declarations.CountIn(kind);
        Assert.True(commands.Invoke(commandId).Success);

        Assert.Equal(before, asset.Declarations.CountIn(kind));
    }

    /// <summary>
    /// ⭐⭐ <b>And the dialog's confirm path creates the right KIND, with the type it was given.</b>
    /// ⚠ The modal itself cannot be driven headlessly (it is ImGui), so this exercises the create
    /// path the modal is wired to — the same split every other create modal's tests use.
    /// </summary>
    [Theory]
    // ⭐ Batch 86 — RESTATED: both surviving kinds.
    [InlineData(DeclarationKind.Parameter)]
    [InlineData(DeclarationKind.Variable)]
    public void TheDialogsCreatePath_TakesANameAndAType(DeclarationKind kind)
    {
        var asset = Builders.BlueprintAssetBuilder.Instance("ConfirmHost").Build();

        var decl = BlueprintDocumentFactory.CreateDeclaration(asset, kind, "Speed", "System.Single");

        Assert.NotNull(decl);
        Assert.Equal(kind,            decl!.Kind);
        Assert.Equal("Speed",         decl.Name);
        Assert.Equal("System.Single", decl.Type.TypeId);
        Assert.Equal(1, asset.Declarations.CountIn(kind));
    }

    /// <summary>
    /// ⭐ The rejection rules are the variable modal's, unchanged and shared — ⛔ not a second
    /// half-copy that drifts. A blank name and a name already taken by ANY kind both refuse.
    /// </summary>
    [Theory]
    // ⭐ Batch 86 — RESTATED: both surviving kinds.
    [InlineData(DeclarationKind.Parameter)]
    [InlineData(DeclarationKind.Variable)]
    public void TheDialogsCreatePath_RefusesBlankAndDuplicateNames(DeclarationKind kind)
    {
        var asset = Builders.BlueprintAssetBuilder.Instance("RefuseHost").Build();
        BlueprintDocumentFactory.CreateVariable(asset, "Health", "System.Int32");

        // ⭐ Batch 86 — RESTATED: the "Health" the setup creates is itself a Variable now, so the
        //   count is measured as a DELTA rather than pinned at 0. ⛔ Same claim, and it no longer
        //   depends on which kind the fixture's own declaration happens to be.
        int before = asset.Declarations.CountIn(kind);

        Assert.Null(BlueprintDocumentFactory.CreateDeclaration(asset, kind, "   ",    "System.Int32"));
        Assert.Null(BlueprintDocumentFactory.CreateDeclaration(asset, kind, "Health", "System.Int32"));
        Assert.Equal(before, asset.Declarations.CountIn(kind));
    }

    /// <summary>
    /// ⚠ <b>Repeated clicks must not collide</b>, and the uniqueness search is across ALL kinds
    /// (<c>U-14</c>/<c>BP-232</c>): a <c>Parameter</c> and a <c>Variable</c> sharing a name would let
    /// <c>Stage5</c>'s name fallback decide by list order which one a reference reached.
    /// </summary>
    [Fact]
    public void RepeatedCreates_ProduceDistinctNames_AcrossEveryKind()
    {
        var asset = Builders.BlueprintAssetBuilder.Instance("UniqueHost").Build();

        // ⭐ Batch 86 — RESTATED. The middle create was a WorkingState; R-01 makes it a second
        //   Variable. ⛔ THREE creates are KEPT, not two: the point is that repeats collide neither
        //   ACROSS kinds (a vs b) nor WITHIN one (b vs c), and dropping to two would test only one.
        var a = BlueprintDocumentFactory.AddDeclaration(asset, DeclarationKind.Parameter, "Thing");
        var b = BlueprintDocumentFactory.AddDeclaration(asset, DeclarationKind.Variable,  "Thing");
        var c = BlueprintDocumentFactory.AddDeclaration(asset, DeclarationKind.Variable,  "Thing");

        Assert.Equal(3, new[] { a.Name, b.Name, c.Name }.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
