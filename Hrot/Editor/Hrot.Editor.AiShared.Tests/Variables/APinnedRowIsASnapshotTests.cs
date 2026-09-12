using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Fdp.Core;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐⭐ <b>Batch 93 measured the defect here; Batch 94 (<c>94a</c>) FIXED it, and these rails were
/// INVERTED rather than deleted — they are the acceptance test.</b> 📌 <c>Q46</c> §4f:
/// <i>"deleting it would remove the only proof the fix works."</i>
///
/// <para>⭐ <b>The class name is kept deliberately.</b> A pinned row IS still a snapshot of its
/// <b>identity and accessors</b> — ⭐ what changed is that the accessor is now a <b>camera</b>
/// *(closes over the provider)* rather than a <b>photograph</b> *(closed over one frame's value)*.</para>
///
/// <para>⭐⭐⭐ <b>Batch 93 — THE MEASUREMENT THAT STOPPED THE BATCH (<c>BP-344</c>).</b>
///
/// <para>📄 <b>The handoff's premise, verbatim (§2):</b> <i>"Batch 90's live arms are read PER FRAME,
/// and a pinned row carries its arm with it ⇒ a row pinned from a live Details row is live in the Watch
/// with no new polling code."</i> ⛔⛔ <b>MEASURED FALSE</b>, and the handoff's own §7 makes that a
/// STOP: <i>"If the value feed does NOT come free from Batch 90's arms — STOP AND REPORT."</i></para>
///
/// <para>⭐⭐⭐ <b>The distinction the premise misses.</b> The arms ARE invoked every frame — but the
/// arm a row source builds <b>closes over THAT FRAME'S VALUE</b>, not over the provider. ⇒ liveness in
/// the Details table comes from <b>rebuilding the ROW every frame</b>
/// (<c>VariableTableModel.Build()</c> → <c>GetRows()</c>), ⛔ <b>not</b> from the delegate.
/// <c>PinnedVariableRowSource.GetRows()</c> returns its stored records untouched ⇒ ⭐ <b>a pinned row
/// is a SNAPSHOT taken at pin time.</b></para>
///
/// <para>✅ <b>FIXED, Batch 94.</b> Both arms of both row sources now close over the provider, so a
/// pinned row tracks the run. ⭐ Each rail below names what it asserted BEFORE and what it asserts
/// NOW.</para>
///
/// <para>⭐ <b>What is NOT broken, and this matters for the fix:</b>
/// <see cref="AHandBuiltRowWithALiveArmStaysLiveWhenPinned"/> proves the <b>row type and the store are
/// fine</b> — a <c>VariableRow</c> whose arm closes over the SOURCE stays live through
/// <c>PinnedVariableRowSource</c> unchanged. ⇒ ⭐⭐ <b>the gap is in the two row SOURCES, not in the
/// Watch.</b> ⛔ It is still not a wiring fix — see the report.</para>
/// </summary>
public sealed class APinnedRowIsASnapshotTests
{
    private static readonly Guid AssetId = new("aaaaaaaa-0000-0000-0000-00000000000a");

    // ══ the object arm — Blueprint's feed ═════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE headline claim, INVERTED.</b> One live map, two surfaces, one frame apart —
    /// ⭐ <b>both now read 99</b>.
    ///
    /// <para>🔴 <b>Batch 93 asserted the opposite here</b> *(Details 99, Watch 10)* and said so; this is
    /// that assertion flipped, ⛔ not a new test. 📌 <c>Q46</c> §2 rule 1.</para>
    /// </summary>
    [Fact]
    public void ARowPinnedFromTheDetailsSourceTracksTheRun()
    {
        var map     = new Dictionary<string, object> { ["Health"] = 10 };
        var section = ObjectArmSource(() => map, "Health");

        // FRAME 1 — the designer pins a row the Details table just drew.
        var pinned = new PinnedVariableRowSource();
        pinned.Pin(section.GetRows()[0]);

        // the run advances
        map["Health"] = 99;

        // FRAME 2 — both surfaces rebuild.
        Assert.Equal(99, section.GetRows()[0].ReadValueObject!.Invoke());

        // ✅ Batch 94: the pin is a CAMERA. 🔴 Batch 93 asserted 10 here.
        Assert.Equal(99, pinned.GetRows()[0].ReadValueObject!.Invoke());
    }

    /// <summary>
    /// ⭐⭐ <b>The byte arm tracks too — and this one is load-bearing.</b> ⛔⛔ Fixing only the object
    /// arm would make pinning work on Blueprint and <b>silently freeze on BTree/HSM</b>, which is
    /// exactly the split <c>U-6</c> removed *(<c>Q46</c> §4a)*. 🔴 Batch 93 asserted the freeze.
    /// </summary>
    [Fact]
    public void TheByteArmTracksOnTheSameRule()
    {
        int live    = 10;
        var section = ByteArmSource(_ => I32(live), "Health");

        var pinned = new PinnedVariableRowSource();
        pinned.Pin(section.GetRows()[0]);

        live = 99;

        Assert.Equal(99, ReadI32(section.GetRows()[0]));
        Assert.Equal(99, ReadI32(pinned.GetRows()[0]));   // ✅ Batch 94; 🔴 Batch 93 asserted 10
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>(pending)</c> UNPENDS, INVERTED.</b> 🔴 Batch 93 asserted that a variable the run
    /// starts writing AFTER the pin says <c>(pending)</c> for ever. ✅ Batch 94 (<c>94e</c>) added the
    /// optional <c>ReadWritten</c> arm, so the answer is asked again rather than remembered.
    ///
    /// <para>⚠ <b>The raw field is deliberately still frozen</b> — it is the pin-time answer and the
    /// record is immutable. ⭐ What changed is that every reader asks <see cref="VariableRow.WrittenNow"/>
    /// instead, which prefers the arm. ⛔ The <c>bool</c> was NOT widened *(<c>Q46</c> §4e)*.</para>
    /// </summary>
    [Fact]
    public void PendingUnpendsWhenTheRunStartsWritingAfterThePin()
    {
        var map     = new Dictionary<string, object>();          // nothing written yet
        var section = ObjectArmSource(() => map, "Health");

        var pinned = new PinnedVariableRowSource();
        pinned.Pin(section.GetRows()[0]);
        Assert.False(pinned.GetRows()[0].WrittenNow, "nothing was written at pin time");

        map["Health"] = 42;                                      // the run writes it

        Assert.True(section.GetRows()[0].WrittenNow, "Details notices the write");
        Assert.True(pinned.GetRows()[0].WrittenNow,  "✅ Batch 94; 🔴 Batch 93 asserted false");

        // ⚠ …and the pin-time FIELD is still what it was, which is why readers must ask WrittenNow.
        Assert.False(pinned.GetRows()[0].HasEverBeenWritten);
    }

    // ══ what is NOT broken ════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The row type and the store are FINE.</b> A <c>VariableRow</c> whose arm closes over the
    /// SOURCE — rather than over a frame's value — stays live through <c>PinnedVariableRowSource</c>
    /// with no change to the store, the window, or the table.
    ///
    /// <para>⇒ ⭐⭐ <b>the gap is in the two row SOURCES' <c>ToRow</c></b>, ⛔ not in Watch, and ⛔ not
    /// in anything <c>93a</c> was asked to build. ⚠ It is still not a wiring fix: what a
    /// <c>VariableRow</c> MEANS changes with it.</para>
    /// </summary>
    [Fact]
    public void AHandBuiltRowWithALiveArmStaysLiveWhenPinned()
    {
        int live = 10;
        var row  = new VariableRow(
            Origin:          new VariableRowOrigin(AssetId, default, "s", "Health", "Alpha"),
            ShortName:       "Health",
            TypeText:        "int",
            ClrType:         typeof(int),
            ReadValue:       () => Array.Empty<byte>(),
            ReadValueObject: () => live);            // ⭐ closes over the SOURCE, not over a value

        var pinned = new PinnedVariableRowSource();
        pinned.Pin(row);

        live = 99;

        Assert.Equal(99, pinned.GetRows()[0].ReadValueObject!.Invoke());
    }

    // ══ 94b — the pulse reaches the CONSTRUCTED row ═══════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 94 (<c>94b</c>) — every production row now carries a tick source.</b>
    ///
    /// <para>🔴🔴 <b>Before this, every production row passed <c>AssetTick: null</c></b>
    /// *(<c>BlackboardSectionRowSource:95</c> literally, and <c>SectionVariableRowSource</c> because no
    /// caller supplied one)* ⇒ <c>VariableChangeMonitor</c> returned <c>None</c> on its first line, in
    /// production, always. ⇒ ⛔ <b>no host has ever shown a change highlight.</b> 📌 <c>R-67</c>: it was
    /// not a missing capability, it was a missing wire.</para>
    ///
    /// <para>⭐⭐ <b>Asked of the CONSTRUCTED ROW</b> — ⛔ never of the source's constructor arguments,
    /// which is the shape that cannot see a wiring defect.</para>
    /// </summary>
    [Fact]
    public void AProductionRowCarriesTheBehaviourFramePulse()
    {
        var row = ObjectArmSource(() => new Dictionary<string, object> { ["Health"] = 1 }, "Health")
                  .GetRows()[0];

        Assert.NotNull(row.AssetTick);
        Assert.Equal(BehaviorFrame.Current, row.AssetTick!.Invoke());
    }

    /// <summary>⭐ …and the row's pulse MOVES with the sim — ⛔ it is not a captured constant either.</summary>
    [Fact]
    public void TheRowsPulseMovesWhenTheBehaviourFrameAdvances()
    {
        var row = ObjectArmSource(() => new Dictionary<string, object> { ["Health"] = 1 }, "Health")
                  .GetRows()[0];

        uint before = row.AssetTick!.Invoke()!.Value;
        BehaviorFrame.Advance();

        Assert.NotEqual(before, row.AssetTick!.Invoke()!.Value);
    }

    /// <summary>
    /// ⚠ <b>An explicitly supplied tick source still WINS</b> — ⭐ the pulse is a default, ⛔ not an
    /// override, so a host with a finer clock is not silently replaced.
    /// </summary>
    [Fact]
    public void AnExplicitAssetTickIsNotOverriddenByThePulse()
    {
        var source = new SectionVariableRowSource(
            assetId: AssetId, assetName: "Alpha", entity: default, section: "s",
            schema:  new StubSchema(new[] { "Health" }),
            readRaw: null,
            assetTick: () => 4242u);

        Assert.Equal(4242u, source.GetRows()[0].AssetTick!.Invoke());
    }

    /// <summary>⚠ …and the store hands back the very record it was given — ⛔ it neither rebuilds nor
    /// re-resolves, which is why the freeze is total rather than partial.</summary>
    [Fact]
    public void ThePinnedStoreReturnsTheSameRecordItWasGiven()
    {
        var section = ObjectArmSource(() => new Dictionary<string, object> { ["Health"] = 1 }, "Health");
        var row     = section.GetRows()[0];

        var pinned = new PinnedVariableRowSource();
        pinned.Pin(row);

        Assert.Same(row, pinned.GetRows()[0]);
    }

    // ══ helpers ══════════════════════════════════════════════════════════════

    private static SectionVariableRowSource ObjectArmSource(
        Func<IReadOnlyDictionary<string, object>?> live, params string[] names)
        => new(
            assetId: AssetId, assetName: "Alpha", entity: default, section: "s",
            schema:  new StubSchema(names),
            liveObjects: live);

    private static SectionVariableRowSource ByteArmSource(
        Func<string, byte[]> readRaw, params string[] names)
        => new(
            assetId: AssetId, assetName: "Alpha", entity: default, section: "s",
            schema:  new StubSchema(names),
            readRaw: readRaw);

    private static byte[] I32(int v) { var b = new byte[4]; MemoryMarshal.Write(b, in v); return b; }

    private static int ReadI32(VariableRow row) => MemoryMarshal.Read<int>(row.ReadValue());

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
        // ⭐ 99a — the interface has NO default body on purpose (U-5/BP-230: "a default body is
        //   the interface volunteering to lie on an implementer's behalf"), so every double answers.
        //   ⚠ These doubles do not exercise the Properties form, so they RECORD rather than no-op —
        //   a silent { } here is the very shape the rule exists to stop.
        public System.Collections.Generic.List<(string Name, Hrot.Editor.AiShared.Variables.VariablePropertyValues Values)> PropertyWrites { get; } = new();
        public void UpdateVariableProperties(
            string name, Hrot.Editor.AiShared.Variables.VariablePropertyValues values)
            => PropertyWrites.Add((name, values));
        public Hrot.Editor.AiShared.Variables.DeclarationPropertySnapshot? ReadVariableProperties(string name)
            => null;
        public void MoveVariable(int sourceIndex, int destIndex) { }
        public int CountNodesReferencingVariable(string name) => 0;
        public IReadOnlyList<UnboundRequirementViewModel> UnboundRequirements
            => Array.Empty<UnboundRequirementViewModel>();
        public void AddAlias(string name, BlackboardAliasBinding binding) { }
        public void RemoveAlias(string name, Guid requirementAssetId, Guid requirementElementId) { }
        public IReadOnlyDictionary<Guid, int>? GetParallelRegionMap() => null;
    }
}
