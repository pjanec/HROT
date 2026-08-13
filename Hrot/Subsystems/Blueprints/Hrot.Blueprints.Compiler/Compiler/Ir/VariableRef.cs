namespace Hrot.Blueprints.Core.Compiler.Ir;

/// <summary>
/// U-3 / <c>BP-226</c> — which of the asset's three declaration lists a variable reference names.
///
/// <para>
/// ⭐⭐ <b><see cref="Unresolved"/> is deliberately the DEFAULT (0).</b> A zero-initialised
/// <see cref="VariableRef"/> therefore means <i>"nobody set this"</i>, and
/// <c>EmissionContext.VarFieldName</c> throws on it. ⛔ Had <c>Variable</c> been 0, a forgotten
/// assignment would have silently meant <c>Variables[0]</c> — the same class of quiet-wrong-field
/// defect this task exists to remove.
/// </para>
/// </summary>
public enum VariableKind
{
    /// <summary>Stage 5 resolved nothing. Reaching Emit with this is a bug in the Stage 2 rails.</summary>
    Unresolved = 0,

    /// <summary><c>BlueprintAsset.Variables</c> — the <c>State</c> struct (offset 16).</summary>
    Variable,

    /// <summary><c>BlueprintAsset.WorkingState</c> — the AiPrimitive working-state struct (offset 8).</summary>
    WorkingState,

    /// <summary><c>BlueprintAsset.Parameters</c> — the <c>Params</c> struct (offset 0).</summary>
    Parameter,
}

/// <summary>
/// U-3 / <c>BP-226</c> — a resolved variable reference: <b>which list</b>, and the position
/// <b>within that list</b>.
///
/// <para>
/// ⛔ <b>What this replaces was a bare <c>int</c>, and the two ends read it differently.</b>
/// <c>Stage5.FindVariableIndex</c> searched <c>Variables</c>, then <c>WorkingState</c>, then
/// <c>Parameters</c>, returning the position <b>within whichever list matched</b> — and threw the
/// list away. <c>EmissionContext.VarFieldName</c> then read that integer as a <b>priority-ordered
/// union</b>: <c>Variables</c> first, then <c>WorkingState</c>, and <b>no <c>Parameters</c> arm at
/// all</b>. The two agree only while at most one list is populated, which <c>BP1024</c>/<c>BP1031</c>
/// happen to enforce for every shipped asset.
/// </para>
///
/// <para>
/// ⭐ <b>The three lists are three different structs at three different offsets</b> — <c>Params</c>,
/// working state, <c>State</c>. An integer that does not say which one is a type error the language
/// was never given the chance to catch. This type is that chance.
/// </para>
///
/// <para>
/// ⚠ <b>Not a combined index.</b> <see cref="Index"/> is always list-relative, so no consumer needs to
/// rebase it — and the un-rebased read that used to be the bug is now simply correct by construction.
/// </para>
/// </summary>
public readonly record struct VariableRef(VariableKind Kind, int Index)
{
    /// <summary>Stage 5 found nothing. See <see cref="VariableKind.Unresolved"/>.</summary>
    public static VariableRef Unresolved => default;

    public bool IsResolved => Kind != VariableKind.Unresolved;

    public override string ToString()
        => IsResolved ? $"{Kind}[{Index}]" : "unresolved";
}
