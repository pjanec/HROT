using System;
using System.Collections.Generic;
using System.Linq;

namespace Hrot.Blueprints.Core.Assets;

/// <summary>
/// ⭐⭐ <b>THE ONE intent → hosting table</b> — which <see cref="AiPrimitiveHosting"/> a primitive of a given
/// <see cref="AiPrimitiveIntent"/> may declare. 📄 <c>docs/blueprints/DESIGN_Product_First_Authoring.md</c> §6 D7.
///
/// <para>⭐ Moved here from <c>V_DispatchKindCompatibility</c>'s two private arrays (<c>CE-461</c>, <c>2026-09-30</c>)
/// so the editor's New Action / New Condition templates can READ it instead of keeping a second copy. The
/// validator's BP1022 / BP1023 read it too ⇒ the rule the compiler enforces and the hostings the editor
/// mints cannot drift apart.</para>
///
/// <para>⚠ A hosting in NEITHER list (today only <see cref="AiPrimitiveHosting.BlueprintCall"/>) is
/// intent-NEUTRAL: legal for both intents.</para>
/// </summary>
public static class AiPrimitiveHostingRules
{
    private static readonly AiPrimitiveHosting[] ActionHostings =
        { AiPrimitiveHosting.BTreeAction, AiPrimitiveHosting.HsmAction };

    private static readonly AiPrimitiveHosting[] ConditionHostings =
        { AiPrimitiveHosting.BTreeCondition, AiPrimitiveHosting.HsmGuard };

    /// <summary>
    /// False when <paramref name="hosting"/> is shaped for the OTHER intent — an action hosted as a
    /// condition/guard (BP1022) or a condition hosted as an action (BP1023).
    /// </summary>
    public static bool IsCompatible(AiPrimitiveIntent intent, AiPrimitiveHosting hosting) => intent switch
    {
        AiPrimitiveIntent.Action    => !ConditionHostings.Contains(hosting),
        AiPrimitiveIntent.Condition => !ActionHostings.Contains(hosting),
        _                           => false,
    };

    /// <summary>
    /// ⭐ Every hosting a primitive of <paramref name="intent"/> may declare, in enum order — derived from
    /// <see cref="IsCompatible"/>, so a hosting added to the enum is included or excluded by the same rule
    /// the validator applies. 🔒 User, <c>2026-09-30</c>: an action is usable by BTrees, HSMs AND blueprint
    /// behaviours; a condition as a BTree condition AND an HSM guard.
    /// </summary>
    public static IReadOnlyList<AiPrimitiveHosting> AllValidFor(AiPrimitiveIntent intent)
        // ⚠ Non-generic Enum.GetValues: this assembly also targets netstandard2.0.
        => Enum.GetValues(typeof(AiPrimitiveHosting)).Cast<AiPrimitiveHosting>()
               .Where(h => IsCompatible(intent, h)).ToArray();
}
