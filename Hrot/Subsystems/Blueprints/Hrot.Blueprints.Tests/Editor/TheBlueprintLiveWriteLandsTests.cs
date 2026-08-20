using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Tests.Debug;
using Hrot.Editor;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Validation;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b>Batch 97 (<c>97c</c>) — a paused edit LANDS IN THE BLACKBOARD, end to end.</b>
///
/// <para>🔴🔴 <b>What was measured before building.</b> <c>TryWriteWorkingStateField</c> (Batch 84) and
/// the <c>WriteLiveValue</c> delegate both shipped complete and tested with <b>ZERO production call
/// sites</b>, and 📐 Batch 96 measured that the composition root passed no <c>writeLive</c> ⇒
/// <c>VariableEditCommit.Commit</c> answered <c>LiveWriteUnavailable</c> for <b>every paused edit, on
/// every host, always.</b> ⛔ <c>R-67</c>'s seventh instance.</para>
///
/// <para>⭐⭐⭐ <b>WHICH LAYER EACH RAIL FAKES</b> — stated because 📌 <c>M-22</c>
/// (<i>"'is it connected?' is not 'does anything flow?'"</i>) and <c>M-29</c> both demand it:</para>
/// <list type="table">
///   <item><term>⭐ <see cref="TheCompositionRootHandsBlueprintALiveWriter"/></term><description>
///   <b>Fakes NOTHING and RUNS nothing</b> — it reads <c>EditorSubsystem.cs</c> as text. ⛔ Weaker than
///   a behavioural rail and honest about it: <c>EditorSubsystem</c> cannot be constructed headless.
///   ⭐ It is the ONLY rail here that can see a composition-root defect — 📌 <c>R-67</c>: <i>a rail
///   that builds its own composition root cannot see one</i>, and every rail below builds its
///   own.</description></item>
///   <item><term>⭐⭐ <see cref="APausedEdit_LandsInTheBlackboard"/></term><description>
///   <b>Real</b> <c>PerspectiveWorkspaceServices</c> → <c>CreateRegistrar</c> → binder → launcher →
///   <c>StructEdit</c> session → <c>VariableEditCommit</c> → <c>BlueprintLiveValueWriter</c> → real
///   <c>BlueprintDebugSession</c>. ⚠ <b>Faked: the DRAW layer</b> (📌 <c>R-21</c>/<c>R-62</c> — the
///   gesture is raised by calling <c>OnEditValue</c>, as no headless rail can drive ImGui)
///   <b>and the ECS drain</b> — staging→world is <c>StagedFieldWriteEntryPointTests</c>'
///   job.</description></item>
/// </list>
/// </summary>
public sealed class TheBlueprintLiveWriteLandsTests
{
    // ⭐ A field at a NON-ZERO offset on purpose: at offset 0 the raw and header-applied offsets
    //   differ by the header, and a rail that used 0 would pass under EITHER convention.
    private const int RawOffset = 12;
    private const int Width     = 4;
    private const string FieldName = "Health";

    // ══ the composition root — the only rail that can see the defect ═════════

    /// <summary>
    /// 🔴🔴 <b>RED before <c>97c</c>:</b> the Blueprint <c>CreateRegistrar</c> call passed
    /// <c>liveValueProvider</c> and no <c>writeLive</c>.
    ///
    /// <para>⭐⭐ <b>And the asymmetry is asserted too</b>, because it is a DECISION rather than an
    /// omission: BTree and HSM have no staged surgical write, so their paused edits must keep
    /// answering <c>LiveWriteUnavailable</c>. ⛔ A <c>writeLive</c> appearing on their calls would mean
    /// somebody guessed a byte offset for a host whose state is laid out another way — 📌 <c>Q32</c>
    /// §2.1: <i>"an out-of-range offset is MEMORY CORRUPTION, not a wrong value."</i></para>
    /// </summary>
    [Fact]
    public void TheCompositionRootHandsBlueprintALiveWriter()
    {
        var text = File.ReadAllText(RepoFile("Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs"));

        Assert.True(
            text.Contains("new BlueprintLiveValueWriter(", StringComparison.Ordinal),
            "EditorSubsystem no longer constructs the blueprint live writer — a paused edit cannot land.");

        var blueprint = CreateRegistrarCall(text, "\"Blueprint\"");
        Assert.True(
            blueprint.Contains("writeLive:", StringComparison.Ordinal),
            "The Blueprint registrar is built without writeLive, so VariableEditCommit answers "
          + "LiveWriteUnavailable for every paused edit. This is R-67's seventh instance returning.");

        foreach (var host in new[] { "\"BTree\"", "\"HSM\"" })
            Assert.False(
                CreateRegistrarCall(text, host).Contains("writeLive:", StringComparison.Ordinal),
                $"{host} was given a live writer. Neither host has a staged surgical write; supplying "
              + "one means a guessed offset into Blackboard1024, which BTree, HSM and Blueprint SHARE.");
    }

    /// <summary>⭐ The text of the <c>CreateRegistrar(…)</c> call whose first argument is <paramref name="host"/>.</summary>
    private static string CreateRegistrarCall(string text, string host)
    {
        // ⚠ Split on the call rather than searching by line number, so the rail survives edits above
        //   it. ⭐ A chunk BELONGS to a host when its first argument is that host's name.
        var chunks = text.Split("CreateRegistrar(").Skip(1)
                         .Where(c => c.TrimStart().StartsWith(host, StringComparison.Ordinal))
                         .ToList();
        Assert.True(chunks.Count == 1,
            $"Expected exactly one CreateRegistrar call for {host}; found {chunks.Count}.");

        // ⭐ Up to this call's closing ");" -- ⛔ never into the next call's arguments.
        var body = chunks[0];
        int end = body.IndexOf(");", StringComparison.Ordinal);
        Assert.True(end > 0, $"Could not find the end of the {host} CreateRegistrar call.");
        return body[..end];
    }

    // ══ the write, through the production chain ══════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The rail that has been missing all week.</b> One gesture, one Accept, and the bytes are
    /// STAGED at the field's address — ⛔ <b>not</b> an assertion that <c>writeLive</c> is non-null.
    /// 📌 <c>M-22</c>.
    /// </summary>
    [Fact]
    public void APausedEdit_LandsInTheBlackboard()
    {
        var h = Harness();

        h.Registrar.EditGestures!.OnEditValue(Row(h.AssetId));
        var outcome = h.Registrar.EditGestures!.Accept();

        Assert.Equal(VariableEditCommit.Outcome.Ok, outcome);
        var staged = Assert.Single(h.Manager.Staged);
        Assert.Equal(typeof(Blackboard1024), staged.ComponentType);
        Assert.Equal(Width, staged.Bytes.Length);
        // ⭐ The VALUE, not just the width — a wrapper leaking through would be the right size and the
        //   wrong bytes (📌 97a's ScalarEditBox).
        Assert.Equal(4242, BitConverter.ToInt32(staged.Bytes, 0));
    }

    /// <summary>
    /// ⛔⛔⛔ <b>THE HEADER IS APPLIED EXACTLY ONCE.</b>
    ///
    /// <para>📐 The READ path computes <c>WorkingStateLayout.ComponentOffsetOf(field.OffsetBytes)</c>
    /// before slicing, so a resolver that copied the read's <c>start</c> would hand an
    /// ALREADY-CONVERTED offset to <c>TryWriteWorkingStateField</c>, which converts again. ⇒ the write
    /// lands <c>HeaderBytes</c> past the field — ⚠ <b>on the NEIGHBOUR</b>, silently, and
    /// <c>Blackboard1024</c> is one component shared by BTree, HSM and Blueprint at disjoint offsets,
    /// so the neighbour may not even be a blueprint's. 📌 <c>Q32</c> §2.1.</para>
    ///
    /// <para>⭐ Both halves are pinned: the resolver returns the layout's RAW number, and the staged
    /// address is the header applied to it <b>once</b>.</para>
    /// </summary>
    /// <remarks>
    /// ⭐⭐ <b>BOTH ARMS, and that is not padding.</b> 📐 <b>Measured by the revert probe:</b> mutating
    /// only the debug-map arm to return a CONVERTED offset left this rail GREEN when it covered the
    /// <c>def.StateFields</c> fallback alone. ⇒ ⛔ <b>two tables means two places the convention can be
    /// got wrong</b>, and a rail over one of them proves nothing about the other.
    /// </remarks>
    [Theory]
    [InlineData(null)]   // ⭐ no debug map ⇒ the def.StateFields FALLBACK arm
    [InlineData(40)]     // ⭐ a debug map  ⇒ the mapIndex.StateLayout arm
    public void TheOffsetIsRaw_AndTheHeaderIsAppliedExactlyOnce(int? mapOffset)
    {
        var h   = Harness(mapOffset: mapOffset);
        int raw = mapOffset ?? RawOffset;

        var field = h.Session.ResolveWorkingStateField(h.Entity, h.AssetId, FieldName);
        Assert.NotNull(field);
        Assert.Equal(raw,   field!.RawOffsetBytes);          // ⛔ RAW — unconverted
        Assert.Equal(Width, field.SizeBytes);

        h.Registrar.EditGestures!.OnEditValue(Row(h.AssetId));
        h.Registrar.EditGestures!.Accept();

        int once  = WorkingStateLayout.ComponentOffsetOf(raw);
        int twice = WorkingStateLayout.ComponentOffsetOf(once);
        Assert.NotEqual(once, twice);   // ⭐ the rail is only meaningful because these differ
        Assert.Equal(once, Assert.Single(h.Manager.Staged).ByteOffset);
    }

    /// <summary>
    /// ⛔⛔ <b>A payload of the wrong width is REFUSED, and stages nothing.</b> Wider overruns the
    /// neighbouring field; narrower leaves half the old value in place. ⭐ Both are silent, and neither
    /// shows as a wrong number in the cell being edited — 📌 <c>Q32</c> §2.1 again.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(8)]
    public void APayloadOfTheWrongWidth_IsRefused(int width)
    {
        var h = Harness();

        Assert.False(h.Writer.Write(Row(h.AssetId), new byte[width]));
        Assert.Empty(h.Manager.Staged);
    }

    /// <summary>
    /// ⛔ <b>An <c>Instance</c> blueprint resolves to NOTHING.</b> Its fields are offset within a
    /// per-instance payload — a different address space — and the writer applies the
    /// <c>AiPrimitive</c> convention. ⚠ Answering would not mis-report a value, it would corrupt memory.
    /// </summary>
    [Fact]
    public void ADispatchKindLaidOutAnotherWay_ResolvesToNothing()
    {
        var h = Harness(kind: BlueprintDispatchKind.Instance);

        Assert.Null(h.Session.ResolveWorkingStateField(h.Entity, h.AssetId, FieldName));
        Assert.False(h.Writer.Write(Row(h.AssetId), BitConverter.GetBytes(4242)));
        Assert.Empty(h.Manager.Staged);
    }

    /// <summary>⛔ An unknown name resolves to nothing — ⭐ fail closed, never a guessed offset.</summary>
    [Fact]
    public void AnUnknownName_ResolvesToNothing()
    {
        var h = Harness();
        Assert.Null(h.Session.ResolveWorkingStateField(h.Entity, h.AssetId, "NoSuchVariable"));
    }

    /// <summary>
    /// ⭐⭐ <b>The DEBUG MAP wins over the definition's fallback</b>, which is the same precedence the
    /// READ uses (<c>mapIndex.StateLayout.Fields</c> first, <c>def.StateFields</c> second).
    ///
    /// <para>⚠ <b>Why this is railed rather than shared.</b> 📐 The read ITERATES every field to produce
    /// values; this LOOKS ONE UP by name — the loops cannot be one loop. ⇒ ⭐ the agreement that
    /// matters is the ORDER of the two tables, and that is what this pins. 📌 Batch 96's rule: a rail
    /// must take its input from the same object the UI takes it from.</para>
    /// </summary>
    [Fact]
    public void TheDebugMapWins_JustAsItDoesForTheRead()
    {
        const int mapOffset = 40;
        var h = Harness(mapOffset: mapOffset);

        var field = h.Session.ResolveWorkingStateField(h.Entity, h.AssetId, FieldName);
        Assert.Equal(mapOffset, field!.RawOffsetBytes);
    }

    // ══ the honest refusals ══════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐ <b>No selected entity ⇒ <c>false</c>, and the row's own entity is NOT used as a fallback.</b>
    ///
    /// <para>📌 <c>R-78</c>: a Details row carries <c>entity: default</c> as the CHAMELEON SENTINEL —
    /// <i>"whoever is selected"</i> — ⛔ so falling back to it would write into entity 0. ⭐ And the
    /// entity must be the one the READ displayed, or the designer edits one entity while looking at
    /// another's value.</para>
    /// </summary>
    [Fact]
    public void WithNoSelectedEntity_TheWriteRefuses()
    {
        var h = Harness();
        h.Store.SelectedEntity = null;

        Assert.False(h.Writer.Write(Row(h.AssetId), BitConverter.GetBytes(4242)));
        Assert.Empty(h.Manager.Staged);
    }

    /// <summary>
    /// ⛔ <b>Not frozen ⇒ nothing is staged</b> — 📌 ruling 15. ⭐ The gate is the SESSION's, not a
    /// second rule in the writer: the editor's run state and the session's pause flag are two
    /// observations, and the write must survive their disagreement without corrupting anything.
    /// </summary>
    [Fact]
    public void WhileTheSessionIsNotFrozen_NothingIsStaged()
    {
        var h = Harness(paused: false);

        Assert.False(h.Writer.Write(Row(h.AssetId), BitConverter.GetBytes(4242)));
        Assert.Empty(h.Manager.Staged);
    }

    /// <summary>⛔ No active blueprint session ⇒ <c>false</c>, never a throw into the dialog.</summary>
    [Fact]
    public void WithNoActiveSession_TheWriteRefuses()
    {
        var store = new Hrot.Editor.AiShared.Selection.EditorSelectionStore { SelectedEntity = new Entity(7, 1) };
        var writer = new BlueprintLiveValueWriter(() => null, store);

        Assert.False(writer.Write(Row(Guid.NewGuid()), BitConverter.GetBytes(4242)));
    }

    // ── the harness ─────────────────────────────────────────────────────────

    private sealed record Rig(
        Guid AssetId, Entity Entity, BlueprintDebugSession Session,
        TheSessionWritesWhileFrozenTests.RecordingManager Manager,
        Hrot.Editor.AiShared.Selection.EditorSelectionStore Store, BlueprintLiveValueWriter Writer,
        PerspectiveWorkspaceRegistrar Registrar);

    /// <summary>
    /// ⭐ Everything REAL except the draw layer: a real <c>BlueprintRegistry</c>, a real
    /// <c>BlueprintDebugSession</c>, the real services bundle and the real registrar.
    /// ⚠ 📌 <c>R-67</c> — this builds its OWN composition root, so it cannot see a composition-root
    /// defect; <see cref="TheCompositionRootHandsBlueprintALiveWriter"/> is what covers that.
    /// </summary>
    private static Rig Harness(
        bool paused = true,
        BlueprintDispatchKind kind = BlueprintDispatchKind.AiPrimitive,
        int? mapOffset = null)
    {
        var assetId = Guid.NewGuid();
        var entity  = new Entity(7, 1);

        var registry = new BlueprintRegistry();
        var def = new BlueprintDefinition
        {
            Name          = "LiveWriteRail",
            Kind          = kind,
            StructureHash = 0,
            StateSize     = 64,
            StateFields   = new Dictionary<string, BlueprintFieldDescriptor>(StringComparer.Ordinal)
            {
                [FieldName] = new BlueprintFieldDescriptor(FieldName, typeof(int), RawOffset, Width, ""),
            },
        };
        int id = BlueprintIdHash.Compute(assetId);
        if (kind == BlueprintDispatchKind.AiPrimitive) registry.RegisterAiPrimitive(id, def);
        else                                           registry.RegisterInstance(id, def);

        var session = new BlueprintDebugSession(registry, new EntityRepository(), new MockTimeController());
        var manager = new TheSessionWritesWhileFrozenTests.RecordingManager();
        session.SetDataBreakpointManager(manager);
        if (paused) session.Pause();

        if (mapOffset is { } m)
            session.RegisterDebugMap(new Hrot.Blueprints.Core.Compiler.Emit.DebugMap
            {
                AssetId     = assetId,
                BlueprintId = id,
                StateLayout = new Hrot.Blueprints.Core.Compiler.Emit.DebugStateLayout
                {
                    Fields = new[] { new Hrot.Blueprints.Core.Compiler.Emit.StateLayoutField(FieldName, "int", m, Width) },
                },
            });

        var store = new Hrot.Editor.AiShared.Selection.EditorSelectionStore { SelectedEntity = entity };
        var debugRegistry = new Hrot.Editor.AiShared.Debug.DebugSessionRegistry();
        debugRegistry.SetActiveSession(session);

        var writer = new BlueprintLiveValueWriter(
            () => debugRegistry.ActiveSession as IBlueprintDebugSession, store);

        var services = new PerspectiveWorkspaceServices(
            new Hrot.Editor.AiShared.Catalog.AssetCatalog(),
            new NoRefactor(),
            debugRegistry,
            new StructEdit.Reflection.ComponentEditServiceBuilder().Build(),
            // ⭐ Paused = the sim is up AND frozen. 📌 Ruling 15 — that is the only state a live write
            //   is legal in, and it is derived here exactly as EditorSubsystem derives it.
            isSimUp:  () => true,
            isFrozen: () => true);

        var registrar = services.CreateRegistrar(
            "Blueprint", store,
            validators: Array.Empty<IAssetValidator>(),
            writeLive:  writer.Write);

        return new Rig(assetId, entity, session, manager, store, writer, registrar);
    }

    /// <summary>
    /// ⭐ A Details row for the field. ⚠ <c>entity: default</c> is 📌 <c>R-78</c>'s CHAMELEON SENTINEL,
    /// not a defect — the row means <i>"whoever is selected"</i>, and the writer resolves that from the
    /// selection store.
    /// </summary>
    private static VariableRow Row(Guid assetId)
        => new(
            Origin:    new VariableRowOrigin(assetId, default, "vars", FieldName, "LiveWriteRail"),
            ShortName: FieldName,
            TypeText:  "int",
            ClrType:   typeof(int),
            ReadValue: () => Array.Empty<byte>(),
            RowKind:   VariableRowKind.Normal,
            // ⭐ Batch 95 — the declaration travels with the row, which is what lets the binder open a
            //   session at all on a Blueprint row (BlueprintAsset is not an IBlackboardManagedAsset).
            ReadDeclaration: () => new BlackboardVariableEntry(FieldName, typeof(int), null, DefaultValueJson: "4242"));

    private static string RepoFile(string relative)
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir != null; i++)
        {
            var candidate = Path.Combine(dir, relative);
            if (File.Exists(candidate)) return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new FileNotFoundException($"Could not locate '{relative}' above {AppContext.BaseDirectory}.");
    }

    /// <summary>⭐ Nothing here exercises refactoring.</summary>
    private sealed class NoRefactor : Hrot.Editor.AiShared.Refactor.IRefactorService
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
