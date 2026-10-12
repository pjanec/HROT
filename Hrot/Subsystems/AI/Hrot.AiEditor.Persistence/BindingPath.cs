using System;

namespace Hrot.AiEditor.Persistence;

/// <summary>
/// ⭐⭐ <c>CE-3182</c> — a binding's target as a PATH: a block variable's name, or <c>Variable.Field[.Sub…]</c> for one of its
/// fields (<c>docs/DESIGN_Orders_Params_Entity_Classes.md</c> §5d, AQ90 §2.3j). A variable name is an identifier
/// (<c>BlackboardNameValidator</c>), so the first <c>.</c> ends the root.
/// <para>⭐ The ONE answer to "does this binding target variable X" and "rename X in this binding", for every editor and the
/// emitters alike — ⛔ never a string <c>==</c> on <c>ExpressionTargetField</c>, which misses every field path.</para>
/// </summary>
public static class BindingPath
{
    /// <summary>The separator between a variable and its field.</summary>
    public const char Separator = '.';

    /// <summary>The variable a path starts at (the whole string when it names a variable); <c>null</c> for an empty path.</summary>
    public static string? Root(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        int dot = path!.IndexOf(Separator);
        return dot < 0 ? path : path.Substring(0, dot);
    }

    /// <summary>The field part after the root (<c>"walk"</c>, <c>"walk.Speed"</c>), or <c>null</c> when the path names a variable.</summary>
    public static string? Field(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        int dot = path!.IndexOf(Separator);
        return dot < 0 ? null : path.Substring(dot + 1);
    }

    /// <summary>True when <paramref name="path"/> targets <paramref name="variable"/> or one of its fields. ⚠ Case-insensitive, as
    /// HSM's <c>IsExpressionTargetOf</c> always was; variable names differing only in case are refused at authoring.</summary>
    public static bool Targets(string? path, string? variable)
        => !string.IsNullOrEmpty(variable) && string.Equals(Root(path), variable, StringComparison.OrdinalIgnoreCase);

    /// <summary><paramref name="path"/> with its root <paramref name="from"/> renamed to <paramref name="to"/>, its field part kept;
    /// unchanged when it does not target <paramref name="from"/>.</summary>
    public static string? Retarget(string? path, string from, string to)
    {
        if (!Targets(path, from)) return path;
        string? field = Field(path);
        return field == null ? to : Compose(to, field);
    }

    /// <summary>The path of <paramref name="field"/> of <paramref name="variable"/>.</summary>
    public static string Compose(string variable, string field) => variable + Separator + field;
}
