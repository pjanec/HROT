namespace Hrot.AiEditor.Persistence.Emit;

/// <summary>
/// ⭐ <c>CE-2039</c> — the PUBLIC door to the one identifier sanitizer (<c>Fdp.Toolkit.Behavior.Shared.IdentifierSanitizer</c>,
/// a linked internal file) for the net8.0 editors, which cannot link it without colliding with this assembly's copy.
/// 📄 <c>DESIGN_Unified_Behaviour_Run.md</c> "S8g" G3.
/// </summary>
public static class Identifiers
{
    /// <summary>Non-identifier chars → <c>_</c>; a leading digit gets <c>_</c>; nothing left → <paramref name="emptyResult"/>.</summary>
    public static string ReplaceInvalid(string? name, string emptyResult, bool bare = false)
        => global::Fdp.Toolkit.Behavior.Shared.IdentifierSanitizer.ReplaceInvalid(name, emptyResult, bare);

    /// <summary>Non-identifier chars removed; a leading digit gets <c>_</c>; nothing left → <paramref name="fallback"/>.</summary>
    public static string StripInvalid(string? name, string fallback, bool bare = false)
        => global::Fdp.Toolkit.Behavior.Shared.IdentifierSanitizer.StripInvalid(name, fallback, bare);

    /// <summary>Letter/digit runs joined PascalCase; a leading digit gets <c>_</c>; nothing left → <paramref name="fallback"/>.</summary>
    public static string PascalJoin(string? name, string fallback)
        => global::Fdp.Toolkit.Behavior.Shared.IdentifierSanitizer.PascalJoin(name, fallback);

    /// <summary>True for a C# reserved keyword.</summary>
    public static bool IsReservedKeyword(string? name)
        => global::Fdp.Toolkit.Behavior.Shared.IdentifierSanitizer.IsReservedKeyword(name);
}
