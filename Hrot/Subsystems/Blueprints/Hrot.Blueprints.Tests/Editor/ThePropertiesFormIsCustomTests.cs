using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Editor.Windows;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Variables;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b>Batch 99 (<c>99a</c>) — <i>"Properties…"</i> opens the DECLARATION, as a CUSTOM form.</b>
///
/// <para>📌 <b><c>R-108</c></b>: the two menu items are <b>TWO OBJECTS</b>, not two scopes ·
/// 📌 <b><c>R-109</c></b>: ⛔ <b>and it cannot be a StructEdit document</b>, because <c>Name</c> is a
/// RENAME and <c>Type</c> is a RETYPE MIGRATION — <b>operations, not struct field writes</b>.</para>
///
/// <para>⭐⭐⭐ <b>WHICH LAYER IS FAKED</b> *(📌 <c>M-29</c>)*: ⛔ <b>the DRAW, entirely</b> —
/// 📌 <c>R-21</c>/<c>R-62</c>, no headless rail can drive ImGui, so <b>nothing below asserts that a
/// control appears</b>. ⭐ What IS asserted: which properties the SCHEMA offers, that the commit reaches
/// the real declaration through the real schema source, and that a rename runs the REFACTOR SERVICE
/// rather than changing a string. ⚠ The form's own <c>Draw</c> is exercised by nothing, and it says so.</para>
/// </summary>
public sealed class ThePropertiesFormIsCustomTests
{
    // ══ the schema is the filter ═════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE SCHEMA DECIDES THE CONTROLS, for all three carriers.</b>
    /// 📌 The handoff's first rail. ⛔ Not a hand-kept list, and ⛔ not a per-field flag — a property
    /// with no backing member cannot be offered, which is what <c>VariablePropertySchema</c> buys.
    /// </summary>
    [Theory]
    [InlineData(VariableDeclarationKind.BlueprintVariable,  8)]
    [InlineData(VariableDeclarationKind.BlueprintParameter, 5)]
    [InlineData(VariableDeclarationKind.BlackboardEntry,    4)]
    public void TheSchemaDecidesWhichPropertiesTheFormOffers(VariableDeclarationKind kind, int count)
    {
        var offered = VariablePropertySchema.For(kind);
        Assert.Equal(count, offered.Count);

        // ⭐ Every offered property has a BACKING MEMBER — ⛔ a control with nowhere to save is the
        //   thing this schema exists to make unrepresentable.
        foreach (var p in offered)
            Assert.NotNull(VariablePropertySchema.BackingMember(kind, p));
    }

    /// <summary>
    /// ⛔⛔ <b><c>Role</c>/<c>Scope</c> is not a property at all</b> — user ruling <c>2026-08-16</c>:
    /// <b>the SECTION is the classification.</b> ⚠ <c>BlackboardVariableEntry</c> DOES carry both
    /// members, which is exactly why their absence has to be asserted rather than assumed.
    /// </summary>
    [Theory]
    [InlineData(VariableDeclarationKind.BlueprintVariable)]
    [InlineData(VariableDeclarationKind.BlueprintParameter)]
    [InlineData(VariableDeclarationKind.BlackboardEntry)]
    public void RoleAndScopeAreNeverOffered(VariableDeclarationKind kind)
    {
        foreach (var p in VariablePropertySchema.For(kind))
            Assert.True(VariablePropertySchema.BackingMember(kind, p) is not "Role" and not "Scope");
    }

    // ══ the commit reaches the real declaration ══════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>OK writes the PROPERTIES to the declaration</b> — ⛔ not to the variable's value, which
    /// is what <c>BP-359</c> was: <i>"'Properties…' has never edited properties."</i>
    /// ⭐ Through the REAL <c>BlueprintVariableSchemaSource</c> and a REAL <c>BlueprintAsset</c>.
    /// </summary>
    [Fact]
    public void OkWritesThePropertiesToTheDeclaration()
    {
        var s = Scene();

        Assert.True(s.Modal.Open(s.Row, s.Schema, s.Asset.AssetId, editable: true));
        s.Modal.State.Tooltip  = "how much health";
        s.Modal.State.Comment  = "authored";
        s.Modal.State.Category = "Combat";
        s.Modal.State.IsExposedOnSpawn = true;
        s.Modal.Commit();

        var decl = Decl(s.Asset);
        Assert.Equal("how much health", decl.Tooltip);
        Assert.Equal("authored",        decl.Comment);
        Assert.Equal("Combat",          decl.Category);
        Assert.True(decl.IsExposedOnSpawn);
    }

    /// <summary>
    /// ⭐⭐ <b>The form OPENS SEEDED from what the declaration holds now</b> — ⛔ not from the type's
    /// defaults. 📌 <c>BP-367</c> is the reason this is asserted: an unseeded form whose OK lands is
    /// how an authored value gets silently overwritten.
    /// </summary>
    [Fact]
    public void TheFormOpensSeededFromTheDeclaration()
    {
        var s = Scene(d => { d.Tooltip = "seeded"; d.Category = "Cat"; d.DefaultValueJson = "7"; });

        Assert.True(s.Modal.Open(s.Row, s.Schema, s.Asset.AssetId, editable: true));

        Assert.Equal("seeded", s.Modal.State.Tooltip);
        Assert.Equal("Cat",    s.Modal.State.Category);
        Assert.Equal("7",      s.Modal.State.DefaultValueJson);
        Assert.Equal("Health", s.Modal.State.Name);
    }

    /// <summary>
    /// ⛔ <b>An unedited open-and-OK is a NO-OP in value terms.</b> ⭐ Same property <c>98a</c>'s rail
    /// pins for the value dialog — 📌 <c>BP-367</c>: the destructive case is a form that opens empty.
    /// </summary>
    [Fact]
    public void AnUneditedRoundTripChangesNothing()
    {
        var s = Scene(d => { d.Tooltip = "keep"; d.DefaultValueJson = "7"; });

        Assert.True(s.Modal.Open(s.Row, s.Schema, s.Asset.AssetId, editable: true));
        s.Modal.Commit();

        Assert.Equal("keep", Decl(s.Asset).Tooltip);
        Assert.Equal("7",    Decl(s.Asset).DefaultValueJson);
    }

    // ══ Name is an OPERATION ═════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>A RENAME RUNS THE REFACTOR SERVICE.</b> 📌 The handoff, verbatim: <i>"assert the service
    /// is INVOKED, not that a string changed."</i>
    ///
    /// <para>⚠ 📌 <c>M-15</c>: BTree/HSM store the variable's NAME STRING in the binding and
    /// <c>RenameVariable</c> does not fix up <c>ExpressionTargetField</c> ⇒ ⛔ skipping the service is a
    /// <b>dangling binding</b>, caught at build as <c>BTREE0002</c> — a whole-asset skip.</para>
    /// </summary>
    [Fact]
    public void ARenameRunsTheRefactorService()
    {
        var refactor = new RecordingRefactor();
        var s = Scene(refactor: refactor);

        Assert.True(s.Modal.Open(s.Row, s.Schema, s.Asset.AssetId, editable: true));
        s.Modal.State.Name = "Hitpoints";
        s.Modal.Commit();

        Assert.Equal(VariableRenameCommit.Outcome.Ok, s.Modal.LastRenameOutcome);
        Assert.Single(refactor.Previews);
        Assert.Equal(1, refactor.Applied);
        Assert.Equal("Hitpoints", Decl(s.Asset, "Hitpoints").Name);
    }

    /// <summary>
    /// ⛔⛔ <b>An ERROR from the refactor preview aborts BOTH halves.</b> ⭐ This is the one deliberate
    /// change from <c>VariablesPanelControl.CommitRename</c>, which renamed the declaration anyway —
    /// ⚠ leaving the references behind, which is exactly the dangling state <c>M-15</c> describes.
    /// </summary>
    [Fact]
    public void ARefactorErrorAbortsTheRenameEntirely()
    {
        var refactor = new RecordingRefactor { FailWithError = true };
        var s = Scene(refactor: refactor);

        Assert.True(s.Modal.Open(s.Row, s.Schema, s.Asset.AssetId, editable: true));
        s.Modal.State.Name = "Hitpoints";
        s.Modal.Commit();

        Assert.Equal(VariableRenameCommit.Outcome.RefusedByRefactor, s.Modal.LastRenameOutcome);
        Assert.Equal(0, refactor.Applied);
        Assert.Equal("Health", Decl(s.Asset).Name);   // ⭐ the declaration did NOT move
    }

    /// <summary>
    /// ⛔⛔ <b>No refactor service ⇒ NO RENAME, and the form says why.</b>
    /// ⭐ <c>BlueprintDetailsWindow</c> is exactly this case — measured: it holds neither a schema nor a
    /// refactor service. ⚠ A Name box that silently does not commit is the trap this refuses.
    /// </summary>
    [Fact]
    public void WithNoRefactorService_TheNameFieldIsRefusedWithAReason()
    {
        var s = Scene(withRefactor: false);

        Assert.True(s.Modal.Open(s.Row, s.Schema, s.Asset.AssetId, editable: true));
        Assert.False(s.Modal.CanRename);
        Assert.False(string.IsNullOrWhiteSpace(VariablePropertiesModal.RenameUnavailableHere));

        s.Modal.State.Name = "Hitpoints";
        s.Modal.Commit();

        Assert.Null(s.Modal.LastRenameOutcome);
        Assert.Equal("Health", Decl(s.Asset).Name);
    }

    // ══ Type is an OPERATION, and is shipped disabled ════════════════════════

    /// <summary>
    /// ⛔⛔ <b>A retype is NEVER written this batch, and the reason is shown.</b> 📌 The handoff:
    /// <i>"do NOT silently write the new type and leave <c>DefaultValueJson</c> unconvertible"</i>.
    /// ⭐ Editing the state's <c>TypeId</c> — which only a future enabled control could do — changes
    /// nothing, because <c>Commit</c> never reads it.
    /// </summary>
    [Fact]
    public void ARetypeIsNeverWritten()
    {
        var s = Scene();

        Assert.True(s.Modal.Open(s.Row, s.Schema, s.Asset.AssetId, editable: true));
        s.Modal.State.TypeId = "System.Single";
        s.Modal.Commit();

        Assert.Equal("System.Int32", Decl(s.Asset).Type.TypeId);
        Assert.False(string.IsNullOrWhiteSpace(VariablePropertiesModal.RetypeUnavailable));
    }

    // ══ read-only is DIALOG-level ════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐ <b>Read-only is DIALOG-LEVEL and comes from <c>VariableEditPolicy</c></b> — ⛔ not a second
    /// matrix *(ruling 9)*, and ⛔ not a per-field flag *(📌 <c>R-109</c>)*.
    /// ⚠ The design's matrix: planning ⇒ editable · running/paused ⇒ read-only · replay ⇒ <b>Denied</b>,
    /// so the gesture never even reaches the form.
    /// </summary>
    [Theory]
    [InlineData(VariableRunState.Planning, VariableEditAvailability.Editable)]
    [InlineData(VariableRunState.Running,  VariableEditAvailability.ReadOnly)]
    [InlineData(VariableRunState.Paused,   VariableEditAvailability.ReadOnly)]
    [InlineData(VariableRunState.Replay,   VariableEditAvailability.Denied)]
    public void ThePolicyDecidesEditability(VariableRunState run, VariableEditAvailability expected)
        => Assert.Equal(expected, VariableEditPolicy.Resolve(
               VariableEditAction.Properties, run, Scene().Row));

    // ══ the gesture no longer opens a StructEdit session ═════════════════════

    /// <summary>
    /// 🔴🔴 <b>RED before <c>99a</c>: the Properties gesture opened a StructEdit session over the
    /// VALUE</b> — 📌 <c>BP-359</c>, and the user's own words: <i>"the 'Properties' context menu now
    /// opens the same 'Edit variable' modal as 'Edit'. This is wrong."</i>
    ///
    /// <para>⭐ Now it raises the form event and opens NO session — ⛔ the two menu items are two
    /// objects, and only one of them is a struct.</para>
    /// </summary>
    [Fact]
    public void ThePropertiesGesture_OpensNoStructEditSession()
    {
        var s = Scene();
        VariableRow? raised = null;
        bool         editable = false;

        var binder = new VariableEditGestureBinder(
            new VariableEditLauncher(new StructEdit.Reflection.ComponentEditServiceBuilder().Build()),
            entryResolver: _ => new BlackboardVariableEntry("Health", typeof(int), null),
            runState:      () => VariableRunState.Planning);
        binder.PropertiesRequestedForRow += (r, e) => { raised = r; editable = e; };

        binder.OnProperties(s.Row);

        Assert.Null(binder.ActiveSession);      // ⛔ NO StructEdit document
        Assert.NotNull(raised);                 // ⭐ the custom form was asked for instead
        Assert.True(editable);
        Assert.True(binder.HasPropertiesHost);
    }

    /// <summary>⭐ And "Edit value…" is UNCHANGED — 📌 the steer: <i>"'Edit value…' stays StructEdit."</i></summary>
    [Fact]
    public void TheValueGesture_StillOpensAStructEditSession()
    {
        var s = Scene();
        var binder = new VariableEditGestureBinder(
            new VariableEditLauncher(new StructEdit.Reflection.ComponentEditServiceBuilder().Build()),
            entryResolver: _ => new BlackboardVariableEntry("Health", typeof(int), null),
            runState:      () => VariableRunState.Planning);

        binder.OnEditValue(s.Row);

        Assert.NotNull(binder.ActiveSession);
    }

    // ── the harness ─────────────────────────────────────────────────────────

    private sealed record Rig(
        VariablePropertiesModal Modal, VariableRow Row,
        IVariablesSchemaSource Schema, Hrot.Blueprints.Core.Assets.BlueprintAsset Asset);

    /// <param name="refactor">
    /// ⚠ <b>Defaulted via a SENTINEL, not <c>??</c>.</b> 📐 The first draft wrote
    /// <c>refactor ?? new RecordingRefactor()</c> — which silently substituted a service when a rail
    /// passed <c>null</c> ON PURPOSE, and that rail failed for the right reason. ⭐ Keeping the
    /// distinction is the whole point of the no-service case.
    /// </param>
    private static Rig Scene(
        Action<Hrot.Blueprints.Core.Assets.VariableDecl>? configure = null,
        IRefactorService? refactor = null,
        bool withRefactor = true)
    {
        var asset = new Hrot.Blueprints.Core.Assets.BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "PropsHost",
            Dispatch = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.AiPrimitive,
            Graphs   = new List<Hrot.Blueprints.Core.Assets.Graph>(),
            Header   = new Hrot.Blueprints.Core.Assets.Header(),
        };
        Hrot.Blueprints.Editor.Host.BlueprintDocumentFactory.CreateVariable(asset, "Health", "System.Int32");
        configure?.Invoke(Decl(asset));

        // ⭐ The REAL schema source, so the commit lands in the REAL declaration.
        var schema = new Hrot.Blueprints.Editor.Variables.BlueprintVariableSchemaSource(
            asset, Hrot.Blueprints.Core.Compiler.Ir.VariableKind.Variable, onChanged: () => { });

        var row = new SectionVariableRowSource(
                assetId: asset.AssetId, assetName: asset.Name, entity: default,
                section: "vars", schema: schema)
            .GetRows().Single();

        var service = refactor ?? (withRefactor ? new RecordingRefactor() : null);
        return new Rig(new VariablePropertiesModal(service), row, schema, asset);
    }

    private static Hrot.Blueprints.Core.Assets.VariableDecl Decl(
        Hrot.Blueprints.Core.Assets.BlueprintAsset asset, string name = "Health")
        => asset.Declarations.Of(Hrot.Blueprints.Core.Assets.DeclarationKind.Variable)
                .First(d => d.Name == name).AsVariableDecl!;

    /// <summary>
    /// ⭐⭐ Records that the service was ASKED — 📌 the handoff wants the INVOCATION asserted, not a
    /// changed string. ⚠ <c>FailWithError</c> drives the abort path.
    /// </summary>
    private sealed class RecordingRefactor : IRefactorService
    {
        public List<(string From, string To)> Previews { get; } = new();
        public int  Applied { get; private set; }
        public bool FailWithError { get; init; }

        public RefactorPreview PreviewRename(string f, string t, RefactorOptions o)
        {
            Previews.Add((f, t));
            var issues = FailWithError
                ? new[] { new RefactorIssue(RefactorIssueSeverity.Error, "blocked by the rail", null) }
                : Array.Empty<RefactorIssue>();
            return new RefactorPreview(f, t, Array.Empty<RefactorFileEdit>(), issues);
        }

        public RefactorResult ApplyRename(RefactorPreview p)
        {
            Applied++;
            return new RefactorResult(true, Array.Empty<string>(), null);
        }

        public IReadOnlyList<AssetReferenceInfo> FindReferences(string k)
            => Array.Empty<AssetReferenceInfo>();
        public IReadOnlyList<AssetReferenceInfo> FindReferencesInAsset(Guid id)
            => Array.Empty<AssetReferenceInfo>();
        public DeletePreview PreviewDelete(Guid id, DeleteOptions o)
            => new(id, Array.Empty<AssetReferenceInfo>(), Array.Empty<RefactorIssue>());
        public RefactorResult ApplyDelete(DeletePreview p)
            => new(true, Array.Empty<string>(), null);
        public System.Threading.Tasks.Task<RefactorPreview> PreviewRenameAsync(
            string f, string t, RefactorOptions o, System.Threading.CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(PreviewRename(f, t, o));
        public System.Threading.Tasks.Task<RefactorResult> ApplyRenameAsync(
            RefactorPreview p, System.Threading.CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(ApplyRename(p));
    }
}
