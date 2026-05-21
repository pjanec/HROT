using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Blueprints.Components;

/// <summary>
/// Small blackboard slot -- up to 928 bytes of Blueprint state plus a 96-byte header.
/// Component ID 205 reserved for Blueprint subsystem.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 1024)]
[ComponentId(GlobalComponentIds.BlueprintBlackboard1024)]
public unsafe struct BlueprintBlackboard1024
{
    public fixed byte Data[1024];
}
