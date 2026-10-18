using System.Numerics;
using System.Runtime.InteropServices;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Squad.Command
{
    /// <summary>
    /// ⭐ <c>CE-2292</c> CC-B — reading and keeping a commander's <see cref="CommandPicture"/>: the ONE place that maps a subordinate to its
    /// row and a roster slot to a subordinate, shared by the picture guards, the command actions and the staff's inbox (CC-C).
    /// </summary>
    public static class CommandPictureOps
    {
        /// <summary>The id a picture keys a unit by: its network id, else its local handle (a world with no network — a test).</summary>
        public static long NetIdOf(ISimulationView view, Entity e)
        {
            if (view is EntityRepository repo && !repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>())
                return (long)e.PackedValue;
            return view.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e)
                ? view.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e).Value
                : (long)e.PackedValue;
        }

        /// <summary>The commander's subordinate in roster slot <paramref name="slot"/> (<see cref="Entity.Null"/> when there is none).</summary>
        public static Entity Subordinate(ISimulationView view, Entity commander, int slot)
        {
            if (slot < 0 || !view.HasComponent<UnitRoster>(commander)) return Entity.Null;
            ref readonly var roster = ref view.GetComponentRO<UnitRoster>(commander);
            return slot < roster.Count ? roster.SubordinateEntities[slot] : Entity.Null;
        }

        /// <summary>The picture's row index for <paramref name="unitNetId"/>, or -1.</summary>
        public static int RowOf(in CommandPicture picture, long unitNetId)
        {
            for (int i = 0; i < picture.Subordinates.Count; i++)
                if (picture.Subordinates.Items[i].UnitNetId == unitNetId) return i;
            return -1;
        }

        /// <summary>The row for <paramref name="unitNetId"/>, added when absent (full ⇒ -1). Makes the picture on first use.</summary>
        public static int EnsureRow(EntityRepository world, Entity commander, long unitNetId)
        {
            if (!world.IsComponentTypeRegistered<CommandPicture>()) return -1;
            if (!world.HasComponent<CommandPicture>(commander)) world.AddComponent(commander, new CommandPicture());
            ref var picture = ref world.GetComponentRW<CommandPicture>(commander);
            int row = RowOf(picture, unitNetId);
            if (row >= 0 || picture.Subordinates.Count >= CommandPicture.MaxSubordinates) return row;
            row = picture.Subordinates.Count++;
            picture.Subordinates.Items[row] = new SubordinateRow { UnitNetId = unitNetId };
            return row;
        }

        /// <summary>⭐ CC-B — the commander gave <paramref name="subordinate"/> a NEW order: forget its progress on the last one, so a guard
        /// waiting for "Set" waits for THIS order's Set (a stale Set from the previous leg would pass it at once).</summary>
        public static void OrderGiven(EntityRepository world, Entity commander, Entity subordinate)
        {
            int row = EnsureRow(world, commander, NetIdOf(world, subordinate));
            if (row < 0) return;
            ref var sub = ref world.GetComponentRW<CommandPicture>(commander).Subordinates.Items[row];
            sub.Progress = ProgressKind.NoProgress;
            sub.OrderedAt = Now(world);   // CE-2294: a PROGRESS observed before this order is the last order's
        }

        /// <summary>The latest progress the subordinate in <paramref name="slot"/> reported for its current order.</summary>
        public static ProgressKind ProgressOf(ISimulationView view, Entity commander, int slot)
        {
            var sub = Subordinate(view, commander, slot);
            if (sub.IsNull || !view.HasComponent<CommandPicture>(commander)) return ProgressKind.NoProgress;
            ref readonly var picture = ref view.GetComponentRO<CommandPicture>(commander);
            int row = RowOf(picture, NetIdOf(view, sub));
            return row < 0 ? ProgressKind.NoProgress : picture.Subordinates.Items[row].Progress;
        }

        /// <summary>Sim seconds now (the world clock), 0 when none.</summary>
        public static float Now(ISimulationView view)
            => view is EntityRepository repo && repo.HasSingletonUnmanaged<GlobalTime>() ? (float)repo.GetSingletonUnmanaged<GlobalTime>().TotalTime : 0f;
    }

    /// <summary>Params of <see cref="PictureConditions.ProgressReached"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ProgressReachedParams
    {
        /// <summary>Which subordinate: its slot in the commander's roster.</summary>
        public int Slot;
        public ProgressKind Progress;
    }

    /// <summary>Params of <see cref="PictureConditions.SubordinateColour"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SubordinateColourParams
    {
        public int Slot;
        /// <summary>True when the subordinate's strength is this colour or worse (Amber ⇒ Amber, Red or Black).</summary>
        public StatusColour AtLeast;
    }

    /// <summary>Params of <see cref="PictureConditions.SubordinateFresh"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SubordinateFreshParams
    {
        public int Slot;
        /// <summary>Heard from within this many sim seconds.</summary>
        public float WithinSeconds;
    }

    /// <summary>Params of <see cref="PictureConditions.ElementReported"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ElementReportedParams
    {
        /// <summary>The smallest element that counts (SizeUnknown = any).</summary>
        public ElementSize MinSize;
        /// <summary>Within this many metres of the commander (0 = anywhere).</summary>
        public float WithinMetres;
        /// <summary>Observed within this many sim seconds (0 = any age).</summary>
        public float FreshSeconds;
    }

    /// <summary>Params of <see cref="PictureConditions.PendingRequest"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PendingRequestParams
    {
        /// <summary>The kind asked for (NoRequest = any).</summary>
        public RequestKind Kind;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2292</c> CC-B (CB-46) — the PICTURE GUARDS: shared C# conditions any BTree / HSM / blueprint binds, reading only the
    /// commander's <see cref="CommandPicture"/> — the unit's knowledge, the same for the task and a reaction that pauses it (R-2042). A
    /// subordinate is named by its roster SLOT, so one play works for any platoon. 📄 Command Chain §6, §8 CC-B.
    /// </summary>
    public static class PictureConditions
    {
        /// <summary>The subordinate in <see cref="ProgressReachedParams.Slot"/> last reported exactly this progress for its current order
        /// (a new order resets it — <see cref="CommandPictureOps.OrderGiven"/>).</summary>
        [SharedAiCondition]
        public static bool ProgressReached(ref ProgressReachedParams p, Entity self, EntityRepository world)
            => p.Progress != ProgressKind.NoProgress && CommandPictureOps.ProgressOf(world, self, p.Slot) == p.Progress;

        /// <summary>The subordinate's reported strength is <see cref="SubordinateColourParams.AtLeast"/> or worse.</summary>
        [SharedAiCondition]
        public static bool SubordinateColour(ref SubordinateColourParams p, Entity self, EntityRepository world)
        {
            if (!TryRow(world, self, p.Slot, out var row)) return false;
            return row.Strength != StatusColour.ColourUnknown && row.Strength >= p.AtLeast;
        }

        /// <summary>The subordinate reported anything within <see cref="SubordinateFreshParams.WithinSeconds"/>; false = overdue.</summary>
        [SharedAiCondition]
        public static bool SubordinateFresh(ref SubordinateFreshParams p, Entity self, EntityRepository world)
            => TryRow(world, self, p.Slot, out var row) && row.LastReportAt > 0f
               && CommandPictureOps.Now(world) - row.LastReportAt <= p.WithinSeconds;

        /// <summary>The picture holds an element at least <see cref="ElementReportedParams.MinSize"/>, near enough and fresh enough.</summary>
        [SharedAiCondition]
        public static bool ElementReported(ref ElementReportedParams p, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<CommandPicture>(self)) return false;
            ref readonly var picture = ref world.GetComponentRO<CommandPicture>(self);
            var at = world.HasComponent<SimTransform>(self) ? world.GetComponentRO<SimTransform>(self).Position : Vector3.Zero;
            float now = CommandPictureOps.Now(world);
            for (int i = 0; i < picture.Elements.Count; i++)
            {
                ref readonly var e = ref picture.Elements.Items[i];
                if (e.Size < p.MinSize) continue;
                if (p.FreshSeconds > 0f && now - e.ObservedAt > p.FreshSeconds) continue;
                if (p.WithinMetres > 0f && Vector2.Distance(new Vector2(e.At.X, e.At.Y), new Vector2(at.X, at.Y)) > p.WithinMetres) continue;
                return true;
            }
            return false;
        }

        /// <summary>A subordinate's request of this kind waits for an answer.</summary>
        [SharedAiCondition]
        public static bool PendingRequest(ref PendingRequestParams p, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<CommandPicture>(self)) return false;
            ref readonly var picture = ref world.GetComponentRO<CommandPicture>(self);
            for (int i = 0; i < picture.Requests.Count; i++)
                if (p.Kind == RequestKind.NoRequest || picture.Requests.Items[i].Kind == p.Kind) return true;
            return false;
        }

        private static bool TryRow(EntityRepository world, Entity self, int slot, out SubordinateRow row)
        {
            row = default;
            var sub = CommandPictureOps.Subordinate(world, self, slot);
            if (sub.IsNull || !world.HasComponent<CommandPicture>(self)) return false;
            ref readonly var picture = ref world.GetComponentRO<CommandPicture>(self);
            int i = CommandPictureOps.RowOf(picture, CommandPictureOps.NetIdOf(world, sub));
            if (i < 0) return false;
            row = picture.Subordinates.Items[i];
            return true;
        }
    }
}
