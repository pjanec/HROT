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
    /// </summary>
    [Fact]
    public void TheTwoActions_DifferOnlyByTheEditScope()
    {
        var properties = VariableEditLauncher.ScopeFor(VariableEditAction.Properties, "Health");
        var value      = VariableEditLauncher.ScopeFor(VariableEditAction.EditValue,  "Health");

        Assert.Same(EditScope.WholeComponent, properties);          // all fields
        Assert.Single(value.IncludedPaths);                          // exactly the one field
        Assert.Equal("Health", value.IncludedPaths[0].ToString());
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
    /// 📌 <b><c>InspectorWindow</c>'s FACET session is a different concept and is TOLERATED BY NAME.</b>
    /// A facet is not a variable; folding it in would make the rail about "opens any dialog", which is
    /// not what §9 says. ⛔ It is named in the expected set rather than skipped by a predicate, so a
    /// variable dialog reappearing in that type fails the test instead of being absorbed by it.
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
                "DefaultValueAuthoring.OpenSession",   // ⭐ THE variable edit session, both scopes
                "InspectorWindow.DrawClientArea",      // 📌 the FACET session -- a different concept
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
