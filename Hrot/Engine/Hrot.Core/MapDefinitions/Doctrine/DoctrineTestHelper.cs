using System;
using System.Reflection;

namespace Hrot.Map.Definitions.Doctrine
{
    /// <summary>
    /// Test utility for resolving behavior-ID strings from
    /// <see cref="DoctrineContractAttribute"/>-decorated DTOs.
    /// Avoids hardcoding behavior-ID strings in unit tests.
    /// </summary>
    public static class DoctrineTestHelper
    {
        /// <summary>
        /// Returns the <see cref="DoctrineContractAttribute.BehaviorId"/> declared on
        /// <typeparamref name="TDto"/>.
        /// </summary>
        /// <typeparam name="TDto">A DTO type decorated with <see cref="DoctrineContractAttribute"/>.</typeparam>
        /// <exception cref="InvalidOperationException">
        /// Thrown when <typeparamref name="TDto"/> is missing <see cref="DoctrineContractAttribute"/>.
        /// </exception>
        public static string GetBehaviorId<TDto>()
        {
            var attr = typeof(TDto).GetCustomAttribute<DoctrineContractAttribute>()
                ?? throw new InvalidOperationException(
                    $"{typeof(TDto).Name} is missing [DoctrineContractAttribute]");
            return attr.BehaviorId;
        }
    }
}
