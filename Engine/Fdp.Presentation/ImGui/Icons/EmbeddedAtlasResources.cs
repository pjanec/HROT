namespace Fdp.Presentation.Icons;

/// <summary>
/// Provides access to icon-atlas PNG files embedded as managed resources in this assembly.
///
/// <para>The integration layer (e.g. <c>Hrot.ClusterRunner</c>, <c>Hrot.IG</c>) is responsible
/// for uploading the returned bytes to the GPU and constructing an <see cref="IconAtlas"/>
/// from the resulting texture handle.  This class is GPU-framework-agnostic.</para>
/// </summary>
public static class EmbeddedAtlasResources
{
    private const string SilkResourceName = "FDP.Toolkit_ImGui.Icons.famfamfam-silk.png";

    /// <summary>
    /// Returns the raw PNG bytes of the <em>FamFamFam Silk</em> 16 × 16 icon atlas
    /// (<c>famfamfam-silk.png</c>).
    ///
    /// <para>Usage example (Raylib integration):</para>
    /// <code>
    /// byte[] pngBytes = EmbeddedAtlasResources.GetSilkAtlasPngBytes();
    /// var img = Raylib.LoadImageFromMemory(".png", pngBytes);
    /// var tex = Raylib.LoadTextureFromImage(img);
    /// var atlas = new IconAtlas(tex.Id, tex.Width, tex.Height, iconSize: 16f);
    /// </code>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the embedded resource is not found — this indicates a build misconfiguration
    /// (the <c>EmbeddedResource</c> item in <c>FDP.Toolkit_ImGui.csproj</c> is missing).
    /// </exception>
    public static byte[] GetSilkAtlasPngBytes()
    {
        var asm    = typeof(EmbeddedAtlasResources).Assembly;
        using var stream = asm.GetManifestResourceStream(SilkResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{SilkResourceName}' not found. " +
                "Ensure 'famfamfam-silk.png' is included as an EmbeddedResource in FDP.Toolkit_ImGui.csproj.");

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
