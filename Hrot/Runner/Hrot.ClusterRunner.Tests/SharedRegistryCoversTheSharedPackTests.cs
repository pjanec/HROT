using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Hrot.Map.Common;
using Hrot.Map.Common.Translators;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// ⭐⭐ CE-4013 D19 (R-4057 + R-3015) — a node that registers ONLY <see cref="HrotSharedComponentRegistry"/> accepts every sample the
/// SHARED translator pack can apply. Every networked ECS node runs that pack, so what it writes must be registered with it.
///
/// <para>📌 Measured 2026-10-17 on a five-process cluster: the traffic node (shared registry only) crashed on <i>"Component EntityInfo
/// is not registered"</i> from <c>EntityInfoIngressTranslator</c>. A sweep of all 34 shared ingress translators found six such types
/// (EntityInfo, UnitSubordinate, CmdAssign/RemoveSubordinate, EditablePolyline, MapOverlayStyle, RoutePlan).</para>
///
/// <para>⭐ Behavioural, not a list: the pack is built on a real participant, and every ingress translator is fed a default sample of
/// its OWN wire type (read from its <c>DdsReader&lt;T&gt;</c> field) through <c>ApplyToEntity</c>. Only "not registered" fails the rail;
/// any other throw from a default sample is not this rail's concern.</para>
/// </summary>
public sealed class SharedRegistryCoversTheSharedPackTests
{
    private const int Domain = 197;

    [Fact]
    public void ASharedRegistryOnlyWorld_AcceptsEverySharedIngress()
    {
        var world = new EntityRepository();
        HrotSharedComponentRegistry.RegisterAll(world);
        var (failures, applied) = ApplyEveryIngress(world);
        Assert.True(applied > 10, $"only {applied} ingress translators were exercised — the walk is not finding the pack.");
        Assert.True(failures.Count == 0, "the shared pack writes types HrotSharedComponentRegistry does not register: " + string.Join(" | ", failures));
    }

    /// <summary>⭐ The rail can fail: on a world with nothing registered, the same walk reports the gap.</summary>
    [Fact]
    public void TheWalk_ReportsAnUnregisteredType()
    {
        var (failures, _) = ApplyEveryIngress(new EntityRepository());
        Assert.Contains(failures, f => f.StartsWith("EntityInfoIngressTranslator", StringComparison.Ordinal));   // ⭐ the measured crash path
    }

    [Fact]
    public void TheSweptTypes_AreRegistered()
    {
        var world = new EntityRepository();
        HrotSharedComponentRegistry.RegisterAll(world);
        Assert.True(world.IsComponentTypeRegistered<EntityInfo>());
        Assert.True(world.IsComponentTypeRegistered<Fdp.Core.CommandHierarchy.UnitSubordinate>());
        Assert.True(world.IsComponentTypeRegistered<Hrot.IG.Components.MapOverlayStyle>());
        Assert.True(world.Bus.IsRegistered<Fdp.Core.CommandHierarchy.CmdAssignSubordinate>());
        Assert.True(world.Bus.IsRegistered<Fdp.Core.CommandHierarchy.CmdRemoveSubordinate>());
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3268</c> — every component a shared-pack translator TARGETS (<c>TargetComponentIds</c>) is registered by the shared registry:
    /// an ingress may add it on any node. 📌 Found live (2026-10-19): SimHost crashed on the first sector order — <c>EngagementArea</c> (417,
    /// replicated since CE-3261) was registered only by the brain's cognitive registry. The walk above could not see it: those ingresses apply
    /// through their command buffer, not <c>ApplyToEntity</c>. ✅ Red before the fix (EngagementArea).
    /// </summary>
    [Fact]
    public void CE3268_EveryComponentASharedTranslatorTargets_IsRegisteredByTheSharedRegistry()
    {
        var world = new EntityRepository();
        HrotSharedComponentRegistry.RegisterAll(world);
        var registered = world.GetRegisteredComponentTypes();
        var byId = new Dictionary<int, List<Type>>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray()!; }
            foreach (var t in types)
                if (t.GetCustomAttribute<ComponentIdAttribute>() is { } a)
                    (byId.TryGetValue(a.Id, out var l) ? l : byId[a.Id] = new List<Type>()).Add(t);
        }

        using var participant = new DdsParticipant(Domain);
        var entityMap = new NetworkEntityMap();
        var translators = SharedTranslatorPack.Create(
            participant, entityMap, localNodeId: 1, world.Bus, new GhostCreationSystem(entityMap)).ToList();
        var missing = new List<string>();
        int checkedIds = 0;
        foreach (var t in translators.OfType<IDescriptorTranslator>())
            foreach (int id in t.TargetComponentIds)
            {
                checkedIds++;
                if (!byId.TryGetValue(id, out var candidates) || !candidates.Any(registered.ContainsKey))
                    missing.Add($"{t.GetType().Name} → {id} ({string.Join("/", candidates?.Select(c => c.Name) ?? new[] { "?" })})");
            }
        Assert.True(checkedIds > 10, $"only {checkedIds} target ids were walked — the pack is not being found.");
        Assert.True(missing.Count == 0, "a shared translator targets a component HrotSharedComponentRegistry does not register: " + string.Join(" | ", missing.Distinct()));
    }

    private static (List<string> Failures, int Applied) ApplyEveryIngress(EntityRepository world)
    {
        using var participant = new DdsParticipant(Domain);
        var entityMap = new NetworkEntityMap();
        var translators = SharedTranslatorPack.Create(
            participant, entityMap, localNodeId: 1, world.Bus, new GhostCreationSystem(entityMap)).ToList();

        var failures = new List<string>();
        int applied = 0;
        foreach (var t in translators.OfType<IDescriptorTranslator>().Where(t => t.Direction != TranslatorDirection.Egress))
        {
            Type? wire = WireType(t);
            if (wire == null) continue;
            object sample;
            try { sample = Activator.CreateInstance(wire)!; } catch { continue; }
            var entity = world.CreateEntity();
            applied++;
            try { t.ApplyToEntity(entity, sample, world); }
            catch (Exception e) when (e.GetBaseException().Message.Contains("not registered", StringComparison.Ordinal))
            {
                failures.Add($"{t.GetType().Name}: {e.GetBaseException().Message}");
            }
            catch { /* a default sample may be invalid for other reasons — not this rail's question */ }
        }
        return (failures, applied);
    }

    private static Type? WireType(object translator)
    {
        for (var type = translator.GetType(); type != null; type = type.BaseType)
            foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                if (f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(DdsReader<>))
                    return f.FieldType.GetGenericArguments()[0];
        return null;
    }
}
