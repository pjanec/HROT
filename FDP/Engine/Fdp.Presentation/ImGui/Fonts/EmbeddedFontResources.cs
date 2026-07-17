namespace Fdp.Presentation.Fonts;

/// <summary>
/// Provides access to UI font files embedded as managed resources in this assembly.
///
/// <para>The integration / rendering layer is responsible for uploading the returned bytes
/// to Raylib (e.g. via <c>Raylib.LoadFontFromMemory</c>) and caching the resulting
/// <c>Font</c> handle.  This class is GPU-framework-agnostic and mirrors
/// <see cref="Fdp.Presentation.Icons.EmbeddedAtlasResources"/>.</para>
/// </summary>
public static class EmbeddedFontResources
{
    private const string RobotoRegularResourceName = "FDP.Toolkit_ImGui.Fonts.Roboto-Regular.ttf";
    private const string FontAwesomeSolidResourceName = "FDP.Toolkit_ImGui.Fonts.fa-solid-900.ttf";

    /// <summary>
    /// Returns the raw TTF bytes of the <em>Roboto Regular</em> font (<c>Roboto-Regular.ttf</c>).
    ///
    /// <para>Usage example (Raylib integration):</para>
    /// <code>
    /// byte[] ttf = EmbeddedFontResources.GetRobotoRegularTtfBytes();
    /// unsafe
    /// {
    ///     fixed (byte* p = ttf)
    ///         font = Raylib.LoadFontFromMemory(".ttf", p, ttf.Length, fontSize, (int*)null, 0);
    /// }
    /// </code>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the embedded resource is not found — this indicates a build misconfiguration
    /// (the <c>EmbeddedResource</c> item in <c>Fdp.Presentation.csproj</c> is missing).
    /// </exception>
    public static byte[] GetRobotoRegularTtfBytes()
    {
        var asm = typeof(EmbeddedFontResources).Assembly;
        using var stream = asm.GetManifestResourceStream(RobotoRegularResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{RobotoRegularResourceName}' not found. " +
                "Ensure 'Roboto-Regular.ttf' is included as an EmbeddedResource in Fdp.Presentation.csproj.");

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Returns the raw TTF bytes of <em>Font Awesome 6 Free Solid</em> (<c>fa-solid-900.ttf</c>).
    ///
    /// <para>The glyphs live in the Unicode Private Use Area (see
    /// <see cref="IconsFontAwesome6"/> for named codepoint constants). Merge this face onto
    /// the primary UI font with <c>ImFontConfig.MergeMode = true</c> and the FA glyph range so
    /// icon glyphs fill in alongside text.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the embedded resource is not found — indicates a build misconfiguration
    /// (the <c>EmbeddedResource</c> item in <c>Fdp.Presentation.csproj</c> is missing).
    /// </exception>
    public static byte[] GetFontAwesomeSolidTtfBytes()
    {
        var asm = typeof(EmbeddedFontResources).Assembly;
        using var stream = asm.GetManifestResourceStream(FontAwesomeSolidResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{FontAwesomeSolidResourceName}' not found. " +
                "Ensure 'fa-solid-900.ttf' is included as an EmbeddedResource in Fdp.Presentation.csproj.");

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
