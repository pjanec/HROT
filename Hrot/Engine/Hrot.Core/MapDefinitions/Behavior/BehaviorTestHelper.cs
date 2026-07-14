using System;
using System.Reflection;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// Test utility for resolving behavior-ID strings from
    /// <see cref="BehaviorContractAttribute"/>-decorated DTOs.
    /// Avoids hardcoding behavior-ID strings in unit tests.
    /// </summary>
    public static class BehaviorTestHelper
    {
        /// <summary>
        /// Returns the <see cref="BehaviorContractAttribute.BehaviorName"/> declared on
        /// <typeparamref name="TDto"/>.
        /// </summary>
        /// <typeparam name="TDto">A DTO type decorated with <see cref="BehaviorContractAttribute"/>.</typeparam>
        /// <exception cref="InvalidOperationException">
        /// Thrown when <typeparamref name="TDto"/> is missing <see cref="BehaviorContractAttribute"/>.
        /// </exception>
        public static string GetBehaviorName<TDto>()
        {
            var attr = typeof(TDto).GetCustomAttribute<BehaviorContractAttribute>()
                ?? throw new InvalidOperationException(
                    $"{typeof(TDto).Name} is missing [BehaviorContractAttribute]");
            return attr.BehaviorName;
        }
    }
}
