using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Common.Infrastructure;
using Xunit;

namespace Hrot.NodeComposition.Tests;

/// <summary>
/// Rails for <see cref="NodeBootPlan"/> — the declared-and-verified boot plan
/// (docs/DESIGN_Subsystem_Composition_Unification.md §4.1P).
///
/// <para>
/// These are written to be NON-VACUOUS. §4.1N measured three real dependencies in
/// <c>SharedApplicationBootstrapper</c> that travel through channels no signature shows and that all
/// fail SILENTLY when the order breaks. The whole value of the plan is that those become loud, so a
/// rail that only asserts "the happy path still runs" would prove nothing. Each rail below therefore
/// pins the FAILURE, and the last one pins that the plan does NOT reorder.
/// </para>
/// </summary>
public sealed class NodeBootPlanRails
{
    /// <summary>
    /// The core claim: an unmet dependency THROWS, and the exception names both the offending step
    /// and the key it was missing — the two things a person needs to fix it.
    ///
    /// <para>
    /// ⛔ INVERSE-EDIT RED-PROOF: delete the <c>provided.Contains(need)</c> check in
    /// <c>NodeBootPlan.Run</c> and this test fails — no exception is thrown at all. It is the check
    /// itself that is under test, not the plumbing around it.
    /// </para>
    /// </summary>
    [Fact]
    public void AnUnmetDependencyThrows_AndNamesTheStepAndTheMissingKey()
    {
        var ran = new List<string>();
        var plan = new NodeBootPlan()
            .Step("context",    provides: new[] { "context" }, run: () => ran.Add("context"))
            .Step("serializer", requires: new[] { "domain-components" }, run: () => ran.Add("serializer"));

        var ex = Assert.Throws<BootDependencyException>(() => plan.Run("TestHost"));

        Assert.Equal("serializer",        ex.StepKey);
        Assert.Equal("domain-components", ex.MissingKey);
        Assert.Contains("TestHost",       ex.Message);
        Assert.Contains("context",        ex.Message);   // reports what WAS provided

        // and it threw BEFORE running the offending step
        Assert.Equal(new[] { "context" }, ran);
    }

    /// <summary>
    /// The dependency must be satisfied by an EARLIER step, not merely present somewhere in the list.
    /// A plan that provided the key later would still be broken at run time, so declaring it late
    /// must not satisfy the requirement.
    /// </summary>
    [Fact]
    public void AKeyProvidedLaterDoesNotSatisfyAnEarlierRequirement()
    {
        var plan = new NodeBootPlan()
            .Step("serializer",        requires: new[] { "domain-components" }, run: () => { })
            .Step("domain-components", provides: new[] { "domain-components" }, run: () => { });

        var ex = Assert.Throws<BootDependencyException>(() => plan.Run("TestHost"));
        Assert.Equal("serializer", ex.StepKey);
    }

    /// <summary>
    /// A satisfied plan runs every step, in declaration order.
    /// </summary>
    [Fact]
    public void ASatisfiedPlanRunsEveryStepInDeclarationOrder()
    {
        var ran = new List<string>();
        new NodeBootPlan()
            .Step("context",           provides: new[] { "context" },           run: () => ran.Add("context"))
            .Step("domain-components", requires: new[] { "context" },
                                       provides: new[] { "domain-components" }, run: () => ran.Add("domain-components"))
            .Step("serializer",        requires: new[] { "domain-components" }, run: () => ran.Add("serializer"))
            .Run("TestHost");

        Assert.Equal(new[] { "context", "domain-components", "serializer" }, ran);
    }

    /// <summary>
    /// ⭐⭐⭐ The design decision of §4.1P ①, pinned: the plan VERIFIES, it does not SORT.
    ///
    /// <para>
    /// If a future edit "helpfully" made the runner topologically sort, this plan would be
    /// reordered into a working one and the test would go green while the boot order silently
    /// changed. §4.1N proved three of the base's real dependencies are invisible, so sorting on an
    /// incomplete declaration reorders into a silent break. The plan must refuse instead.
    /// </para>
    /// </summary>
    [Fact]
    public void ThePlanVerifiesRatherThanSorts_AnOutOfOrderPlanIsRejectedNotRepaired()
    {
        var ran = new List<string>();
        var plan = new NodeBootPlan()
            .Step("needs-it", requires: new[] { "thing" }, run: () => ran.Add("needs-it"))
            .Step("has-it",   provides: new[] { "thing" }, run: () => ran.Add("has-it"));

        Assert.Throws<BootDependencyException>(() => plan.Run("TestHost"));

        // ⛔ Nothing ran. A sorting runner would have run both, in the repaired order.
        Assert.Empty(ran);
    }

    /// <summary>
    /// The declared keys are readable without running, so a host's plan can be inspected.
    /// </summary>
    [Fact]
    public void StepKeysReportsTheDeclaredOrder()
    {
        var plan = new NodeBootPlan()
            .Step("a", run: () => { })
            .Step("b", run: () => { });

        Assert.Equal(new[] { "a", "b" }, plan.StepKeys.ToArray());
    }
}
