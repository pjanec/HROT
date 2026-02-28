using System;
using System.Collections.Generic;
using Bagira.SimHost.Translators;
using Fdp.Interfaces;

namespace Bagira.SimHost.Tests
{
    /// <summary>
    /// Structural tests for the SimHost EntityMaster egress translator.
    /// </summary>
    public class EntityMasterTranslatorRegistrationTests
    {
        /// <summary>
        /// EntityMasterEgressTranslator must be assignable to
        /// <see cref="IDescriptorTranslator"/> so it can be inserted into
        /// the <c>translators</c> list consumed by <c>CycloneNetworkModule</c>.
        /// </summary>
        [Fact]
        public void EntityMasterEgressTranslator_ImplementsIDescriptorTranslator()
        {
            // Verify at the type/reflection level — no live DDS participant required.
            var translatorType = typeof(EntityMasterEgressTranslator);

            Assert.True(
                typeof(IDescriptorTranslator).IsAssignableFrom(translatorType),
                "EntityMasterEgressTranslator must implement IDescriptorTranslator.");
        }

        /// <summary>
        /// EntityMasterEgressTranslator must be a concrete (non-generic) type.
        /// </summary>
        [Fact]
        public void EntityMasterEgressTranslator_IsConcreteType()
        {
            var translatorType = typeof(EntityMasterEgressTranslator);

            Assert.False(translatorType.IsGenericTypeDefinition,
                "EntityMasterEgressTranslator must be a non-generic concrete type.");
        }
    }
}
