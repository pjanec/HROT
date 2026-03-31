using System.Collections.Generic;
using System.Reflection;
using Hrot.Map.Definitions;

namespace Hrot.Map.Common.Tests
{
    public class ComponentIdTests
    {
        /// <summary>
        /// Every <c>const byte</c> field on <see cref="HrotComponentIds"/> must have a
        /// unique value. A duplicate would mean two component types share the same ID,
        /// causing silent data corruption in the ECS.
        /// </summary>
        [Fact]
        public void HrotComponentIds_NoDuplicates()
        {
            var fields = typeof(HrotComponentIds)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(byte))
                .ToList();

            var seen = new Dictionary<byte, string>();
            foreach (var field in fields)
            {
                var value = (byte)field.GetRawConstantValue()!;
                if (seen.TryGetValue(value, out var existing))
                    Assert.Fail($"Duplicate HrotComponentId value {value}: '{existing}' and '{field.Name}'");
                seen[value] = field.Name;
            }
        }
    }
}
