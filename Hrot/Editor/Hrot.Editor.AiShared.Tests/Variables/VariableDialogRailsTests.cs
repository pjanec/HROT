using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Editor.AiShared.Variables;
using StructEdit.Core;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐⭐ <b><c>C-dialog</c> — <c>DESIGN_Variable_Details_And_Editing.md</c> §2/§3/§5, rails from §9.</b>
///
/// <para>
/// ⭐⭐ <b>One dialog, two scopes.</b> <i>"Edit value…"</i> and <i>"Properties…"</i> differ <b>only</b>
/// by the <see cref="EditScope"/> argument — same <c>IEditSession</c> lifecycle, same OK/Cancel, same
/// validation. ⭐ The USER picks the act; run state only decides availability.
/// </para>
/// </summary>
public sealed class VariableDialogRailsTests
{
    private static Entity Ent(int i) => new Entity(i, 1);

    private static VariableRow Row(
        VariableRowKind kind = VariableRowKind.Normal, bool stale = false)
        => new(
            Origin:    new VariableRowOrigin(Guid.NewGuid(), Ent(1), "Variables", "Health", "Alpha"),
            ShortName: "Health", TypeText: "Int32", ClrType: typeof(int),
            ReadValue: () => Array.Empty<byte>(),
            RowKind:   kind, IsStale: stale);

    // ── §9 · two dialogs, ONE implementation ────────────────────────────────────

    /// <summary>
    /// ⭐ <b>The scope is the ONLY difference between the two menu items.</b>
    ///
    /// <para>
    /// ⛔⛔ <b>UPDATED Batch 75 — this rail pinned the WRONG SPACE.</b> It asserted the included path
    /// was <c>"Health"</c>, i.e. it read the expectation straight out of the argument it passed in.
    /// 📐 <c>ReflectionEditDocumentBuilder.FilterNode</c> matches against <c>node.JsonPath</c>, which
    /// for a top-level field is <c>"$.Health"</c> ⇒ the bare name matched nothing and the value
    /// dialog opened <b>empty</b>. ⭐ The rail was green throughout, because a scope that selects
    /// nothing still has exactly one included path.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>It could only surface once something CONSTRUCTED the launcher</b> —
    /// <c>VariableEditGestureBinderTests</c> asserts the resulting document's root, which is the
    /// observable that tells "the field" apart from "nothing".
    /// </para>
    /// </summary>
    /// <summary>
    /// ⭐⭐⭐ <b>INVERTED, Batch 96 (<c>96b</c>) — a whole-variable edit is the WHOLE DOCUMENT.</b>
    ///
    /// <para>⛔⛔ <b>This rail used to assert the defect.</b> It pinned
    /// <c>value.IncludedPaths[0] == "$.Health"</c> — 📐 but the session is opened over THE VARIABLE'S
    /// VALUE, so the document root IS the value at <c>$</c>, and <c>"$.Health"</c> asked for a field
    /// named <c>Health</c> INSIDE the <c>int</c>. ⇒ <c>FilterNode</c> matched nothing and the dialog
    /// drew an empty body — exactly what the user reported.</para>
    ///
    /// <para>⚠ <b>Batch 75 already "fixed" this rail once</b>, from <c>"Health"</c> to
    /// <c>"$.Health"</c> — ⭐ it corrected the SPACE and kept the PREMISE, and stayed green for four
    /// more batches. 📌 <b>Inverted rather than deleted</b>, so the old expectation cannot come back
    /// quietly.</para>
    /// </summary>
    [Fact]
    public void TheTwoActions_BothOpenTheWholeDocumentForAWholeVariable()
    {
        var properties = VariableEditLauncher.ScopeFor(VariableEditAction.Properties);
        var value      = VariableEditLauncher.ScopeFor(VariableEditAction.EditValue);

        Assert.Same(EditScope.WholeComponent, properties);
        Assert.Same(EditScope.WholeComponent, value);
    }

    /// <summary>
    /// ⭐⭐ <b>The <c>ForField</c> arm is ALIVE, for what it is actually for</b> — a path to a field
    /// INSIDE a DTO variable. ⛔ No production caller passes one yet *(that gesture does not exist)*,
    /// and 📌 the handoff is explicit that the arm stays: <i>"stop feeding it the variable name"</i>.
    /// </summary>
    [Fact]
    public void ARealSubPathStillNarrowsTheScope()
    {
        var value = VariableEditLauncher.ScopeFor(VariableEditAction.EditValue, "Speed");

        Assert.NotSame(EditScope.WholeComponent, value);
        Assert.Equal("$.Speed", Assert.Single(value.IncludedPaths).ToString());
    }

    /// <summary>⭐ An already-rooted sub-path passes through untouched — a caller that knows the
    /// document shape is not second-guessed, and rooting is idempotent.</summary>
    [Fact]
    public void AnAlreadyRootedPath_IsNotRootedTwice()
    {
        var value = VariableEditLauncher.ScopeFor(VariableEditAction.EditValue, "$.Nested.Field");

        Assert.Equal("$.Nested.Field", value.IncludedPaths[0].ToString());
    }

    /// <summary>
    /// ⭐⭐⭐ <b>§9's rail: exactly ONE call site constructs a variable edit session.</b>
    ///
    /// <para>
    /// 🔴🔴 <b>This FAILED before Batch 68 and the failure was real, not theoretical.</b>
    /// <c>InspectorWindow:352-365</c> inlined its own copy of <c>DefaultValueAuthoring.Hydrate</c> —
    /// the same deserialize-or-<c>Activator</c> try/catch — and then called
    /// <c>IComponentEditService.Open</c> itself. ⇒ the "STATIC PARAMETERS" section and the new dialog
    /// would have hydrated a default value by two code paths that could drift on JSON options alone.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>The technique, and its bound.</b> The IL is scanned for <c>call</c>/<c>callvirt</c> tokens
    /// that RESOLVE to <c>IComponentEditService.Open</c>, without a full opcode walk — so in principle
    /// a byte pattern inside another instruction's operand could match. ⛔ It would have to be the
    /// exact opcode byte followed by a token resolving to this one method; a false positive would make
    /// the test STRICTER, never looser, so it cannot hide a second call site.
    /// </para>
    ///
    /// <para>
    /// 📌 <b>The FACET session is a different concept and is TOLERATED BY NAME.</b>
    /// A facet is not a variable; folding it in would make the rail about "opens any dialog", which is
    /// not what §9 says. ⛔ It is named in the expected set rather than skipped by a predicate, so a
    /// variable dialog reappearing in that type fails the test instead of being absorbed by it.
    /// </para>
    ///
    /// <para>
    /// ⭐ <b><c>S2</c> (<c>BP-399</c>, <c>2026-08-22</c>) — the tolerated caller MOVED, and only moved.</b>
    /// 📄 §7.6 ②: <c>InspectorWindow</c>'s node arms were EXTRACTED to
    /// <c>NodePropertiesDetailsView</c>, so the facet session is opened in <c>DrawFacetArm</c> now.
    /// ⚠ <b>Still exactly ONE variable-session call site</b> — that is the claim, and it is unchanged.
    /// </para>
    /// </summary>
    [Fact]
    public void ExactlyOneCallSite_OpensAVariableEditSession()
    {
        var open = typeof(IComponentEditService).GetMethod(nameof(IComponentEditService.Open))!;
        var assembly = typeof(DefaultValueAuthoring).Assembly;

        // ⭐ ONE ENTRY PER CALL SITE, not per method. 🔴 Learned from this rail's own revert probe:
        //   counting DISTINCT METHODS made restoring the duplicate invisible, because both Open calls
        //   lived in the same method. A rail that cannot see the defect it was written for is worse
        //   than none.
        var callers = FindCallSites(assembly, open)
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        // ⭐⭐ The tolerated caller is NAMED, not skipped by a predicate. A predicate ("ignore
        //     InspectorWindow") would silently widen the moment a variable dialog was opened there
        //     again -- which is the exact regression this rail exists to catch. ⇒ the whole set is
        //     asserted, so ANY new call site fails the test and has to be justified in the diff.
        Assert.Equal(
            new[]
            {
                "DefaultValueAuthoring.OpenSession",         // ⭐ THE variable edit session, both scopes
                "NodePropertiesDetailsView.DrawFacetArm",   // 📌 the FACET session -- a different concept
            },
            callers);
    }

    private static IReadOnlyList<MethodBase> FindCallSites(Assembly assembly, MethodInfo target)
    {
        var module = assembly.ManifestModule;
        var result = new List<MethodBase>();

        foreach (var type in assembly.GetTypes())
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                             | BindingFlags.Instance | BindingFlags.Static
                                             | BindingFlags.DeclaredOnly)
                                   .Cast<MethodBase>()
                                   .Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic
                                                              | BindingFlags.Instance | BindingFlags.Static)))
        {
            byte[]? il;
            try { il = method.GetMethodBody()?.GetILAsByteArray(); } catch { continue; }
            if (il is null) continue;

            for (int i = 0; i + 4 < il.Length; i++)
            {
                if (il[i] != 0x28 && il[i] != 0x6F) continue;        // call / callvirt
                int token = BitConverter.ToInt32(il, i + 1);
                MethodBase? resolved;
                try { resolved = module.ResolveMethod(token, type.GetGenericArguments(), null); }
                catch { continue; }
                if (resolved is MethodInfo mi && mi.Name == target.Name
                    && mi.DeclaringType == target.DeclaringType)
                    result.Add(method);          // ⛔ no break -- N calls in one method are N call sites
            }
        }
        return result;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 74 item 4 — measured instead of retired, and the measurement inverts the ask.</b>
    ///
    /// <para>
    /// 📌 <b>The batch asked to retire <c>InspectorWindow</c>'s "STATIC PARAMETERS" block</b> as a
    /// second surface for one concept, on the grounds that Track C's dialog had replaced it.
    /// 📐 <b>Measured: the opposite is true.</b> <c>VariableEditLauncher</c> — Track C's dialog entry
    /// point (📄 <c>DESIGN_Variable_Details_And_Editing.md</c> §3, reached from the <c>⋮</c> menu and a
    /// value-cell double-click) — is <b>constructed by nothing</b>, because the table's context menu is
    /// not wired yet. ⛔ Retiring the panel would have deleted the ONLY LIVE authoring surface for a
    /// bound variable's default value and left the replacement unreachable.
    /// </para>
    ///
    /// <para>
    /// ⭐⭐ <b>And ruling 9 is already satisfied</b>, which is what the ask was really about: Batch 68
    /// routed the panel through <see cref="DefaultValueAuthoring.OpenSession"/>, so the two are ONE
    /// implementation with two entry points — pinned by
    /// <see cref="ExactlyOneCallSite_OpensAVariableEditSession"/> above, not by this test.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>This asserts a GAP, deliberately, and is named for it</b> (Batch 70's rule): when the
    /// Track C menu lands, <b>INVERT this, do not delete it</b> — at which point retiring the panel
    /// becomes a real question rather than an assumed one.
    /// </para>
    /// </summary>
    [Fact]
    public void TrackCsVariableDialog_NowHasAnEntryPoint_AndTheNodeScopedPanelStays()
    {
        var assembly = typeof(DefaultValueAuthoring).Assembly;

        // ⭐ The panel really is wired: the node-properties view opens a variable session through the
        //   ONE opener.
        // ⭐⭐ S2 (BP-399, 2026-08-22): the panel MOVED — it is `NodePropertiesDetailsView`'s
        //    default-value arm now, not InspectorWindow's (§7.6 ② / BP-431: both node arms had to move
        //    together because they shared one facet cache). ⚠ The GAP this rail asserts is unchanged.
        var openSession = typeof(DefaultValueAuthoring)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(DefaultValueAuthoring.OpenSession));
        Assert.Contains(
            FindCallSites(assembly, openSession),
            m => m.DeclaringType!.Name == "NodePropertiesDetailsView");

        // ⭐⭐ INVERTED in Batch 75: the launcher is now CONSTRUCTED — VariableEditGestureBinder binds
        //    the table's two gestures to it, so the asset-scoped dialog has an entry point at last.
        //    ⛔ The half above still holds: the node-scoped panel stays, and the two share one
        //    implementation (ExactlyOneCallSite_OpensAVariableEditSession, unchanged).
        Assert.NotEmpty(FindCallSites(assembly, typeof(VariableEditLauncher)
            .GetMethod(nameof(VariableEditLauncher.Open))!));
    }

    /// <summary>⭐ <c>newobj</c> sites for <paramref name="target"/> — the constructor twin of
    /// <see cref="FindCallSites"/>, using the same token-resolution technique and the same bound
    /// (a false positive makes the rail stricter, never looser).</summary>
    private static IReadOnlyList<MethodBase> FindConstructionSites(Assembly assembly, Type target)
    {
        var module = assembly.ManifestModule;
        var result = new List<MethodBase>();

        foreach (var type in assembly.GetTypes())
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                             | BindingFlags.Instance | BindingFlags.Static
                                             | BindingFlags.DeclaredOnly)
                                   .Cast<MethodBase>()
                                   .Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic
                                                              | BindingFlags.Instance | BindingFlags.Static)))
        {
            byte[]? il;
            try { il = method.GetMethodBody()?.GetILAsByteArray(); } catch { continue; }
            if (il is null) continue;

            for (int i = 0; i + 4 < il.Length; i++)
            {
                if (il[i] != 0x73) continue;                       // newobj
                int token = BitConverter.ToInt32(il, i + 1);
                MethodBase? resolved;
                try { resolved = module.ResolveMethod(token, type.GetGenericArguments(), null); }
                catch { continue; }
                if (resolved is ConstructorInfo ci && ci.DeclaringType == target)
                    result.Add(method);
            }
        }
        return result;
    }

    // ── §9 · kind-driven fields ─────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>Every offered property has a BACKING MEMBER on its carrier.</b> ⛔ <c>D7</c>'s Replication
    /// and Range groups have none, which is why they are absent from the design rather than deferred:
    /// building them would produce controls with nowhere to save.
    /// </summary>
    [Theory]
    [InlineData(VariableDeclarationKind.BlueprintVariable,  typeof(VariableDecl))]
    [InlineData(VariableDeclarationKind.BlueprintParameter, typeof(ParameterDecl))]
    [InlineData(VariableDeclarationKind.BlackboardEntry,    typeof(BlackboardVariableEntry))]
    public void EveryOfferedProperty_HasABackingMemberOnItsCarrier(
        VariableDeclarationKind kind, Type carrier)
    {
        foreach (var property in VariablePropertySchema.For(kind))
        {
            var member = VariablePropertySchema.BackingMember(kind, property);
            Assert.NotNull(member);
            Assert.True(
                carrier.GetProperty(member!) is not null || carrier.GetField(member!) is not null,
                $"{kind}.{property} claims backing member '{member}', which {carrier.Name} does not have");
        }
    }

    /// <summary>⭐ The set DIFFERS BY KIND (§2) — the dialog is driven by the kind, not by one fixed form.</summary>
    [Fact]
    public void TheEditableSet_DiffersByDeclarationKind()
    {
        var variable  = VariablePropertySchema.For(VariableDeclarationKind.BlueprintVariable);
        var parameter = VariablePropertySchema.For(VariableDeclarationKind.BlueprintParameter);
        var entry     = VariablePropertySchema.For(VariableDeclarationKind.BlackboardEntry);

        // Category / IsEditable / IsExposedOnSpawn exist only on VariableDecl.
        Assert.Contains(VariableProperty.Category,   variable);
        Assert.DoesNotContain(VariableProperty.Category, parameter);
        Assert.DoesNotContain(VariableProperty.Category, entry);

        // Tooltip exists on both blueprint carriers but not on the blackboard entry.
        Assert.Contains(VariableProperty.Tooltip, parameter);
        Assert.DoesNotContain(VariableProperty.Tooltip, entry);

        // ⭐ Name / Type / DefaultValue are universal -- they are what a variable IS.
        foreach (var set in new[] { variable, parameter, entry })
        {
            Assert.Contains(VariableProperty.Name, set);
            Assert.Contains(VariableProperty.Type, set);
            Assert.Contains(VariableProperty.DefaultValue, set);
        }
    }

    /// <summary>
    /// ⛔⛔ <b><c>Role</c>/<c>Scope</c> is NOT A PROPERTY AT ALL</b> (§1c) — the SECTION is the
    /// classification. ⭐ Asserted rather than assumed, because <c>BlackboardVariableEntry</c> DOES
    /// carry both members: their absence has to be a decision, not an oversight.
    /// </summary>
    [Fact]
    public void RoleAndScope_AreNotProperties_ThoughTheCarrierHasThem()
    {
        Assert.NotNull(typeof(BlackboardVariableEntry).GetProperty("Role"));
        Assert.NotNull(typeof(BlackboardVariableEntry).GetProperty("Scope"));

        Assert.DoesNotContain(Enum.GetNames<VariableProperty>(),
            n => n.Contains("Role", StringComparison.Ordinal)
              || n.Contains("Scope", StringComparison.Ordinal));

        foreach (var kind in Enum.GetValues<VariableDeclarationKind>())
        foreach (var p in VariablePropertySchema.For(kind))
            Assert.True(VariablePropertySchema.BackingMember(kind, p) is not "Role" and not "Scope");
    }

    /// <summary>
    /// ⚠ <c>IsExposedOnSpawn</c> is KEPT — persisted, with a backing member, though nothing reads it at
    /// spawn. ⛔ <b>Do not "clean it up"</b>: unreferenced ≠ unintentional. 📐 The gap is filed.
    /// </summary>
    [Fact]
    public void IsExposedOnSpawn_IsKeptBecauseItIsPersisted_EvenThoughNothingReadsIt()
    {
        Assert.Contains(VariableProperty.IsExposedOnSpawn,
                        VariablePropertySchema.For(VariableDeclarationKind.BlueprintVariable));
        Assert.NotNull(typeof(VariableDecl).GetProperty("IsExposedOnSpawn"));
    }

    // ── §9 · the run-state matrix ───────────────────────────────────────────────

    /// <summary>⭐ §5 as a table-driven test, <b>including replay ⇒ no dialog</b>.</summary>
    [Theory]
    // planning: both fully editable
    [InlineData(VariableEditAction.EditValue,  VariableRunState.Planning, VariableEditAvailability.Editable)]
    [InlineData(VariableEditAction.Properties, VariableRunState.Planning, VariableEditAvailability.Editable)]
    // running / paused: value staged, properties read-only -- ⛔ you cannot retype mid-run
    [InlineData(VariableEditAction.EditValue,  VariableRunState.Running, VariableEditAvailability.Editable)]
    [InlineData(VariableEditAction.Properties, VariableRunState.Running, VariableEditAvailability.ReadOnly)]
    [InlineData(VariableEditAction.EditValue,  VariableRunState.Paused,  VariableEditAvailability.Editable)]
    [InlineData(VariableEditAction.Properties, VariableRunState.Paused,  VariableEditAvailability.ReadOnly)]
    // ⛔ replay: no dialog at all
    [InlineData(VariableEditAction.EditValue,  VariableRunState.Replay,  VariableEditAvailability.Denied)]
    [InlineData(VariableEditAction.Properties, VariableRunState.Replay,  VariableEditAvailability.Denied)]
    public void TheRunStateMatrix(
        VariableEditAction action, VariableRunState runState, VariableEditAvailability expected)
        => Assert.Equal(expected, VariableEditPolicy.Resolve(action, runState, Row()));

    /// <summary>⭐ Row-kind refusal, <b>proven by trying</b>: 🔒 and node-owned rows never get a
    /// writable dialog, in EITHER mode.</summary>
    [Theory]
    [InlineData(VariableRowKind.ReadOnlyPassthrough, VariableRunState.Planning)]
    [InlineData(VariableRowKind.ReadOnlyPassthrough, VariableRunState.Running)]
    [InlineData(VariableRowKind.NodeOwned,           VariableRunState.Planning)]
    [InlineData(VariableRowKind.NodeOwned,           VariableRunState.Running)]
    public void ReadOnlyAndNodeOwnedRows_AreNeverEditable(VariableRowKind kind, VariableRunState state)
    {
        foreach (var action in Enum.GetValues<VariableEditAction>())
            Assert.Equal(VariableEditAvailability.ReadOnly,
                         VariableEditPolicy.Resolve(action, state, Row(kind)));
    }

    /// <summary>⭐ A stale row's asset or entity is gone — no dialog, in any mode.</summary>
    [Fact]
    public void AStaleRow_GetsNoDialogAtAll()
    {
        foreach (var state in Enum.GetValues<VariableRunState>())
        foreach (var action in Enum.GetValues<VariableEditAction>())
            Assert.Equal(VariableEditAvailability.Denied,
                         VariableEditPolicy.Resolve(action, state, Row(stale: true)));
    }
}
