using System;
using System.Linq;
using Fdp.ModuleHost.Abstractions;
using Hrot.Common.Systems;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <b>Q65 obstacle ① — the REQUEST TIER must live in a SHARED assembly.</b>
    ///
    /// <para>🔒 <b>The ruling</b> (user, <c>2026-08-31</c>): <i>"the shared code for entity creation support
    /// should not restrict any ECS enabled node from creating own networked entities … no exceptions, not
    /// removing capabilities by design."</i> 📐 Q65 measured that <c>isDefaultProcessor</c> is a BROADCAST
    /// TIEBREAKER, not an authority gate — <c>CreateEntityRequestSystem</c> processes a request targeted at
    /// the local node regardless of it — so every ECS node should register this system.</para>
    ///
    /// <para>⛔ <b>While these three types lived in <c>Hrot.CGF</c>, "every node registers it" was not even
    /// EXPRESSIBLE</b>: only CGF, the Editor and the Stride editor could construct them. They moved to
    /// <c>Hrot.Common</c> on <c>2026-08-31</c>. ⭐ These rails exist so nobody moves them back into a host
    /// assembly, which would silently re-impose the restriction the ruling forbids.</para>
    ///
    /// <para>⚠ <b>Why <c>Hrot.Common</c> and not <c>Hrot.Core</c></b> — Q65 §5.4 originally said
    /// <c>Hrot.Core</c> and that was WRONG: <c>CreateEntityRequestSystem</c> constructs
    /// <c>Hrot.Common.Serializers.InitialUnitSubordinateIntent</c>, and <c>Hrot.Common</c> already references
    /// <c>Hrot.Core</c>, so <c>Hrot.Core → Hrot.Common</c> would be a cycle.</para>
    ///
    /// <para>📄 <c>docs/blueprints/Architect_Question_65_Entity_Genesis_Uniformity.md</c> §5.4 · §6.</para>
    /// </summary>
    public class RequestTierPlacementRails
    {
        public static TheoryData<Type> RequestTierTypes => new()
        {
            typeof(CreateEntityRequestSystem),
            typeof(DeleteEntityRequestSystem),
            typeof(EntityRequestFinalizationSystem),
        };

        /// <summary>
        /// ⛔⛔ <b>None of the request-tier types may live in a HOST assembly.</b> A host assembly is one only
        /// some nodes reference — <c>Hrot.CGF</c>, <c>Hrot.SimHost</c>, <c>Hrot.IG</c>, <c>Hrot.Editor</c> —
        /// and putting a universally-needed system in one is exactly the defect Q65 obstacle ① removed.
        /// </summary>
        [Theory]
        [MemberData(nameof(RequestTierTypes))]
        public void RequestTierTypes_DoNotLiveInAHostAssembly(Type t)
        {
            string asm = t.Assembly.GetName().Name!;

            Assert.DoesNotContain("Hrot.CGF", asm);
            Assert.DoesNotContain("Hrot.SimHost", asm);
            Assert.DoesNotContain("Hrot.IG", asm);
            Assert.DoesNotContain("Hrot.Editor", asm);
            Assert.Equal("Hrot.Common", asm);
        }

        /// <summary>
        /// ⭐ <b>Nor may their NAMESPACE still say CGF.</b> A type every node registers must not be named as
        /// though it belonged to one host — that wording is what kept the "only CGF creates entities"
        /// misconception alive, and Q65 exists to kill it.
        /// </summary>
        [Theory]
        [MemberData(nameof(RequestTierTypes))]
        public void RequestTierTypes_NamespaceDoesNotClaimAHost(Type t)
        {
            Assert.DoesNotContain("CGF", t.Namespace!);
            Assert.Equal("Hrot.Common.Systems", t.Namespace);
        }

        /// <summary>
        /// ⭐⭐ <b>The expressibility claim, made checkable:</b> each type is public with a public constructor,
        /// so ANY host that references <c>Hrot.Common</c> can construct it. ⛔ If one of these became
        /// <c>internal</c>, "every node registers it" would quietly stop being true outside
        /// <c>Hrot.Common</c>'s <c>InternalsVisibleTo</c> list.
        /// </summary>
        [Theory]
        [MemberData(nameof(RequestTierTypes))]
        public void RequestTierTypes_ArePubliclyConstructible(Type t)
        {
            Assert.True(t.IsPublic, $"{t.Name} must be public so any host can construct it.");
            Assert.NotEmpty(t.GetConstructors());   // GetConstructors() returns public ctors only
        }

        /// <summary>
        /// ⭐ They are ECS systems, so a host schedules them the same way everywhere. This is the property the
        /// <c>EntityCreationPack</c> (DESIGN step 3) will rely on when it hands them to the kernel.
        /// </summary>
        [Theory]
        [MemberData(nameof(RequestTierTypes))]
        public void RequestTierTypes_AreEcsModuleSystems(Type t)
        {
            Assert.True(typeof(IEcsModuleSystem).IsAssignableFrom(t),
                $"{t.Name} must implement IEcsModuleSystem so every host schedules it identically.");
        }
    }
}
