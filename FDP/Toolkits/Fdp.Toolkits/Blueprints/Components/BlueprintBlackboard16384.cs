using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Blueprints.Components;

/// <summary>
/// Large blackboard slot -- up to 16368 bytes of Blueprint state plus a 16-byte header.
/// Component ID 207 reserved for Blueprint subsystem.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 16384)]
[ComponentId(207)]
public unsafe struct BlueprintBlackboard16384
{
    public fixed byte Data[16384];
}
