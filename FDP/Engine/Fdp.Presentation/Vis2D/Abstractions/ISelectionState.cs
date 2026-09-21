using Fdp.Core;
using System.Collections.Generic;

namespace Fdp.Toolkit.Vis2D.Abstractions
{
    /// <summary>
    /// The one selection surface every 2-D map host presents to panels, tools and systems.
    ///
    /// <para>⭐⭐⭐ <b>This is a VIEW, not a store</b> — 📄 <c>docs/UX/UX_Feature_Selection.md</c> §2.7
    /// (<c>UXI-11</c>). The truth lives wherever the host keeps it: on an ECS host it is the
    /// <c>SelectionState</c> component and the view is a read-through; on a host with no world
    /// (ExCon, the CarKinem example) it is that host's own selection manager. ⛔ A consumer must
    /// never hold a concrete implementation in order to reach a member — that coupling is what
    /// <c>S-1</c> exists to remove.</para>
    ///
    /// <para>⚠ <b>The mutators are slice <c>S-1</c>'s addition</b> (§2.7.5). They exist so that
    /// multi-select has a vocabulary at all — before them the only way to change the selection was
    /// to assign <see cref="PrimarySelected"/>, which every implementation interpreted as
    /// <em>"replace the whole selection with this one entity"</em>. 📌 <c>S-2</c> makes
    /// <c>SelectionRequestSystem</c> the only writer, at which point call sites publish a request
    /// instead of calling these directly; they remain the seam that system writes through.</para>
    /// </summary>
    public interface ISelectionState
    {
        bool IsSelected(Entity entity);
        IReadOnlyCollection<Entity> SelectedEntities { get; }

        /// <summary>
        /// The primary (first) entity of the selection.
        /// ⚠ <b>Assigning REPLACES the whole selection</b> with that single entity — or clears it
        /// when set to <c>null</c>. That is the long-standing contract of every implementation and
        /// the reason <see cref="Add"/> / <see cref="SetMultiple"/> had to be added rather than
        /// overloading this setter.
        /// </summary>
        Entity? PrimarySelected { get; set; }

        /// <summary>
        /// The entity under the cursor. ⚠ <b>View-local everywhere</b>, including on ECS hosts:
        /// hover is per-viewport transient state and has no component backing it.
        /// </summary>
        Entity? HoveredEntity { get; set; }

        /// <summary>
        /// A monotonically-increasing change token. Bumps whenever the selection this view reports
        /// differs from what it last reported.
        ///
        /// <para>⭐⭐ <b>It is per-OBSERVER, not global</b> — two views over one truth keep independent
        /// counters, which is exactly what a poller needs. 📌 <c>EditorStrideSubsystem.SyncSelection2D3D</c>
        /// uses it to drive the 2-D↔3-D sync one direction per frame without feedback bounce.</para>
        /// </summary>
        int Version { get; }

        /// <summary>Adds <paramref name="entity"/> to the selection and makes it primary. Idempotent.</summary>
        void Add(Entity entity);

        /// <summary>
        /// Removes <paramref name="entity"/> from the selection. When it was primary, another
        /// selected entity becomes primary (or none remains). A no-op when it is not selected.
        /// </summary>
        void Remove(Entity entity);

        /// <summary>
        /// Replaces the selection with exactly <paramref name="entities"/>. The first becomes
        /// primary. An empty set is equivalent to <see cref="Clear"/>.
        /// </summary>
        void SetMultiple(IReadOnlyCollection<Entity> entities);

        /// <summary>Clears the selection entirely, primary included.</summary>
        void Clear();
    }
}
