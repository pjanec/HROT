using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Hrot.Diagnostics.Breakpoints;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐⭐ <b><c>BP-502</c> — the Watch pin set ⇄ the debug-session file.</b>
/// 📄 <c>DESIGN_Variable_Watch_Pinning.md</c> §5.
///
/// <para>⭐⭐ <b>Why the mapping lives HERE and not in the persistence layer.</b>
/// <c>Hrot.Diagnostics.Breakpoints</c> knows nothing about <c>VariableRow</c> — and must not, or the
/// dependency runs the wrong way *(<c>AiShared</c> references <c>Breakpoints</c>, never the reverse)</b>.
/// ⇒ the file carries a flat DTO and this class is the only thing that knows both shapes.</para>
///
/// <para>⛔⛔ <b>RESTORE REBUILDS A BINDING, NOT A ROW.</b> A <c>VariableRow</c> carries live accessor
/// delegates — <c>ReadValue</c>, <c>AssetTick</c>, the write-backs — which close over a source that only
/// exists while the asset is open. ⇒ ⭐ a row CANNOT be deserialised; what is restored is the
/// <b>identity plus its binding</b>, and the row is rebuilt by whichever source owns that asset when it
/// loads. ⚠ Until then the pin is a request, not a row — which is why <see cref="Restore"/> returns
/// descriptors and not rows.</para>
///
/// <para>⚠ <b>Not wired to a production save yet, and that is measured, not assumed:</b> 📐
/// <c>DebugSessionPersistence.Save</c> has <b>no production caller</b> — only tests. The editor's live
/// path still goes through the <c>[Obsolete]</c> <c>DataBreakpointManager.SaveWatches</c>, which is
/// breakpoint-only. ⭐ Wiring a real save touches <c>EditorSubsystem</c>, which the concurrent allocator
/// batch owns ⇒ carved out and reported rather than raced.</para>
/// </summary>
public static class PinnedVariablePersistence
{
    /// <summary>
    /// ⭐ The pin set, ready for <c>DebugSessionPersistence.Save</c>.
    /// <para>⚠ <b>Rows whose binding cannot be persisted are DROPPED, and the caller is told how many</b>
    /// via <paramref name="skipped"/> — 📌 a concrete pin on an entity with no <c>NetworkIdentity</c>
    /// *(an editor-only entity)* has nothing durable to key on. ⛔ Writing it with <c>NetworkId 0</c>
    /// would restore a pin pointing at nothing, which reads as data loss on the next load rather than as
    /// the within-session pin it always was.</para>
    /// </summary>
    public static IReadOnlyList<PinnedVariableEntry> Capture(
        PinnedVariableRowSource source, out int skipped)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));

        var entries = new List<PinnedVariableEntry>();
        skipped = 0;

        foreach (var (row, binding) in source.PinnedWithBindings())
        {
            if (!binding.IsPersistable) { skipped++; continue; }

            entries.Add(new PinnedVariableEntry
            {
                AssetId      = row.Origin.AssetId,
                Section      = row.Origin.Section,
                VariablePath = row.Origin.VariablePath,
                AssetName    = row.Origin.AssetName,
                BindingKind  = binding.Kind.ToString(),
                NetworkId    = binding.NetworkId,
            });
        }

        return entries;
    }

    /// <summary>
    /// ⭐⭐ What a restored pin IS before its asset loads: an identity and a binding.
    /// ⛔ Deliberately not a <c>VariableRow</c> — see the class remarks.
    /// </summary>
    /// <param name="AssetId">The asset the variable belongs to.</param>
    /// <param name="Section">Its section, for re-finding it in the rebuilt source.</param>
    /// <param name="VariablePath">The variable.</param>
    /// <param name="AssetName">Display text only.</param>
    /// <param name="Binding">
    /// ⚠ For a CONCRETE pin, <c>Captured</c> is <c>default</c> — the in-session handle is not in the file
    /// and must be resolved from <c>NetworkId</c> by whoever can see the entity map.
    /// </param>
    public readonly record struct RestoredPin(
        Guid          AssetId,
        string        Section,
        string        VariablePath,
        string        AssetName,
        EntityBinding Binding);

    /// <summary>
    /// ⭐ Reads the pin set back. ⚠ <b>An unknown <c>BindingKind</c> is SKIPPED, not coerced</b> — an
    /// enum's zero value happens to be <c>Concrete</c>, so a silent parse failure would turn a future
    /// kind into a concrete pin on entity 0 and show the wrong entity's value.
    /// </summary>
    public static IReadOnlyList<RestoredPin> Restore(DebugSessionFile file, out int skipped)
    {
        if (file is null) throw new ArgumentNullException(nameof(file));

        var pins = new List<RestoredPin>();
        skipped = 0;

        foreach (var e in file.PinnedVariables)
        {
            if (!Enum.TryParse<EntityBindingKind>(e.BindingKind, ignoreCase: false, out var kind))
            {
                skipped++;
                continue;
            }

            var binding = kind == EntityBindingKind.Chameleon
                ? EntityBinding.Chameleon
                // ⛔ default(Entity): the handle is NOT in the file. The caller resolves NetworkId.
                : EntityBinding.Concrete(e.NetworkId, default);

            pins.Add(new RestoredPin(e.AssetId, e.Section, e.VariablePath, e.AssetName, binding));
        }

        return pins;
    }
}
