using System;
using System.Collections.Generic;
using Hrot.Common.Infrastructure;
using Hrot.MuscleCharacter.Animation;
using Hrot.MuscleCharacter.Animation.Baking;
using Hrot.MuscleCharacter.Animation.Contracts;
using Hrot.MuscleCharacter.Animation.Fake;

namespace Hrot.SimHost;

/// <summary>
/// ⭐ <c>CE-2121</c> — the character body's animation Muscle: registers <see cref="AnimationMuscleModule"/> (which now carries the
/// stance transition) over a backend the host chooses — the existing <see cref="FakeAnimationBackend"/> by default, standing in
/// for a 3D renderer (🔒 user <c>2026-10-07</c>). Declared in SimHost's plan and the editor's default plan; a Brain-only node
/// (CGF) never resolves it. 📄 docs/DESIGN_Decision_Layer.md §3.3g.
/// </summary>
/// <remarks>
/// ⭐ Module-only (<see cref="Register"/>, like <c>NavigationSolver</c>): it contributes no phase-group systems, so every system
/// sequence the composition rails pin is unchanged. ⛔ Montage / look-at requests stay unconsumed until their components are
/// registered (<c>CE-3010</c>) — the module's other systems match nothing.
/// </remarks>
public sealed class AnimationMuscleCapability : INodeCapability
{
    private readonly Func<IAnimationBackend> _backendFactory;

    public AnimationMuscleCapability(Func<IAnimationBackend>? backendFactory = null)
        => _backendFactory = backendFactory ?? (() => new FakeAnimationBackend());

    /// <summary>The backend <see cref="Register"/> built (null before it ran). For rails and diagnostics.</summary>
    public IAnimationBackend? Backend { get; private set; }

    public string Key => CapabilityKeys.AnimationMuscle;

    public IReadOnlyList<string> Needs { get; } = Array.Empty<string>();

    public void Register(HrotNodeContext context, NodeBootValues values)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        Backend = _backendFactory();
        context.Kernel.RegisterModule(new AnimationMuscleModule(Backend, new BakedAnimationCache(null)));
    }
}
