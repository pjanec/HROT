using System;
using System.Collections.Generic;
using Fdp.Core;
using Hrot.Blueprints.Core.Debug;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Selection;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>88a</c> — Blueprint's live-value provider.</b>
///
/// <para>⚠⚠ <b>READ THIS BEFORE TRUSTING WHAT THESE RAILS COVER.</b> 📐 Measured during the batch:
/// <see cref="Hrot.Editor.AiShared.Blackboard.ILiveBlackboardValueProvider"/> is consumed by
/// <b>exactly one surface — <c>BlackboardAuthoringWindow</c></b> *(<c>:514</c>)*. ⛔ <b>It does NOT
/// feed the Track C Details table</b>, whose live arm is <c>SectionVariableRowSource.readRaw</c>
/// *(name → <b>BYTES</b>)* and which Blueprint constructs with <c>readRaw: null</c> and
/// <c>entity: default</c>.</para>
///
/// <para>⇒ ⭐⭐ <b>These rails assert what this provider ACTUALLY drives</b>, and ⛔ deliberately do NOT
/// claim the guide's <c>C7</c> is fixed. 📌 The handoff's rail instruction — <i>"assert the cell text
/// the control would draw"</i> — rests on the same conflation; the seam it names is not the seam this
/// interface is on. ⚠ <b>Reported, not worked around.</b></para>
/// </summary>
public sealed class BlueprintLiveValueProviderTests
{
    private static readonly Guid AssetId = Guid.NewGuid();

    private sealed class FakeAsset : IEditableAsset
    {
        public Guid      AssetId        => BlueprintLiveValueProviderTests.AssetId;
        public string    Name           => "Alpha";
        public AssetKind Kind           => AssetKind.Blueprint;
        public string    SourceFilePath => "";
        public bool      IsDirty        => false;
        public bool      IsEditorOwned  => false;
        public event Action? Changed;
        public FakeAsset() { _ = Changed; }
    }

    private static BlueprintStateSnapshot Snapshot(params (string Name, object Value)[] fields)
    {
        var map = new Dictionary<string, object>();
        foreach (var f in fields) map[f.Name] = f.Value;
        return new BlueprintStateSnapshot(
            new Entity(1, 1), AssetId, "Alpha", Fdp.Toolkit.Blueprints.BlueprintDispatchKind.Instance, map, null);
    }

    /// <summary>⭐ The narrow seam is what makes this a THREE-LINE fake instead of 36 stubbed members.</summary>
    private static (BlueprintLiveValueProvider Provider, EditorSelectionStore Store)
        Make(BlueprintStateSnapshot? snapshot, bool selectEntity = true, bool haveReader = true)
    {
        var store = new EditorSelectionStore();
        if (selectEntity) store.SelectedEntity = new Entity(1, 1);

        ReadBlueprintState? reader = haveReader
            ? (_, assetId) => assetId == AssetId ? snapshot : null
            : null;

        return (new BlueprintLiveValueProvider(() => reader, store), store);
    }

    // ══ honest emptiness — "(pending)", never a zero that looks like a value ══

    /// <summary>⛔ No selected entity ⇒ empty. ⚠ There is no blackboard to read, and a guess would be a
    /// value the designer never asked for.</summary>
    [Fact]
    public void WithNoSelectedEntity_TheMapIsEmpty()
    {
        var (provider, _) = Make(Snapshot(("Health", 7)), selectEntity: false);
        Assert.Empty(provider.GetLiveVariableValues(new FakeAsset()));
    }

    /// <summary>
    /// ⛔ No active session ⇒ empty. ⚠ 📌 <c>R-66</c>: <c>ActiveSession</c> means "a document is open",
    /// not "the sim is up" — ⭐ which is why liveness is decided by the SNAPSHOT, and why this case and
    /// the next one are BOTH required.
    /// </summary>
    [Fact]
    public void WithNoActiveSession_TheMapIsEmpty()
    {
        var (provider, _) = Make(Snapshot(("Health", 7)), haveReader: false);
        Assert.Empty(provider.GetLiveVariableValues(new FakeAsset()));
    }

    /// <summary>⭐⭐ A session with NO snapshot ⇒ empty. 🔴 This is the "sim not running" case, and it is
    /// the one that must stay <c>(pending)</c> rather than becoming zeros.</summary>
    [Fact]
    public void WithNoSnapshot_TheMapIsEmpty()
    {
        var (provider, _) = Make(snapshot: null);
        Assert.Empty(provider.GetLiveVariableValues(new FakeAsset()));
    }

    // ══ the live read ═══════════════════════════════════════════════════════

    /// <summary>⭐⭐⭐ <b>THE rail</b> — a snapshot's fields reach the map, keyed by variable name.</summary>
    [Fact]
    public void ASnapshotsFieldsAreReturnedByName()
    {
        var (provider, _) = Make(Snapshot(("Health", 7), ("Ammo", 3)));

        var values = provider.GetLiveVariableValues(new FakeAsset());

        Assert.Equal("7", values["Health"]);
        Assert.Equal("3", values["Ammo"]);
    }

    /// <summary>
    /// ⭐⭐ <b>The SHARED formatter, not a second one.</b> 📌 <c>C8</c>/<c>BP-01</c>: a hex string is the
    /// regression. ⭐ A multi-field struct renders as <c>Field=value</c> pairs — the same shape
    /// <c>LiveBlackboardValueProvider</c> produces for BTree/HSM, because it IS that method.
    /// </summary>
    [Fact]
    public void AStructIsFormattedByTheSharedFormatter()
    {
        var (provider, _) = Make(Snapshot(("Wave", new Pair { A = 1, B = 2 })));

        var text = provider.GetLiveVariableValues(new FakeAsset())["Wave"];

        Assert.Equal(LiveBlackboardValueProvider.FormatValue(new Pair { A = 1, B = 2 }, typeof(Pair)), text);
        Assert.Contains("A=1", text);
        Assert.Contains("B=2", text);
    }

    private struct Pair { public int A; public int B; }

    /// <summary>⚠ A different asset id ⇒ the session declines ⇒ empty. ⛔ The provider must never show
    /// one asset's state under another's name.</summary>
    [Fact]
    public void AForeignAssetIdYieldsNothing()
    {
        var (provider, _) = Make(Snapshot(("Health", 7)));

        Assert.Empty(provider.GetLiveVariableValues(new OtherAsset()));
    }

    private sealed class OtherAsset : IEditableAsset
    {
        public Guid      AssetId        { get; } = Guid.NewGuid();
        public string    Name           => "Beta";
        public AssetKind Kind           => AssetKind.Blueprint;
        public string    SourceFilePath => "";
        public bool      IsDirty        => false;
        public bool      IsEditorOwned  => false;
        public event Action? Changed;
        public OtherAsset() { _ = Changed; }
    }

    /// <summary>⛔ Never throws into the UI — the interface's own contract.</summary>
    [Fact]
    public void ANullAssetIsEmptyRatherThanAThrow()
    {
        var (provider, _) = Make(Snapshot(("Health", 7)));
        Assert.Empty(provider.GetLiveVariableValues(null!));
    }
}
