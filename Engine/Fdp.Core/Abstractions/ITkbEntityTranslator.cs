using System;
using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Interfaces
{
    /// <summary>
    /// Projects N TKB descriptor DTOs into M ECS components on a live entity.
    /// Mirrors IDescriptorTranslator for TKB content; same N:M projection mechanics.
    /// </summary>
    public interface ITkbEntityTranslator
    {
        /// <summary>
        /// Returns the CLR types of TKB descriptor DTOs this translator consumes.
        /// The pipeline uses this to track which descriptors have been projected.
        /// </summary>
        IEnumerable<Type> GetConsumedDescriptors();

        /// <summary>
        /// Projects data from <paramref name="template"/> into ECS components on
        /// <paramref name="entity"/>. Implementations MUST call
        /// <c>repo.IsComponentTypeRegistered&lt;T&gt;()</c> before every
        /// <c>repo.AddComponent&lt;T&gt;()</c> call.
        /// </summary>
        void Inject(EntityRepository repo, Entity entity, TkbTemplate template);
    }
}
