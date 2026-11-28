using System;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Devices;
using Hrot.AI.Behaviors.Brains;   // the StatefulAction builder extension
using Hrot.Map.Definitions.Behavior;

namespace Hrot.AI.Behaviors.Devices.Binoculars
{
    /// <summary>
    /// ⭐⭐⭐ Simulated devices S1d-3 (docs/designs/devices/DESIGN_Simulated_Devices.md §3.10 D-42 / D-75, §3.1 D-8 / D-57, §3.5) — THE BINOCULARS' logic:
    /// ONE stateful action on the DEVICE entity, run every frame (paused or not) by the device tick, started from <c>DeviceInfo.Behavior</c> and
    /// restarted if it ever ends. Its state is <see cref="BinocularsState"/> (unit memory).
    /// <para>Each tick: ① the PRESSES (the one reader, <see cref="DevicePressReader"/> — re-based on a new operator epoch, R7-11); ② ZOOM IN / OUT step
    /// the zoom (clamped); ③ its own <see cref="DevicePose"/>: it points where its operator looks (the frame's orientation, R5-13) and a view bound to it
    /// shows the step's field of view (<see cref="BinocularsZoom.FovDegrees"/>) — written on change (<see cref="DeviceLooks.Follow"/>).</para>
    /// <para>⭐ The parallel rule: it writes ONLY its own entity. ⭐ Pause (R-4300): a press still steps the zoom — the device's own state. 0 B per tick.
    /// ⚠ What it does NOT do: raise the held binoculars — that is the BODY's brain (<c>OperatorControlBehavior</c>, D-63), reading the frame's
    /// <c>Viewed</c>; the device never writes its carrier.</para>
    /// </summary>
    public static class BinocularsBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct BinocularsParams
        {
            /// <summary>None yet — the steps are placeholders until the TKB type (<see cref="BinocularsZoom"/>).</summary>
            public byte Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BinocularsWork
        {
            /// <summary>Ticks of this run (diagnostic).</summary>
            public uint Ticks;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BinocularsBlackboard
        {
            public BinocularsParams Params;
            public BinocularsWork Work;
        }

        [BehaviorResolver(BehaviorNames.DeviceBinoculars)]
        public static void Resolve(in NoParams authored, ref BinocularsBlackboard block, EntityRepository world, Entity self)
            => block = default;

        // ⛔ Offered to nothing but a device: Requires DeviceInfo (BehaviorFit's AND), so no unit's task picker lists it.
        [Behavior(BehaviorNames.DeviceBinoculars, Params = typeof(NoParams), Requires = new[] { typeof(DeviceInfo) },
                  Description = "A device's logic: the binoculars (run by the device tick, never assigned).")]
        [BTreeDefinition(BehaviorNames.DeviceBinoculars, Curated = true, Group = "Library/Devices", ParamsType = typeof(BinocularsBlackboard))]
        public static BTreeBuilder<BinocularsBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<BinocularsBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Params, bb => bb.Work, Tick);

        /// <summary>One tick of the binoculars (see the class summary). Never ends: Running.</summary>
        [SharedAiAction]
        public static NodeStatus Tick(ref BinocularsParams p, ref BinocularsWork ws, Entity self, EntityRepository world)
        {
            ws.Ticks++;
            ref var st = ref UnitMemory.Ref<BinocularsState>(world, self);
            if (st.Ready == 0) { st.Zoom = BinocularsZoom.DefaultStep; st.Ready = 1; }
            Span<int> moved = stackalloc int[BinocularsChannels.Count];
            DevicePressReader.Read(world, self, ref st.Presses, moved);
            int top = BinocularsZoom.Steps.Length - 1;
            int zoom = st.Zoom + moved[BinocularsChannels.ZoomIn] - moved[BinocularsChannels.ZoomOut];
            st.Zoom = (byte)Math.Clamp(zoom, 0, top);
            DeviceLooks.Follow(world, self, BinocularsZoom.FovDegrees(st.Zoom));
            return NodeStatus.Running;
        }
    }
}
