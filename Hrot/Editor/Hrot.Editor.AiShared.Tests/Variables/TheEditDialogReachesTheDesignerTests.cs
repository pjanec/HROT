using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Variables;
using StructEdit.Core;
using StructEdit.Reflection;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐⭐ <b>Row 59 — the StructEdit dialog reaches the designer, and the NOT-RUNNING write lands.</b>
///
/// <para>📌 <b><c>Q32</c> ruling 5:</b> <i>"A three-dot button right of the value opens a
/// StructEdit-based editing window, OK / Cancel, initialised to the variable's current value"</i> —
/// <i>"promoted from vectors only to <b>everything</b>"</i>. 📌 <b>ruling 10:</b> <i>"<b>Reuse</b> the
/// existing StructEdit generic value-editing dialog."</i> 📌 <b>ruling 7:</b> <i>"not running ⇒ writes
/// the <b>initial value in JSON</b>."</i></para>
///
/// <para>⭐⭐ <b>THE PRE-DECIDED FORK, resolved ✅.</b> The handoff said: build on
/// <c>IComponentEditService</c> IF it can be driven with a boxed value + CLR type and no ECS entity —
/// ⛔ otherwise STOP. 📐 <b>Measured:</b> <c>Open(object component, Type componentType, EditScope?,
/// EditContext?)</c>. <b>No entity, no component store, no world.</b> ⇒ ✅ and the existing
/// <c>VariableEditLauncher</c> was already built on it.</para>
///
/// <para>🔴🔴 <b>THE ELEVENTH INSTANCE.</b> <c>VariableEditLauncher</c> and
/// <c>VariableEditGestureBinder</c> shipped complete and tested in Batch 75 — and were constructed
/// <b>ONLY IN TESTS</b>. Measured: <b>zero production call sites</b>. ⇒ ⭐ the dialog, its two scopes
/// and its run-state policy all existed, and no designer could reach any of them.</para>
/// </summary>
public sealed class TheEditDialogReachesTheDesignerTests
{
    // ══ the fork, asserted rather than described ═════════════════════════════

    /// <summary>
    /// ⭐⭐ <b>The shared service is drivable from the editor</b> — a boxed value and its type, with no
    /// ECS anything. ⛔ Had this needed an entity, the handoff's instruction was to STOP item 2.
    /// </summary>
    [Fact]
    public void TheSharedEditService_TakesABoxedValueAndAType_WithNoEntity()
    {
        var service = new ComponentEditServiceBuilder().Build();

        using var session = service.Open(42, typeof(int), EditScope.WholeComponent);

        Assert.NotNull(session);
        Assert.Equal(42, session.Commit());
    }

    /// <summary>
    /// ⭐⭐⭐ <b>INVERTED, Batch 96 (<c>96b</c>).</b> 📌 The design said <i>"Edit value…" ⇒
    /// <c>ForField</c> · "Properties…" ⇒ <c>WholeComponent</c></i>, and 📐 measurement overrules it for
    /// a WHOLE-variable edit: the session is opened over the variable's VALUE, so the document root IS
    /// the value and <c>ForField("$.Health")</c> selects nothing.
    ///
    /// <para>⚠ <b>What genuinely distinguishes "Properties…" is NOT the scope</b> — it should edit the
    /// DECLARATION *(name, type, tooltip, comment, …)*, and it opens the value instead. ⛔ That is a
    /// capability question, filed rather than built here.</para>
    /// </summary>
    [Fact]
    public void TheTwoMenuItemsOpenTheWholeValueDocument()
    {
        Assert.Same(EditScope.WholeComponent,
                    VariableEditLauncher.ScopeFor(VariableEditAction.Properties));
        Assert.Same(EditScope.WholeComponent,
                    VariableEditLauncher.ScopeFor(VariableEditAction.EditValue));
    }

    // ══ the wiring — the eleventh instance, closed ═══════════════════════════

    /// <summary>
    /// 🔴🔴 <b>RED before row 59.</b> The gesture binder was constructed only by tests, so the two row
    /// commands opened nothing in the running editor. ⭐ The registrar now builds it from the edit
    /// service it was ALREADY given — ⛔ not a new argument the composition root can forget.
    /// </summary>
    [Fact]
    public void TheRegistrarBindsTheRowGestures_OnTheDefaultPath()
    {
        var reg = Registrar(withEditService: true);

        Assert.NotNull(reg.EditGestures);
    }

    /// <summary>
    /// ⭐ And it is ATTACHED to the table's control, not merely constructed beside it — ⛔ "built but
    /// connected to nothing" is the pattern this whole rail exists to close.
    /// </summary>
    [Fact]
    public void TheGesturesAreAttachedToTheTablesControl()
    {
        var store = new EditorSelectionStore();
        var asset = FakeAsset.With(Entry("Health", typeof(int)));
        store.ActiveAsset = asset;
        var reg = Registrar(withEditService: true, store: store);

        // ⭐ Raised the way the control raises it — ⛔ the ⋮ menu itself needs ImGui, which no
        //   headless test can drive; this is the same call it makes.
        reg.Variables.Control.RaisePropertiesRequested(Row("Health", typeof(int)));

        Assert.Equal(VariableEditAction.Properties, reg.EditGestures!.LastAction);
    }

    /// <summary>⚠ A headless host with no edit service gets no gestures, and does not throw.</summary>
    [Fact]
    public void WithNoEditService_ThereAreNoGestures_AndNoThrow()
        => Assert.Null(Registrar(withEditService: false).EditGestures);

    // ══ ruling 7 — the NOT-RUNNING write, and only that half ═════════════════

    /// <summary>
    /// ⭐⭐ <b>Planning ⇒ the edit lands in <c>DefaultValueJson</c>.</b> 📌 ruling 7's not-running arm.
    /// </summary>
    [Fact]
    public void InPlanning_TheEditLandsInTheDeclarationsJson()
    {
        var asset   = FakeAsset.With(Entry("Health", typeof(int)));
        var service = new ComponentEditServiceBuilder().Build();
        using var session = service.Open(7, typeof(int), EditScope.WholeComponent);

        var outcome = VariableEditCommit.CommitInitialValue(
            session, asset, Row("Health", typeof(int)), typeof(int), VariableRunState.Planning);

        Assert.Equal(VariableEditCommit.Outcome.Ok, outcome);
        Assert.Equal("7", asset.WrittenJson["Health"]);
    }

    /// <summary>
    /// ⛔⛔ <b><c>CommitInitialValue</c> writes the INITIAL arm and nothing else</b> — running, paused
    /// and replay all refuse it. ⭐ Batch 84 added the LIVE arm as a SEPARATE target
    /// (<c>VariableEditCommit.Commit</c> + <c>TargetFor</c>); ⛔ this entry point deliberately did not
    /// grow one, so a caller that means "the declaration's default" cannot accidentally write the world.
    ///
    /// <para>⚠⚠ <b>Correction, Batch 84 — <c>R-65</c>.</b> This comment used to justify the refusal with
    /// ruling 14's <i>"the whole-component route exceeds <c>MaxComponentSize</c>"</i>. 📐 <b>FALSE:</b>
    /// <c>Blackboard1024</c> is exactly 1024 and the guard is <c>&gt;</c> — it fits. ⭐ The true reason is
    /// that the blackboard is <b>SHARED by BTree, HSM and Blueprint at disjoint offsets</b>, so a
    /// whole-component write clobbers them.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(VariableRunState.Running)]
    [InlineData(VariableRunState.Paused)]
    [InlineData(VariableRunState.Replay)]
    public void WhileRunning_TheInitialWriteRefuses(VariableRunState runState)
    {
        var asset   = FakeAsset.With(Entry("Health", typeof(int)));
        var service = new ComponentEditServiceBuilder().Build();
        using var session = service.Open(7, typeof(int), EditScope.WholeComponent);

        var outcome = VariableEditCommit.CommitInitialValue(
            session, asset, Row("Health", typeof(int)), typeof(int), runState);

        Assert.Equal(VariableEditCommit.Outcome.RefusedRunning, outcome);
        Assert.Empty(asset.WrittenJson);
    }

    /// <summary>
    /// ⭐⭐ <b>A refusal does not COMMIT the session.</b> ⛔ Committing and then discarding would leave
    /// the designer's edit applied to a boxed copy nobody keeps — it would look accepted and vanish,
    /// which is worse than a refusal.
    /// </summary>
    [Fact]
    public void ARefusalLeavesTheSessionUncommitted()
    {
        var asset   = FakeAsset.With(Entry("Health", typeof(int)));
        var service = new ComponentEditServiceBuilder().Build();
        using var session = service.Open(7, typeof(int), EditScope.WholeComponent);

        VariableEditCommit.CommitInitialValue(
            session, asset, Row("Health", typeof(int)), typeof(int), VariableRunState.Running);

        // ⭐ Still commitable afterwards — nothing was consumed.
        Assert.Equal(7, session.Commit());
    }

    /// <summary>⛔ A node-owned or passthrough row is never writable, in any run state.</summary>
    [Theory]
    [InlineData(VariableRowKind.NodeOwned)]
    [InlineData(VariableRowKind.ReadOnlyPassthrough)]
    public void ANonWritableRowRefuses(VariableRowKind kind)
    {
        var asset   = FakeAsset.With(Entry("Health", typeof(int)));
        var service = new ComponentEditServiceBuilder().Build();
        using var session = service.Open(7, typeof(int), EditScope.WholeComponent);

        var outcome = VariableEditCommit.CommitInitialValue(
            session, asset, Row("Health", typeof(int), kind), typeof(int), VariableRunState.Planning);

        Assert.Equal(VariableEditCommit.Outcome.RefusedReadOnly, outcome);
        Assert.Empty(asset.WrittenJson);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The write target and the displayed value can never disagree</b> — both ask
    /// <see cref="VariableValue.ModeFor"/>. ⛔ Two readings of "is it running?" is exactly the drift
    /// ruling 9 forbids.
    /// </summary>
    [Theory]
    [InlineData(VariableRunState.Planning)]
    [InlineData(VariableRunState.Running)]
    [InlineData(VariableRunState.Paused)]
    [InlineData(VariableRunState.Replay)]
    public void TheWriteTargetAgreesWithTheDisplayedArm(VariableRunState runState)
    {
        var asset   = FakeAsset.With(Entry("Health", typeof(int)));
        var service = new ComponentEditServiceBuilder().Build();
        using var session = service.Open(7, typeof(int), EditScope.WholeComponent);

        var wroteInitial = VariableEditCommit.CommitInitialValue(
            session, asset, Row("Health", typeof(int)), typeof(int), runState)
            == VariableEditCommit.Outcome.Ok;

        Assert.Equal(VariableValue.ModeFor(runState) == VariableValueMode.Initial, wroteInitial);
    }

    // ══ the policy the launcher already owned, still owned in one place ══════

    /// <summary>
    /// ⭐ ruling 7's availability table: ⛔ <b>"you cannot retype a variable mid-run"</b> ⇒ Properties
    /// is read-only while running; the VALUE dialog stays editable *(its live arm is 59c's)*.
    /// </summary>
    [Theory]
    [InlineData(VariableEditAction.Properties, VariableRunState.Running,  VariableEditAvailability.ReadOnly)]
    [InlineData(VariableEditAction.Properties, VariableRunState.Planning, VariableEditAvailability.Editable)]
    [InlineData(VariableEditAction.EditValue,  VariableRunState.Running,  VariableEditAvailability.Editable)]
    [InlineData(VariableEditAction.EditValue,  VariableRunState.Replay,   VariableEditAvailability.Denied)]
    public void AvailabilityFollowsRunState(
        VariableEditAction action, VariableRunState runState, VariableEditAvailability expected)
        => Assert.Equal(expected,
                        VariableEditPolicy.Resolve(action, runState, Row("Health", typeof(int))));

    // ── helpers ─────────────────────────────────────────────────────────────

    private static VariableRow Row(string name, Type clr, VariableRowKind kind = VariableRowKind.Normal)
        => new(
            Origin:    new VariableRowOrigin(Guid.NewGuid(), default, "sec", name, "Asset"),
            ShortName: name,
            TypeText:  clr.Name,
            ClrType:   clr,
            ReadValue: () => Array.Empty<byte>(),
            RowKind:   kind);

    private static BlackboardVariableEntry Entry(string name, Type clr)
        => new(name, clr, null);

    private static Hrot.Editor.AiShared.Windows.PerspectiveWorkspaceRegistrar Registrar(
        bool withEditService, EditorSelectionStore? store = null)
        => new(
            perspectiveName: "BTree",
            selectionStore:  store ?? new EditorSelectionStore(),
            catalog:         new Hrot.Editor.AiShared.Catalog.AssetCatalog(),
            refactorService: new StubRefactor(),
            debugRegistry:   new DebugSessionRegistry(),
            facetEditService: withEditService ? new ComponentEditServiceBuilder().Build() : null);

    /// <summary>
    /// ⭐ BATCH 84 — <b>internal</b>, so item 3's rails reuse this asset rather than writing a second
    /// one. ⛔ Two fakes of one interface drift, and the drift shows up as a test that "passes".
    /// </summary>
    internal sealed class FakeAsset : Hrot.Editor.AiShared.IEditableAsset, IBlackboardManagedAsset
    {
        private readonly List<BlackboardVariableEntry> _vars;
        private FakeAsset(IEnumerable<BlackboardVariableEntry> vars) => _vars = vars.ToList();
        internal static FakeAsset With(params BlackboardVariableEntry[] vars) => new(vars);

        /// <summary>⭐ What actually landed — the observable outcome, not "the method was called".</summary>
        public Dictionary<string, string?> WrittenJson { get; } = new(StringComparer.Ordinal);

        public Guid AssetId { get; } = Guid.NewGuid();
        public string Name => "FakeAsset";
        public Hrot.Editor.AiShared.AssetKind Kind => Hrot.Editor.AiShared.AssetKind.BTree;
        public string SourceFilePath => "/fake.json";
        public bool IsDirty => false;
        public bool IsEditorOwned => true;
        public event Action? Changed { add { } remove { } }

        public bool IsBlackboardEditorManaged => true;
        public void SetBlackboardEditorManaged(bool managed) { }
        public IReadOnlyList<BlackboardVariableEntry> BlackboardVariables => _vars;
        public void AddVariable(BlackboardVariableEntry entry) => _vars.Add(entry);
        public void RemoveVariable(string name) => _vars.RemoveAll(v => v.Name == name);
        public void UpdateVariableComment(string name, string? comment) { }
        public void UpdateVariableDefaultValueJson(string name, string? json) => WrittenJson[name] = json;
        public void MoveVariable(int sourceIndex, int destIndex) { }
        public void RenameVariable(string oldName, string newName) { }
        public int CountNodesReferencingVariable(string name) => 0;
        public IReadOnlyList<BlackboardAliasBinding> GetAliasesFor(string variableName)
            => Array.Empty<BlackboardAliasBinding>();
        public void AddAlias(string variableName, BlackboardAliasBinding binding) { }
        public void RemoveAlias(string variableName, Guid requiringAssetId, Guid requiringElementId) { }
        public void RemoveVariables(IReadOnlyList<string> names) { }
    }

    private sealed class StubRefactor : Hrot.Editor.AiShared.Refactor.IRefactorService
    {
        public IReadOnlyList<Hrot.Editor.AiShared.Refactor.AssetReferenceInfo> FindReferences(string k)
            => Array.Empty<Hrot.Editor.AiShared.Refactor.AssetReferenceInfo>();
        public IReadOnlyList<Hrot.Editor.AiShared.Refactor.AssetReferenceInfo> FindReferencesInAsset(Guid id)
            => Array.Empty<Hrot.Editor.AiShared.Refactor.AssetReferenceInfo>();
        public Hrot.Editor.AiShared.Refactor.RefactorPreview PreviewRename(
            string f, string t, Hrot.Editor.AiShared.Refactor.RefactorOptions o)
            => new(f, t, Array.Empty<Hrot.Editor.AiShared.Refactor.RefactorFileEdit>(),
                   Array.Empty<Hrot.Editor.AiShared.Refactor.RefactorIssue>());
        public Hrot.Editor.AiShared.Refactor.RefactorResult ApplyRename(
            Hrot.Editor.AiShared.Refactor.RefactorPreview p) => new(true, Array.Empty<string>(), null);
        public Hrot.Editor.AiShared.Refactor.DeletePreview PreviewDelete(
            Guid id, Hrot.Editor.AiShared.Refactor.DeleteOptions o)
            => new(id, Array.Empty<Hrot.Editor.AiShared.Refactor.AssetReferenceInfo>(),
                   Array.Empty<Hrot.Editor.AiShared.Refactor.RefactorIssue>());
        public Hrot.Editor.AiShared.Refactor.RefactorResult ApplyDelete(
            Hrot.Editor.AiShared.Refactor.DeletePreview p) => new(true, Array.Empty<string>(), null);
        public System.Threading.Tasks.Task<Hrot.Editor.AiShared.Refactor.RefactorPreview> PreviewRenameAsync(
            string f, string t, Hrot.Editor.AiShared.Refactor.RefactorOptions o,
            System.Threading.CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(PreviewRename(f, t, o));
        public System.Threading.Tasks.Task<Hrot.Editor.AiShared.Refactor.RefactorResult> ApplyRenameAsync(
            Hrot.Editor.AiShared.Refactor.RefactorPreview p, System.Threading.CancellationToken ct = default)
            => System.Threading.Tasks.Task.FromResult(ApplyRename(p));
    }
}
