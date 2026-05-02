using System;
using System.Linq;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Hrot.Common.Systems;
using Xunit;

namespace Hrot.SimHost.Tests.Integration;

/// <summary>
/// CS025-T02 — Atomic capacity validation.
///
/// Verifies that when 17 CmdAssignSubordinate events are processed against a single
/// commander whose UnitRoster has a capacity of 16, the 17th subordinate is rejected:
/// UnitRoster.Count stays at 16, the 17th entity has no UnitSubordinate,
/// and CmdAssignSubordinateRejected is published exactly once.
/// </summary>
public sealed class HierarchyCapacityIntegrationTests : IDisposable
{
    private readonly EntityRepository _repo;

    public HierarchyCapacityIntegrationTests()
    {
        _repo = new EntityRepository();
        _repo.RegisterComponent<UnitRoster>();
        _repo.RegisterComponent<UnitSubordinate>();
        _repo.RegisterEvent<CmdAssignSubordinate>();
        _repo.RegisterEvent<CmdRemoveSubordinate>();
        _repo.RegisterEvent<CmdAssignSubordinateRejected>();
    }

    public void Dispose() => _repo.Dispose();

    private void Tick()
    {
        _repo.Bus.SwapBuffers();
        new UnitHierarchySystem().Execute(_repo, 0.016f);
    }

    // CS025-T02
    [Fact]
    public void Assign_17Subordinates_16AcceptedOneRejected()
    {
        var commander = _repo.CreateEntity();
        // UnitRoster is added by the system on first assignment; no need to pre-add it.

        var subordinates = new Entity[17];
        for (int i = 0; i < 17; i++)
            subordinates[i] = _repo.CreateEntity();

        // Publish all 17 assign events in one batch.
        for (int i = 0; i < 17; i++)
            _repo.Bus.Publish(new CmdAssignSubordinate
            {
                Subordinate = subordinates[i],
                Commander   = commander,
                Designation = TacticalDesignation.Undefined,
            });

        Tick();

        // Assert: roster count is capped at 16.
        Assert.True(_repo.HasComponent<UnitRoster>(commander));
        var roster = _repo.GetComponent<UnitRoster>(commander);
        Assert.Equal(UnitRoster.Capacity, roster.Count);

        // Assert: the 17th subordinate has no UnitSubordinate component.
        // The 17th is the last one — the first 16 should have been accepted.
        var acceptedCount = subordinates.Count(s => _repo.HasComponent<UnitSubordinate>(s));
        Assert.Equal(16, acceptedCount);

        var rejectedEntity = subordinates.Single(s => !_repo.HasComponent<UnitSubordinate>(s));

        // Assert: CmdAssignSubordinateRejected was published for the rejected entity.
        _repo.Bus.SwapBuffers();
        var rejections = _repo.Bus.Read<CmdAssignSubordinateRejected>().ToArray();
        Assert.Single(rejections);
        Assert.Equal(rejectedEntity, rejections[0].Subordinate);
    }
}
