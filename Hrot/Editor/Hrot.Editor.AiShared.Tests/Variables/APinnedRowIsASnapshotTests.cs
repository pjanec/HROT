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
/// ⭐⭐⭐ <b>Batch 93 — THE MEASUREMENT THAT STOPPED THE BATCH (<c>BP-344</c>).</b>
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
/// <para>⭐⭐ <b>These rails record the CURRENT semantics, and one of them is meant to be INVERTED</b>
/// when the design answers. ⚠ They are written so the inversion is obvious and local:
/// <see cref="ARowPinnedFromTheDetailsSourceFreezesAtPinTime"/> is the one that flips.
/// ⛔ <b>Do not "fix" it by deleting it.</b></para>
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
    /// ⭐⭐⭐ <b>THE measurement.</b> One live map, two surfaces, one frame apart:
    /// ⭐ <b>Details reads 99</b> *(the new value)* · ⛔ <b>Watch reads 10</b> *(the pinned one)*.
    ///
    /// <para>⚠ <b>This rail asserts the CURRENT, DEFECTIVE behaviour on purpose</b>, so the defect is a
    /// measured fact rather than a claim. ⭐ When the design rules on the value feed, <b>invert the
    /// second assertion</b> — it is the acceptance test for that fix.</para>
    /// </summary>
    [Fact]
    public void ARowPinnedFromTheDetailsSourceFreezesAtPinTime()
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

        // ⛔⛔ THE DEFECT, measured. ⭐ INVERT THIS when the value feed is ruled on.
        Assert.Equal(10, pinned.GetRows()[0].ReadValueObject!.Invoke());
    }

    /// <summary>
    /// ⭐⭐ <b>The byte arm freezes too</b> — ⛔ so this is not a Blueprint-only or object-arm-only
    /// property. <c>SectionVariableRowSource</c> captures <c>bytes</c> exactly as it captures
    /// <c>value</c>, and <c>BlackboardSectionRowSource</c> does the same at its own <c>ToRow</c>.
    /// </summary>
    [Fact]
    public void TheByteArmFreezesOnTheSameRule()
    {
        int live    = 10;
        var section = ByteArmSource(_ => I32(live), "Health");

        var pinned = new PinnedVariableRowSource();
        pinned.Pin(section.GetRows()[0]);

        live = 99;

        Assert.Equal(99, ReadI32(section.GetRows()[0]));
        Assert.Equal(10, ReadI32(pinned.GetRows()[0]));   // ⛔ frozen — invert with the rail above
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>(pending)</c> freezes with the value, and that is the SECOND half of the design
    /// question.</b> 📌 <c>BP-338</c> made <c>HasEverBeenWritten</c> a per-name, per-frame MEASUREMENT
    /// — but it is a <c>bool</c> on the record, decided when the row is built. ⇒ ⛔ <b>a variable the
    /// run writes AFTER it was pinned reads <c>(pending)</c> in the Watch forever</b>, while Details
    /// shows its value. ⚠ Guide row <c>C9</c> is about the opposite error; this is its mirror.
    /// </summary>
    [Fact]
    public void PendingFreezesToo_SoAVariableWrittenAfterPinningNeverUnpends()
    {
        var map     = new Dictionary<string, object>();          // nothing written yet
        var section = ObjectArmSource(() => map, "Health");

        var pinned = new PinnedVariableRowSource();
        pinned.Pin(section.GetRows()[0]);
        Assert.False(pinned.GetRows()[0].HasEverBeenWritten, "nothing was written at pin time");

        map["Health"] = 42;                                      // the run writes it

        Assert.True(section.GetRows()[0].HasEverBeenWritten,  "Details notices the write");
        Assert.False(pinned.GetRows()[0].HasEverBeenWritten, "⛔ the Watch never will — invert on the fix");
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
        public void MoveVariable(int sourceIndex, int destIndex) { }
        public int CountNodesReferencingVariable(string name) => 0;
        public IReadOnlyList<UnboundRequirementViewModel> UnboundRequirements
            => Array.Empty<UnboundRequirementViewModel>();
        public void AddAlias(string name, BlackboardAliasBinding binding) { }
        public void RemoveAlias(string name, Guid requirementAssetId, Guid requirementElementId) { }
        public IReadOnlyDictionary<Guid, int>? GetParallelRegionMap() => null;
    }
}
