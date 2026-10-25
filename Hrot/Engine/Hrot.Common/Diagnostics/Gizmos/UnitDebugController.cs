using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Diagnostics;

namespace Hrot.Common.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐⭐ <c>CE-1257</c> (BV-26, R-1029) — "Debug this unit": ONE switch holding ONE debugged unit. On = its traces armed (the BTree / HSM
    /// rings and the utility trace) and the AI graph's live overlay follows it (⛔ CE-1317: no longer every gizmo family pinned) (the pump asks
    /// <see cref="Follow"/>) instead of the selection; off = all of it undone. The Brain tab, the map's AI-trace submenu and
    /// <c>POST /entities/{id}/debug</c> are three buttons on this one object. 📄 docs/DESIGN_Entity_Brain_View.md §10.
    /// <para>⭐ Composed by a host that has the AI debuggers (the editor) — <see cref="Current"/> is null elsewhere, and the buttons hide.</para>
    /// </summary>
    public sealed class UnitDebugController
    {
        /// <summary>The host's controller, or null where nothing composes one.</summary>
        public static UnitDebugController? Current { get; set; }

        /// <summary>The unit being debugged, or null.</summary>
        public Entity? Debugged { get; private set; }

        /// <summary>Raised after <see cref="Debugged"/> changes (the new value).</summary>
        public event Action<Entity?>? Changed;

        /// <summary>⭐ <c>CE-1258</c> — arms a "stop when it changes its mind" breakpoint on a unit (the host wires its breakpoint manager);
        /// returns the breakpoint id, or null where there is no manager.</summary>
        public Func<EntityRepository, Entity, string?>? ArmJournalBreak { get; set; }

        /// <summary>⭐ <c>CE-1254</c> (BV-21) — opens a behaviour's graph by name in its editor (the host wires its catalog and documents);
        /// false when this host has no graph of that name. A C#-built one opens read-only (CE-1252).</summary>
        public Func<string, bool>? OpenGraph { get; set; }

        /// <summary>⭐ <c>CE-1318</c> (BV-55) — opens a behaviour's graph PINNED to a unit (name, unit, the tab's label for it): its own
        /// document whose live overlay stays on that unit. The Brain tab uses it when the tab itself is pinned.</summary>
        public Func<string, Entity, string, bool>? OpenGraphPinned { get; set; }

        public bool IsDebugged(Entity unit) => Debugged is { } d && d == unit;

        /// <summary>The unit the AI graph's overlay should follow: the debugged one while it lives, else <paramref name="selection"/>.</summary>
        public Entity? Follow(ISimulationView? world, Entity? selection)
            => Debugged is { } d && world is not null && world.IsAlive(d) ? d : selection;

        /// <summary>Turns debugging of <paramref name="unit"/> on or off. On moves it from any previously debugged unit.</summary>
        public void Set(EntityRepository world, Entity unit, bool on)
        {
            ArgumentNullException.ThrowIfNull(world);
            if (unit == Entity.Null || !world.IsAlive(unit)) return;
            if (on)
            {
                if (IsDebugged(unit)) return;
                if (Debugged is { } previous && world.IsAlive(previous)) Apply(world, previous, false);
                Apply(world, unit, true);
                Debugged = unit;
            }
            else
            {
                if (!IsDebugged(unit)) return;
                Apply(world, unit, false);
                Debugged = null;
            }
            Changed?.Invoke(Debugged);
        }

        public void Toggle(EntityRepository world, Entity unit) => Set(world, unit, !IsDebugged(unit));

        private static void Apply(EntityRepository world, Entity unit, bool on)
        {
            AiTraceActions.Set(world, unit, BehaviorDebugFlags.EnableTraceBuffer, on);
            // ⛔ CE-1317 (BV-49, R-1038) — no longer pins every gizmo family ("turning all gizmos at once would be overwhelming"): the
            //   Brain tab's "Show on map" pins one family per checkbox.
        }
    }
}
