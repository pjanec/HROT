using System;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐⭐ <c>Q94 A-3</c> (R-3018; 📄 docs/blueprints/Architect_Question_94_Brain_Geometry_Sites.md §4) — the weapon brain's SHOT LEDGER: the
    /// brain only ASKS for a round (<see cref="Ask"/>, the id rides <see cref="WeaponFireIntent.RequestId"/>); the muscle fires it or refuses it,
    /// and its <see cref="WeaponFireNotification"/> — matched by that id — is what SPENDS the round (<see cref="Answer"/>). What is asked and
    /// not yet answered is RESERVED: <see cref="Magazine.Ready"/> counts it, so a burst never asks for more than the weapon holds.
    /// A refusal holds the mount <see cref="RefusalHoldSeconds"/> with its reason (<see cref="Held"/>) instead of asking again every frame;
    /// an answer that never comes clears after <see cref="TimeoutSeconds"/>.
    /// <para>The ledger is four fields of <see cref="WeaponState"/> — the one counter stays the brain's (W-4), on the entity that holds it.
    /// Ids are per mount and rise by one; the pending count is <c>LastRequestId − LastAnsweredId</c>. ⛔ A turret's rounds are not asked
    /// one by one (the muscle's <c>RoundsFired</c> report spends them, W-4) and a dispenser's release is never answered — neither asks here.</para>
    /// </summary>
    public static class ShotLedger
    {
        /// <summary>An ask unanswered this long is dropped (a lost request or answer): nothing spent, the reservation freed.</summary>
        public const float TimeoutSeconds = 2f;

        /// <summary>How long a refusal holds the mount before it asks again (the muscle's answer to the same shot would not change sooner).</summary>
        public const float RefusalHoldSeconds = 1f;

        /// <summary>Asks in flight per mount at most — beyond it the mount waits for answers.</summary>
        public const int MaxPending = 8;

        /// <summary>Rounds asked and not yet answered.</summary>
        public static int Pending(in WeaponState w) => (int)unchecked(w.LastRequestId - w.LastAnsweredId);

        /// <summary>One round asked: reserved now, spent when the muscle says it flew. Returns the id the intent carries.</summary>
        public static uint Ask(ref WeaponState w)
        {
            if (Pending(w) == 0) w.PendingSeconds = 0f;
            return unchecked(++w.LastRequestId);
        }

        /// <summary>True while the last refusal still holds this mount; <paramref name="why"/> is the muscle's reason.</summary>
        public static bool Held(in WeaponState w, out FireRefusal why)
        {
            why = w.Refusal;
            return w.RefusalHoldSeconds > 0f && w.Refusal != FireRefusal.None;
        }

        /// <summary>The frame's clock on the ledger: an old ask times out, a refusal hold runs down (the dispatcher's drain, beside the cooldown).</summary>
        public static void Tick(ref WeaponState w, float dt)
        {
            if (w.RefusalHoldSeconds > 0f) w.RefusalHoldSeconds = MathF.Max(0f, w.RefusalHoldSeconds - dt);
            if (Pending(w) <= 0) return;
            w.PendingSeconds += dt;
            if (w.PendingSeconds >= TimeoutSeconds)
            {
                w.LastAnsweredId = w.LastRequestId;
                w.PendingSeconds = 0f;
            }
        }

        /// <summary>
        /// The muscle's answer to ask <paramref name="requestId"/> of the mount <paramref name="mount"/>: a fired round is spent (the total, the
        /// magazine, the loaded type); a refusal spends nothing and holds the mount with its reason. An answer that is not newer than the last one
        /// (a duplicate, a late one after a newer answer or the timeout) or names an id never asked changes nothing. Returns whether it counted.
        /// </summary>
        public static bool Answer(EntityRepository world, Entity mount, ref WeaponState w, uint requestId, FireRefusal refusal)
        {
            if (requestId == 0) return false;
            int ahead = (int)unchecked(requestId - w.LastAnsweredId);
            int asked = (int)unchecked(w.LastRequestId - requestId);
            if (ahead <= 0 || asked < 0) return false;
            w.LastAnsweredId = requestId;   // the asks before it that never got an answer are dropped with it
            w.PendingSeconds = 0f;
            if (refusal == FireRefusal.None)
            {
                Magazine.Spend(ref w);
                AmmoChoice.SpendLoaded(world, mount);
            }
            else
            {
                // the round never left: its cooldown goes with it, and the hold (with the muscle's reason) takes its place
                w.Refusal = refusal;
                w.RefusalHoldSeconds = RefusalHoldSeconds;
                w.CooldownSecondsRemaining = 0f;
            }
            return true;
        }

        /// <summary>
        /// The frame's answers on this node: every <see cref="WeaponFireNotification"/> carrying a request id is matched to the weapon that
        /// asked — <see cref="WeaponFireNotification.Shooter"/>'s own <see cref="WeaponState"/> for mount 0, else its mount child of that
        /// index — and <see cref="Answer"/>ed. A notification nobody here asked for (another node's brain, an untracked round) changes nothing.
        /// Called once a frame by <c>WeaponDispatcherSystem</c>, before the executors run.
        /// </summary>
        public static void Settle(EntityRepository world)
        {
            if (!world.Bus.IsRegistered<WeaponFireNotification>()) return;
            foreach (var n in world.Bus.Read<WeaponFireNotification>())
            {
                if (n.RequestId == 0 || !world.IsAlive(n.Shooter)) continue;
                var mount = MountOf(world, n.Shooter, n.WeaponIndex);
                if (mount.IsNull) continue;
                Answer(world, mount, ref world.GetComponentRW<WeaponState>(mount), n.RequestId, n.Refusal);
            }
        }

        /// <summary>The entity holding the <see cref="WeaponState"/> of <paramref name="shooter"/>'s mount <paramref name="index"/>
        /// (the executors' rule: mount 0 on the shooter, a mount child otherwise); <see cref="Entity.Null"/> when there is none.</summary>
        public static Entity MountOf(EntityRepository world, Entity shooter, int index)
        {
            if (index == 0) return world.HasComponent<WeaponState>(shooter) ? shooter : Entity.Null;
            if (!world.IsComponentTypeRegistered<WeaponMountInfo>()) return Entity.Null;
            Span<Entity> mounts = stackalloc Entity[8];
            int count = WeaponMountQuery.EnumerateMounts(world, shooter, mounts);
            for (int i = 0; i < count; i++)
                if (world.HasComponent<WeaponMountInfo>(mounts[i]) && world.GetComponentRO<WeaponMountInfo>(mounts[i]).MountIndex == index
                    && world.HasComponent<WeaponState>(mounts[i]))
                    return mounts[i];
            return Entity.Null;
        }
    }
}
