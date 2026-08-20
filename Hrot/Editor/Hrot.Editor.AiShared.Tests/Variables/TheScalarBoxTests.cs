using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Editor.AiShared.Variables;
using StructEdit.Core;
using StructEdit.Reflection;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐⭐ <b>Batch 97 (<c>97a</c>) — the one-field wrapper: it makes a scalar editable, and it NEVER
/// ESCAPES.</b>
///
/// <para>🔴🔴 <c>BP-356</c>: <c>CreateLeafBinding</c> needs a MEMBER and a document ROOT has none ⇒ a
/// scalar variable's root came back unbound and <c>DrawLeafNode</c>'s <c>node.Binding?.SetBoxed(value)</c>
/// discarded the typing. ⭐ <c>ScalarEditBox&lt;T&gt;</c> gives the root a bound CHILD.</para>
///
/// <para>⛔⛔ <b>The two things that could go wrong with a wrapper, both railed here</b>: it could fail
/// to apply to a type that needed it *(§1 — pinned against the REAL builder, not against a list)*, and
/// it could <b>leak into storage</b> *(§2/§3 — the declaration's JSON and the live bytes are the
/// SCALAR)*.</para>
/// </summary>
public sealed class TheScalarBoxTests
{
    /// <summary>⭐ A composite — it must NOT be wrapped; its own members are already bindable.</summary>
    public struct Params
    {
#pragma warning disable CS0649   // written only by StructEdit's reflection
        public int   Count;
        public float Speed;
#pragma warning restore CS0649
    }

    private enum Mood { Calm, Angry }

    /// <summary>⭐ Every type the editor can plausibly declare a variable as.</summary>
    public static IEnumerable<object[]> TypeCorpus() => new[]
    {
        new object[] { typeof(int) },     new object[] { typeof(uint) },
        new object[] { typeof(long) },    new object[] { typeof(ulong) },
        new object[] { typeof(short) },   new object[] { typeof(ushort) },
        new object[] { typeof(byte) },    new object[] { typeof(sbyte) },
        new object[] { typeof(float) },   new object[] { typeof(double) },
        new object[] { typeof(bool) },    new object[] { typeof(Guid) },
        new object[] { typeof(DateTime) },new object[] { typeof(Mood) },
        new object[] { typeof(Params) },  new object[] { typeof(System.Numerics.Vector3) },
    };

    // ══ 1 — the predicate agrees with the REAL builder ═══════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The mirror is pinned to the original.</b> 📐 <c>ReflectionEditDocumentBuilder.DetermineKind</c>
    /// is <b>private</b>, so <see cref="ScalarEditBox.NeedsBoxing"/> mirrors its leaf arms — ⛔ and a
    /// mirror nobody checks is how the next primitive falls off the list.
    ///
    /// <para>⭐ The observable is the one that actually matters: <b>a type needs boxing exactly when a
    /// session opened over it produces a root with NO binding and NO children</b> — which is precisely
    /// the shape <c>DrawLeafNode</c> cannot write through.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(TypeCorpus))]
    public void NeedsBoxingAgreesWithWhatTheBuilderActuallyProduces(Type type)
    {
        var service = new ComponentEditServiceBuilder().Build();
        using var bare = service.Open(
            Activator.CreateInstance(type)!, type, EditScope.WholeComponent);

        var root = bare.Document.Root;
        bool unwritable = root.Binding is null && root.Children.Count == 0;

        Assert.Equal(unwritable, ScalarEditBox.NeedsBoxing(type));
    }

    /// <summary>
    /// ⭐⭐ <b>…and the wrapper actually fixes it.</b> ⛔ Agreeing about which types are broken would be
    /// worthless if the box did not then produce a bound child for them.
    /// </summary>
    [Theory]
    [MemberData(nameof(TypeCorpus))]
    public void TheEditTypeAlwaysHasSomethingBoundToWrite(Type type)
    {
        var service = new ComponentEditServiceBuilder().Build();
        var seed    = ScalarEditBox.Wrap(Activator.CreateInstance(type)!, type);

        using var session = service.Open(seed, ScalarEditBox.EditTypeFor(type), EditScope.WholeComponent);

        Assert.True(Bound(session.Document.Root),
            $"a session over {type.Name} still has nothing DrawLeafNode could write through.");
    }

    private static bool Bound(EditNode node)
        => node.Binding is not null || node.Children.Any(Bound);

    // ══ 2 — the JSON arm: the declaration receives the SCALAR ════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE ROUND TRIP, JSON arm</b> — 📌 the handoff's extra gate: <i>seed → edit → commit →
    /// read back the SCALAR</i>. ⛔ <c>{"Value":7}</c> in a declaration would fail to hydrate for every
    /// later reader, and it would look like a corrupt asset rather than a leaked wrapper.
    /// </summary>
    [Fact]
    public void TheJsonArmWritesTheScalarAndItRehydrates()
    {
        var entry = new BlackboardVariableEntry("Count", typeof(int), Comment: null, DefaultValueJson: "7");
        var service = new ComponentEditServiceBuilder().Build();

        using var session = DefaultValueAuthoring.OpenSession(service, entry, EditScope.WholeComponent);
        Field(session, "Value").Binding!.SetBoxed(99);

        var json = DefaultValueAuthoring.CommitAndSerialize(session, typeof(int));

        Assert.Equal("99", json);                                   // ⛔ not {"Value":99}
        Assert.Equal(99, DefaultValueAuthoring.Hydrate(typeof(int), json));
    }

    /// <summary>⭐ …and a COMPOSITE still round-trips exactly as it did — ⛔ the wrapper must not touch
    /// the path that already worked.</summary>
    [Fact]
    public void ACompositeIsUnaffected()
    {
        var entry = new BlackboardVariableEntry("Settings", typeof(Params), Comment: null);
        var service = new ComponentEditServiceBuilder().Build();

        using var session = DefaultValueAuthoring.OpenSession(service, entry, EditScope.WholeComponent);
        Field(session, "Count").Binding!.SetBoxed(42);

        var json = DefaultValueAuthoring.CommitAndSerialize(session, typeof(Params));

        Assert.Contains("\"Count\":42", json);
        Assert.DoesNotContain("Value", json);
        Assert.Equal(42, ((Params)DefaultValueAuthoring.Hydrate(typeof(Params), json)).Count);
    }

    // ══ 3 — the bytes arm: the blackboard receives the SCALAR ════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE ROUND TRIP, bytes arm.</b> 📌 The handoff demands both — ⚠ <b>and this one is the
    /// dangerous half</b>: bytes go into a live blackboard at a field's offset, so a wrapper image
    /// there does not read wrong, it <b>scribbles</b>.
    ///
    /// <para>⚠ <b>Asserted on the VALUE, not on the size.</b> A single-field struct happens to share
    /// its field's layout today, so a size check would pass even if the box leaked. ⭐ Decoding the
    /// bytes back is the claim that survives someone adding a second field to the wrapper.</para>
    /// </summary>
    [Fact]
    public void TheBytesArmWritesTheScalarsImage()
    {
        var entry = new BlackboardVariableEntry("Count", typeof(int), Comment: null, DefaultValueJson: "7");
        var service = new ComponentEditServiceBuilder().Build();

        var row = new VariableRow(
            Origin:    new VariableRowOrigin(Guid.NewGuid(), default, "s", "Count", "Alpha"),
            ShortName: "Count", TypeText: "int", ClrType: typeof(int),
            ReadValue: () => Array.Empty<byte>());

        using var session = DefaultValueAuthoring.OpenSession(service, entry, EditScope.WholeComponent);
        Field(session, "Value").Binding!.SetBoxed(99);

        byte[]? written = null;
        var outcome = VariableEditCommit.Commit(
            session, asset: null, row, typeof(int), VariableRunState.Paused,
            writeLive: (_, bytes) => { written = bytes.ToArray(); return true; });

        Assert.Equal(VariableEditCommit.Outcome.Ok, outcome);
        Assert.NotNull(written);
        Assert.Equal(4, written!.Length);
        Assert.Equal(99, BitConverter.ToInt32(written, 0));
    }

    // ══ 4 — the wrapper's own contract ═══════════════════════════════════════

    /// <summary>⭐ <c>Unwrap</c> FAILS OPEN — a caller that never wrapped is unharmed, which is what
    /// lets both commit arms call it unconditionally.</summary>
    [Fact]
    public void UnwrapLeavesAnUnboxedValueAlone()
    {
        // ⚠ ONE boxed instance, so reference identity is a meaningful claim about "untouched".
        object composite = new Params { Count = 3 };
        Assert.Same(composite, ScalarEditBox.Unwrap(composite, typeof(Params)));

        // ⭐ A leaf type whose value is ALREADY the scalar — the commit arms call Unwrap
        //   unconditionally, so this path has to be harmless.
        Assert.Equal(7, ScalarEditBox.Unwrap(7, typeof(int)));

        // ⭐ …and a real box does unwrap.
        Assert.Equal(7, ScalarEditBox.Unwrap(ScalarEditBox.Wrap(7, typeof(int)), typeof(int)));
    }

    /// <summary>⛔ A composite is never given a wrapper — ⭐ its own members are already bindable, and
    /// wrapping it would put an extra level in front of the designer for nothing.</summary>
    [Fact]
    public void ACompositeIsNeverWrapped()
    {
        Assert.Same(typeof(Params), ScalarEditBox.EditTypeFor(typeof(Params)));
        Assert.False(ScalarEditBox.NeedsBoxing(typeof(Params)));
    }

    private static EditNode Field(IEditSession session, string name)
        => session.Document.Root.Children.Single(c => c.Name == name);
}
