namespace Hrot.Blueprints.Core.Compiler.Emit;

public static class Sanitizer
{
    /// <summary>
    /// Convert a Blueprint name to a C# identifier.
    /// E.g. "Move To And Fire" -> "MoveToAndFire"
    /// </summary>
    public static string SanitizeName(string name)
    {
        var sb = new System.Text.StringBuilder();
        bool capitalizeNext = true;
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(capitalizeNext ? char.ToUpperInvariant(c) : c);
                capitalizeNext = false;
            }
            else
            {
                capitalizeNext = true;
            }
        }
        return sb.Length > 0 ? sb.ToString() : "UnknownBlueprint";
    }

    /// <summary>
    /// E.g. "MoveToAndFire" + 0xA1B2C3D4 + false -> "MoveToAndFire_A1B2C3D4_Bp.g.cs"
    ///                                   + true  -> "BlueprintRegistrar_MoveToAndFire_A1B2C3D4_Bp.g.cs"
    /// Per Q-18.4 class name: {SanitizedName}_{BlueprintId:X8}_Bp
    /// </summary>
    public static string GeneratedFileName(string sanitizedName, int blueprintId, bool isRegistrar)
    {
        return isRegistrar
            ? $"BlueprintRegistrar_{sanitizedName}_{blueprintId:X8}_Bp.g.cs"
            : $"{sanitizedName}_{blueprintId:X8}_Bp.g.cs";
    }
}
