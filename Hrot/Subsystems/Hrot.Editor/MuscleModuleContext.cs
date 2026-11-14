using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;

namespace Hrot.Editor;

/// <summary>
/// Passed to <see cref="EditorSubsystem.MuscleModuleFactory"/> during
/// <c>EditorSubsystem.Initialize()</c> so the caller can construct muscle
/// modules that share the same <see cref="World"/> and <see cref="EntityMap"/>
/// as the editor's ECS kernel.
/// </summary>
/// <param name="World">
/// The live <see cref="EntityRepository"/> the kernel operates on.
/// </param>
/// <param name="EntityMap">
/// The network-entity identity map shared across all modules in the kernel.
/// </param>
public sealed record MuscleModuleContext(
    EntityRepository World,
    NetworkEntityMap EntityMap);
