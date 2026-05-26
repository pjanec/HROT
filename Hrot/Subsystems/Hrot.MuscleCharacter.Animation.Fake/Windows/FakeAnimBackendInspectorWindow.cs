using System;
using Fdp.Core;
using Fdp.Presentation.WindowManager;
using Hrot.MuscleCharacter.Animation.Fake.Components;

namespace Hrot.MuscleCharacter.Animation.Fake.Windows;

/// <summary>
/// ANC-P1-09: ImGui diagnostic window for FakeAnimationBackend inspection.
/// Registers to SimHostSubsystem (verified in BATCH-01).
/// Placeholder for Phase 1 - deferred to Phase 2 pending editor infrastructure.
/// </summary>
public sealed class FakeAnimBackendInspectorWindow : ManagedWindow
{
    private EntityRepository? _repo;

    public FakeAnimBackendInspectorWindow()
        : base("anim_backend_inspector", "Animation Backend Inspector", "Authoring", WindowScope.PerspectiveBound)
    {
    }

    public void SetBackend(EntityRepository repo)
    {
        _repo = repo;
    }

    protected override void DrawClientArea()
    {
        // Placeholder - deferred to Phase 2
        // TODO: Implement ImGui inspection panel
    }
}
