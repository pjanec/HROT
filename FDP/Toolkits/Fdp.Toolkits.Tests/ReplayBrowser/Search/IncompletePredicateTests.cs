using System;
using System.Collections.Generic;
using System.Text.Json;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Search;
using StructEdit.Reflection;
using Xunit;

namespace Fdp.Toolkit.ReplayBrowser.Search;

/// <summary>
/// An INCOMPLETE predicate — one whose <c>ComponentType</c> is null — must never reach the
/// component registry.
///
/// <para>
/// This is not a hypothetical. The Breakpoints panel's "Add" builds a bare
/// <c>new PropertyMatchDto()</c>, whose <c>ComponentType</c> is declared <c>null!</c>. That empty
/// "New Breakpoint" was saved into <c>.debug/bpsession.json</c> on exit and reloaded on the next
/// launch, where <c>CollectMandatoryComponents</c> put the null into
/// <c>MandatoryComponents</c> and <c>DataBreakpointSystem</c> handed it to
/// <c>ComponentTypeRegistry.GetId</c> — which threw <see cref="ArgumentNullException"/> from inside
/// a dictionary lookup and killed the editor. Every launch thereafter, with no route back through
/// the UI.
/// </para>
/// </summary>
public sealed class IncompletePredicateTests
{
    private readonly IPredicateCompiler _compiler =
        new PredicateCompiler(new ComponentEditServiceBuilder().Build());

    // ── the registry lookup ──────────────────────────────────────────────────

    [Fact]
    public void GetId_OfNull_ReturnsMinusOne_RatherThanThrowing()
    {
        // The XML doc on GetId has always promised "-1 if not registered". Null is not registered.
        Assert.Equal(-1, ComponentTypeRegistry.GetId(null));
    }

    [Fact]
    public void GetId_OfAnUnregisteredType_StillReturnsMinusOne()
    {
        // Guards against "fixing" the null case by throwing somewhere else instead.
        Assert.Equal(-1, ComponentTypeRegistry.GetId(typeof(IncompletePredicateTests)));
    }

    // ── the collector ────────────────────────────────────────────────────────

    [Fact]
    public void ExtractMandatoryComponents_SkipsAPredicateWithNoComponentType()
    {
        var incomplete = new PropertyMatchDto();      // exactly what the panel's "Add" creates

        var mandatory = _compiler.ExtractMandatoryComponents(incomplete);

        Assert.Empty(mandatory);
        Assert.DoesNotContain(mandatory, t => t is null);
    }

    [Fact]
    public void ExtractMandatoryComponents_SkipsTheNullInsideAnAndCompound()
    {
        // The resolvable sibling must survive — skipping the null must not discard the whole AND.
        var compound = new CompoundPredicateDto
        {
            Operator   = LogicalOperator.And,
            Conditions = new List<SearchPredicateDto>
            {
                new PropertyMatchDto { ComponentType = typeof(SimTransform), PropertyPath = "X" },
                new PropertyMatchDto(),               // incomplete
            },
        };

        var mandatory = _compiler.ExtractMandatoryComponents(compound);

        Assert.DoesNotContain(mandatory, t => t is null);
        Assert.Contains(typeof(SimTransform), mandatory);
        Assert.Single(mandatory);
    }

    [Fact]
    public void ExtractMandatoryComponents_SkipsNullsNestedTwoDeep()
    {
        var nested = new CompoundPredicateDto
        {
            Operator   = LogicalOperator.And,
            Conditions = new List<SearchPredicateDto>
            {
                new CompoundPredicateDto
                {
                    Operator   = LogicalOperator.And,
                    Conditions = new List<SearchPredicateDto> { new PropertyMatchDto() },
                },
            },
        };

        Assert.Empty(_compiler.ExtractMandatoryComponents(nested));
    }

    // ── the seam that actually broke: save -> reload -> use ──────────────────

    [Fact]
    public void ATypeNameThatNoLongerResolves_DeserializesToNull_AndIsStillSkipped()
    {
        // The other way ComponentType goes null: TypeNameJsonConverter returns null by design when
        // the persisted type name cannot be resolved (a renamed or removed component).
        const string json = """
            {
              "$type": "PropertyMatch",
              "ComponentType": "Some.Component.That.Was.Deleted, GoneAssembly",
              "PropertyPath": "X",
              "Operator": 0,
              "Predicate": null
            }
            """;

        var dto = JsonSerializer.Deserialize<SearchPredicateDto>(json);
        var propMatch = Assert.IsType<PropertyMatchDto>(dto);
        Assert.Null(propMatch.ComponentType);          // the precondition this test exists for

        var mandatory = _compiler.ExtractMandatoryComponents(propMatch);

        Assert.Empty(mandatory);
    }

    [Fact]
    public void TheWholeChain_IncompletePredicateToRegistryLookup_DoesNotThrow()
    {
        // End to end: whatever the collector yields must be safe to feed to GetId, which is exactly
        // what DataBreakpointSystem does every frame for every mounted predicate.
        var incomplete = new PropertyMatchDto();

        var mandatory = _compiler.ExtractMandatoryComponents(incomplete);

        var ex = Record.Exception(() =>
        {
            foreach (var t in mandatory) ComponentTypeRegistry.GetId(t);
        });

        Assert.Null(ex);
    }
}
