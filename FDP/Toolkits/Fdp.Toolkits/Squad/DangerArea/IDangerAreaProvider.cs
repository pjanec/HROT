using System;
using Fdp.Core;

namespace Fdp.Toolkit.Squad.DangerArea
{
    /// <summary>
    /// Contract for a danger-area sensor that populates a <see cref="DangerAreaDescriptor"/> buffer
    /// for a given squad commander entity.
    /// </summary>
    /// <remarks>
    /// Implementations are injected into the squad HSM at construction time.
    /// The production implementation queries the navmesh extension; <see cref="Fake.FakeDangerAreaProvider"/>
    /// is used in tests.
    /// </remarks>
    public interface IDangerAreaProvider
    {
        /// <summary>
        /// Refreshes the danger-area buffer for the squad led by <paramref name="squadCommander"/>.
        /// </summary>
        /// <param name="repo">The ECS entity repository.</param>
        /// <param name="squadCommander">The commanding entity whose route is inspected.</param>
        /// <param name="dest">Caller-supplied output buffer (typically stack-allocated).</param>
        /// <param name="count">Number of valid descriptors written to <paramref name="dest"/>.</param>
        void Refresh(EntityRepository repo, Entity squadCommander,
                     Span<DangerAreaDescriptor> dest, out int count);
    }
}
