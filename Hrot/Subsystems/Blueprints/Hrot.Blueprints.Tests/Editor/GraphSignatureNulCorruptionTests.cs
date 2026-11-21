using System.Text;
using Fdp.Presentation.Utils;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Variables;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// BP-86 — the designer's gesture, end to end: rename a function parameter to something
/// SHORTER than the generated default and assert the stored <see cref="ParameterDecl.Name"/>
/// carries no interior NUL.
///
/// <para>
/// This is the round-trip the handoff calls for. The pure decode behaviours live in
/// <c>Fdp.Presentation.Tests.ImGui.ImGuiBufferTextTests</c>; this one guards the seam between
/// the ImGui buffer and the persisted asset model, which is where the corruption actually
/// escaped to disk (trap #9: two halves of a contract, each tested alone, never together).
/// </para>
/// </summary>
public sealed class GraphSignatureNulCorruptionTests
{
    /// <summary>Exactly what <c>GraphSignatureWindow.DrawParameterRows</c> hands to ImGui.</summary>
    private static byte[] SeedBuffer(string current, int capacity = 256)
    {
        var buf = Encoding.UTF8.GetBytes(current + "\0");
        Array.Resize(ref buf, capacity);
        return buf;
    }

    /// <summary>Simulates ImGui writing <paramref name="typed"/> + terminator over the front.</summary>
    private static void TypeOver(byte[] buf, string typed)
    {
        var bytes = Encoding.UTF8.GetBytes(typed);
        bytes.CopyTo(buf, 0);
        buf[bytes.Length] = 0;      // remainder deliberately left stale
    }

    private static (Graph graph, GraphSignatureEditModel model) MakeInputsModel()
    {
        var graph = new Graph { Id = Guid.NewGuid(), Name = "TestFunc", Kind = GraphKind.Function };
        return (graph, new GraphSignatureEditModel(graph, isOutputs: false, () => { }));
    }

    [Fact]
    public void RenameParameter_ToShorterName_StoresNoInteriorNul()
    {
        var (graph, model) = MakeInputsModel();
        model.AddParameter("Param0", "System.Single");

        // The designer's gesture: the row's buffer holds "Param0"; they type "P1".
        var buf = SeedBuffer("Param0");
        TypeOver(buf, "P1");
        model.RenameParameter("Param0", ImGuiBufferText.Decode(buf));

        Assert.Equal("P1", graph.Inputs[0].Name);
        Assert.DoesNotContain('\0', graph.Inputs[0].Name);
    }

    [Fact]
    public void RenameParameter_ThreeParams_AllRenamedShorter_AreExact()
    {
        var (graph, model) = MakeInputsModel();
        for (int i = 0; i < 3; i++)
            model.AddParameter($"Param{i}", "System.Single");

        for (int i = 0; i < 3; i++)
        {
            var buf = SeedBuffer($"Param{i}");
            TypeOver(buf, $"P{i + 1}");
            model.RenameParameter($"Param{i}", ImGuiBufferText.Decode(buf));
        }

        Assert.Collection(graph.Inputs,
            p => Assert.Equal("P1", p.Name),
            p => Assert.Equal("P2", p.Name),
            p => Assert.Equal("P3", p.Name));
        Assert.All(graph.Inputs, p => Assert.DoesNotContain('\0', p.Name));
    }

    [Fact]
    public void RenameParameter_ThenBackToLonger_IsExact()
    {
        var (graph, model) = MakeInputsModel();
        model.AddParameter("Param0", "System.Single");

        var shorter = SeedBuffer("Param0");
        TypeOver(shorter, "P1");
        model.RenameParameter("Param0", ImGuiBufferText.Decode(shorter));

        var longer = SeedBuffer("P1");
        TypeOver(longer, "LongerName");
        model.RenameParameter("P1", ImGuiBufferText.Decode(longer));

        Assert.Equal("LongerName", graph.Inputs[0].Name);
        Assert.DoesNotContain('\0', graph.Inputs[0].Name);
    }

    [Fact]
    public void RenameOutputParameter_ToShorterName_StoresNoInteriorNul()
    {
        // Outputs share DrawParameterRows, so the same seam must hold for the Return-node side.
        var graph = new Graph { Id = Guid.NewGuid(), Name = "TestFunc", Kind = GraphKind.Function };
        var model = new GraphSignatureEditModel(graph, isOutputs: true, () => { });
        model.AddParameter("Param0", "System.Single");

        var buf = SeedBuffer("Param0");
        TypeOver(buf, "R1");
        model.RenameParameter("Param0", ImGuiBufferText.Decode(buf));

        Assert.Equal("R1", graph.Outputs[0].Name);
        Assert.DoesNotContain('\0', graph.Outputs[0].Name);
    }
}
