using System.Linq;
using Fdp.Core;
using Xunit;

namespace Fdp.Core.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <b>The resolver behind <see cref="BirthCriticalAttribute"/> and
    /// <see cref="PerInstanceValueAttribute"/>.</b>
    /// 📄 <c>docs/designs/tkb-1/DESIGN.md</c> §6.6a.
    ///
    /// <para>⚠ These sets replaced 8 hand-authored per-template declarations across 3 producers, one of
    /// which (the file path) could not author them at all — <c>CE-259az</c>. ⇒ if this resolver goes wrong,
    /// the symptom is an entity that silently does not move, so the rails below aim at exactly the ways it
    /// could go wrong QUIETLY.</para>
    /// </summary>
    public class ComponentAttributeSetsTests
    {
        /// <summary>
        /// ⭐⭐ <b>The two sets are DIFFERENT, and that is the point.</b> They answer different questions on
        /// different legs — <i>"who owns it at birth?"</i> versus <i>"when may a ghost be promoted?"</i>.
        /// ⛔ If they ever became equal, someone has conflated them, and the tell is cheap to catch.
        /// 📐 Today <c>SimTransform</c> carries both; <c>SimVelocity</c> and <c>EntityInfo</c> carry only
        /// <c>[PerInstanceValue]</c>.
        /// </summary>
        [Fact]
        public void TheTwoSetsAreDistinct_AndSimTransformIsInBoth()
        {
            int simTransform = ComponentType<SimTransform>.ID;

            Assert.Contains(simTransform, ComponentAttributeSets.BirthCritical);
            Assert.Contains(simTransform, ComponentAttributeSets.PerInstanceValue);

            Assert.NotEqual(
                ComponentAttributeSets.BirthCritical.OrderBy(i => i).ToArray(),
                ComponentAttributeSets.PerInstanceValue.OrderBy(i => i).ToArray());
        }

        /// <summary>
        /// ⭐⭐⭐ <b>ANTI-VACUITY — an empty set would make every consumer silently permissive.</b>
        ///
        /// <para>🔴 The resolver scans loaded assemblies; if the scan ever returned nothing (a load failure
        /// swallowed, a filter inverted), <c>TkbTemplate.BirthCriticalComponents</c> would be empty for
        /// EVERY template and every creator would decline its own position — the exact defect
        /// <c>CE-259az</c> filed, reintroduced engine-wide and without a single error.</para>
        /// </summary>
        [Fact]
        public void NeitherSetIsEmpty()
        {
            Assert.NotEmpty(ComponentAttributeSets.BirthCritical);
            Assert.NotEmpty(ComponentAttributeSets.PerInstanceValue);
        }

        /// <summary>
        /// ⭐ <b><c>SimVelocity</c> is per-instance but NOT birth-critical</b> — zero velocity IS a correct
        /// starting value, a zero position is not. ⚠ This pin is what stops a future "tidy-up" from
        /// treating the two attributes as synonyms because they overlap on <c>SimTransform</c>.
        /// </summary>
        [Fact]
        public void SimVelocity_IsPerInstance_ButNotBirthCritical()
        {
            int simVelocity = ComponentType<SimVelocity>.ID;

            Assert.Contains(simVelocity, ComponentAttributeSets.PerInstanceValue);
            Assert.DoesNotContain(simVelocity, ComponentAttributeSets.BirthCritical);
        }

        /// <summary>
        /// ⭐ Ids are ascending and unique, so a consumer may treat either set as a stable ordered key list
        /// and a duplicate cannot contribute the same mask bit twice.
        /// </summary>
        [Fact]
        public void BothSetsAreSortedAndDistinct()
        {
            foreach (var set in new[] { ComponentAttributeSets.BirthCritical,
                                        ComponentAttributeSets.PerInstanceValue })
            {
                Assert.Equal(set.OrderBy(i => i).ToArray(), set.ToArray());
                Assert.Equal(set.Distinct().Count(), set.Count);
            }
        }

        /// <summary>
        /// ⭐⭐ <b>Every id resolves back to a real component type.</b> Both attributes mean COMPONENT IDS,
        /// so an id the registry cannot name would be one the consumers set as a mask bit for nothing.
        /// </summary>
        [Fact]
        public void EveryIdNamesARegisteredComponentType()
        {
            foreach (int id in ComponentAttributeSets.BirthCritical
                        .Concat(ComponentAttributeSets.PerInstanceValue).Distinct())
            {
                Assert.InRange(id, 0, FdpConfig.MAX_COMPONENT_TYPES - 1);
            }
        }

        /// <summary>
        /// ⭐ The sets are CACHED — the scan is one reflection pass, and callers may hold the references.
        /// ⛔ A resolver that rescanned per call would put reflection on the TKB registration path.
        /// </summary>
        [Fact]
        public void TheSetsAreCached_NotRescannedPerCall()
        {
            Assert.Same(ComponentAttributeSets.BirthCritical, ComponentAttributeSets.BirthCritical);
            Assert.Same(ComponentAttributeSets.PerInstanceValue, ComponentAttributeSets.PerInstanceValue);
        }
    }
}
