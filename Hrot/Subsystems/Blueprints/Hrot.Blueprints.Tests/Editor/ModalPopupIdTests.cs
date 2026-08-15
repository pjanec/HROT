using System.Reflection;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.Windows;
using NodeEditor.Core.Action;
using BlueprintDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐ <b>Found by the VISUAL CHECK at §A2 — the first time this surface was looked at.</b>
///
/// <para>
/// ⛔ <b>The defect:</b> <see cref="VariableCreateModal"/> held its ImGui popup id in a
/// <c>const</c>, and <see cref="BlueprintMyBlueprintWindow"/> builds <b>two</b> of them — asset
/// variables and <c>BP-57</c>'s graph locals. ⚠ <c>BeginPopupModal</c> called twice in one frame
/// with the same id <b>appends to the same window</b> rather than opening a second one, so pressing
/// <c>+</c> on <b>Local Variables</b> drew every field twice and the first <b>Create</b> button
/// belonged to the <i>asset</i> modal. ⇒ <b>declaring a local silently created a global.</b>
/// </para>
///
/// <para>
/// ⭐⭐ <b>Why no existing test caught it:</b> every headless test drives the confirm CALLBACK, which
/// was always correctly wired to the right list. The bug lives entirely in which of two overlapping
/// ImGui windows the button belongs to — invisible to anything that never draws. ⇒ this file asserts
/// the <b>identity</b> of the surfaces rather than the behaviour behind them, which is the part that
/// is checkable without a context.
/// </para>
///
/// <para>
/// ⭐ <b>Stated over ALL the window's modals, not over the pair that broke.</b>
/// <see cref="FunctionCreateModal"/> already had the per-instance id (<c>BP-77</c>, when Macro was
/// added), and is instantiated twice as well — so the general form is free and it is the form that
/// catches the third duplication instead of this one being re-found by hand in another twenty
/// batches.
/// </para>
/// </summary>
public sealed class ModalPopupIdTests
{
    // ── the pair that actually broke ──────────────────────────────────────────

    /// <summary>
    /// ⭐ The direct statement. ⛔ Before the fix both sides of this were the same <c>const</c>, so
    /// this assertion is the whole bug in one line.
    /// </summary>
    [Fact]
    public void AnAssetVariableModalAndALocalVariableModalDoNotShareAPopupId()
    {
        var asset  = new VariableCreateModal((_, _, _, _) => { });
        var locals = new VariableCreateModal((_, _, _, _) => { }, asset: null, noun: "Local Variable");

        Assert.NotEqual(asset.PopupId, locals.PopupId);
    }

    /// <summary>
    /// ⚠ <b>And the title differs, which is not cosmetic.</b> The two dialogs are otherwise
    /// field-for-field identical, so the title is the only thing telling the designer which list
    /// they are writing to. ⭐ Asserted on the id's visible half — everything before <c>##</c>.
    /// </summary>
    [Fact]
    public void EachVariableModalNamesTheListItWritesTo()
    {
        Assert.StartsWith("Create Variable##",
            new VariableCreateModal((_, _, _, _) => { }).PopupId, StringComparison.Ordinal);
        Assert.StartsWith("Create Local Variable##",
            new VariableCreateModal((_, _, _, _) => { }, null, "Local Variable").PopupId,
            StringComparison.Ordinal);
    }

    // ── the general form ──────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>Every modal the window draws in one frame has its own id.</b>
    ///
    /// <para>
    /// ⚠ <b>"Draws in one frame" is the operative scope.</b> <c>DrawClientArea</c> calls <c>Draw()</c>
    /// on all six unconditionally, so any two sharing an id collide — it does not matter that only
    /// one is ever <i>open</i>, because the closed one's <c>BeginPopupModal</c> still matches the
    /// open one's id and appends into it. ⛔ That is precisely how the asset modal's fields appeared
    /// inside the locals dialog.
    /// </para>
    ///
    /// <para>
    /// 📌 Reads the id by reflection over three spellings — an <c>internal</c> property, a private
    /// instance field, a private <c>const</c> — deliberately, so a modal keeps its id private and
    /// this stays honest about there being no shared base type to hang a contract on.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryModalTheWindowDrawsHasItsOwnPopupId()
    {
        var window = new BlueprintMyBlueprintWindow();
        window.Retarget(null, MakeAsset(), null, new EditorCommandsImpl());

        var byId = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var field in typeof(BlueprintMyBlueprintWindow)
                     .GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
        {
            var modal = field.GetValue(window);
            if (modal is null) continue;

            var id = TryReadPopupId(modal);
            if (id is null) continue;

            if (!byId.TryGetValue(id, out var owners))
                byId[id] = owners = new List<string>();
            owners.Add($"{field.Name} ({modal.GetType().Name})");
        }

        // ⚠ If reflection found nothing the loop above is vacuously green, which is the failure mode
        //   this whole file exists to stop. Six modals are built by Retarget; require at least four
        //   so a renamed field does not quietly empty the test.
        Assert.True(byId.Count >= 4,
            $"only {byId.Count} modal popup ids were discovered — the reflection above has gone "
            + "stale and this test is no longer checking anything.");

        var shared = byId.Where(e => e.Value.Count > 1)
                         .Select(e => $"{e.Key} ← {string.Join(" + ", e.Value)}")
                         .ToList();

        Assert.True(shared.Count == 0,
            "these modals share one ImGui popup id, so they draw into ONE window and the first "
            + "Create/OK button belongs to whichever draws first:\n  " + string.Join("\n  ", shared));
    }

    /// <summary>The id, however the modal chose to keep it. Null when the type has no popup id.</summary>
    private static string? TryReadPopupId(object modal)
    {
        var type = modal.GetType();
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static
                               | BindingFlags.Public   | BindingFlags.NonPublic;

        if (type.GetProperty("PopupId", Any)?.GetValue(modal) is string fromProperty)
            return fromProperty;

        foreach (var name in new[] { "_popupId", "PopupId" })
            if (type.GetField(name, Any)?.GetValue(modal) is string fromField)
                return fromField;

        return null;
    }

    private static BlueprintAsset MakeAsset() => new()
    {
        AssetId  = Guid.NewGuid(),
        Name     = "ModalIdHost",
        Dispatch = BlueprintDispatchKind.Instance,
        Header   = new Header(),
        Graphs   = { new Graph { Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Event } },
    };
}
