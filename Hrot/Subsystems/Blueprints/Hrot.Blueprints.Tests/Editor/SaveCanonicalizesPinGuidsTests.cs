using System.IO;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.Host;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// Anti-drift regression for the projection-only save (DEBT-BCP-005 deep fix).
///
/// <para>
/// Root cause: pins are reconstructed on reload and links reference pins by GUID only. Editor
/// pins are born with RANDOM GUIDs (<see cref="IdGenerator.NewPinId"/>), so on reload their links
/// hit the ORDER-FRAGILE positional binding — and a non-canonical link order silently swaps two
/// pins that share a direction bucket. The textbook case is <see cref="BranchNode"/>, whose In
/// bucket is <c>[exec "In", data "Condition"]</c>: if the "Condition" link is stored before the
/// exec "In" link, positional binding assigns the exec pin the Condition link's GUID and vice
/// versa, so the boolean producer ends up wired to exec flow and the graph corrupts.
/// </para>
/// <para>
/// The fix canonicalizes every link endpoint to its pin's deterministic
/// <c>Deterministic("pin:{node}:{name}:{dir}")</c> GUID on save, so reconstruction binds BY NAME
/// (order-independent) and the swap becomes impossible.
/// </para>
/// </summary>
public sealed class SaveCanonicalizesPinGuidsTests
{
    // Branch's canonical In-bucket pin names (BuiltInNodeRegistry.BranchPins): exec "In" + data "Condition".
    private static Guid DetPin(Guid nodeId, string name, string dir)
        => IdGenerator.Deterministic($"pin:{nodeId:N}:{name}:{dir}");

    /// <summary>
    /// Builds a Branch node with its four canonical pins PRESENT (random GUIDs, as the editor
    /// creates them), plus two incoming links stored in the DRIFT-INDUCING order: the data
    /// "Condition" link first, the exec "In" link second. Producers are referenced by id only
    /// (the projection does not need the source nodes to resolve the Branch's own In pins).
    /// </summary>
    private static (BlueprintAsset asset, Guid branchId, Guid execPinId, Guid condPinId,
                    Guid boolProducer, Guid execProducer) BuildReversedBranchAsset()
    {
        var branchId = new Guid("11111111-0000-0000-0000-000000000001");
        var execPin  = new Pin { Id = Guid.NewGuid(), Name = "In",        Direction = "In",  IsExec = true };
        var truePin  = new Pin { Id = Guid.NewGuid(), Name = "True",      Direction = "Out", IsExec = true };
        var falsePin = new Pin { Id = Guid.NewGuid(), Name = "False",     Direction = "Out", IsExec = true };
        var condPin  = new Pin { Id = Guid.NewGuid(), Name = "Condition", Direction = "In",
                                 TypeRef = new BlueprintTypeRef { TypeId = "System.Boolean" } };

        var boolProducer = new Guid("22222222-0000-0000-0000-000000000002");
        var execProducer = new Guid("33333333-0000-0000-0000-000000000003");

        var asset = new BlueprintAsset
        {
            AssetId  = new Guid("44444444-0000-0000-0000-000000000004"),
            Name     = "ReversedBranch",
            Dispatch = BlueprintDispatchKind.Instance,
            Graphs   =
            [
                new Graph
                {
                    Id   = new Guid("55555555-0000-0000-0000-000000000005"),
                    Name = "EventGraph",
                    Kind = GraphKind.Event,
                    Nodes = [ new BranchNode { Id = branchId, Pins = [ execPin, truePin, falsePin, condPin ] } ],
                    Links =
                    [
                        // Condition link FIRST (first-occurrence order drives positional binding).
                        new Link { FromNodeId = boolProducer, FromPinId = Guid.NewGuid(),
                                   ToNodeId = branchId, ToPinId = condPin.Id },
                        // exec "In" link SECOND.
                        new Link { FromNodeId = execProducer, FromPinId = Guid.NewGuid(),
                                   ToNodeId = branchId, ToPinId = execPin.Id },
                    ],
                },
            ],
        };
        return (asset, branchId, execPin.Id, condPin.Id, boolProducer, execProducer);
    }

    // ── The fix: saved links carry deterministic pin GUIDs, producer→pin mapping preserved ──

    [Fact]
    public void Save_ReversedBranchLinks_PersistsDeterministicPinGuids_NotRandom()
    {
        var (asset, branchId, execPinId, condPinId, boolProducer, execProducer) = BuildReversedBranchAsset();
        var path = Path.Combine(Path.GetTempPath(), $"canon_{Guid.NewGuid():N}.bp.json");
        try
        {
            SaveActiveBlueprintCommand.Save(asset, path);
            var reloaded = BlueprintJsonServices.Deserialize(File.ReadAllText(path));
            Assert.NotNull(reloaded);

            var links = reloaded!.Graphs[0].Links;

            var condLink = links.Single(l => l.FromNodeId == boolProducer);
            var execLink = links.Single(l => l.FromNodeId == execProducer);

            // Each link's ToPinId is now the DETERMINISTIC GUID of the pin it feeds (bound by name),
            // NOT the original random GUID — so reload can never positional-swap them.
            Assert.Equal(DetPin(branchId, "Condition", "In"), condLink.ToPinId);
            Assert.Equal(DetPin(branchId, "In",        "In"), execLink.ToPinId);

            // Sanity: canonicalization actually changed the persisted GUIDs (random → deterministic).
            Assert.NotEqual(condPinId, condLink.ToPinId);
            Assert.NotEqual(execPinId, execLink.ToPinId);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    /// <summary>
    /// End-to-end payoff: after save+reload, the editor projection binds the Branch's "Condition"
    /// pin to the boolean producer's link (not swapped to exec flow). Before the fix this reload
    /// bound "Condition" to the exec producer's link — the corruption the user kept hitting.
    /// </summary>
    [Fact]
    public void Save_ThenReloadProjection_BranchConditionBindsToBoolProducer()
    {
        var (asset, branchId, _, _, boolProducer, execProducer) = BuildReversedBranchAsset();
        var path = Path.Combine(Path.GetTempPath(), $"canon_{Guid.NewGuid():N}.bp.json");
        try
        {
            SaveActiveBlueprintCommand.Save(asset, path);
            var reloaded = BlueprintJsonServices.Deserialize(File.ReadAllText(path))!;
            var graph    = reloaded.Graphs[0];

            // Reconstruct pins on load exactly as the canvas does.
            var model = new BlueprintGraphModel(reloaded, graph);

            // The Condition pin's reconstructed GUID (deterministic, by name).
            var condGuid = DetPin(branchId, "Condition", "In");
            var execGuid = DetPin(branchId, "In",        "In");
            Assert.NotNull(model.FindPin(new PinId(condGuid)));
            Assert.NotNull(model.FindPin(new PinId(execGuid)));

            // The link that reaches the Condition pin originates from the BOOL producer, and the
            // exec pin's link from the EXEC producer — no swap.
            var condLink = graph.Links.Single(l => l.ToPinId == condGuid);
            var execLink = graph.Links.Single(l => l.ToPinId == execGuid);
            Assert.Equal(boolProducer, condLink.FromNodeId);
            Assert.Equal(execProducer, execLink.FromNodeId);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // ── Guard: loaded projection-only assets (no in-memory pins) are left byte-stable ──

    [Fact]
    public void Save_NodeWithoutInMemoryPins_LeavesLinkGuidsUntouched()
    {
        // A node whose Pins list is empty (already projection-only) must not be canonicalized:
        // its links are either already deterministic or handled by the one-time migration.
        var branchId = new Guid("66666666-0000-0000-0000-000000000006");
        var randomTo = Guid.NewGuid();
        var asset = new BlueprintAsset
        {
            AssetId = Guid.NewGuid(),
            Name    = "NoPins",
            Graphs  =
            [
                new Graph
                {
                    Id = Guid.NewGuid(), Name = "EventGraph", Kind = GraphKind.Event,
                    Nodes = [ new BranchNode { Id = branchId } ], // Pins empty
                    Links = [ new Link { FromNodeId = Guid.NewGuid(), FromPinId = Guid.NewGuid(),
                                         ToNodeId = branchId, ToPinId = randomTo } ],
                },
            ],
        };
        var path = Path.Combine(Path.GetTempPath(), $"canon_{Guid.NewGuid():N}.bp.json");
        try
        {
            SaveActiveBlueprintCommand.Save(asset, path);
            var reloaded = BlueprintJsonServices.Deserialize(File.ReadAllText(path))!;
            Assert.Equal(randomTo, reloaded.Graphs[0].Links[0].ToPinId); // unchanged
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
