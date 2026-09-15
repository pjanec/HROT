using System;
using System.Linq;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Refactor;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐⭐ <b>Batch 99 (<c>99a</c>) — THE rename, in one place, for every surface that offers one.</b>
///
/// <para>🔴 <b>Why this is extracted rather than written.</b> 📐 <c>VariablesPanelControl.CommitRename</c>
/// already did exactly this — <c>GetRefactorKey</c> → <c>PreviewRename</c> → <c>ApplyRename</c> (unless
/// it has errors) → <c>schema.RenameVariable</c> — and it was <b>private</b>. ⇒ the Properties form
/// would have been a <b>SECOND implementation of rename</b> *(ruling 9)*, and the weaker one, because
/// the refactor half is easy to leave out and nothing would have said so.</para>
///
/// <para>⛔⛔ <b>THE REFACTOR HALF IS NOT OPTIONAL, and on BTree/HSM it is the whole safety story.</b>
/// 📌 <c>M-15</c>: those hosts store the variable's <b>NAME STRING</b> in the binding, and
/// <c>RenameVariable</c> does <b>not</b> fix up <c>ExpressionTargetField</c> ⇒ ⛔ <b>renaming a bound AI
/// variable DANGLES it</b>, caught at build as <c>BTREE0002</c> — <b>a whole-asset skip</b>.
/// ⭐ On Blueprint the declaration carries a persisted <c>Guid Id</c> and references store a
/// <c>VariableId</c> *(📌 <c>M-16</c>)*, so a rename is safe there — ⚠ <b>but the route is the same on
/// both</b>, and that is the design's requirement of both routes rather than a per-host rule.</para>
///
/// <para>⭐ <b>An ERROR-severity issue aborts the whole rename</b>, declaration included — ⛔ renaming
/// the declaration while the references failed to move is the dangling state this exists to prevent.
/// ⚠ That is a CHANGE from <c>CommitRename</c>, which renamed the declaration anyway; see
/// <see cref="Rename"/>.</para>
/// </summary>
public static class VariableRenameCommit
{
    /// <summary>Why a rename did not happen, or <see cref="Ok"/>.</summary>
    public enum Outcome
    {
        /// <summary>⭐ References were moved (or there were none) and the declaration was renamed.</summary>
        Ok,

        /// <summary>⭐ The new name is blank, or identical to the old one. ⛔ Not an error — nothing to do.</summary>
        Unchanged,

        /// <summary>
        /// ⛔⛔ The refactor preview reported an ERROR, so <b>nothing was renamed at all</b>.
        /// ⭐ 📌 <c>M-15</c> — a declaration renamed while its references stayed put is precisely the
        /// dangling binding that fails the build as <c>BTREE0002</c>.
        /// </summary>
        RefusedByRefactor,
    }

    /// <summary>
    /// ⭐⭐ Renames <paramref name="oldName"/> to <paramref name="newName"/>, references first.
    ///
    /// <para>⚠⚠ <b>ONE DELIBERATE CHANGE from the panel's original.</b> <c>CommitRename</c> ran
    /// <c>ApplyRename</c> only when the preview had no errors — ⛔ <b>and then renamed the declaration
    /// unconditionally anyway</b>, so an error left the references behind and the declaration moved.
    /// ⭐ Here an error aborts BOTH. 📐 The panel's own comment calls a dangling binding the failure to
    /// avoid; ⛔ the old order produced it.</para>
    /// </summary>
    /// <param name="fallbackAssetId">
    /// ⭐ Used only when <c>GetRefactorKey</c> answers <c>null</c> — the panel's own fallback shape,
    /// kept so a source that does not key its variables behaves exactly as before.
    /// </param>
    public static Outcome Rename(
        IVariablesSchemaSource schema,
        IRefactorService       refactorService,
        Guid                   fallbackAssetId,
        string                 oldName,
        string?                newName)
    {
        if (schema is null)          throw new ArgumentNullException(nameof(schema));
        if (refactorService is null) throw new ArgumentNullException(nameof(refactorService));

        var trimmed = newName?.Trim();
        if (string.IsNullOrEmpty(trimmed) || string.Equals(trimmed, oldName, StringComparison.Ordinal))
            return Outcome.Unchanged;

        var fromKey = schema.GetRefactorKey(oldName) ?? $"{fallbackAssetId:D}::{oldName}";
        var toKey   = schema.GetRefactorKey(trimmed) ?? $"{fallbackAssetId:D}::{trimmed}";

        var preview = refactorService.PreviewRename(fromKey, toKey, new RefactorOptions());
        if (preview.Issues.Any(i => i.Severity == RefactorIssueSeverity.Error))
            return Outcome.RefusedByRefactor;

        // ⭐ References FIRST, declaration second. ⛔ The other order leaves a window in which the
        //   declaration is gone and the references still name it.
        refactorService.ApplyRename(preview);
        schema.RenameVariable(oldName, trimmed);
        return Outcome.Ok;
    }
}
