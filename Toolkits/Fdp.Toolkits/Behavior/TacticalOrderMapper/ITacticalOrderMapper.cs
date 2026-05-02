using Fdp.Core;
using Fdp.Toolkit.Behavior.Events;

namespace Fdp.Toolkit.Behavior.TacticalOrderMapper
{
    /// <summary>
    /// Stateless translation rule that maps a generic tactical intent identifier
    /// to a concrete <see cref="AssignBehaviorEvent"/> for a specific entity.
    ///
    /// <para>
    /// Implementations receive the full <see cref="EntityRepository"/> so they can
    /// query capability components, <c>TkbIdentity</c>, or any other ECS state
    /// needed to select the correct behavior and format its DTO.  Stateful
    /// dependencies (e.g. a network entity map) may be injected via the
    /// implementation's constructor.
    /// </para>
    ///
    /// <para>
    /// Implementations must be <b>thread-safe</b> for reads after registration;
    /// all mappers are registered at startup before the simulation loop starts.
    /// </para>
    /// </summary>
    public interface ITacticalOrderMapper
    {
        /// <summary>
        /// The intent identifier this mapper handles, e.g. <c>"DefendArea"</c>.
        /// Must be unique across all mappers registered in a
        /// <see cref="TacticalIntentMapperRegistry"/>.
        /// </summary>
        string TargetIntentId { get; }

        /// <summary>
        /// Attempts to translate the intent into a concrete behavior assignment.
        /// </summary>
        /// <param name="self">The entity receiving the tactical order.</param>
        /// <param name="repo">
        /// The <see cref="EntityRepository"/> on the receiving node.  Use this to
        /// query capability components or any ECS state required for translation.
        /// </param>
        /// <param name="jsonParams">
        /// The serialised JSON parameter payload from the original intent event.
        /// </param>
        /// <param name="assignment">
        /// When this method returns <c>true</c>, contains the fully populated
        /// <see cref="AssignBehaviorEvent"/> ready for publication.  Undefined
        /// when this method returns <c>false</c>.
        /// </param>
        /// <returns>
        /// <c>true</c> if translation succeeded and <paramref name="assignment"/>
        /// is ready; <c>false</c> if the mapper cannot handle the entity at this
        /// time (e.g. required capability component absent).
        /// </returns>
        bool TryMap(Entity self, EntityRepository repo, string jsonParams,
                    out AssignBehaviorEvent assignment);
    }
}
