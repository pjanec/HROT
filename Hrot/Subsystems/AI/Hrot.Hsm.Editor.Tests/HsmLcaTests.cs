using System;
using System.Linq;
using FluentAssertions;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using Hrot.Hsm.Editor.Inspector;
using Hrot.Hsm.Editor.Model;
using Xunit;

namespace Hrot.Hsm.Editor.Tests;

public sealed class HsmLcaTests
{
    // ---- helpers ----

    private static (HsmDefinitionBlob blob, MachineMetadata metadata) Compile(HsmBuilder builder)
    {
        var graph = builder.Build();
        HsmNormalizer.Normalize(graph);
        var flatData = HsmFlattener.Flatten(graph);
        var blob = HsmEmitter.Emit(flatData);
        var metadata = HsmEmitter.BuildMachineMetadata(graph);
        return (blob, metadata);
    }

    private static HsmAsset Project(HsmDefinitionBlob blob, MachineMetadata metadata,
        string name = "TestMachine")
    {
        return HsmAssetProjector.Project(
            blob, metadata, null,
            Guid.NewGuid(), name, "", false, "");
    }

    private static HsmAsset BuildAsset(string name, Action<HsmBuilder> configure)
    {
        var builder = new HsmBuilder(name);
        configure(builder);
        var (blob, metadata) = Compile(builder);
        return Project(blob, metadata, name);
    }

    // ---- tests ----

    [Fact]
    public void FindLca_two_siblings_returns_parent()
    {
        // Tree: Root -> A -> [B, C]
        var asset = BuildAsset("T1", b =>
        {
            b.State("A").Initial()
                .Child("B", bb => bb.Initial())
                .Child("C", cc => { });
        });
        var mapper = new HsmFacetMapper(asset);
        var b = asset.AllStates.First(s => s.Name == "B");
        var c = asset.AllStates.First(s => s.Name == "C");

        mapper.FindLca(b, c).Name.Should().Be("A");
    }

    [Fact]
    public void FindLca_state_with_itself_returns_same()
    {
        // Tree: Root -> A
        var asset = BuildAsset("T2", b => b.State("A").Initial());
        var mapper = new HsmFacetMapper(asset);
        var a = asset.AllStates.First(s => s.Name == "A");

        mapper.FindLca(a, a).Should().Be(a);
    }

    [Fact]
    public void FindLca_ancestor_and_descendant_returns_ancestor()
    {
        // Tree: Root -> A -> B -> C
        var asset = BuildAsset("T3", b =>
        {
            b.State("A").Initial()
                .Child("B", bb =>
                {
                    bb.Initial();
                    bb.Child("C", cc => { });
                });
        });
        var mapper = new HsmFacetMapper(asset);
        var a = asset.AllStates.First(s => s.Name == "A");
        var c = asset.AllStates.First(s => s.Name == "C");

        mapper.FindLca(a, c).Name.Should().Be("A");
    }

    [Fact]
    public void FindLca_states_in_different_subtrees()
    {
        // Tree: Root -> [A, X -> [Y, Z]]
        var asset = BuildAsset("T4", b =>
        {
            b.State("A").Initial();
            b.State("X")
                .Child("Y", y => y.Initial())
                .Child("Z", z => { });
        });
        var mapper = new HsmFacetMapper(asset);
        var a = asset.AllStates.First(s => s.Name == "A");
        var y = asset.AllStates.First(s => s.Name == "Y");

        // A and Y are in different subtrees; their LCA is the compiler root (a.Parent)
        mapper.FindLca(a, y).Should().BeSameAs(a.Parent);
    }

    [Fact]
    public void FindLca_deep_tree()
    {
        // Tree: Root -> A -> B -> [C -> D, E]
        var asset = BuildAsset("T5", b =>
        {
            b.State("A").Initial()
                .Child("B", bb =>
                {
                    bb.Initial();
                    bb.Child("C", cc =>
                    {
                        cc.Initial();
                        cc.Child("D", dd => { });
                    });
                    bb.Child("E", ee => { });
                });
        });
        var mapper = new HsmFacetMapper(asset);
        var d = asset.AllStates.First(s => s.Name == "D");
        var e = asset.AllStates.First(s => s.Name == "E");

        mapper.FindLca(d, e).Name.Should().Be("B");
    }

    [Fact]
    public void FindLca_direct_parent_child()
    {
        // Tree: Root -> A -> B
        var asset = BuildAsset("T6", b =>
        {
            b.State("A").Initial()
                .Child("B", bb => bb.Initial());
        });
        var mapper = new HsmFacetMapper(asset);
        var a = asset.AllStates.First(s => s.Name == "A");
        var bState = asset.AllStates.First(s => s.Name == "B");

        mapper.FindLca(a, bState).Name.Should().Be("A");
    }

    [Fact]
    public void FindLca_top_level_siblings()
    {
        // Tree: Root -> [X, Y]
        var asset = BuildAsset("T7", b =>
        {
            b.State("X").Initial();
            b.State("Y");
        });
        var mapper = new HsmFacetMapper(asset);
        var x = asset.AllStates.First(s => s.Name == "X");
        var y = asset.AllStates.First(s => s.Name == "Y");

        // X and Y are top-level siblings; their LCA is their shared parent (compiler root)
        mapper.FindLca(x, y).Should().BeSameAs(x.Parent);
    }

    [Fact]
    public void DepthOf_computed_correctly_via_lca_cost()
    {
        // Tree: Root -> A -> B -> C, transition B -[E]-> C
        // LCA(B, C) = B, LcaCost = depth(B) + depth(C) - 2*depth(B) = 2 + 3 - 4 = 1
        var asset = BuildAsset("T8", b =>
        {
            b.Event("E", 1);
            b.State("A").Initial()
                .Child("B", bb =>
                {
                    bb.Initial();
                    bb.Child("C", cc => { });
                    bb.On(1).GoTo("C");
                });
        });
        var mapper = new HsmFacetMapper(asset);
        var t = asset.AllTransitions.First(tr => tr.Source.Name == "B" && tr.Target.Name == "C");
        var facet = mapper.GetTransitionFacet(t.VisualId);

        facet.LcaStateName.Should().Be("B");
        facet.LcaCost.Should().Be(1);
    }
}
