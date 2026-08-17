using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;

namespace Hrot.Blueprints.Editor.ActionCatalog;

/// <summary>
/// ⭐⭐⭐ <b>The ONE producer catalog</b> behind both <c>G7</c>'s resolver picker and <c>W10</c>'s
/// initializer picker (plan §4c, ruling 9).
///
/// <para>
/// ⭐ Composing, in <see cref="BehaviorActionCatalog"/>'s shape: contributors in, one immutable
/// snapshot out, replaced atomically, with a <see cref="Changed"/> event. 📄 <c>AN7-REPORT.md:73–95</c>
/// is the named precedent for reusing that shape rather than coining a picker.
/// </para>
///
/// <para>
/// ⛔⛔ <b>Both contributors are REQUIRED, with no silent default.</b> A caller that has no CLR
/// producers passes an explicit empty supplier. 📌 This is the repo's own silent-default rule applied
/// at construction: <i>"what distinguishes the dangerous case is not the default but that the caller
/// HELD the value and did not pass it"</i> — an optional supplier here would make "this source
/// contributes nothing" and "I forgot to wire this source" the same observation.
/// </para>
/// </summary>
/// <para>
/// ⛔⛔ <b>PARKED (Batch 74). Nothing on either side calls this, and that is DELIBERATE — do not
/// delete it, and do not wire it.</b>
/// </para>
///
/// <para>
/// 📐 <b>Measured <c>2026-08-17</c>:</b> no panel constructs the picker, no registrar supplies the
/// catalog, no asset field stores what <c>Persist()</c> returns, ⛔ <b>and the runtime it would feed
/// does not exist</b> — the blueprint-authored resolver (<c>R1</c>/<c>R2</c>/<c>R4</c>, resolver
/// design §8.1) is unbuilt.
/// </para>
///
/// <para>
/// ⭐ <b>Not deleted</b> because unreferenced is not unintentional (<c>2026-08-15</c> ruling): this is
/// built to a design (plan §4c, architect <c>AQ2</c>) whose answers are ruled — deleting removes a
/// capability, not a mistake. ⭐ <b>Not wired</b> because that is the very thing the
/// <c>2026-08-17</c> user ruling forbids: an authoring surface whose consumer does not exist.
/// </para>
///
/// <para>
/// ⭐⭐ <b>The state is ASSERTED, not just described</b> —
/// <c>ProducerPickerTests.ThePickerIsInert_UntilTheResolverRuntimeExists</c> fails the moment someone
/// constructs it, so wiring becomes the reminder to build the consumer too. <b>Invert it when
/// <c>R1</c>/<c>R2</c>/<c>R4</c> land.</b>
/// </para>
public sealed class ProducerCatalog : IProducerCatalog
{
    private readonly Func<IReadOnlyList<BlueprintSignature>> _librarySignatures;
    private readonly Func<IReadOnlyList<MethodInfo>>         _clrProducers;

    private volatile IReadOnlyList<ProducerEntry> _snapshot = Array.Empty<ProducerEntry>();

    /// <inheritdoc />
    public event Action? Changed;

    /// <param name="librarySignatures">
    /// Every known blueprint signature; the catalog keeps the <c>Library</c>-dispatch ones and offers
    /// each of their exported Function graphs.
    /// </param>
    /// <param name="clrProducers">
    /// Hand-written static methods offered as producers (the resolver design's interim escape hatch).
    /// Pass <c>() =&gt; Array.Empty&lt;MethodInfo&gt;()</c> when a host genuinely has none.
    /// </param>
    public ProducerCatalog(
        Func<IReadOnlyList<BlueprintSignature>> librarySignatures,
        Func<IReadOnlyList<MethodInfo>>         clrProducers)
    {
        _librarySignatures = librarySignatures ?? throw new ArgumentNullException(nameof(librarySignatures));
        _clrProducers      = clrProducers      ?? throw new ArgumentNullException(nameof(clrProducers));
        Rebuild();
    }

    /// <inheritdoc />
    public IReadOnlyList<ProducerEntry> GetProducers() => _snapshot;

    /// <inheritdoc />
    public IReadOnlyList<ProducerEntry> GetProducersReturning(string typeId)
        => string.IsNullOrEmpty(typeId)
            ? Array.Empty<ProducerEntry>()
            : _snapshot
                .Where(p => p.Outputs.Count == 1
                            && string.Equals(p.Outputs[0].TypeId, typeId, StringComparison.Ordinal))
                .ToList();

    /// <inheritdoc />
    public ProducerEntry? Lookup(string fqn)
        => string.IsNullOrEmpty(fqn)
            ? null
            : _snapshot.FirstOrDefault(p => string.Equals(p.Fqn, fqn, StringComparison.Ordinal));

    /// <summary>Re-reads every contributor and swaps in a fresh snapshot.</summary>
    public void Rebuild()
    {
        var entries = new List<ProducerEntry>();

        foreach (var sig in _librarySignatures() ?? Array.Empty<BlueprintSignature>())
        {
            if (sig is null || sig.Dispatch != BlueprintDispatchKind.Library) continue;
            foreach (var fn in sig.ExportedFunctions)
            {
                entries.Add(new ProducerEntry(
                    Fqn:         LibraryFunctionFqn(sig, fn.Name),
                    DisplayName: $"{sig.Name}.{fn.Name}",
                    Category:    sig.Name,
                    Inputs:      fn.Inputs,
                    Outputs:     fn.Outputs,
                    Source:      ProducerSource.LibraryFunction));
            }
        }

        foreach (var m in _clrProducers() ?? Array.Empty<MethodInfo>())
        {
            if (m is null) continue;
            entries.Add(new ProducerEntry(
                Fqn:         ClrMethodFqn(m),
                DisplayName: $"{m.DeclaringType?.Name}.{m.Name}",
                Category:    m.DeclaringType?.Name,
                Inputs:      m.GetParameters()
                              .Select(p => new BlueprintParamSig(
                                  p.Name ?? "arg",
                                  (p.ParameterType.IsByRef
                                      ? p.ParameterType.GetElementType()!
                                      : p.ParameterType).FullName ?? p.ParameterType.Name))
                              .ToList(),
                Outputs:     m.ReturnType == typeof(void)
                              ? Array.Empty<BlueprintParamSig>()
                              : new[] { new BlueprintParamSig("return", m.ReturnType.FullName ?? m.ReturnType.Name) },
                Source:      ProducerSource.ClrMethod));
        }

        // ⚠ Deterministic order, so the picker's list does not reshuffle between rebuilds.
        entries.Sort((a, b) => string.CompareOrdinal(a.Fqn, b.Fqn));
        _snapshot = entries;
        Changed?.Invoke();
    }

    /// <summary>
    /// ⭐⭐ <b>The generated FQN of a Library function.</b> ⛔ Not the AssetId (architect <c>AQ2</c>).
    /// ⚠ It repeats <c>LibraryEmitter</c>'s class-name formula, and that is the one duplication this
    /// item could not avoid: the emitter builds it from an <c>IrAsset</c> in the compiler assembly,
    /// the picker from a <c>BlueprintSignature</c> in the editor. ⭐ Both inputs come from the SAME
    /// parse, so <see cref="BlueprintSignature.SanitizedName"/> and
    /// <see cref="BlueprintSignature.BlueprintId"/> are the emitter's own values — and the rail
    /// <c>TheLibraryFqn_MatchesTheEmittersClassNameFormula</c> pins the two together.
    /// </summary>
    public static string LibraryFunctionFqn(BlueprintSignature sig, string functionName)
        => $"Hrot.AI.Behaviors.Generated.{sig.SanitizedName}_{sig.BlueprintId:X8}_Bp.{functionName}";

    /// <summary>The FQN of a hand-written producer: <c>{DeclaringType.FullName}.{MethodName}</c>.</summary>
    public static string ClrMethodFqn(MethodInfo m)
        => $"{m.DeclaringType?.FullName}.{m.Name}";
}
