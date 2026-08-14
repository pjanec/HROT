using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor.Host;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// <b>U-8 (stage B′) — the type-choice union, and <c>BP-87</c>'s lock restored.</b>
///
/// <para>
/// ⛔ <b>Two disjoint answers to one question.</b> <c>BlueprintTypeSystem.SelectableTypeIds</c> was 13
/// hardcoded primitive FQNs with <b>no struct types at all</b>, while the Variables panel's own list
/// (<c>BlackboardTypeChoiceBuilder.BuildDefault</c>) offered primitives <b>and</b> every discovered
/// <c>[BlackboardDtoStruct]</c>. ⇒ whether a designer could declare a struct-typed variable depended
/// on which window they had open.
/// </para>
///
/// <para>
/// ⭐⭐ <b>Why this needs no editor-side type oracle.</b> The struct half is <b>discovered by
/// reflection over loaded assemblies</b>, so an offered entry cannot name a type that does not exist —
/// discovery IS the existence proof, which is stronger than checking a hand-written list. And
/// ⚠ <b>measured: there is no editor compile path to attach an oracle to</b> — of the three
/// <c>CompileOptions</c> construction sites, only <c>BlueprintIncrementalGenerator</c> is a live
/// production caller; <c>QuickReloadService</c> has none (<c>BP-229</c>). <c>BP1671</c> guards the
/// build, which is where a fabricated type actually bit.
/// </para>
/// </summary>
public sealed class TypeChoiceUnionTests
{
    /// <summary>
    /// ⚠ The struct half is discovered over <b>loaded</b> assemblies, and nothing in a bare test host
    /// has loaded <c>Hrot.AI.Behaviors</c> — the same preload the golden corpus needs for `BP1602`.
    /// </summary>
    public TypeChoiceUnionTests() => _ = typeof(Hrot.AI.Behaviors.BpComponentDemo).Assembly;

    private static CompileOptions Options() => new(
        Mode:              CompilerMode.Release,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static BlueprintAsset AssetWithVariableTyped(string typeId)
    {
        var asset = BlueprintAssetBuilder.Instance("TypeChoiceHost").Build();
        asset.Variables.Add(new VariableDecl
        {
            Id = Guid.NewGuid(), Name = "V",
            Type = new BlueprintTypeRef { TypeId = typeId }, DefaultValueJson = "",
        });

        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true };
        entry.Pins.Add(entryOut);
        var ret = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = new Pin { Id = Guid.NewGuid(), Name = "In", Direction = "In", IsExec = true };
        ret.Pins.Add(retIn);

        asset.Graphs.Add(new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = { entry, ret },
            Links = { new Link { FromNodeId = entry.Id, FromPinId = entryOut.Id, ToNodeId = ret.Id, ToPinId = retIn.Id } },
        });
        return asset;
    }

    /// <summary>
    /// ⭐⭐ <b>Pass 1 — every offered type COMPILES.</b> ⛔ This is <c>BP-87</c>'s *"every offered type
    /// is guaranteed resolvable"* lock, which the Batch 38 review found had nothing to check against.
    /// ⚠ Driven through the real compiler, one asset per offered id — a list-shape assertion would
    /// prove nothing about whether the compiler accepts them.
    /// </summary>
    [Fact]
    public void EveryOfferedTypeCompiles()
    {
        var failures = new List<string>();
        foreach (var typeId in BlueprintTypeSystem.SelectableTypeIds)
        {
            var result = new BlueprintCompiler().Compile(AssetWithVariableTyped(typeId), Options());
            if (!result.Succeeded)
                failures.Add($"{typeId}: {string.Join(",", result.Diagnostics.Where(d => d.IsError).Select(d => d.Code))}");
        }

        Assert.True(failures.Count == 0,
            "offered types that do not compile:\n  " + string.Join("\n  ", failures));
    }

    /// <summary>
    /// ⭐ <b>Pass 2 — the union really is a union.</b> Every discovered <c>[BlackboardDtoStruct]</c>
    /// FQN is offered, and the primitives survived.
    /// </summary>
    [Fact]
    public void TheListContainsEveryDiscoveredStructAndEveryPrimitive()
    {
        var offered = BlueprintTypeSystem.SelectableTypeIds.ToHashSet(StringComparer.Ordinal);

        var structs = BlackboardTypeChoiceBuilder.DiscoverBlackboardDtoStructTypes();
        Assert.NotEmpty(structs);   // ⛔ an empty discovery would make this test vacuous
        foreach (var t in structs)
            Assert.Contains(t.FullName!, offered);

        foreach (var primitive in new[]
                 {
                     BlueprintTypeSystem.Bool, BlueprintTypeSystem.Int32, BlueprintTypeSystem.Single,
                     BlueprintTypeSystem.Entity, BlueprintTypeSystem.FixedString32,
                 })
            Assert.Contains(primitive, offered);
    }

    /// <summary>
    /// ⭐ <b>Pass 3 — no short names.</b> ⛔ A short name is <c>BP1500</c> at compile time and a grey
    /// unnamed pin in the palette (<c>BP-203</c>). Every entry must be a dotted FQN.
    /// </summary>
    [Fact]
    public void NoShortNamesAreOffered()
    {
        foreach (var id in BlueprintTypeSystem.SelectableTypeIds)
        {
            Assert.False(string.IsNullOrWhiteSpace(id));
            Assert.Contains(".", id);
            Assert.DoesNotContain("global::", id);
        }
    }

    /// <summary>The list is stable and duplicate-free — the picker must not reshuffle between runs.</summary>
    [Fact]
    public void TheListIsStableAndDuplicateFree()
    {
        var ids = BlueprintTypeSystem.SelectableTypeIds;
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(ids, BlueprintTypeSystem.SelectableTypeIds);
    }
}
