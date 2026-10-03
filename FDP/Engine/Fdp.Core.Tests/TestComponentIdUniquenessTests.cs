using System;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Xunit;

namespace Fdp.Core.Tests
{
    /// <summary>
    /// ⭐ <c>CE-2045</c> — no two test fixtures in this assembly may claim the same <c>[ComponentId]</c>.
    /// <para>🔴 The registry is process-wide and this assembly runs serially, so two fixtures sharing an id made whichever
    /// registered SECOND throw <c>Component ID collision</c> — depending on test order, which shifts between runs. That is
    /// what made <c>CheckpointIOWorkerTests</c>, <c>LifeCycleSchemaTests</c> and the benchmarks look flaky (ids 205, 240, 200).</para>
    /// <para>⚠ <c>ComponentIdAttributeTests</c> is exempt: its nested pairs collide ON PURPOSE to test the collision check, and it
    /// clears the registry around each test.</para>
    /// </summary>
    public class TestComponentIdUniquenessTests
    {
        [Fact]
        public void EveryFixtureComponentId_IsUnique()
        {
            var duplicates = typeof(TestComponentIdUniquenessTests).Assembly.GetTypes()
                .Where(t => t.DeclaringType?.Name != "ComponentIdAttributeTests")
                .Select(t => (Type: t, Attr: t.GetCustomAttribute<ComponentIdAttribute>(inherit: false)))
                .Where(x => x.Attr != null)
                .GroupBy(x => x.Attr!.Id)
                .Where(g => g.Count() > 1)
                .Select(g => $"{g.Key}: {string.Join(", ", g.Select(x => x.Type.FullName))}")
                .ToList();

            Assert.True(duplicates.Count == 0, "fixtures sharing a [ComponentId]:\n" + string.Join("\n", duplicates));
        }
    }
}
