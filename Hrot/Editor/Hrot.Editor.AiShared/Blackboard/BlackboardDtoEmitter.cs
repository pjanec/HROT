using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Hrot.Editor.AiShared.Emit;

namespace Hrot.Editor.AiShared.Blackboard;

// ---------------------------------------------------------------------------
// Model types
// ---------------------------------------------------------------------------

/// <summary>
/// Represents a single field entry in the editor's blackboard model.
/// </summary>
public abstract record BlackboardFieldEntry(string Name, Type FieldType);

/// <summary>
/// An editor-managed field: regenerated from the editor model on each save.
/// </summary>
/// <param name="Name">Field identifier.</param>
/// <param name="FieldType">CLR type of the field.</param>
/// <param name="Comment">
/// If non-null, emitted as a <c>/// &lt;summary&gt;...&lt;/summary&gt;</c> block above the
/// field declaration. If null, no doc comment is emitted.
/// </param>
public record EditorManagedFieldEntry(
    string Name,
    Type FieldType,
    string? Comment)
    : BlackboardFieldEntry(Name, FieldType);

/// <summary>
/// A read-only passthrough field: the verbatim text captured from the source file is emitted
/// unchanged. Not editable in the Variables panel.
/// </summary>
/// <param name="Name">Field identifier.</param>
/// <param name="FieldType">CLR type of the field (for using-directive collection).</param>
/// <param name="VerbatimText">
/// The exact text to emit for this field (from the source span captured by the parser).
/// </param>
public record ReadOnlyFieldEntry(
    string Name,
    Type FieldType,
    string VerbatimText)
    : BlackboardFieldEntry(Name, FieldType);

/// <summary>
/// The full model for a blackboard DTO file to be emitted.
/// </summary>
/// <param name="AssetId">Unique asset identifier, written into the marker block.</param>
/// <param name="AssetName">Human-readable asset name, written into the marker block.</param>
/// <param name="StructName">Simple (unqualified) name of the emitted struct.</param>
/// <param name="Namespace">C# file-scoped namespace for the emitted file.</param>
/// <param name="Fields">Field entries in canonical declaration order.</param>
public record BlackboardDtoModel(
    Guid AssetId,
    string AssetName,
    string StructName,
    string Namespace,
    IReadOnlyList<BlackboardFieldEntry> Fields);

// ---------------------------------------------------------------------------
// Emitter
// ---------------------------------------------------------------------------

/// <summary>
/// Stateless emitter for blackboard DTO companion files
/// (<c>{AssetName}.Blackboard.cs</c>).
/// Emits a file-scoped namespace C# file with <c>[StructLayout(LayoutKind.Sequential)]</c>,
/// deterministic using directives, and field entries in canonical order.
/// </summary>
public static class BlackboardDtoEmitter
{
    private const string NL = "\n";
    private const string Indent = "    ";

    // Second marker line for the 4-line header block.
    private const string HandIntroducedLine =
        "// Hand-introduced fields with attributes or non-standard types are preserved verbatim.";

    // C# primitive type aliases -- these types do not require a using directive.
    private static readonly HashSet<Type> AliasTypes = new()
    {
        typeof(bool),
        typeof(byte),
        typeof(sbyte),
        typeof(char),
        typeof(short),
        typeof(ushort),
        typeof(int),
        typeof(uint),
        typeof(long),
        typeof(ulong),
        typeof(float),
        typeof(double),
        typeof(decimal),
        typeof(string),
        typeof(object),
        typeof(void),
    };

    // C# type alias names for primitives.
    internal static readonly Dictionary<Type, string> TypeAliases = new()
    {
        { typeof(bool),    "bool"    },
        { typeof(byte),    "byte"    },
        { typeof(sbyte),   "sbyte"   },
        { typeof(char),    "char"    },
        { typeof(short),   "short"   },
        { typeof(ushort),  "ushort"  },
        { typeof(int),     "int"     },
        { typeof(uint),    "uint"    },
        { typeof(long),    "long"    },
        { typeof(ulong),   "ulong"   },
        { typeof(float),   "float"   },
        { typeof(double),  "double"  },
        { typeof(decimal), "decimal" },
        { typeof(string),  "string"  },
        { typeof(object),  "object"  },
    };

    /// <summary>
    /// Returns the complete .cs file content for <paramref name="model"/>. Deterministic.
    /// </summary>
    public static string Emit(BlackboardDtoModel model)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));

        var sb = new StringBuilder();

        // ---- 4-line marker block ----
        sb.Append(FluentCSharpEmitterBase.EditorGeneratedMarker).Append(NL);
        sb.Append(HandIntroducedLine).Append(NL);
        sb.Append("// OwningAssetId: ").Append(model.AssetId.ToString("D")).Append(NL);
        sb.Append("// OwningAssetName: ").Append(model.AssetName).Append(NL);
        sb.Append(NL);

        // ---- using directives ----
        var usings = new UsingDirectiveSet();
        usings.Add("System.Runtime.InteropServices"); // always required for [StructLayout]
        foreach (var field in model.Fields)
        {
            CollectUsings(field.FieldType, usings);
        }

        foreach (var ns in usings.ToSortedList())
        {
            if (ns.Length == 0)
                sb.Append(NL); // blank-line separator between System.* and other usings
            else
                sb.Append("using ").Append(ns).Append(';').Append(NL);
        }
        sb.Append(NL);

        // ---- namespace ----
        sb.Append("namespace ").Append(model.Namespace).Append(';').Append(NL);
        sb.Append(NL);

        // ---- struct declaration ----
        sb.Append("[StructLayout(LayoutKind.Sequential)]").Append(NL);
        sb.Append("public partial struct ").Append(model.StructName).Append(NL);
        sb.Append('{').Append(NL);

        // ---- fields ----
        for (int i = 0; i < model.Fields.Count; i++)
        {
            var field = model.Fields[i];

            if (field is EditorManagedFieldEntry managed)
            {
                EmitEditorManagedField(sb, managed);
            }
            else if (field is ReadOnlyFieldEntry readOnly)
            {
                EmitReadOnlyField(sb, readOnly);
            }

            // Blank line between fields (but not after the last one).
            if (i < model.Fields.Count - 1)
                sb.Append(NL);
        }

        sb.Append('}').Append(NL);

        return sb.ToString();
    }

    /// <summary>
    /// Convenience: emit <paramref name="model"/> and write atomically to
    /// <paramref name="filePath"/>. Returns true if the file was written (content changed).
    /// </summary>
    public static bool EmitAndWrite(BlackboardDtoModel model, string filePath)
    {
        if (model == null)    throw new ArgumentNullException(nameof(model));
        if (filePath == null) throw new ArgumentNullException(nameof(filePath));

        string content = Emit(model);
        return FluentCSharpEmitterBase.WriteAtomic(filePath, content);
    }

    /// <summary>
    /// Validates that a save operation is allowed given the asset's current load state.
    /// Throws <see cref="InvalidOperationException"/> when:
    /// <list type="bullet">
    /// <item>LoadState is <see cref="BlackboardLoadState.StructParseFailed"/> or
    /// <see cref="BlackboardLoadState.AssemblyFailed"/> (saving is always blocked).</item>
    /// <item>LoadState is <see cref="BlackboardLoadState.SpanCaptureFailed"/> and
    /// <paramref name="allowLossySave"/> is false (caller must opt-in explicitly).</item>
    /// </list>
    /// No-op when LoadState is <see cref="BlackboardLoadState.Clean"/>.
    /// </summary>
    public static void ValidateSaveAllowed(IBlackboardManagedAsset asset, bool allowLossySave = false)
    {
        if (asset == null) throw new ArgumentNullException(nameof(asset));
        switch (asset.LoadState)
        {
            case BlackboardLoadState.StructParseFailed:
            case BlackboardLoadState.AssemblyFailed:
                throw new InvalidOperationException(
                    $"Cannot save blackboard: load state is {asset.LoadState}. {asset.LoadDiagnosticMessage}");
            case BlackboardLoadState.SpanCaptureFailed when !allowLossySave:
                throw new InvalidOperationException(
                    $"Cannot save blackboard: span capture failed and lossy save was not confirmed. {asset.LoadDiagnosticMessage}");
        }
    }

    /// <summary>
    /// Emits the companion heavy struct file content.
    /// Only called when the pack result indicates heavy variables exist.
    /// The caller is responsible for filtering <paramref name="model"/> so that
    /// <c>Fields</c> contains only the heavy-tier fields.
    /// </summary>
    /// <param name="model">
    /// A <see cref="BlackboardDtoModel"/> whose <c>Fields</c> list contains only the
    /// fields destined for the heavy struct. All other model properties (AssetId,
    /// AssetName, Namespace) are taken from this model.
    /// </param>
    /// <param name="heavyStructName">
    /// The simple name of the heavy struct, e.g. <c>OrcGuard_HeavyBlackboard</c>.
    /// </param>
    public static string EmitHeavy(BlackboardDtoModel model, string heavyStructName)
    {
        if (model == null)          throw new ArgumentNullException(nameof(model));
        if (heavyStructName == null) throw new ArgumentNullException(nameof(heavyStructName));

        var sb = new StringBuilder();

        // ---- 4-line marker block (same asset, different struct name) ----
        sb.Append(FluentCSharpEmitterBase.EditorGeneratedMarker).Append(NL);
        sb.Append(HandIntroducedLine).Append(NL);
        sb.Append("// OwningAssetId: ").Append(model.AssetId.ToString("D")).Append(NL);
        sb.Append("// OwningAssetName: ").Append(model.AssetName).Append(NL);
        sb.Append(NL);

        // ---- using directives ----
        var usings = new UsingDirectiveSet();
        usings.Add("System.Runtime.InteropServices");
        foreach (var field in model.Fields)
        {
            CollectUsings(field.FieldType, usings);
        }

        foreach (var ns in usings.ToSortedList())
        {
            if (ns.Length == 0)
                sb.Append(NL);
            else
                sb.Append("using ").Append(ns).Append(';').Append(NL);
        }
        sb.Append(NL);

        // ---- namespace ----
        sb.Append("namespace ").Append(model.Namespace).Append(';').Append(NL);
        sb.Append(NL);

        // ---- struct declaration ----
        sb.Append("[StructLayout(LayoutKind.Sequential)]").Append(NL);
        sb.Append("public partial struct ").Append(heavyStructName).Append(NL);
        sb.Append('{').Append(NL);

        // ---- fields ----
        for (int i = 0; i < model.Fields.Count; i++)
        {
            var field = model.Fields[i];

            if (field is EditorManagedFieldEntry managed)
            {
                EmitEditorManagedField(sb, managed);
            }
            else if (field is ReadOnlyFieldEntry readOnly)
            {
                EmitReadOnlyField(sb, readOnly);
            }

            if (i < model.Fields.Count - 1)
                sb.Append(NL);
        }

        sb.Append('}').Append(NL);

        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // Field emission helpers
    // -------------------------------------------------------------------------

    private static void EmitEditorManagedField(StringBuilder sb, EditorManagedFieldEntry field)
    {
        if (field.Comment != null)
        {
            sb.Append(Indent).Append("/// <summary>").Append(field.Comment).Append("</summary>").Append(NL);
        }
        if (field.FieldType == typeof(bool))
        {
            sb.Append(Indent).Append("[MarshalAs(UnmanagedType.I1)]").Append(NL);
        }
        sb.Append(Indent)
          .Append("public ")
          .Append(GetTypeName(field.FieldType))
          .Append(' ')
          .Append(field.Name)
          .Append(';')
          .Append(NL);
    }

    private static void EmitReadOnlyField(StringBuilder sb, ReadOnlyFieldEntry field)
    {
        // Emit the verbatim text exactly as captured. The text already includes the
        // appropriate indentation from the source file.
        sb.Append(field.VerbatimText);
        // Ensure the verbatim text ends with a newline so the next field starts on a new line.
        if (field.VerbatimText.Length > 0 && field.VerbatimText[field.VerbatimText.Length - 1] != '\n')
            sb.Append(NL);
    }

    // -------------------------------------------------------------------------
    // Type name and using helpers
    // -------------------------------------------------------------------------

    /// <summary>Returns the C# type name to use in the emitted field declaration.</summary>
    private static string GetTypeName(Type t)
    {
        if (TypeAliases.TryGetValue(t, out string? alias))
            return alias;
        return t.Name;
    }

    /// <summary>
    /// Adds the namespace(s) required to reference <paramref name="t"/> to
    /// <paramref name="usings"/>.
    /// </summary>
    private static void CollectUsings(Type t, UsingDirectiveSet usings)
    {
        // Primitive aliases do not need a using directive.
        if (AliasTypes.Contains(t))
            return;

        if (!string.IsNullOrEmpty(t.Namespace))
            usings.Add(t.Namespace);
    }
}
