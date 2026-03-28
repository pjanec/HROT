using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Fdp.Examples.NetworkDemo.Tests
{
    internal static class TestAssemblyInit
    {
        /// <summary>
        /// Runs before any type initializer or test code in this assembly.
        /// Points CYCLONEDDS_URI at the local test config so that CycloneDDS
        /// picks up fast-discovery settings (SPDPInterval=100ms, loopback
        /// interface) the first time a DdsParticipant is created.
        /// </summary>
        [ModuleInitializer]
        internal static void SetupCycloneDds()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CYCLONEDDS_URI")))
                return; // honour an explicit override in CI / developer environment

            // Inline XML: only reduce SPDP interval — keep the existing interface selection
            // (usually Ethernet with multicast enabled) so that endpoint discovery still works.
            // SPDPInterval=100ms means two participants on the same machine discover each
            // other within ~100 ms instead of the default ~2.5 s (LeaseDuration/4=10s/4).
            const string inlineXml =
                "<CycloneDDS>" +
                "<Domain>" +
                "<Discovery><SPDPInterval>100ms</SPDPInterval></Discovery>" +
                "</Domain>" +
                "</CycloneDDS>";

            Environment.SetEnvironmentVariable("CYCLONEDDS_URI", inlineXml);
        }
    }
}
