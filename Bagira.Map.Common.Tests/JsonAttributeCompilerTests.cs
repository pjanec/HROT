using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Bagira.IG.Components;
using FDP.Toolkit.Replication.Patching;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Abstractions;

namespace Bagira.Map.Common.Tests;

// ─────────────────────────────────────────────────────────────
// Test helper types
// ─────────────────────────────────────────────────────────────

/// <summary>Minimal unmanaged struct used as a stand-in in compiler tests.</summary>
public struct TestWeaponState
{
    public int Count;
}

// ─────────────────────────────────────────────────────────────
// ATTR-S4T1 — IEntityPatchContext compile-time test
// ─────────────────────────────────────────────────────────────

public class IEntityPatchContextTests
{
    /// <summary>
    /// Compile-time verification: a <see cref="ValueAttributeSetter{T}"/> can be declared
    /// with a <c>ref T</c> parameter and assigned without error.
    /// </summary>
    [Fact]
    public void IEntityPatchContext_ValueAttributeSetter_AcceptsRef()
    {
        ValueAttributeSetter<SimTransform> setter =
            (ref SimTransform component, scoped ReadOnlySpan<int> indices, ref Utf8JsonReader reader) =>
            {
                component.Position = new System.Numerics.Vector3(1f, 2f, 3f);
                reader.GetString(); // consume token
            };

        // The assignment above compiles only if ref T is valid — that is the assertion.
        Assert.NotNull(setter);
    }
}

// ─────────────────────────────────────────────────────────────
// ATTR-S4T2 — AttributeCompilerBuilder tests
// ─────────────────────────────────────────────────────────────

public class AttributeCompilerBuilderTests
{
    [Fact]
    public void AttributeCompilerBuilder_RegisterValuePath_CanBuildAndCompile()
    {
        var compiler = new AttributeCompilerBuilder()
            .RegisterValuePath<SimTransform>("GeoPosition",
                (ref SimTransform c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
                    c.Position = new System.Numerics.Vector3((float)r.GetDouble(), 0f, 0f))
            .Build();

        Assert.NotNull(compiler);
        Assert.IsType<JsonAttributeCompiler>(compiler);
    }

    [Fact]
    public void AttributeCompilerBuilder_DuplicatePath_Throws()
    {
        var builder = new AttributeCompilerBuilder()
            .RegisterReferencePath<IgEntityData>("Name",
                (IgEntityData c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
                    c.Name = r.GetString() ?? string.Empty);

        Assert.Throws<InvalidOperationException>(() =>
        {
            builder.RegisterReferencePath<IgEntityData>("Name",
                (IgEntityData c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
                    c.Name = r.GetString() ?? string.Empty);
        });
    }

    [Fact]
    public void AttributeCompilerBuilder_RegisterReferencePath_CanBuildAndCompile()
    {
        var compiler = new AttributeCompilerBuilder()
            .RegisterReferencePath<IgEntityData>("Name",
                (IgEntityData c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
                    c.Name = r.GetString() ?? string.Empty)
            .Build();

        Assert.NotNull(compiler);
        Assert.IsType<JsonAttributeCompiler>(compiler);
    }

    [Fact]
    public void AttributeCompilerBuilder_EmptyBuilder_BuildsValidCompilerThatIgnoresAllJson()
    {
        var compiler = new AttributeCompilerBuilder().Build();
        Assert.NotNull(compiler);

        var ctx = new ListPatchContext(null);

        // Should not throw
        compiler.Compile("{\"Name\":\"X\"}", ctx);

        // No routes registered — context should produce an empty list.
        var result = ctx.FlushComponents();
        Assert.Empty(result);
    }
}

// ─────────────────────────────────────────────────────────────
// ATTR-S4T3 — ListPatchContext tests
// ─────────────────────────────────────────────────────────────

public class ListPatchContextTests
{
    [Fact]
    public void ListPatchContext_GetManagedComponent_ReturnsExistingInstance()
    {
        var existing = new IgEntityData { Name = "existing" };
        var ctx = new ListPatchContext(new List<object> { existing });

        var got = ctx.GetManagedComponent<IgEntityData>();

        Assert.Same(existing, got);
        Assert.Equal("existing", got.Name);
    }

    [Fact]
    public void ListPatchContext_GetManagedComponent_CreatesDefaultWhenMissing()
    {
        var ctx = new ListPatchContext(null);

        var got = ctx.GetManagedComponent<IgEntityData>();

        Assert.NotNull(got);
        Assert.IsType<IgEntityData>(got);
    }

    [Fact]
    public void ListPatchContext_FlushComponents_ContainsExactlyOnePerType()
    {
        var ctx = new ListPatchContext(null);

        // Retrieve same type twice — both must return the same cached instance.
        var data1 = ctx.GetManagedComponent<IgEntityData>();
        var data2 = ctx.GetManagedComponent<IgEntityData>();
        Assert.Same(data1, data2);

        var result = ctx.FlushComponents();
        int count = result.OfType<IgEntityData>().Count();
        Assert.Equal(1, count);
    }

    [Fact]
    public void ListPatchContext_OverwriteFlaw_DualPatch_BothChangesPreserved()
    {
        // Seed with an existing IgEntityData.
        var seeded = new IgEntityData { Name = "old", ForceId = ForceId.Friend };
        var ctx = new ListPatchContext(new List<object> { seeded });

        // Build a compiler that patches "Name" and "Affiliation" on the same component type.
        var compiler = new AttributeCompilerBuilder()
            .RegisterReferencePath<IgEntityData>("Name",
                (IgEntityData c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
                    c.Name = r.GetString() ?? string.Empty)
            .RegisterReferencePath<IgEntityData>("Affiliation",
                (IgEntityData c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
                    c.ForceId = r.GetString() == "FORCE_HOSTILE" ? ForceId.Hostile : ForceId.Unknown)
            .Build();

        compiler.Compile("{\"Name\":\"new\",\"Affiliation\":\"FORCE_HOSTILE\"}", ctx);

        var result = ctx.FlushComponents();
        var found = result.OfType<IgEntityData>().Single();

        // Both changes should be present on the single instance (overwrite flaw prevented).
        Assert.Equal("new", found.Name);
        Assert.Equal(ForceId.Hostile, found.ForceId);
    }

    /// <summary>
    /// ATTR-S5T2 — <see cref="ListPatchContext.FlushDirtyMarks"/> is an explicit no-op:
    /// it must not throw and must not produce any ECS side-effects (no SmartEgressUtil calls).
    /// </summary>
    [Fact]
    public void ListPatchContext_FlushDirtyMarks_IsNoOp()
    {
        var ctx = new ListPatchContext(null);

        // No exception must be thrown.
        var ex = Record.Exception(() => ctx.FlushDirtyMarks());
        Assert.Null(ex);

        // No ECS repository exists here — the absence of any exception confirms
        // that SmartEgressUtil was never called (it would fail without a live repo).
    }
}

// ─────────────────────────────────────────────────────────────
// ATTR-S4T3 — EcsPatchContext tests
// ─────────────────────────────────────────────────────────────

public class EcsPatchContextTests
{
    private static EntityRepository CreateRepo()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterManagedComponent<IgEntityData>();
        return repo;
    }

    [Fact]
    public void EcsPatchContext_GetUnmanagedComponent_ReturnsRefToEcs()
    {
        var repo = CreateRepo();
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SimTransform { Position = new System.Numerics.Vector3(0f, 0f, 0f) });

        // Build an empty compiler (no routes needed for this test).
        var compiler = new AttributeCompilerBuilder().Build();
        var ctx = compiler.CreatePatchContext(repo, entity);

        // Mutate through the context ref.
        ref SimTransform t = ref ctx.GetUnmanagedComponent<SimTransform>();
        t.Position = new System.Numerics.Vector3(42f, 0f, 0f);

        // Read back directly from ECS.
        ref readonly SimTransform readBack = ref repo.GetComponentRO<SimTransform>(entity);
        Assert.Equal(42f, readBack.Position.X);
    }

    [Fact]
    public void EcsPatchContext_FlushDirtyMarks_CallsSmartEgressForTouchedComponents()
    {
        const long TestOrdinal = 1001L;

        var repo = CreateRepo();
        var entity = repo.CreateEntity();
        repo.SetManagedComponent(entity, new IgEntityData { Name = "alpha" });

        var compiler = new AttributeCompilerBuilder()
            .RegisterReferencePath<IgEntityData>("Name",
                (IgEntityData c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
                    c.Name = r.GetString() ?? string.Empty,
                descriptorOrdinal: TestOrdinal)
            .Build();

        var ctx = compiler.CreatePatchContext(repo, entity);

        // Simulate a delegate invocation touching IgEntityData (ordinal TestOrdinal gets recorded).
        _ = ctx.GetManagedComponent<IgEntityData>();

        // Flush — should call SmartEgressUtil.MarkDirty(repo, entity, TestOrdinal).
        ctx.FlushDirtyMarks();

        // Verify side-effect: EgressPublicationState.DirtyDescriptors contains TestOrdinal.
        var state = ((ISimulationView)repo).GetManagedComponentRO<EgressPublicationState>(entity);
        Assert.NotNull(state);
        Assert.Contains(TestOrdinal, state.DirtyDescriptors);
    }

    [Fact]
    public void EcsPatchContext_FlushDirtyMarks_DeduplicatesOrdinals()
    {
        const long SharedOrdinal = 2002L;

        var repo = CreateRepo();
        var entity = repo.CreateEntity();
        repo.SetManagedComponent(entity, new IgEntityData { Name = "beta" });

        // Both "Name" and "Affiliation" map to the same ordinal (like dtEntityInfo).
        var compiler = new AttributeCompilerBuilder()
            .RegisterReferencePath<IgEntityData>("Name",
                (IgEntityData c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
                    c.Name = r.GetString() ?? string.Empty,
                descriptorOrdinal: SharedOrdinal)
            .RegisterReferencePath<IgEntityData>("Affiliation",
                (IgEntityData c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
                    c.ForceId = ForceId.Hostile,
                descriptorOrdinal: SharedOrdinal)
            .Build();

        var ctx = compiler.CreatePatchContext(repo, entity);

        // Simulate two delegate invocations on the same component type → same ordinal twice.
        _ = ctx.GetManagedComponent<IgEntityData>();
        _ = ctx.GetManagedComponent<IgEntityData>();

        ctx.FlushDirtyMarks();

        // EgressPublicationState.DirtyDescriptors uses HashSet semantics — ordinal appears once.
        var state = ((ISimulationView)repo).GetManagedComponentRO<EgressPublicationState>(entity);
        int occurrences = state.DirtyDescriptors.Count(o => o == SharedOrdinal);
        Assert.Equal(1, occurrences);
    }
}

// ─────────────────────────────────────────────────────────────
// ATTR-S3T1 — JsonAttributeCompiler tests
// ─────────────────────────────────────────────────────────────

public class JsonAttributeCompilerTests
{
    [Fact]
    public void JsonAttributeCompiler_NullJson_DoesNotThrow()
    {
        var compiler = new AttributeCompilerBuilder().Build();
        var ctx = new ListPatchContext(null);

        var ex = Record.Exception(() => compiler.Compile((string?)null, ctx));

        Assert.Null(ex);
        Assert.Empty(ctx.FlushComponents());
    }

    [Fact]
    public void JsonAttributeCompiler_EmptyJson_DoesNotThrow()
    {
        var compiler = new AttributeCompilerBuilder().Build();
        var ctx = new ListPatchContext(null);

        var ex = Record.Exception(() => compiler.Compile("", ctx));

        Assert.Null(ex);
        Assert.Empty(ctx.FlushComponents());
    }

    [Fact]
    public void JsonAttributeCompiler_FlatStringProperty_InvokesDelegate()
    {
        var compiler = new AttributeCompilerBuilder()
            .RegisterReferencePath<IgEntityData>("Name",
                (IgEntityData c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
                    c.Name = r.GetString() ?? string.Empty)
            .Build();

        var ctx = new ListPatchContext(null);
        compiler.Compile("{\"Name\":\"Alpha-1\"}", ctx);

        var result = ctx.FlushComponents();
        var data = result.OfType<IgEntityData>().Single();
        Assert.Equal("Alpha-1", data.Name);
    }

    [Fact]
    public void JsonAttributeCompiler_NestedProperty_InvokesCorrectDelegate()
    {
        double capturedLat = 0;
        int invocations = 0;

        var compiler = new AttributeCompilerBuilder()
            .RegisterValuePath<SimTransform>("GeoPosition.Latitude",
                (ref SimTransform c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
                {
                    capturedLat = r.GetDouble();
                    invocations++;
                })
            .Build();

        var ctx = new ListPatchContext(null);
        compiler.Compile("{\"GeoPosition\":{\"Latitude\":32.5,\"Longitude\":34.8,\"Altitude\":0}}", ctx);

        Assert.Equal(1, invocations);
        Assert.Equal(32.5, capturedLat, precision: 5);
    }

    [Fact]
    public void JsonAttributeCompiler_UnknownProperty_IsIgnored()
    {
        bool delegateInvoked = false;

        var compiler = new AttributeCompilerBuilder()
            .RegisterReferencePath<IgEntityData>("Name",
                (IgEntityData c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
                {
                    delegateInvoked = true;
                    c.Name = r.GetString() ?? string.Empty;
                })
            .Build();

        var ctx = new ListPatchContext(null);
        compiler.Compile("{\"Unknown\":42}", ctx);

        Assert.False(delegateInvoked);
    }
}

// ─────────────────────────────────────────────────────────────
// ATTR-S3T2 — FNV-1a path hashing tests
// ─────────────────────────────────────────────────────────────

public class FnvHashTests
{
    [Fact]
    public void FnvHash_SamePathSameHash()
    {
        ulong h1 = JsonAttributeCompiler.HashPath("Name");
        ulong h2 = JsonAttributeCompiler.HashPath("Name");

        Assert.Equal(h1, h2);
    }

    [Fact]
    public void FnvHash_DifferentPathDifferentHash()
    {
        ulong h1 = JsonAttributeCompiler.HashPath("Name");
        ulong h2 = JsonAttributeCompiler.HashPath("Affiliation");

        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void FnvHash_ArrayIndexNormalisedToWildcard()
    {
        // Streaming "Weapons.0.Ammo.Count" and "Weapons.5.Ammo.Count" must trigger the same route.
        int index0Count = 0;
        int index5Count = 0;

        var compiler = new AttributeCompilerBuilder()
            .RegisterValuePath<TestWeaponState>("Weapons.*.Ammo.Count",
                (ref TestWeaponState c, scoped ReadOnlySpan<int> indices, ref Utf8JsonReader r) =>
                {
                    int idx = indices.Length > 0 ? indices[0] : -1;
                    c.Count = r.GetInt32();
                    if (idx == 0) index0Count++;
                    if (idx == 5) index5Count++;
                })
            .Build();

        var ctx0 = new ListPatchContext(null);
        compiler.Compile("{\"Weapons\":{\"0\":{\"Ammo\":{\"Count\":10}}}}", ctx0);

        var ctx5 = new ListPatchContext(null);
        compiler.Compile("{\"Weapons\":{\"5\":{\"Ammo\":{\"Count\":20}}}}", ctx5);

        Assert.Equal(1, index0Count);
        Assert.Equal(1, index5Count);
    }

    [Fact]
    public void FnvHash_DepthRestoreOnEndObject()
    {
        // {"A":{"B":"x"},"C":1} — after EndObject for the inner A block,
        // "C" should dispatch to the route registered for HashPath("C").
        bool bInvoked = false;
        bool cInvoked = false;

        var compiler = new AttributeCompilerBuilder()
            .RegisterReferencePath<IgEntityData>("A.B",
                (IgEntityData c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
                {
                    bInvoked = true;
                    c.Name = r.GetString() ?? string.Empty;
                })
            .RegisterValuePath<TestWeaponState>("C",
                (ref TestWeaponState c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
                {
                    cInvoked = true;
                    c.Count = r.GetInt32();
                })
            .Build();

        var ctx = new ListPatchContext(null);
        compiler.Compile("{\"A\":{\"B\":\"x\"},\"C\":1}", ctx);

        Assert.True(bInvoked, "Nested path 'A.B' should have been invoked.");
        Assert.True(cInvoked,
            "Top-level path 'C' should have been invoked after depth was restored by EndObject.");
    }
}
// ─────────────────────────────────────────────────────────────
// EcsPatchContext authority tests
// ─────────────────────────────────────────────────────────────

public class EcsPatchContextAuthorityTests
{
    private static EntityRepository CreateRepo()
    {
        var repo = new EntityRepository();
        repo.RegisterComponent<SimTransform>();
        repo.RegisterManagedComponent<IgEntityData>();
        return repo;
    }

    [Fact]
    public void EcsPatchContext_CanWrite_ReturnsFalseWithoutSetAuthority()
    {
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SimTransform());
        // No SetAuthority call — authority bit is off.

        var compiler = new AttributeCompilerBuilder().Build();
        var ctx = compiler.CreatePatchContext(repo, entity);

        Assert.False(ctx.CanWrite<SimTransform>());
    }

    [Fact]
    public void EcsPatchContext_CanWrite_ReturnsTrueAfterSetAuthority()
    {
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        repo.AddComponent(entity, new SimTransform());
        repo.SetAuthority<SimTransform>(entity, true);

        var compiler = new AttributeCompilerBuilder().Build();
        var ctx = compiler.CreatePatchContext(repo, entity);

        Assert.True(ctx.CanWrite<SimTransform>());
    }

    [Fact]
    public void EcsPatchContext_CanWriteManaged_ReturnsFalseWithoutSetAuthority()
    {
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        repo.SetManagedComponent(entity, new IgEntityData { Name = "x" });
        // No SetAuthority call.

        var compiler = new AttributeCompilerBuilder().Build();
        var ctx = compiler.CreatePatchContext(repo, entity);

        Assert.False(ctx.CanWriteManaged<IgEntityData>());
    }

    [Fact]
    public void EcsPatchContext_CanWriteManaged_ReturnsTrueAfterSetAuthority()
    {
        var repo   = CreateRepo();
        var entity = repo.CreateEntity();
        repo.SetManagedComponent(entity, new IgEntityData { Name = "x" });
        repo.SetAuthority(entity, ManagedComponentType<IgEntityData>.ID, true);

        var compiler = new AttributeCompilerBuilder().Build();
        var ctx = compiler.CreatePatchContext(repo, entity);

        Assert.True(ctx.CanWriteManaged<IgEntityData>());
    }
}

// ─────────────────────────────────────────────────────────────
// Invoker-level split-authority tests
// Using a stub context that wraps ListPatchContext but restricts
// CanWrite/CanWriteManaged to a configurable set of owned types.
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Test stub that wraps a <see cref="ListPatchContext"/> and overrides
/// <c>CanWrite</c> / <c>CanWriteManaged</c> to simulate split-authority.
/// </summary>
internal sealed class SplitAuthorityContext : IEntityPatchContext
{
    private readonly ListPatchContext _inner;
    private readonly System.Collections.Generic.HashSet<Type> _ownedTypes;

    public SplitAuthorityContext(ListPatchContext inner, params Type[] ownedTypes)
    {
        _inner = inner;
        _ownedTypes = new System.Collections.Generic.HashSet<Type>(ownedTypes);
    }

    public ref T GetUnmanagedComponent<T>() where T : struct => ref _inner.GetUnmanagedComponent<T>();
    public T GetManagedComponent<T>() where T : class => _inner.GetManagedComponent<T>();
    public void FlushDirtyMarks() => _inner.FlushDirtyMarks();
    public bool CanWrite<T>() where T : struct => _ownedTypes.Contains(typeof(T));
    public bool CanWriteManaged<T>() where T : class => _ownedTypes.Contains(typeof(T));
}

public class InvokerAuthorityTests
{
    /// <summary>
    /// When a node owns only <c>IgEntityData</c> and not <c>TestWeaponState</c>, a JSON
    /// payload containing both fields must: skip the unowned <c>Count</c> field without
    /// crashing, and successfully apply the owned <c>Name</c> field.
    /// This is the textbook split-authority scenario from a multicast update broadcast.
    /// </summary>
    [Fact]
    public void ValueInvoker_SkipsUnownedComponent_OwnedComponentStillApplied()
    {
        bool weaponSetterCalled = false;

        var compiler = new AttributeCompilerBuilder()
            .RegisterValuePath<TestWeaponState>("Count",
                (ref TestWeaponState c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
                {
                    weaponSetterCalled = true;
                    c.Count = r.GetInt32();
                })
            .RegisterReferencePath<IgEntityData>("Name",
                (IgEntityData c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
                    c.Name = r.GetString() ?? string.Empty)
            .Build();

        var innerCtx = new ListPatchContext(null);
        // Only IgEntityData is owned — TestWeaponState is not.
        var splitCtx = new SplitAuthorityContext(innerCtx, typeof(IgEntityData));

        // Should not throw — unowned JSON sub-tree is skipped via reader.Skip().
        var ex = Record.Exception(() =>
            compiler.Compile("{\"Count\":99,\"Name\":\"Alpha\"}", splitCtx));

        Assert.Null(ex);
        Assert.False(weaponSetterCalled, "Setter for unowned component must not be invoked.");

        var results = innerCtx.FlushComponents();
        var data = results.OfType<IgEntityData>().Single();
        Assert.Equal("Alpha", data.Name);
    }

    /// <summary>
    /// When JSON contains a known route path that is not owned, <c>reader.Skip()</c> must
    /// leave the stream in a valid state so subsequent tokens are parsed correctly.
    /// </summary>
    [Fact]
    public void ValueInvoker_SkipDoesNotCorruptStream_FollowingTokensStillParsed()
    {
        int nameInvocations = 0;
        int weaponInvocations = 0;

        var compiler = new AttributeCompilerBuilder()
            .RegisterValuePath<TestWeaponState>("Count",
                (ref TestWeaponState c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
                {
                    weaponInvocations++;
                    c.Count = r.GetInt32();
                })
            .RegisterReferencePath<IgEntityData>("Name",
                (IgEntityData c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
                {
                    nameInvocations++;
                    c.Name = r.GetString() ?? string.Empty;
                })
            .Build();

        var innerCtx = new ListPatchContext(null);
        // Neither type is owned — both routes must be silently skipped.
        var splitCtx = new SplitAuthorityContext(innerCtx /*, empty owned set */);

        var ex = Record.Exception(() =>
            compiler.Compile("{\"Count\":5,\"Name\":\"Beta\"}", splitCtx));

        Assert.Null(ex);
        Assert.Equal(0, weaponInvocations);
        Assert.Equal(0, nameInvocations);
    }
}

// ─────────────────────────────────────────────────────────────
// ListPatchContext.Reset tests
// ─────────────────────────────────────────────────────────────

public class ListPatchContextResetTests
{
    [Fact]
    public void ListPatchContext_Reset_PriorUnmanagedSlotsDontLeak()
    {
        var ctx = new ListPatchContext(null);

        // Seed a value in the first "session".
        ref var slot1 = ref ctx.GetUnmanagedComponent<TestWeaponState>();
        slot1.Count = 42;

        // Reset with empty seed — slots must be cleared.
        ctx.Reset(null);

        // After reset, GetUnmanagedComponent must return the zero-initialised default.
        ref var slot2 = ref ctx.GetUnmanagedComponent<TestWeaponState>();
        Assert.Equal(0, slot2.Count);
    }

    [Fact]
    public void ListPatchContext_Reset_PriorManagedComponentsDontLeak()
    {
        var ctx = new ListPatchContext(null);

        var first = ctx.GetManagedComponent<IgEntityData>();
        first.Name = "first";

        ctx.Reset(null);

        // After reset, a new default instance must be created (not the old one).
        var second = ctx.GetManagedComponent<IgEntityData>();
        // The old instance may have been recycled, but the Name must be the default.
        Assert.NotEqual("first", second.Name ?? string.Empty);
    }

    [Fact]
    public void ListPatchContext_Reset_PicksUpNewSeedValues()
    {
        var ctx = new ListPatchContext(null);
        _ = ctx.GetManagedComponent<IgEntityData>(); // prime the cache

        var newSeed = new IgEntityData { Name = "seeded-name" };
        ctx.Reset(new List<object> { newSeed });

        var got = ctx.GetManagedComponent<IgEntityData>();
        Assert.Equal("seeded-name", got.Name);
    }

    [Fact]
    public void ListPatchContext_Reset_CanWriteStillReturnsTrue()
    {
        var ctx = new ListPatchContext(null);
        ctx.Reset(new List<object>());

        Assert.True(ctx.CanWrite<TestWeaponState>());
        Assert.True(ctx.CanWriteManaged<IgEntityData>());
    }

    [Fact]
    public void ListPatchContext_Reset_FlushComponentsReturnsOnlyNewValues()
    {
        var oldSeed = new IgEntityData { Name = "old" };
        var ctx = new ListPatchContext(new List<object> { oldSeed });
        _ = ctx.GetManagedComponent<IgEntityData>(); // prime cache with old seed

        var newSeed = new IgEntityData { Name = "new" };
        ctx.Reset(new List<object> { newSeed });

        // Compile a patch using a no-op compiler (just access the component).
        var got = ctx.GetManagedComponent<IgEntityData>();
        got.Name = "patched";

        var flushed = ctx.FlushComponents();
        var entity = flushed.OfType<IgEntityData>().Single();
        Assert.Equal("patched", entity.Name);
    }
}

// ─────────────────────────────────────────────────────────────
// JsonAttributeCompiler span overload tests
// ─────────────────────────────────────────────────────────────

public class JsonAttributeCompilerSpanOverloadTests
{
    [Fact]
    public void Compile_SpanOverload_ProducesIdenticalResultToStringOverload()
    {
        var compiler = new AttributeCompilerBuilder()
            .RegisterReferencePath<IgEntityData>("Name",
                (IgEntityData c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
                    c.Name = r.GetString() ?? string.Empty)
            .Build();

        const string json = "{\"Name\":\"Span-Alpha\"}";

        // String overload.
        var ctxStr = new ListPatchContext(null);
        compiler.Compile(json, ctxStr);
        string fromString = ctxStr.FlushComponents().OfType<IgEntityData>().Single().Name;

        // Span overload with pre-encoded bytes.
        var ctxSpan = new ListPatchContext(null);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
        compiler.Compile(new ReadOnlySpan<byte>(bytes), ctxSpan);
        string fromSpan = ctxSpan.FlushComponents().OfType<IgEntityData>().Single().Name;

        Assert.Equal(fromString, fromSpan);
        Assert.Equal("Span-Alpha", fromString);
    }

    [Fact]
    public void Compile_SpanOverload_EmptySpan_DoesNotThrow()
    {
        var compiler = new AttributeCompilerBuilder().Build();
        var ctx = new ListPatchContext(null);

        var ex = Record.Exception(() => compiler.Compile(ReadOnlySpan<byte>.Empty, ctx));

        Assert.Null(ex);
        Assert.Empty(ctx.FlushComponents());
    }

    [Fact]
    public void Compile_SpanOverload_NestedProperty_InvokesDelegate()
    {
        double capturedLat = 0;

        var compiler = new AttributeCompilerBuilder()
            .RegisterValuePath<SimTransform>("GeoPosition.Latitude",
                (ref SimTransform c, scoped ReadOnlySpan<int> _, ref Utf8JsonReader r) =>
                    capturedLat = r.GetDouble())
            .Build();

        var ctx = new ListPatchContext(null);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(
            "{\"GeoPosition\":{\"Latitude\":55.5,\"Longitude\":0}}");
        compiler.Compile(new ReadOnlySpan<byte>(bytes), ctx);

        Assert.Equal(55.5, capturedLat, precision: 5);
    }
}