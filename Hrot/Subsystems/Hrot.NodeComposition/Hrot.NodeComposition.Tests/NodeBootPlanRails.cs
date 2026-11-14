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

    // ── The value bag (§4.1R) ────────────────────────────────────────────────────────────────

    /// <summary>
    /// A value published by one step is readable by a later one that declares it.
    /// </summary>
    [Fact]
    public void AValuePublishedByOneStepIsReadableByALaterStepThatRequiresIt()
    {
        string? seen = null;
        new NodeBootPlan()
            .Step("producer", provides: new[] { "bus" }, run: v => v.Set("bus", "THE-BUS"))
            .Step("consumer", requires: new[] { "bus" }, run: v => seen = v.Get<string>("bus"))
            .Run("TestHost");

        Assert.Equal("THE-BUS", seen);
    }

    /// <summary>
    /// ⭐⭐⭐ The property that makes the keys honest: a step may NOT publish a key it did not declare.
    /// Without this the declared graph and the real data channel drift apart silently, which is the
    /// whole disease §4.1N measured.
    ///
    /// <para>⛔ INVERSE-EDIT RED-PROOF: delete the <c>Array.IndexOf(_provides, key) &lt; 0</c> guard in
    /// <c>NodeBootValues.Set</c> and this test fails — the undeclared write succeeds.</para>
    /// </summary>
    [Fact]
    public void AStepCannotSetAKeyItDoesNotDeclareItProvides()
    {
        var plan = new NodeBootPlan()
            .Step("sneaky", provides: new[] { "declared" }, run: v => v.Set("undeclared", 1));

        var ex = Assert.Throws<InvalidOperationException>(() => plan.Run("TestHost"));
        Assert.Contains("sneaky",     ex.Message);
        Assert.Contains("undeclared", ex.Message);
    }

    /// <summary>
    /// The mirror property: a step may not READ a key it did not declare it requires — so a hidden
    /// read cannot creep in the way §4.1N's three silent channels did.
    ///
    /// <para>⛔ INVERSE-EDIT RED-PROOF: delete the <c>Array.IndexOf(_requires, key) &lt; 0</c> guard in
    /// <c>NodeBootValues.Get</c> and this test fails.</para>
    /// </summary>
    [Fact]
    public void AStepCannotGetAKeyItDoesNotDeclareItRequires()
    {
        var plan = new NodeBootPlan()
            .Step("producer", provides: new[] { "bus" }, run: v => v.Set("bus", "THE-BUS"))
            .Step("peeker",   run: v => v.Get<string>("bus"));

        var ex = Assert.Throws<InvalidOperationException>(() => plan.Run("TestHost"));
        Assert.Contains("peeker", ex.Message);
        Assert.Contains("bus",    ex.Message);
    }

    /// <summary>
    /// Declaring a key in <c>provides</c> and never publishing it is caught at the consumer, with a
    /// message that says which step failed to publish rather than a bare null.
    /// </summary>
    [Fact]
    public void AProvidedKeyThatWasNeverSetFailsLoudlyAtTheConsumer()
    {
        var plan = new NodeBootPlan()
            .Step("forgetful", provides: new[] { "bus" }, run: _ => { /* declared, never Set */ })
            .Step("consumer",  requires: new[] { "bus" }, run: v => v.Get<string>("bus"));

        var ex = Assert.Throws<InvalidOperationException>(() => plan.Run("TestHost"));
        Assert.Contains("never Set", ex.Message);
    }

    /// <summary>
    /// Reading a value at the wrong type is a named error, not an <c>InvalidCastException</c>.
    /// </summary>
    [Fact]
    public void ReadingAValueAtTheWrongTypeSaysBothTypes()
    {
        var plan = new NodeBootPlan()
            .Step("producer", provides: new[] { "n" }, run: v => v.Set("n", 42))
            .Step("consumer", requires: new[] { "n" }, run: v => v.Get<string>("n"));

        var ex = Assert.Throws<InvalidOperationException>(() => plan.Run("TestHost"));
        Assert.Contains("String", ex.Message);
        Assert.Contains("Int32",  ex.Message);
    }

    /// <summary>
    /// The closure overload still works unchanged — step 1's and ExCon's existing steps are not
    /// required to adopt the bag.
    /// </summary>
    [Fact]
    public void TheClosureOverloadStillWorks()
    {
        var ran = new List<string>();
        new NodeBootPlan()
            .Step("a", provides: new[] { "x" }, run: () => ran.Add("a"))
            .Step("b", requires: new[] { "x" }, run: () => ran.Add("b"))
            .Run("TestHost");

        Assert.Equal(new[] { "a", "b" }, ran);
    }

    // ── The migration boundary: NodeBootPlan.Value<T> (§4.1T) ────────────────────────────────

    /// <summary>
    /// ⭐⭐ The accessor a partially-migrated host depends on: after <c>Run</c>, the plan's OWNER can
    /// read what the steps published, so the code below the migrated region keeps working.
    ///
    /// <para>
    /// 📌 This is not hypothetical plumbing — <c>CgfSubsystem.Initialize</c> declares its head as six
    /// steps and then reads four values back this way (<c>node-config</c>, <c>behavior-registry</c>,
    /// <c>node-factory</c>, <c>replication-module</c>) because the ~570 lines after it are not migrated
    /// yet. If this accessor were wrong, that host boots wrong.
    /// </para>
    /// </summary>
    [Fact]
    public void ValueReadsWhatAStepPublished_AfterThePlanHasRun()
    {
        var plan = new NodeBootPlan()
            .Step("producer", provides: new[] { "bus", "n" }, run: v =>
            {
                v.Set("bus", "THE-BUS");
                v.Set<int>("n", 7);
            });

        plan.Run("TestHost");

        Assert.Equal("THE-BUS", plan.Value<string>("bus"));
        Assert.Equal(7,         plan.Value<int>("n"));
    }

    /// <summary>
    /// A null published under a declared key comes back as null rather than throwing — the hosts
    /// genuinely publish optional values (<c>CgfSubsystem</c>'s <c>node-factory</c> is
    /// <c>_networkFactory?.ConfigureForNode(...)</c>, null when the node runs offline), and a plan that
    /// refused them would force those steps back out into ambient locals.
    /// </summary>
    [Fact]
    public void ValueReturnsNullForAnOptionalValueThatWasPublishedAsNull()
    {
        var plan = new NodeBootPlan()
            .Step("producer", provides: new[] { "factory" }, run: v => v.Set<string?>("factory", null));

        plan.Run("TestHost");

        Assert.Null(plan.Value<string?>("factory"));
    }

    /// <summary>
    /// ⭐ Reading a key nothing published names the keys that DO exist, so a typo at the migration
    /// boundary is a one-line fix rather than a null that surfaces a hundred lines later.
    ///
    /// <para>⛔ INVERSE-EDIT RED-PROOF: make <c>NodeBootValues.Published</c> return <c>default!</c> on a
    /// missing key instead of throwing and this test fails — which is exactly the silent-null failure
    /// mode §4.1N measured.</para>
    /// </summary>
    [Fact]
    public void ValueOnAKeyNothingPublishedFailsLoudlyAndListsTheKnownKeys()
    {
        var plan = new NodeBootPlan()
            .Step("producer", provides: new[] { "bus" }, run: v => v.Set("bus", "THE-BUS"));

        plan.Run("TestHost");

        var ex = Assert.Throws<InvalidOperationException>(() => plan.Value<string>("buss"));
        Assert.Contains("buss", ex.Message);
        Assert.Contains("bus",  ex.Message);   // reports what IS published
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
