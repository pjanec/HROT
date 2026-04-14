using System;
using System.Collections.Generic;
using System.Reflection;
using Hrot.Map.Definitions;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="HrotComponentIds"/> (MOD1-P5T1).
    /// </summary>
    public class HrotComponentIdsTests
    {
        /// <summary>
        /// All constants in <see cref="HrotComponentIds"/> must be unique.
        /// Duplicate IDs would cause silent ECS component collisions at runtime.
        /// </summary>
        [Fact]
        public void HrotComponentIds_NoDuplicates()
        {
            var fields = typeof(HrotComponentIds).GetFields(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

            var seen   = new Dictionary<byte, string>();
            foreach (var field in fields)
            {
                if (field.FieldType != typeof(byte)) continue;

                var id   = (byte)field.GetValue(null)!;
                var name = field.Name;

                Assert.False(seen.ContainsKey(id),
                    $"Duplicate HrotComponentId: {id} is used by both '{seen.GetValueOrDefault(id)}' and '{name}'.");

                seen[id] = name;
            }
        }

        /// <summary>
        /// All <see cref="HrotComponentIds"/> constants must be in the application-level range (160–199).
        /// FDP toolkit classes must not appear in this registry.
        /// </summary>
        [Fact]
        public void HrotComponentIds_AllInApplicationRange()
        {
            var fields = typeof(HrotComponentIds).GetFields(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

            foreach (var field in fields)
            {
                if (field.FieldType != typeof(byte)) continue;
                var id   = (byte)field.GetValue(null)!;
                var name = field.Name;
                Assert.True(id >= 160 && id <= 199,
                    $"HrotComponentIds.{name} = {id} is outside the application-level range 160–199.");
            }
        }
    }
}
