using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Fdp.Toolkit.Behavior.TacticalOrderMapper
{
    /// <summary>
    /// Dictionary-backed registry that holds <see cref="ITacticalOrderMapper"/>
    /// implementations, keyed by <see cref="ITacticalOrderMapper.TargetIntentId"/>.
    ///
    /// <para>
    /// Analogous to the existing <c>DoctrineRegistry</c>: all registrations must
    /// complete before the simulation loop starts; the registry is read-only during
    /// simulation frames.
    /// </para>
    ///
    /// <para>
    /// An empty registry is valid — <c>TacticalIntentResolutionSystem</c> will
    /// simply use the pass-through fallback for every intent it receives.
    /// Concrete mapper implementations are registered by the composition root in
    /// Phase 6 of the Tactical Intent workstream.
    /// </para>
    /// </summary>
    public sealed class TacticalIntentMapperRegistry
    {
        private readonly Dictionary<string, ITacticalOrderMapper> _mappers = new();

        /// <summary>
        /// Registers a mapper.  The mapper's <see cref="ITacticalOrderMapper.TargetIntentId"/>
        /// must be unique within this registry.
        /// </summary>
        /// <param name="mapper">The mapper to register.  Must not be <c>null</c>.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="mapper"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a mapper with the same <see cref="ITacticalOrderMapper.TargetIntentId"/>
        /// has already been registered.
        /// </exception>
        public void Register(ITacticalOrderMapper mapper)
        {
            if (mapper == null) throw new ArgumentNullException(nameof(mapper));
            if (_mappers.ContainsKey(mapper.TargetIntentId))
                throw new InvalidOperationException(
                    $"A mapper for intent '{mapper.TargetIntentId}' is already registered. " +
                    $"Each TargetIntentId must be unique within a TacticalIntentMapperRegistry.");
            _mappers[mapper.TargetIntentId] = mapper;
        }

        /// <summary>
        /// Looks up a mapper by intent identifier.
        /// </summary>
        /// <param name="intentId">The intent identifier to look up.</param>
        /// <param name="mapper">
        /// When this method returns <c>true</c>, contains the registered mapper;
        /// otherwise <c>null</c>.
        /// </param>
        /// <returns>
        /// <c>true</c> if a mapper is registered for <paramref name="intentId"/>;
        /// <c>false</c> otherwise.
        /// </returns>
        public bool TryGetMapper(string intentId,
                                 [NotNullWhen(true)] out ITacticalOrderMapper? mapper)
        {
            return _mappers.TryGetValue(intentId, out mapper);
        }
    }
}
