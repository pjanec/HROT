using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Bagira.IG.Components;
using Bagira.Map.Common.Replication.Utils;
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
        var ctx = new EcsPatchContext(repo, entity, compiler.Routes);

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

        var ctx = new EcsPatchContext(repo, entity, compiler.Routes);

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

        var ctx = new EcsPatchContext(repo, entity, compiler.Routes);

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

        var ex = Record.Exception(() => compiler.Compile(null, ctx));

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
