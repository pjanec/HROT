using System.Linq;
using System.Numerics;
using Hrot.Map.Common;
using Hrot.Map.Definitions.Tkb;

namespace Hrot.Map.Common.Tests
{
    public class HrotEnvironmentTests
    {
        private const double CoordinateToleranceDeg = 0.001;

        [Fact]
        public void CreateTkb_RegistersCatalogTemplates()
        {
            var tkb = HrotEnvironment.CreateTkb();

            Assert.True(tkb.TryGetByType(TkbEntityTypes.Tank_M1Abrams, out _));
            Assert.True(tkb.TryGetByType(TkbEntityTypes.Infantry_Rifleman, out _));
        }

        /// <summary>
        /// ⭐⭐⭐ <b><c>P3</c> step 0 — EVERY template in the shared catalogue declares
        /// <c>SimTransform</c> birth-critical.</b> 📄 <c>docs/DESIGN_Role_Affinity_Ownership.md</c> §3.1.
        ///
        /// <para>⭐⭐ <b>This rail is the deliverable, not the seeding.</b> Seeding is a one-off edit;
        /// the durable risk is the NEXT template, authored months from now by someone who has never
        /// read that design. ⛔ An unseeded template is <b>silent</b>: once steps 1–3 land, a Brain-role
        /// creator produces its <c>SimTransform</c> UNOWNED, every egress translator gates on
        /// <c>HasAuthority</c>, and the spawn coordinate is written correctly and never published —
        /// peers see the entity at the origin. ⇒ this turns "remember to declare it" into a gate.</para>
        ///
        /// <para>⚠ <b>What it deliberately does NOT cover, and the gap is real:</b> templates loaded
        /// from TKB FILES via <c>TkbDeserializer</c>, which builds a template purely from descriptor
        /// keys and declares no components at all. 📐 <c>HrotEnvironment.CreateTkb()</c> is the
        /// development default — the real system loads TKB from files synced to all nodes — so a
        /// file-loaded template is unseeded and this rail cannot see it. That is recorded as an open
        /// item on step 0 in the design rather than papered over here.</para>
        /// </summary>
        [Fact]
        public void CreateTkb_EveryTemplateDeclaresSimTransformBirthCritical()
        {
            var tkb = HrotEnvironment.CreateTkb();
            int simTransformId =
                Fdp.Core.ComponentTypeRegistry.GetOrRegisterManaged(typeof(Fdp.Core.SimTransform));

            var all = tkb.GetAll().ToList();

            // ⛔ Anti-vacuity: an empty catalogue would make every assertion below pass.
            Assert.True(all.Count >= 5,
                $"the shared catalogue returned {all.Count} templates, so this rail asserts nothing. " +
                "Either CreateTkb stopped registering, or this rail is aimed at the wrong database.");

            var missing = all
                .Where(t => !t.BirthCriticalComponents.Contains(simTransformId))
                .Select(t => $"{t.Name} ({t.TkbType})")
                .ToList();

            Assert.True(missing.Count == 0,
                "these catalogue templates do not declare SimTransform birth-critical, so once P3 " +
                "steps 1-3 land their creator will write a spawn position it can never publish and " +
                "every peer will see the entity at the origin: " + string.Join(" · ", missing) +
                ". Add template.AddBirthCriticalComponent<SimTransform>() where the template is " +
                "authored (docs/DESIGN_Role_Affinity_Ownership.md §3.1).");
        }

        [Fact]
        public void CreateGeoTransform_UsesBerlinOrigin()
        {
            var transform = HrotEnvironment.CreateGeoTransform();

            var (lat, lon, _) = transform.ToGeodetic(Vector3.Zero);

            Assert.InRange(lat, 52.52 - CoordinateToleranceDeg, 52.52 + CoordinateToleranceDeg);
            Assert.InRange(lon, 13.405 - CoordinateToleranceDeg, 13.405 + CoordinateToleranceDeg);
        }

        [Fact]
        public void CreateParticipant_UsesProvidedDomainId()
        {
            using var participant = HrotEnvironment.CreateParticipant(10);

            Assert.Equal(10u, participant.DomainId);
        }
    }
}