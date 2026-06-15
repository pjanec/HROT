using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Hrot.Editor.AiShared.Tests.Blackboard;

/// <summary>
/// Tests for <see cref="BlackboardDtoEmitter"/> (TASK-BB-1b-01) and round-trip determinism
/// properties (TASK-BB-1b-06, RT-1 and RT-2).
/// </summary>
public sealed class BlackboardDtoEmitterTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static readonly Guid TestAssetId   = new Guid("12345678-1234-1234-1234-123456789abc");
    private static readonly string TestStructName = "OrcGuard_Blackboard";
    private static readonly string TestNamespace  = "Hrot.Test.Blackboard";

    private static BlackboardDtoModel SimpleModel(params BlackboardFieldEntry[] fields) =>
        new(TestAssetId, "OrcGuard", TestStructName, TestNamespace, fields);

    private static EditorManagedFieldEntry ManagedField(string name, Type type, string? comment = null) =>
        new(name, type, comment);

    private static ReadOnlyFieldEntry ReadOnlyField(string name, Type type, string verbatim) =>
        new(name, type, verbatim);

    private static string[] Lines(string text) =>
        text.Split('\n');

    // -------------------------------------------------------------------------
    // TASK-BB-1b-01: Marker block
    // -------------------------------------------------------------------------

    [Fact]
    public void Emit_StartsWithEditorGeneratedMarker_Line1()
    {
        var model  = SimpleModel(ManagedField("x", typeof(int)));
        var output = BlackboardDtoEmitter.Emit(model);
        var lines  = Lines(output);

        Assert.Equal(FluentCSharpEmitterBase.EditorGeneratedMarker, lines[0]);
    }

    [Fact]
    public void Emit_Line2_IsHandIntroducedComment()
    {
        var model  = SimpleModel(ManagedField("x", typeof(int)));
        var output = BlackboardDtoEmitter.Emit(model);
        var lines  = Lines(output);

        Assert.Equal(
            "// Hand-introduced fields with attributes or non-standard types are preserved verbatim.",
            lines[1]);
    }

    [Fact]
    public void Emit_OwningAssetId_PresentInMarkerBlock()
    {
        var model  = SimpleModel(ManagedField("x", typeof(int)));
        var output = BlackboardDtoEmitter.Emit(model);

        Assert.Contains("// OwningAssetId: " + TestAssetId.ToString("D"), output);
    }

    [Fact]
    public void Emit_OwningAssetName_PresentInMarkerBlock()
    {
        var model  = SimpleModel(ManagedField("x", typeof(int)));
        var output = BlackboardDtoEmitter.Emit(model);

        Assert.Contains("// OwningAssetName: OrcGuard", output);
    }

    [Fact]
    public void Emit_OwningAssetId_Line3()
    {
        var model  = SimpleModel(ManagedField("x", typeof(int)));
        var output = BlackboardDtoEmitter.Emit(model);
        var lines  = Lines(output);

        Assert.Equal("// OwningAssetId: " + TestAssetId.ToString("D"), lines[2]);
    }

    [Fact]
    public void Emit_OwningAssetName_Line4()
    {
        var model  = SimpleModel(ManagedField("x", typeof(int)));
        var output = BlackboardDtoEmitter.Emit(model);
        var lines  = Lines(output);

        Assert.Equal("// OwningAssetName: OrcGuard", lines[3]);
    }

    // -------------------------------------------------------------------------
    // Struct attributes and declaration
    // -------------------------------------------------------------------------

    [Fact]
    public void Emit_ContainsStructLayoutAttribute()
    {
        var model  = SimpleModel(ManagedField("x", typeof(int)));
        var output = BlackboardDtoEmitter.Emit(model);

        Assert.Contains("[StructLayout(LayoutKind.Sequential)]", output);
    }

    [Fact]
    public void Emit_ContainsPublicPartialStruct()
    {
        var model  = SimpleModel(ManagedField("x", typeof(int)));
        var output = BlackboardDtoEmitter.Emit(model);

        Assert.Contains("public partial struct " + TestStructName, output);
    }

    // -------------------------------------------------------------------------
    // Editor-managed fields
    // -------------------------------------------------------------------------

    [Fact]
    public void Emit_EditorManaged_WithComment_HasXmlSummaryBlock()
    {
        var model  = SimpleModel(ManagedField("hitPoints", typeof(int), "Current hit points."));
        var output = BlackboardDtoEmitter.Emit(model);

        Assert.Contains("/// <summary>Current hit points.</summary>", output);
        Assert.Contains("    public int hitPoints;", output);
    }

    [Fact]
    public void Emit_EditorManaged_WithoutComment_NoTripleSlashLine()
    {
        var model  = SimpleModel(ManagedField("speed", typeof(float)));
        var output = BlackboardDtoEmitter.Emit(model);
        var lines  = Lines(output);

        // There must be no line starting with /// for this field
        bool hasTripleSlash = lines.Any(l => l.TrimStart().StartsWith("///"));
        Assert.False(hasTripleSlash);
    }

    [Fact]
    public void Emit_EditorManaged_IntField_CorrectDeclaration()
    {
        var model  = SimpleModel(ManagedField("count", typeof(int)));
        var output = BlackboardDtoEmitter.Emit(model);

        Assert.Contains("    public int count;", output);
    }

    [Fact]
    public void Emit_EditorManaged_BoolField_UsesBoolAlias()
    {
        var model  = SimpleModel(ManagedField("isActive", typeof(bool)));
        var output = BlackboardDtoEmitter.Emit(model);

        Assert.Contains("    public bool isActive;", output);
        // Must NOT emit "System.Boolean"
        Assert.DoesNotContain("System.Boolean", output);
    }

    [Fact]
    public void Emit_BoolField_CarriesMarshalAsI1()
    {
        // Build a model with int A, bool B, int C so we can verify layout.
        var model = SimpleModel(
            ManagedField("A", typeof(int)),
            ManagedField("B", typeof(bool)),
            ManagedField("C", typeof(int)));
        var output = BlackboardDtoEmitter.Emit(model);

        // 1. [MarshalAs(UnmanagedType.I1)] must appear in the output.
        Assert.Contains("[MarshalAs(UnmanagedType.I1)]", output);

        // 2. The attribute line must appear immediately before `public bool B;`.
        var lines = Lines(output);
        int boolLineIdx = Array.FindIndex(lines, l => l.TrimEnd() == "    public bool B;");
        Assert.True(boolLineIdx > 0, "public bool B; line not found");
        Assert.Equal("    [MarshalAs(UnmanagedType.I1)]", lines[boolLineIdx - 1].TrimEnd());

        // 3. ReadOnly (non-bool) fields must NOT have [MarshalAs(UnmanagedType.I1)] injected.
        int aLineIdx = Array.FindIndex(lines, l => l.TrimEnd() == "    public int A;");
        Assert.True(aLineIdx >= 0, "public int A; line not found");
        Assert.DoesNotContain("[MarshalAs", lines[aLineIdx - 1]);

        // 4. Compile the emitted source with Roslyn and verify binary layout via Marshal.
        var syntaxTree = CSharpSyntaxTree.ParseText(output);
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(StructLayoutAttribute).Assembly.Location),
            // System.Runtime (contains MarshalAsAttribute in net8)
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
        };

        var compilation = CSharpCompilation.Create(
            "TestAssembly_BoolMarshalAs",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new System.IO.MemoryStream();
        var emitResult = compilation.Emit(ms);
        Assert.True(emitResult.Success,
            "Roslyn compilation failed:\n" + string.Join("\n", emitResult.Diagnostics));

        ms.Seek(0, System.IO.SeekOrigin.Begin);
        var assembly = Assembly.Load(ms.ToArray());
        var structType = assembly.GetType($"{TestNamespace}.{TestStructName}");
        Assert.NotNull(structType);

        // With [MarshalAs(UnmanagedType.I1)], bool marshals as 1 byte.
        // Sequential layout: int A@0(4), bool B@4(1), pad 3, int C@8(4) -> total 12.
        int offsetC = (int)Marshal.OffsetOf(structType!, "C");
        Assert.Equal(8, offsetC);

        int totalSize = Marshal.SizeOf(structType!);
        Assert.Equal(12, totalSize);
    }

    [Fact]
    public void Emit_EditorManaged_Vector3Field_UsesTypeName()
    {
        var model  = SimpleModel(ManagedField("position", typeof(Vector3)));
        var output = BlackboardDtoEmitter.Emit(model);

        Assert.Contains("    public Vector3 position;", output);
    }

    // -------------------------------------------------------------------------
    // Read-only passthrough fields
    // -------------------------------------------------------------------------

    [Fact]
    public void Emit_ReadOnly_VerbatimText_EmittedExactly()
    {
        const string verbatim = "    [Obsolete]\n    public int legacyField;\n";
        var model  = SimpleModel(ReadOnlyField("legacyField", typeof(int), verbatim));
        var output = BlackboardDtoEmitter.Emit(model);

        Assert.Contains(verbatim, output);
    }

    [Fact]
    public void Emit_ReadOnly_VerbatimText_IsByteIdenticalSubstring()
    {
        const string verbatim = "    public float customField; // hand-written\n";
        var model  = SimpleModel(ReadOnlyField("customField", typeof(float), verbatim));
        var output = BlackboardDtoEmitter.Emit(model);

        int idx = output.IndexOf(verbatim, StringComparison.Ordinal);
        Assert.True(idx >= 0, "verbatim text not found in emitted output");
        Assert.Equal(verbatim, output.Substring(idx, verbatim.Length));
    }

    // -------------------------------------------------------------------------
    // Using directives
    // -------------------------------------------------------------------------

    [Fact]
    public void Emit_AlwaysIncludesSystemRuntimeInteropServices()
    {
        var model  = SimpleModel(ManagedField("x", typeof(int)));
        var output = BlackboardDtoEmitter.Emit(model);

        Assert.Contains("using System.Runtime.InteropServices;", output);
    }

    [Fact]
    public void Emit_Vector3Field_AddsSystemNumericsUsing()
    {
        var model  = SimpleModel(ManagedField("pos", typeof(Vector3)));
        var output = BlackboardDtoEmitter.Emit(model);

        Assert.Contains("using System.Numerics;", output);
    }

    [Fact]
    public void Emit_PrimitiveFieldsOnly_NoExtraUsings()
    {
        var model  = SimpleModel(ManagedField("x", typeof(int)), ManagedField("y", typeof(float)));
        var output = BlackboardDtoEmitter.Emit(model);

        // Only the mandatory System.Runtime.InteropServices should appear.
        var usingLines = Lines(output).Where(l => l.StartsWith("using ")).ToList();
        Assert.Single(usingLines);
        Assert.Equal("using System.Runtime.InteropServices;", usingLines[0]);
    }

    [Fact]
    public void Emit_MultipleNamespaces_SortedSystemFirst()
    {
        var model  = SimpleModel(
            ManagedField("pos", typeof(Vector3)),
            ManagedField("q",   typeof(Quaternion)));
        var output = BlackboardDtoEmitter.Emit(model);

        var usingLines = Lines(output).Where(l => l.StartsWith("using ")).ToList();
        // All System.* usings should appear before any non-System ones.
        // In this case both are System.*, so they should be sorted alphabetically.
        Assert.Contains("using System.Numerics;", usingLines);
        Assert.Contains("using System.Runtime.InteropServices;", usingLines);

        int numericsIdx = usingLines.IndexOf("using System.Numerics;");
        int rioIdx      = usingLines.IndexOf("using System.Runtime.InteropServices;");
        Assert.True(numericsIdx < rioIdx, "System.Numerics should sort before System.Runtime.InteropServices");
    }

    // -------------------------------------------------------------------------
    // Determinism
    // -------------------------------------------------------------------------

    [Fact]
    public void Emit_CalledTwiceWithSameModel_ReturnsSameString()
    {
        var model  = SimpleModel(
            ManagedField("hp", typeof(int), "Hit points"),
            ManagedField("speed", typeof(float)));
        var s1 = BlackboardDtoEmitter.Emit(model);
        var s2 = BlackboardDtoEmitter.Emit(model);

        Assert.Equal(s1, s2);
    }

    // -------------------------------------------------------------------------
    // Mixed model
    // -------------------------------------------------------------------------

    [Fact]
    public void Emit_MixedModel_FieldsInOrder()
    {
        const string verbatim = "    [CustomAttr]\n    public int specialField;\n";
        var model = SimpleModel(
            ManagedField("a", typeof(int), "First field"),
            ReadOnlyField("specialField", typeof(int), verbatim),
            ManagedField("b", typeof(bool)));
        var output = BlackboardDtoEmitter.Emit(model);

        int posA     = output.IndexOf("public int a;",          StringComparison.Ordinal);
        int posSpec  = output.IndexOf("specialField",           StringComparison.Ordinal);
        int posB     = output.IndexOf("public bool b;",         StringComparison.Ordinal);

        Assert.True(posA    >= 0, "field a not found");
        Assert.True(posSpec >= 0, "specialField not found");
        Assert.True(posB    >= 0, "field b not found");
        Assert.True(posA < posSpec, "field a should come before specialField");
        Assert.True(posSpec < posB, "specialField should come before field b");
    }

    // =========================================================================
    // TASK-BB-1b-06: Round-trip determinism tests
    // =========================================================================

    // -------------------------------------------------------------------------
    // RT-1: No-edit round-trip is byte-identical
    // -------------------------------------------------------------------------

    [Fact]
    public void RT1_AllEditorManaged_ParseAndReemit_IsIdentical()
    {
        var model = new BlackboardDtoModel(
            TestAssetId,
            "TestAsset",
            "TestAsset_Blackboard",
            "Test.Namespace",
            new BlackboardFieldEntry[]
            {
                ManagedField("health",   typeof(int),   "Health value"),
                ManagedField("speed",    typeof(float), null),
                ManagedField("isActive", typeof(bool),  "Whether active"),
            });

        string s1 = BlackboardDtoEmitter.Emit(model);

        // Parse s1 to extract fields, then rebuild model from parse result and re-emit.
        var parseResult = BlackboardSourceTextParser.Parse(s1, "TestAsset_Blackboard");
        Assert.True(parseResult.LocateResult.Found, "Parser should find the struct in emitted output");

        // Rebuild fields from parse result: treat all as read-only (verbatim) since we are
        // simulating a no-edit round-trip where the editor just re-reads the file.
        var rebuildFields = new List<BlackboardFieldEntry>();
        foreach (var f in parseResult.Fields)
        {
            string verbatim = s1.Substring(f.VerbatimSpan.Start, f.VerbatimSpan.Length);
            // Preserve the field type from the original model by finding the matching entry.
            var originalField = model.Fields.FirstOrDefault(mf => mf.Name == f.Name);
            var type = originalField?.FieldType ?? typeof(int);
            rebuildFields.Add(ReadOnlyField(f.Name, type, verbatim));
        }

        var model2 = model with { Fields = rebuildFields };
        string s2 = BlackboardDtoEmitter.Emit(model2);

        Assert.Equal(s1, s2);
    }

    [Fact]
    public void RT1_AllEditorManaged_NoComment_ParseAndReemit_IsIdentical()
    {
        var model = new BlackboardDtoModel(
            TestAssetId,
            "NoCommentAsset",
            "NoComment_Blackboard",
            "Test.Namespace",
            new BlackboardFieldEntry[]
            {
                ManagedField("x", typeof(int)),
                ManagedField("y", typeof(float)),
            });

        string s1 = BlackboardDtoEmitter.Emit(model);

        var parseResult = BlackboardSourceTextParser.Parse(s1, "NoComment_Blackboard");
        Assert.True(parseResult.LocateResult.Found);

        var rebuildFields = new List<BlackboardFieldEntry>();
        foreach (var f in parseResult.Fields)
        {
            string verbatim = s1.Substring(f.VerbatimSpan.Start, f.VerbatimSpan.Length);
            var originalField = model.Fields.FirstOrDefault(mf => mf.Name == f.Name);
            var type = originalField?.FieldType ?? typeof(int);
            rebuildFields.Add(ReadOnlyField(f.Name, type, verbatim));
        }

        var model2 = model with { Fields = rebuildFields };
        string s2 = BlackboardDtoEmitter.Emit(model2);

        Assert.Equal(s1, s2);
    }

    // -------------------------------------------------------------------------
    // RT-2: Single-edit round-trip produces confined diff
    // -------------------------------------------------------------------------

    [Fact]
    public void RT2_AddOneField_AllOriginalFieldsPresent_NewFieldPresent()
    {
        var original = new BlackboardDtoModel(
            TestAssetId,
            "EditAsset",
            "Edit_Blackboard",
            "Test.Namespace",
            new BlackboardFieldEntry[]
            {
                ManagedField("alpha", typeof(int)),
                ManagedField("beta",  typeof(float)),
            });

        string s1 = BlackboardDtoEmitter.Emit(original);

        var extended = original with
        {
            Fields = new BlackboardFieldEntry[]
            {
                ManagedField("alpha",  typeof(int)),
                ManagedField("beta",   typeof(float)),
                ManagedField("gamma",  typeof(bool)),
            }
        };
        string s2 = BlackboardDtoEmitter.Emit(extended);

        Assert.Contains("public int alpha;",   s2);
        Assert.Contains("public float beta;",  s2);
        Assert.Contains("public bool gamma;",  s2);
    }

    [Fact]
    public void RT2_RemoveOneField_RemovedFieldAbsent_OthersUnchanged()
    {
        var original = new BlackboardDtoModel(
            TestAssetId,
            "RemoveAsset",
            "Remove_Blackboard",
            "Test.Namespace",
            new BlackboardFieldEntry[]
            {
                ManagedField("keep1",  typeof(int)),
                ManagedField("remove", typeof(float)),
                ManagedField("keep2",  typeof(bool)),
            });

        string s1 = BlackboardDtoEmitter.Emit(original);

        var reduced = original with
        {
            Fields = new BlackboardFieldEntry[]
            {
                ManagedField("keep1", typeof(int)),
                ManagedField("keep2", typeof(bool)),
            }
        };
        string s2 = BlackboardDtoEmitter.Emit(reduced);

        Assert.Contains("public int keep1;",   s2);
        Assert.DoesNotContain("remove",        s2);
        Assert.Contains("public bool keep2;",  s2);
    }

    [Fact]
    public void RT2_ChangeComment_OnlyThatFieldCommentChanges()
    {
        var original = new BlackboardDtoModel(
            TestAssetId,
            "CommentAsset",
            "Comment_Blackboard",
            "Test.Namespace",
            new BlackboardFieldEntry[]
            {
                ManagedField("x", typeof(int), "Original comment"),
                ManagedField("y", typeof(float)),
            });

        string s1 = BlackboardDtoEmitter.Emit(original);

        var modified = original with
        {
            Fields = new BlackboardFieldEntry[]
            {
                ManagedField("x", typeof(int), "Updated comment"),
                ManagedField("y", typeof(float)),
            }
        };
        string s2 = BlackboardDtoEmitter.Emit(modified);

        // The new comment is present, the old is gone.
        Assert.Contains("/// <summary>Updated comment</summary>", s2);
        Assert.DoesNotContain("Original comment", s2);

        // The field declaration itself is unchanged.
        Assert.Contains("    public int x;", s2);

        // Field y (unchanged) must appear in both strings in the same form.
        Assert.Contains("    public float y;", s1);
        Assert.Contains("    public float y;", s2);

        // Compare line-by-line: all lines not belonging to field x must be identical.
        var lines1 = Lines(s1).ToList();
        var lines2 = Lines(s2).ToList();
        // Both outputs have the same structure so line counts differ by 0 (comment replaced).
        Assert.Equal(lines1.Count, lines2.Count);

        for (int i = 0; i < lines1.Count; i++)
        {
            if (lines1[i].Contains("Original comment") || lines2[i].Contains("Updated comment"))
                continue; // this is the changed line
            Assert.Equal(lines1[i], lines2[i]);
        }
    }

    [Fact]
    public void RT2_ReadOnlyFields_ByteIdenticalWhenNotTouched()
    {
        const string verbatim = "    [Obsolete]\n    public int legacy;\n";
        var model = new BlackboardDtoModel(
            TestAssetId,
            "MixedAsset",
            "Mixed_Blackboard",
            "Test.Namespace",
            new BlackboardFieldEntry[]
            {
                ManagedField("managed", typeof(int)),
                ReadOnlyField("legacy", typeof(int), verbatim),
            });

        string s1 = BlackboardDtoEmitter.Emit(model);

        // Add a new managed field; the read-only field must remain byte-identical.
        var extended = model with
        {
            Fields = new BlackboardFieldEntry[]
            {
                ManagedField("managed",    typeof(int)),
                ReadOnlyField("legacy",    typeof(int), verbatim),
                ManagedField("newField",   typeof(bool)),
            }
        };
        string s2 = BlackboardDtoEmitter.Emit(extended);

        // The read-only verbatim text appears identically in both s1 and s2.
        Assert.Contains(verbatim, s1);
        Assert.Contains(verbatim, s2);

        // The substring in s2 is byte-identical to what appeared in s1.
        int idx1 = s1.IndexOf(verbatim, StringComparison.Ordinal);
        int idx2 = s2.IndexOf(verbatim, StringComparison.Ordinal);
        Assert.True(idx1 >= 0 && idx2 >= 0);
        Assert.Equal(s1.Substring(idx1, verbatim.Length), s2.Substring(idx2, verbatim.Length));
    }

    // -------------------------------------------------------------------------
    // TASK-BB-1c-04 Part C: EmitHeavy
    // -------------------------------------------------------------------------

    [Fact]
    public void EmitHeavy_produces_correct_marker_block()
    {
        var model = SimpleModel(ManagedField("hp", typeof(int)));
        string output = BlackboardDtoEmitter.EmitHeavy(model, "OrcGuard_BlackboardHeavy");

        Assert.Contains(Hrot.Editor.AiShared.Emit.FluentCSharpEmitterBase.EditorGeneratedMarker, output);
        Assert.Contains(TestNamespace, output);
        Assert.Contains("struct OrcGuard_BlackboardHeavy", output);
    }

    [Fact]
    public void EmitHeavy_includes_only_heavy_fields()
    {
        // Provide a model with one managed field; EmitHeavy should include it.
        var model = SimpleModel(ManagedField("heavy_value", typeof(float)));
        string output = BlackboardDtoEmitter.EmitHeavy(model, "OrcGuard_BlackboardHeavy");

        Assert.Contains("public float heavy_value;", output);
    }

    [Fact]
    public void EmitHeavy_struct_name_matches_parameter()
    {
        var model  = SimpleModel(ManagedField("x", typeof(int)));
        const string structName = "MyCustomHeavyStruct";
        string output = BlackboardDtoEmitter.EmitHeavy(model, structName);

        Assert.Contains($"struct {structName}", output);
        Assert.DoesNotContain(TestStructName, output);
    }

    [Fact]
    public void EmitHeavy_empty_heavy_fields_produces_empty_struct()
    {
        var model = SimpleModel(); // no fields
        string output = BlackboardDtoEmitter.EmitHeavy(model, "Empty_Heavy");

        // Should still produce valid output with the struct declaration.
        Assert.Contains("struct Empty_Heavy", output);
        // No field declarations ("public int", "public float", etc.) should appear.
        Assert.DoesNotContain("public int", output);
        Assert.DoesNotContain("public float", output);
    }
}
