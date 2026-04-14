using System;
using System.Collections.Generic;

namespace Fdp.Toolkit.Scenario
{
    /// <summary>
    /// Fluent builder that assembles a <see cref="ScenarioSerializer"/> for a specific
    /// subsystem type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Usage:
    /// <code>
    /// var serializer = new ScenarioSerializerBuilder("Hrot.CGF")
    ///     .RegisterTranslator(new MissileOrdnanceTranslator())
    ///     .Build();
    /// </code>
    /// </para>
    /// <para>
    /// <see cref="Build"/> compiles <see cref="FdpAutoSerializer"/> delegates for every
    /// registered saveable component type and freezes the translator list.  The builder
    /// instance should not be reused after <see cref="Build"/> is called.
    /// </para>
    /// </remarks>
    public sealed class ScenarioSerializerBuilder
    {
        private readonly string _subsystemType;
        private readonly List<IEntityScenarioTranslator> _translators = new();
        private bool _built;

        /// <summary>
        /// Creates a builder for the supplied subsystem type.
        /// </summary>
        /// <param name="subsystemType">
        /// Human-readable subsystem identifier written into every saved scenario's
        /// <c>Header.SubsystemType</c> field (e.g. <c>"Hrot.CGF"</c>).
        /// On <c>Deserialize</c>, the <see cref="ScenarioSerializer"/> peeks this
        /// value and skips the file if it does not match.
        /// </param>
        public ScenarioSerializerBuilder(string subsystemType)
        {
            if (string.IsNullOrWhiteSpace(subsystemType))
                throw new ArgumentNullException(nameof(subsystemType));
            _subsystemType = subsystemType;
        }

        /// <summary>
        /// Registers a custom N:M translator.  Translators are evaluated in registration
        /// order; each translator's consumed component bits are removed from the
        /// auto-serializer fallback mask so they are never emitted twice.
        /// </summary>
        /// <returns>This builder (fluent API).</returns>
        public ScenarioSerializerBuilder RegisterTranslator(IEntityScenarioTranslator translator)
        {
            if (_built)
                throw new InvalidOperationException(
                    "Cannot register translators after Build() has been called.");
            _translators.Add(translator ?? throw new ArgumentNullException(nameof(translator)));
            return this;
        }

        /// <summary>
        /// Compiles the <see cref="FdpAutoSerializer"/> and returns a frozen
        /// <see cref="ScenarioSerializer"/>.
        /// </summary>
        public ScenarioSerializer Build()
        {
            if (_built)
                throw new InvalidOperationException("Build() may only be called once per builder.");

            _built = true;

            var autoSerializer = new FdpAutoSerializer();
            autoSerializer.Build();

            return new ScenarioSerializer(_subsystemType, _translators.ToArray(), autoSerializer);
        }
    }
}
