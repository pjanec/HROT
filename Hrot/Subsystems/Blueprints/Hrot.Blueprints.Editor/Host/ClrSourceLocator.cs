using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// Editor punch-list #6 — resolves the source file + line of a reflected CLR method from the
/// portable PDB emitted next to its assembly (<c>DebugType=portable</c>), so the function
/// inspector's "open in editor" button can jump straight to the definition.
///
/// <para>
/// Like <see cref="ClrXmlDocSource"/>, this is a static-build disk artifact: the <c>.pdb</c> sits
/// next to the <c>.dll</c> found via <see cref="Assembly.Location"/>. Dynamic / in-memory
/// hot-reload assemblies have an empty location (and their PDB never hits disk) → this returns
/// <c>null</c> and the caller disables the button. Every failure path is swallowed to null.
/// </para>
/// </summary>
internal static class ClrSourceLocator
{
    /// <summary>A resolved source location.</summary>
    public readonly record struct SourceLocation(string File, int Line);

    /// <summary>
    /// Returns the first (non-hidden) sequence point of <paramref name="method"/> as a source
    /// file + 1-based line, or <c>null</c> when the PDB is missing/unreadable or the assembly is dynamic.
    /// </summary>
    public static SourceLocation? Resolve(MethodInfo method)
    {
        try
        {
            var asm = method.DeclaringType?.Assembly;
            if (asm == null) return null;

            var location = asm.Location;
            if (string.IsNullOrEmpty(location)) return null;

            var pdbPath = Path.ChangeExtension(location, ".pdb");
            if (!File.Exists(pdbPath)) return null;

            using var stream = File.OpenRead(pdbPath);
            using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
            var reader = provider.GetMetadataReader();

            // MethodDebugInformation shares the row number of the method definition token.
            int rowNumber = method.MetadataToken & 0x00FFFFFF;
            if (rowNumber <= 0) return null;
            var mdiHandle = MetadataTokens.MethodDebugInformationHandle(rowNumber);

            var debugInfo = reader.GetMethodDebugInformation(mdiHandle);
            foreach (var sp in debugInfo.GetSequencePoints())
            {
                if (sp.IsHidden) continue;
                var doc = reader.GetDocument(sp.Document);
                var file = reader.GetString(doc.Name);
                if (string.IsNullOrEmpty(file)) return null;
                return new SourceLocation(file, sp.StartLine);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}
