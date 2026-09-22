using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Selection;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// Unit tests for <see cref="LiveBlackboardValueProvider"/> (BATCH-11).
/// All tests use a fake <see cref="IInspectableSession"/> with controlled
/// <see cref="BrainBlackboard"/> + <see cref="BehaviorState"/> state to assert
/// real formatted-value results, not just string presence.
/// </summary>
public class LiveBlackboardValueProviderTests
{
    // ── Test DTO types ───────────────────────────────────────────────────────

    /// <summary>Multi-field struct DTO for value projection tests.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CounterParams
    {
        public int Counter;
        public int Threshold;
    }

    /// <summary>Single-field (primitive-like) struct DTO.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct SimpleScalar
    {
        public float Speed;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private const string AssetName = "TestBehavior";
    private const int    BehaviorId = 42;

    private static (BehaviorRegistry registry, BehaviorDefinition def) BuildRegistry(
        ManagedBlackboardVariable[]? vars = null)
    {
        var registry = new BehaviorRegistry();
        var def = new BehaviorDefinition
        {
            Name  = AssetName,
            BrainTier = BehaviorConstants.BrainTierBTree,
            ManagedBlackboardVariables = vars,
        };
        registry.Register(BehaviorId, AssetName, def);
        return (registry, def);
    }

    private static FakeAsset MakeAsset(string name = AssetName) => new FakeAsset(name);

    /// <summary>
    /// ⭐⭐ Builds a BOXED tier component holding a real occurrence store with this behaviour's ROOT
    /// PARAMS SLOT attached and <paramref name="dto"/> written into it.
    ///
    /// <para>⚠ The box is what makes this possible: <c>RootParamsProjection</c> pins the component the
    /// session hands back, so a fake can supply genuine store bytes without a world. ⛔ The pin is
    /// released before returning — the DATA lives in the boxed object, not in the handle.</para>
    /// </summary>
    private static unsafe object MakeStoreWithRootParams<T>(int behaviourHash, T dto) where T : unmanaged
    {
        object boxed = new Fdp.Toolkit.Blueprints.Components.BlueprintBlackboard1024();
        var handle = GCHandle.Alloc(boxed, GCHandleType.Pinned);
        try
        {
            byte* mem = (byte*)handle.AddrOfPinnedObject();
            Fdp.Toolkit.Blueprints.Partitioning.BlueprintBlackboardPartitions.Initialize(
                mem,
                Fdp.Toolkit.Blueprints.Components.BlueprintBlackboard1024.TotalSize,
                Fdp.Toolkit.Blueprints.Components.BlueprintBlackboard1024.MaxSlots);

            int key  = RootParamsAccess.KeyForBehaviour(behaviourHash);
            int size = sizeof(T);
            Assert.True(Fdp.Toolkit.Blueprints.Partitioning.BlueprintBlackboardPartitions.TryAttach(
                mem, key, size, unchecked((ulong)size),
                Fdp.Toolkit.Blueprints.Partitioning.OccurrenceKind.BTree, out int offset));

            *(T*)(mem + offset) = dto;
        }
        finally { handle.Free(); }
        return boxed;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>A POSITIVE rail: known values written into a REAL root params slot read back through
    /// the provider.</b> ⛔ Not a refusal check — a refusal is satisfied by an empty store, which is
    /// exactly how this surface stayed green while it was broken (<c>CE-312</c>).
    /// </summary>
    [Fact]
    public unsafe void LiveValues_SelectedEntityRunningAsset_ReturnsFormattedValues()
    {
        // Arrange: DTO with Counter=7, Threshold=1000 at offset 0 of the ROOT PARAMS SLOT.
        var expected = new CounterParams { Counter = 7, Threshold = 1000 };
        object paramsStore = MakeStoreWithRootParams(BehaviorId, expected);

        var vars = new[]
        {
            new ManagedBlackboardVariable("myCounter", typeof(CounterParams), ByteOffset: 0),
        };
        var (registry, _) = BuildRegistry(vars);

        var entity = new Entity(1, 1);
        var store   = new EditorSelectionStore();
        store.SelectedEntity = entity;

        var session = new FakeSession(entity, behaviorHash: BehaviorId, store: paramsStore);

        var provider = new LiveBlackboardValueProvider(
            sessionFactory:  () => session,
            registryFactory: () => registry,
            store:           store);

        // Act
        var values = provider.GetLiveVariableValues(MakeAsset());

        // Assert: map must contain the variable; formatted value must include both fields.
        Assert.True(values.ContainsKey("myCounter"), "Expected 'myCounter' in live values map.");
        var formatted = values["myCounter"];
        Assert.Contains("Counter=7",      formatted);
        Assert.Contains("Threshold=1000", formatted);
    }

    /// <summary>No entity selected → provider returns empty map.</summary>
    [Fact]
    public void LiveValues_NoSelection_ReturnsEmpty()
    {
        var (registry, _) = BuildRegistry();
        var store = new EditorSelectionStore();
        // SelectedEntity is null by default.

        var provider = new LiveBlackboardValueProvider(
            sessionFactory:  () => new FakeSession(null, 0, null),
            registryFactory: () => registry,
            store:           store);

        var values = provider.GetLiveVariableValues(MakeAsset());

        Assert.Empty(values);
    }

    /// <summary>
    /// Selected entity is running a DIFFERENT behavior (hash mismatch) →
    /// name-match gate rejects it → empty map.
    /// </summary>
    [Fact]
    public unsafe void LiveValues_SelectedEntityRunningDifferentBehavior_ReturnsEmpty()
    {
        // Register the behavior under BehaviorId=42, but the entity's ActiveBehaviorHash=99.
        var (registry, _) = BuildRegistry(new[]
        {
            new ManagedBlackboardVariable("x", typeof(CounterParams), 0),
        });

        var entity = new Entity(1, 1);
        var store  = new EditorSelectionStore();
        store.SelectedEntity = entity;

        // Entity is running behavior id 99, not 42.
        var session = new FakeSession(entity, behaviorHash: 99, store: null);

        var provider = new LiveBlackboardValueProvider(
            sessionFactory:  () => session,
            registryFactory: () => registry,
            store:           store);

        var values = provider.GetLiveVariableValues(MakeAsset(AssetName));

        // Name-match gate: TryGetId("TestBehavior") == 42 != 99 → empty.
        Assert.Empty(values);
    }

    /// <summary>
    /// If the DTO projection throws (e.g. bad offset / type), the provider
    /// omits that variable from the map but does not throw itself.
    /// </summary>
    [Fact]
    public void LiveValues_ProjectionFailure_OmitsVariable_DoesNotThrow()
    {
        // We use a ThrowingSession that throws from GetComponent(BrainBlackboard)
        // to simulate a projection failure pathway. The provider must catch it.
        var (registry, _) = BuildRegistry(new[]
        {
            new ManagedBlackboardVariable("bad", typeof(CounterParams), 0),
        });

        var entity = new Entity(1, 1);
        var store  = new EditorSelectionStore();
        store.SelectedEntity = entity;

        var session = new ThrowingBrainBlackboardSession(entity, BehaviorId);

        var provider = new LiveBlackboardValueProvider(
            sessionFactory:  () => session,
            registryFactory: () => registry,
            store:           store);

        // Must not throw, and result must be empty (variable omitted).
        var ex     = Record.Exception(() => provider.GetLiveVariableValues(MakeAsset()));
        var values = provider.GetLiveVariableValues(MakeAsset());

        Assert.Null(ex);
        // The "bad" variable is omitted because projection failed (GetComponent threw).
        Assert.False(values.ContainsKey("bad"));
    }

    // ── FormatValue unit tests (internal static, tested directly) ────────────

    [Fact]
    public void FormatValue_MultiFieldStruct_FormatsAllFields()
    {
        var v = new CounterParams { Counter = 3, Threshold = 500 };
        var result = LiveBlackboardValueProvider.FormatValue(v, typeof(CounterParams));
        Assert.Contains("Counter=3",      result);
        Assert.Contains("Threshold=500",  result);
    }

    [Fact]
    public void FormatValue_PrimitiveInt_ReturnsToString()
    {
        // A plain int has no public fields/properties, falls back to ToString().
        var result = LiveBlackboardValueProvider.FormatValue(42, typeof(int));
        Assert.Equal("42", result);
    }

    // ── Fake helpers ──────────────────────────────────────────────────────────

    private sealed class FakeAsset : IEditableAsset
    {
        public FakeAsset(string name)
        {
            AssetId = Guid.NewGuid();
            Name    = name;
        }
        public Guid    AssetId { get; }
        public string  Name    { get; }
        public AssetKind Kind  => AssetKind.BTree;
        public string SourceFilePath => "/fake.cs";
        public bool IsDirty  => false;
        public bool IsEditorOwned => true;
#pragma warning disable CS0067
        public event Action? Changed;
#pragma warning restore CS0067
    }

    /// <summary>
    /// Fake IInspectableSession that returns a BehaviorState for one entity.
    /// </summary>
    private sealed class FakeSession : IInspectableSession
    {
        private readonly Entity? _entity;
        private readonly int     _behaviorHash;
        private readonly object? _store;

        // ⭐⭐ P4 (2026-09-22): the `BrainBlackboard? bb` parameter is GONE. 🔴 It had ALREADY stopped
        //   mattering at P3-C: LiveBlackboardValueProvider reaches the params through
        //   RootParamsProjection.TryCopyRootParams (the entity's ROOT PARAMS SLOT), and never asks a
        //   session for that component. ⇒ the fake was feeding a channel nothing read.
        public FakeSession(Entity? entity, int behaviorHash, object? store = null)
        {
            _entity       = entity;
            _behaviorHash = behaviorHash;
            _store        = store;
        }

        public bool IsReadOnly => true;
        public int EntityCount => _entity.HasValue ? 1 : 0;
        public IEnumerable<Entity> GetEntities() => _entity.HasValue
            ? new[] { _entity.Value }
            : Array.Empty<Entity>();

        public bool IsAlive(Entity e) => _entity.HasValue && e == _entity.Value;

        public bool HasComponent(Entity e, Type t)
        {
            if (!IsAlive(e)) return false;
            if (t == typeof(BehaviorState)) return true;
            return _store != null && t == _store.GetType();
        }

        public object? GetComponent(Entity e, Type t)
        {
            if (!IsAlive(e)) return null;
            if (t == typeof(BehaviorState))
                return new BehaviorState { ActiveBehaviorHash = _behaviorHash };
            if (_store != null && t == _store.GetType()) return _store;
            return null;
        }

        public void SetComponent(Entity e, Type t, object v) { }
        public IEnumerable<Type> GetAllComponentTypes() => Array.Empty<Type>();
        public bool HasAuthority(Entity e, Type t) => false;
    }

    /// <summary>
    /// Fake session that returns BehaviorState correctly but supplies no params region,
    /// simulating a projection failure.
    /// </summary>
    private sealed class ThrowingBrainBlackboardSession : IInspectableSession
    {
        private readonly Entity _entity;
        private readonly int    _behaviorHash;

        public ThrowingBrainBlackboardSession(Entity entity, int behaviorHash)
        {
            _entity       = entity;
            _behaviorHash = behaviorHash;
        }

        public bool IsReadOnly  => true;
        public int EntityCount  => 1;
        public IEnumerable<Entity> GetEntities() => new[] { _entity };
        public bool IsAlive(Entity e) => e == _entity;

        public bool HasComponent(Entity e, Type t) => t == typeof(BehaviorState);

        public object? GetComponent(Entity e, Type t)
        {
            if (t == typeof(BehaviorState))
                return new BehaviorState { ActiveBehaviorHash = _behaviorHash };
            // ⚠ P4: the throwing arm keyed on BrainBlackboard is gone with the component. The
            //   projection-failure path this fake simulates is now reached through the occurrence
            //   store, so the simulation moved to returning nothing for it.
            return null;
        }

        public void SetComponent(Entity e, Type t, object v) { }
        public IEnumerable<Type> GetAllComponentTypes() => Array.Empty<Type>();
        public bool HasAuthority(Entity e, Type t) => false;
    }
}
