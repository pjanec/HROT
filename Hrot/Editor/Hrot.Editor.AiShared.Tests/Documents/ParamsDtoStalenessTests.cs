using System;
using System.IO;
using Fdp.Core;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Documents;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Documents;

/// <summary>A generated params DTO as the params DTO asset's generator emits it (<c>[ParamsDto(AssetId)]</c>, fields in order).</summary>
[ParamsDto(AssetId = "0f0c3186-0000-4000-8000-000000000001")]
public struct CE3186_GeneratedDto { public float Range; public int Loops; }

/// <summary>A hand-written params DTO (no asset).</summary>
[ParamsDto]
public struct CE3186_HandWrittenDto { public float Range; }

/// <summary>
/// ⭐ <c>CE-3186</c> (P2, F4 / R-2001 — docs/DESIGN_Orders_Params_Entity_Classes.md §13.8) — after a rebuild nothing keeps the old DTO
/// type, and a quick reload refuses an asset whose generated DTO changed instead of running it stale.
/// </summary>
public sealed class ParamsDtoStalenessTests
{
    private static string WriteAsset(string json)
    {
        string root = Path.Combine(Path.GetTempPath(), "ce3186-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "CE3186_GeneratedDto.params.json"), json);
        return root;
    }

    private const string Unchanged = """
        { "AssetId": "0f0c3186-0000-4000-8000-000000000001", "Name": "CE3186_GeneratedDto", "Namespace": "Hrot.Editor.AiShared.Tests.Documents",
          "Fields": [ { "Name": "Range", "TypeId": "System.Single" }, { "Name": "Loops", "TypeId": "System.Int32" } ] }
        """;

    private const string FieldAdded = """
        { "AssetId": "0f0c3186-0000-4000-8000-000000000001", "Name": "CE3186_GeneratedDto", "Namespace": "Hrot.Editor.AiShared.Tests.Documents",
          "Fields": [ { "Name": "Range", "TypeId": "System.Single" }, { "Name": "Loops", "TypeId": "System.Int32" },
                      { "Name": "Alerted", "TypeId": "System.Boolean" } ] }
        """;

    /// <summary>⭐ The asset still matches the loaded DTO ⇒ quick reload may go ahead.</summary>
    [Fact]
    public void CE3186_AnUnchangedGeneratedDto_IsNotRefused()
    {
        string root = WriteAsset(Unchanged);
        try { Assert.Null(ParamsDtoStaleness.Check(typeof(CE3186_GeneratedDto).FullName, root)); }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>⭐⭐ The asset gained a field since the build ⇒ refused, naming the full rebuild.
    /// 🔴 Red-proof: make <see cref="ParamsDtoStaleness.Check"/> return null → this fails.</summary>
    [Fact]
    public void CE3186_AChangedGeneratedDto_IsRefused_FullRebuildNeeded()
    {
        string root = WriteAsset(FieldAdded);
        try
        {
            var refusal = ParamsDtoStaleness.Check(typeof(CE3186_GeneratedDto).FullName, root);
            Assert.NotNull(refusal);
            Assert.Contains("full rebuild", refusal);
            Assert.Contains("Alerted", refusal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>A hand-written DTO cannot change without a build (that build IS the full rebuild), and an asset that names no DTO
    /// has nothing to check — neither is refused.</summary>
    [Fact]
    public void CE3186_AHandWrittenDto_OrNoDto_IsNeverRefused()
    {
        string root = WriteAsset(FieldAdded);
        try
        {
            Assert.Null(ParamsDtoStaleness.Check(typeof(CE3186_HandWrittenDto).FullName, root));
            Assert.Null(ParamsDtoStaleness.Check(null, root));
            Assert.Null(ParamsDtoStaleness.Check("No.Such.Type", root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>
    /// ⭐ The type helper answers the LAST loaded copy of a type: a rebuilt twin of this assembly (same name, same FullName) loaded
    /// after it wins. ⚠ The twin is a minimal dynamic assembly, not a second real copy (scanners would see this assembly's fixtures twice).
    /// 🔴 Red-proof: walk the assemblies first-to-last again in <see cref="BlackboardTypeHelper.ResolveClrType"/> → the old type is returned.
    /// </summary>
    [Fact]
    public void CE3186_ResolveClrType_AnswersTheNewestCopy()
    {
        var asm = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
            new System.Reflection.AssemblyName(typeof(ParamsDtoStalenessTests).Assembly.GetName().Name!),
            System.Reflection.Emit.AssemblyBuilderAccess.Run);
        var tb = asm.DefineDynamicModule("rebuild").DefineType(typeof(CE3186_HandWrittenDto).FullName!,
            System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Sealed | System.Reflection.TypeAttributes.SequentialLayout,
            typeof(ValueType));
        tb.DefineField("Range", typeof(float), System.Reflection.FieldAttributes.Public);
        var twin = tb.CreateType();

        var resolved = BlackboardTypeHelper.ResolveClrType(typeof(CE3186_HandWrittenDto).FullName!);
        Assert.NotEqual(typeof(CE3186_HandWrittenDto), twin);
        Assert.Same(twin, resolved);
    }
}
