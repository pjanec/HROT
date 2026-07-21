using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.NodeDrawers;
using Hrot.Blueprints.Tests.Runtime;   // MultiPinShared
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// Q#14 Option B editor: the Make/Break palette bakes the struct FQN + reflected fields at create, and
/// NodePinSchema projects per-field pins + the struct "Value" pin (parity with the compiler's Stage0).
/// </summary>
public sealed class MakeBreakStructPaletteTests
{
    private sealed class StubProvider : ISharedStructTypeProvider
    {
        private readonly IReadOnlyList<string> _fqns;
        public StubProvider(params string[] fqns) => _fqns = fqns;
        public IReadOnlyList<string> GetSharedStructTypeFqns() => _fqns;
    }

    [Fact]
    public void Entries_BakeStructFqnAndFields_ForDiscoveredStruct()
    {
        var fqn = typeof(MultiPinShared).FullName!;
        var entries = MakeBreakStructPaletteEntries.Entries(new StubProvider(fqn)).ToList();

        var make = (MakeStructNode)entries.Single(e => e.DisplayName == "Make MultiPinShared").CreateInstance();
        Assert.Equal(fqn, make.StructTypeId);
        Assert.Equal(new[] { "A", "B", "C" }, make.Fields.Select(f => f.Name).ToArray());

        var brk = (BreakStructNode)entries.Single(e => e.DisplayName == "Break MultiPinShared").CreateInstance();
        Assert.Equal(fqn, brk.StructTypeId);
        Assert.Equal(new[] { "A", "B", "C" }, brk.Fields.Select(f => f.Name).ToArray());
    }

    [Fact]
    public void NodePinSchema_ProjectsMakeAndBreakPins()
    {
        var fqn = typeof(MultiPinShared).FullName!;
        var fields = new List<StructFieldDecl>
        {
            new StructFieldDecl { Name = "A", TypeId = "System.Int32" },
            new StructFieldDecl { Name = "B", TypeId = "System.Int32" },
        };

        var make = new MakeStructNode { Id = Guid.NewGuid(), StructTypeId = fqn, Fields = fields };
        var makePins = NodePinSchema.GetCanonicalPins(make);
        Assert.Equal(new[] { "A", "B", "Value" }, makePins.Select(p => p.Name).ToArray());
        Assert.Equal("Out", makePins.Single(p => p.Name == "Value").Direction);
        Assert.All(makePins.Where(p => p.Name is "A" or "B"), p => Assert.Equal("In", p.Direction));

        var brk = new BreakStructNode { Id = Guid.NewGuid(), StructTypeId = fqn, Fields = fields };
        var brkPins = NodePinSchema.GetCanonicalPins(brk);
        Assert.Equal(new[] { "Value", "A", "B" }, brkPins.Select(p => p.Name).ToArray());
        Assert.Equal("In", brkPins.Single(p => p.Name == "Value").Direction);
        Assert.All(brkPins.Where(p => p.Name is "A" or "B"), p => Assert.Equal("Out", p.Direction));
    }
}
