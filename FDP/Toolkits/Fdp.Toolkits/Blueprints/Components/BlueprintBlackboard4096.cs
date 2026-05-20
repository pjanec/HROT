using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Blueprints.Components;

/// <summary>
/// Medium blackboard slot -- up to 3936 bytes of Blueprint state plus a 160-byte header.
/// Component ID 206 reserved for Blueprint subsystem.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 4096)]
[ComponentId(206)]
public unsafe struct BlueprintBlackboard4096
{
    public fixed byte Data[4096];
}
