using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;

namespace Fdp.Toolkit.Blueprints.Systems;

/// <summary>
/// Handles Blueprint tier upgrades (BeforeSync phase).
/// When an entity holds both the smaller and larger tier components simultaneously,
/// this system copies state from the smaller to the larger and removes the smaller.
/// Per Runtime DD §7 + InlinePatches Q-12.3.
/// </summary>
[UpdateInPhase(SystemPhase.BeforeSync)]
public sealed class BlueprintMaintenanceSystem : IEcsModuleSystem, IProfiledSystem
{
    private EntityQuery? _queryUpgrade1024to4096;
    private EntityQuery? _queryUpgrade4096to16384;

    public string ProfileName => "BlueprintMaintenanceSystem";

    public void Execute(ISimulationView view, float deltaTime)
    {
        var repo = (EntityRepository)view;

        _queryUpgrade1024to4096 ??= repo.Query()
            .With<BlueprintBlackboard1024>()
            .With<BlueprintBlackboard4096>()
            .Build();
        _queryUpgrade4096to16384 ??= repo.Query()
            .With<BlueprintBlackboard4096>()
            .With<BlueprintBlackboard16384>()
            .Build();

        UpgradeTier_1024_to_4096(repo);
        UpgradeTier_4096_to_16384(repo);
    }

    private unsafe void UpgradeTier_1024_to_4096(EntityRepository repo)
    {
        foreach (var entity in _queryUpgrade1024to4096!)
        {
            ref var oldBB   = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
            ref byte srcRef = ref Unsafe.As<BlueprintBlackboard1024, byte>(ref oldBB);
            byte* src       = (byte*)Unsafe.AsPointer(ref srcRef);

            ref var newBB   = ref repo.GetComponentRW<BlueprintBlackboard4096>(entity);
            ref byte dstRef = ref Unsafe.As<BlueprintBlackboard4096, byte>(ref newBB);
            byte* dst       = (byte*)Unsafe.AsPointer(ref dstRef);

            BlueprintBlackboardPartitions.CopyToLargerTier(
                src, BlueprintBlackboard1024.TotalSize,
                dst, BlueprintBlackboard4096.TotalSize, (byte)BlueprintBlackboard4096.MaxSlots);

            repo.RemoveComponent<BlueprintBlackboard1024>(entity);
        }
    }

    private unsafe void UpgradeTier_4096_to_16384(EntityRepository repo)
    {
        foreach (var entity in _queryUpgrade4096to16384!)
        {
            ref var oldBB   = ref repo.GetComponentRW<BlueprintBlackboard4096>(entity);
            ref byte srcRef = ref Unsafe.As<BlueprintBlackboard4096, byte>(ref oldBB);
            byte* src       = (byte*)Unsafe.AsPointer(ref srcRef);

            ref var newBB   = ref repo.GetComponentRW<BlueprintBlackboard16384>(entity);
            ref byte dstRef = ref Unsafe.As<BlueprintBlackboard16384, byte>(ref newBB);
            byte* dst       = (byte*)Unsafe.AsPointer(ref dstRef);

            BlueprintBlackboardPartitions.CopyToLargerTier(
                src, BlueprintBlackboard4096.TotalSize,
                dst, BlueprintBlackboard16384.TotalSize, (byte)BlueprintBlackboard16384.MaxSlots);

            repo.RemoveComponent<BlueprintBlackboard4096>(entity);
        }
    }
}
