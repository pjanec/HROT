using System;
using System.Collections.Generic;
using FluentAssertions;
using Hrot.BTree.Editor.Inspector;
using Hrot.Editor.AiShared.Blackboard;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Inspector;

// ---- Stubs ------------------------------------------------------------------

file sealed class StubExporter : IActionSchemaExporter
{
    private readonly Dictionary<string, ActionSchemaEntry> _map;

    public IReadOnlyDictionary<string, ActionSchemaEntry> All => _map;
    public event Action? Changed { add { } remove { } }

    public StubExporter(params ActionSchemaEntry[] entries)
    {
        _map = new Dictionary<string, ActionSchemaEntry>(StringComparer.Ordinal);
        foreach (var e in entries)
            _map[e.Fqn] = e;
    }

    public ActionSchemaEntry? Lookup(string fqn) => _map.GetValueOrDefault(fqn);
    public void Rebuild() { }
}

// ---- Tests ------------------------------------------------------------------

public sealed class BlackboardFieldPickerAttributeTests
{
    private static readonly IReadOnlyList<BlackboardVariableEntry> NoVars =
        Array.Empty<BlackboardVariableEntry>();

    private static readonly IReadOnlyList<BlackboardVariableEntry> MixedVars = new[]
    {
        new BlackboardVariableEntry("intVar",   typeof(int),   null),
        new BlackboardVariableEntry("floatVar", typeof(float), null),
        new BlackboardVariableEntry("boolVar",  typeof(bool),  null),
    };

    // ---- Constant -----------------------------------------------------------

    [Fact]
    public void NoCompatibleVariablesDisplay_constant_is_correct()
    {
        BlackboardFieldPickerAttribute.NoCompatibleVariablesDisplay
            .Should().Be("(no compatible variables)");
    }

    // ---- Null actionFqn returns all variable names --------------------------

    [Fact]
    public void GetCompatibleVariables_null_fqn_returns_all_names()
    {
        var exporter = new StubExporter();
        var result   = BlackboardFieldPickerAttribute.GetCompatibleVariables(null, MixedVars, exporter);

        result.Should().BeEquivalentTo(new[] { "intVar", "floatVar", "boolVar" });
    }

    [Fact]
    public void GetCompatibleVariables_null_fqn_returns_empty_for_no_vars()
    {
        var exporter = new StubExporter();
        var result   = BlackboardFieldPickerAttribute.GetCompatibleVariables(null, NoVars, exporter);

        result.Should().BeEmpty();
    }

    // ---- Unknown fqn (not in exporter) returns all names -------------------

    [Fact]
    public void GetCompatibleVariables_unknown_fqn_returns_all_names()
    {
        var exporter = new StubExporter();   // empty
        var result   = BlackboardFieldPickerAttribute.GetCompatibleVariables(
            "My.Unknown.Action", MixedVars, exporter);

        result.Should().BeEquivalentTo(new[] { "intVar", "floatVar", "boolVar" });
    }

    // ---- Known fqn filters by DtoType --------------------------------------

    [Fact]
    public void GetCompatibleVariables_known_fqn_filters_to_matching_dto_type()
    {
        var entry    = new ActionSchemaEntry("My.Action", typeof(int), ActionHosting.BTree, BlackboardAccess.ReadWrite, null);
        var exporter = new StubExporter(entry);

        var result = BlackboardFieldPickerAttribute.GetCompatibleVariables(
            "My.Action", MixedVars, exporter);

        result.Should().ContainSingle().Which.Should().Be("intVar");
    }

    [Fact]
    public void GetCompatibleVariables_known_fqn_no_matching_type_returns_empty()
    {
        var entry    = new ActionSchemaEntry("My.Action", typeof(double), ActionHosting.BTree, BlackboardAccess.ReadWrite, null);
        var exporter = new StubExporter(entry);

        var result = BlackboardFieldPickerAttribute.GetCompatibleVariables(
            "My.Action", MixedVars, exporter);

        result.Should().BeEmpty();
    }

    [Fact]
    public void GetCompatibleVariables_known_fqn_multiple_matching_returns_all_matches()
    {
        var vars = new[]
        {
            new BlackboardVariableEntry("a", typeof(float), null),
            new BlackboardVariableEntry("b", typeof(float), null),
            new BlackboardVariableEntry("c", typeof(int),   null),
        };
        var entry    = new ActionSchemaEntry("My.FloatAction", typeof(float), ActionHosting.BTree, BlackboardAccess.ReadWrite, null);
        var exporter = new StubExporter(entry);

        var result = BlackboardFieldPickerAttribute.GetCompatibleVariables(
            "My.FloatAction", vars, exporter);

        result.Should().BeEquivalentTo(new[] { "a", "b" });
    }
}
