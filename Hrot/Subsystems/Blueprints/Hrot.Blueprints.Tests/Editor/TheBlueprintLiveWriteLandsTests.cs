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

    /// <summary>
    /// ⭐⭐ <b>A NON-ZERO structure hash, and Batch 102 (<c>102a</c>) made that load-bearing.</b>
    ///
    /// <para>📐 <b>Measured:</b> this harness used to hand the session a world with <b>no entity and no
    /// component at all</b>, and every rail below still passed — ⛔ because the resolver's
    /// <c>AiPrimitive</c> arm trusted the field table without ever looking at the entity. ⚠ Adding the
    /// read's own identity gate to the write path <b>reddened four rails</b>, which is the gate working:
    /// 📌 the handoff — <i>"a stale layout writing at a valid-looking offset is exactly how memory gets
    /// corrupted."</i></para>
    ///
    /// <para>⛔ It must not be <c>0</c>: a zero-initialised blackboard matches a zero hash by accident,
    /// ⇒ the stale-layout rail would pass under a resolver that never compared anything.</para>
    /// </summary>
    private const ulong StructureHash = 0xA11CE5F00DUL;

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
    /// <para>⭐ Both halves are pinned: the resolver returns the layout's number with the header applied
    /// <b>once</b>, and the staged address is that number <b>unchanged</b>.</para>
    ///
    /// <para>⭐⭐⭐ <b>RE-EXPRESSED, Batch 102 (<c>102a</c>) — the PROPERTY is identical, the OWNER of the
    /// <c>+8</c> moved.</b> ⛔ It used to live in <c>TryWriteWorkingStateField</c, which applied it
    /// UNCONDITIONALLY; ⚠ that is correct for <c>AiPrimitive</c>'s flat block and <b>wrong for an
    /// <c>Instance</c> slot</b>, whose payload the partition allocator places and whose header is a
    /// 16-byte cursor. ⇒ ⭐ the transform now lives in the resolver's <c>AiPrimitive</c> arm, where the
    /// layout is known, and this rail asserts <b>the same "exactly once"</b> one step earlier.</para>
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
    public void TheHeaderIsAppliedExactlyOnce(int? mapOffset)
    {
        var h   = Harness(mapOffset: mapOffset);
        int raw = mapOffset ?? RawOffset;

        int once  = WorkingStateLayout.ComponentOffsetOf(raw);
        int twice = WorkingStateLayout.ComponentOffsetOf(once);
        Assert.NotEqual(once, twice);   // ⭐ the rail is only meaningful because these differ

        var field = h.Session.ResolveWorkingStateField(h.Entity, h.AssetId, FieldName);
        Assert.NotNull(field);
        Assert.Equal(once,  field!.ComponentOffsetBytes);   // ⭐ applied ONCE, by the resolver
        Assert.NotEqual(raw, field.ComponentOffsetBytes);   // ⛔ and it really did convert
        Assert.Equal(Width, field.SizeBytes);

        h.Registrar.EditGestures!.OnEditValue(Row(h.AssetId));
        h.Registrar.EditGestures!.Accept();

        // ⭐ The writer stores what it was given — ⛔ no second conversion anywhere downstream.
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
    /// ⭐⭐⭐ <b>INVERTED, Batch 102 (<c>102a</c>) — an <c>Instance</c> blueprint no longer resolves to
    /// nothing, and the old assertion was a CAPABILITY GAP wearing a safety property's clothes.</b>
    ///
    /// <para>🔴 <b>User:</b> <i>"what is correct about not being able to write into a live blackboard of
    /// instance when simulation is paused?"</i> ⭐⭐ <b>Nothing</b> — 📌 <c>M-36</c> carries the
    /// retraction, and this rail carried the false claim: <i>"answering would corrupt memory."</i>
    /// 📐 The READ has resolved this address all along; ⛔ only the write refused.</para>
    ///
    /// <para>⚠ <b>The old rail's REASONING was sound and its CONCLUSION was not.</b> It was true that
    /// the writer applied the <c>AiPrimitive</c> convention unconditionally, so answering under THAT
    /// contract would have corrupted memory. ⇒ ⭐ <c>102a</c> changed the contract rather than keeping
    /// the refusal — see <c>WorkingStateFieldRef.ComponentOffsetBytes</c>.</para>
    ///
    /// <para>⭐ This entity carries no <c>BlueprintBlackboard*</c> component, so the resolve still
    /// answers <c>null</c> — ⛔ but for the RIGHT reason now, which
    /// <see cref="TheInstanceWriteLandsInTheSlotTests"/> pins from the other side with a real slot.</para>
    /// </summary>
    [Fact]
    public void AnInstanceEntityWithNoBlackboardComponent_StillResolvesToNothing()
    {
        var h = Harness(kind: BlueprintDispatchKind.Instance);

        Assert.Null(h.Session.ResolveWorkingStateField(h.Entity, h.AssetId, FieldName));
        Assert.False(h.Writer.Write(Row(h.AssetId), BitConverter.GetBytes(4242)));
        Assert.Empty(h.Manager.Staged);
    }

    /// <summary>
    /// ⛔⛔⛔ <b>Batch 102 (<c>102a</c>) — A STALE LAYOUT IS REFUSED BY THE WRITE, exactly as it is by
    /// the READ.</b>
    ///
    /// <para>🔴 <b>What was measured.</b> 📐 <c>CaptureAiPrimitiveState:1395</c> refuses to display a
    /// single field when the blackboard's stored hash is not the definition's — ⛔ <b>and the resolve
    /// path had no such check</b>, so the designer would be shown NOTHING while a write happily
    /// scribbled at an offset from a layout the entity no longer has.</para>
    ///
    /// <para>⚠ <b>This is not a "wrong value" case.</b> 📌 <c>Q32</c> §2.1 — a recompiled layout moves
    /// fields, so the old offset lands wherever the new layout put something else, and
    /// <c>Blackboard1024</c> is one component shared by BTree, HSM and Blueprint at disjoint offsets.
    /// ⇒ ⭐ the neighbour need not even be a blueprint's.</para>
    ///
    /// <para>⭐ Both halves: it resolves to nothing, <b>and</b> the writer stages nothing — 📌
    /// <c>M-22</c>, a refusal upstream is only worth asserting where the bytes would have gone.</para>
    /// </summary>
    [Fact]
    public void AStaleLayout_ResolvesToNothing()
    {
        var h = Harness(storedHash: StructureHash ^ 1);   // ⭐ one bit — a recompile, not a garbage entity

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
        Assert.Equal(WorkingStateLayout.ComponentOffsetOf(mapOffset), field!.ComponentOffsetBytes);
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

    // ══ Batch 102 (102b) — the dialog SAYS WHICH REFUSAL IT IS ═══════════════

    /// <summary>
    /// ⭐⭐⭐ <b>FOUR CAUSES, FOUR SENTENCES — read off the PRODUCTION dialog.</b>
    ///
    /// <para>🔴🔴 <b>What this replaces.</b> 📌 <c>M-36</c>: every one of these arrived at the designer
    /// as <i>"no live writer is installed for this host, <b>or</b> it refused the write"</i> — ⛔ <b>an
    /// "or" spanning a MISSING CAPABILITY and a CORRECT GATE.</b> ⚠ That single sentence is why the
    /// coordinator called the Instance refusal <i>"correct"</i> in three consecutive handoffs over a
    /// capability that was simply <b>unbuilt</b>, and why a whole measurement session was spent on it.</para>
    ///
    /// <para>⭐⭐ <b>Asserted through <c>Registrar.EditModal</c>, the object the designer sees</b> — ⛔ not
    /// on <c>LiveWriteAttempt.Message</c>, which would prove only that a string exists. 📌 <c>M-22</c>:
    /// <i>"'is it connected?' is not 'does anything flow?'"</i> — the message has to survive the delegate,
    /// the commit, the binder and the modal, and each of those is a place it used to be dropped.</para>
    ///
    /// <para>⚠ <b>WHICH LAYER IS FAKED</b> *(📌 <c>M-29</c>)*: the gesture is raised by calling
    /// <c>OnEditValue</c> and OK by calling <c>Ok()</c> — ⛔ the ImGui click itself. ⭐ Everything from
    /// the binder down is production, including the real <c>BlueprintDebugSession</c>.</para>
    ///
    /// <para>⛔ <b>Distinctness is asserted as a SET</b>, not sentence by sentence: the defect was two
    /// causes sharing one string, so what must hold is that no two of them collide.</para>
    /// </summary>
    [Fact]
    public void EachRefusalReachesTheDesignerAsItsOwnSentence()
    {
        var messages = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (cause, rig) in Refusals())
        {
            rig.Registrar.EditGestures!.OnEditValue(Row(rig.AssetId));
            var outcome = rig.Registrar.EditModal!.Ok();

            Assert.Equal(VariableEditCommit.Outcome.LiveWriteUnavailable, outcome);

            var message = rig.Registrar.EditModal!.RefusalMessage;
            Assert.False(string.IsNullOrWhiteSpace(message), $"'{cause}' told the designer nothing.");

            // ⛔ The old text, verbatim — its "or" is the defect, so its return is a failure.
            Assert.DoesNotContain("or it refused the write", message!, StringComparison.Ordinal);

            messages[cause] = message!;
            Assert.Empty(rig.Manager.Staged);   // ⭐ a refusal that stages bytes is not a refusal
        }

        Assert.Equal(4, messages.Count);
        Assert.Equal(
            messages.Count,
            messages.Values.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>⭐ One rig per cause, each induced the way production would meet it — ⛔ never by
    /// constructing the refusal directly.</summary>
    private static IEnumerable<(string Cause, Rig Rig)> Refusals()
    {
        // ⛔ Nothing selected. ⚠ 📌 R-78 — the row's own entity is the chameleon sentinel, so there is
        //    no fallback to fall back to.
        var noEntity = Harness();
        noEntity.Store.SelectedEntity = null;
        yield return ("no selected entity", noEntity);

        // ⛔ No blueprint document open. ⚠ 📌 R-66 — this is NOT "the sim is running".
        var noSession = Harness();
        noSession.Sessions.SetActiveSession(null);
        yield return ("no debug session", noSession);

        // ⛔ A STALE LAYOUT — the entity's blackboard carries a different structure hash, so the
        //    compiled offsets describe a layout it no longer has. ⭐ The read refuses this too.
        yield return ("stale layout", Harness(storedHash: StructureHash ^ 1));

        // ⛔ The SESSION is not frozen while the editor thinks it is. ⭐ The one refusal the designer
        //    can act on, and the sentence must be the one that says how.
        yield return ("session not frozen", Harness(paused: false));
    }

    /// <summary>
    /// ⭐⭐ <b>And the FIFTH cause — no writer at all — is named by <c>Hrot.Editor.AiShared</c> itself.</b>
    ///
    /// <para>⭐⭐⭐ <b>This is the one that matters most</b>, because it is the shape 📌 <c>M-36</c> is
    /// about: ⛔ <b>the run state SAID yes and the mechanism never arrived.</b> ⚠ BTree and HSM sit in
    /// exactly this state today, deliberately — ⇒ their designers must be told it is a missing
    /// capability of the HOST, ⛔ not a property of their variable.</para>
    /// </summary>
    [Fact]
    public void WithNoLiveWriterAtAll_TheDialogSaysTheHostHasNone()
    {
        var result = VariableEditCommit.CommitWithDetail(
            new StubSession(), asset: null, Row(Guid.NewGuid()), typeof(int),
            VariableRunState.Paused, writeLive: null);

        Assert.Equal(VariableEditCommit.Outcome.LiveWriteUnavailable, result.Outcome);
        Assert.Contains("host", result.Detail!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("or it refused the write", result.Detail!, StringComparison.Ordinal);
    }

    /// <summary>⭐ A session that is never committed — this rail never reaches the commit. ⛔ Not a
    /// stand-in for StructEdit anywhere else.</summary>
    private sealed class StubSession : StructEdit.Core.IEditSession
    {
        public StructEdit.Core.EditDocument Document      => throw new NotSupportedException();
        public bool                         IsDirty       => false;
        public StructEdit.Core.EditRebuildState RebuildState => StructEdit.Core.EditRebuildState.Stable;
        public void MarkStructuralChange() { }
        public void RebuildDocument() { }
        public StructEdit.Core.ValidationResult Validate() => StructEdit.Core.ValidationResult.Ok();
        public object Commit() => throw new NotSupportedException();
        public void Cancel() { }
        public void Dispose() { }
    }

    // ── the harness ─────────────────────────────────────────────────────────

    private sealed record Rig(
        Guid AssetId, Entity Entity, BlueprintDebugSession Session,
        TheSessionWritesWhileFrozenTests.RecordingManager Manager,
        Hrot.Editor.AiShared.Selection.EditorSelectionStore Store, BlueprintLiveValueWriter Writer,
        PerspectiveWorkspaceRegistrar Registrar,
        // ⭐ Batch 102 (102b) — exposed so a rail can take the SESSION away and see what the dialog
        //   then says. ⛔ Without it "no document is open" is unreachable through the production chain.
        Hrot.Editor.AiShared.Debug.DebugSessionRegistry Sessions);

    /// <summary>
    /// ⭐ Everything REAL except the draw layer: a real <c>BlueprintRegistry</c>, a real
    /// <c>BlueprintDebugSession</c>, the real services bundle and the real registrar.
    /// ⚠ 📌 <c>R-67</c> — this builds its OWN composition root, so it cannot see a composition-root
    /// defect; <see cref="TheCompositionRootHandsBlueprintALiveWriter"/> is what covers that.
    /// </summary>
    private static unsafe Rig Harness(
        bool paused = true,
        BlueprintDispatchKind kind = BlueprintDispatchKind.AiPrimitive,
        int? mapOffset = null,
        ulong? storedHash = null)
    {
        var assetId = Guid.NewGuid();

        // ⭐⭐ Batch 102 (102a) — a REAL world with a REAL blackboard, stamped with the definition's
        //    structure hash exactly as the generated thunk stamps it (📌 AiPrimitiveStateMetadataTests:89).
        // ⛔ Before this, the session was handed `new EntityRepository()` and a fabricated Entity(7,1)
        //    that existed in no world at all — see the StructureHash remark for what that concealed.
        var world = new EntityRepository();
        world.RegisterComponent<Blackboard1024>();
        var entity = world.CreateEntity();
        if (kind == BlueprintDispatchKind.AiPrimitive)
        {
            world.AddComponent(entity, default(Blackboard1024));
            ref var bb = ref world.GetComponentRW<Blackboard1024>(entity);
            fixed (Blackboard1024* p = &bb)
                *(ulong*)p = storedHash ?? StructureHash;
        }

        var registry = new BlueprintRegistry();
        var def = new BlueprintDefinition
        {
            Name          = "LiveWriteRail",
            Kind          = kind,
            StructureHash = StructureHash,
            StateSize     = 64,
            StateFields   = new Dictionary<string, BlueprintFieldDescriptor>(StringComparer.Ordinal)
            {
                [FieldName] = new BlueprintFieldDescriptor(FieldName, typeof(int), RawOffset, Width, ""),
            },
        };
        int id = BlueprintIdHash.Compute(assetId);
        if (kind == BlueprintDispatchKind.AiPrimitive) registry.RegisterAiPrimitive(id, def);
        else                                           registry.RegisterInstance(id, def);

        var session = new BlueprintDebugSession(registry, world, new MockTimeController());
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
            writeLive:  writer.WriteLive);

        return new Rig(assetId, entity, session, manager, store, writer, registrar, debugRegistry);
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
