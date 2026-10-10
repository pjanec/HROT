using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Hrot.Common.Infrastructure
{
    /// <summary>
    /// ⭐ <c>CE-3123</c> (R-228) — which of OUR assemblies a process loads up front, written ONCE: the runner's pre-load
    /// (<c>Hrot.ClusterRunner</c> <c>Program</c>) and the gizmo completeness rail call the same <see cref="LoadAll"/>, so the rail
    /// proves the production path rather than a copy of it. Every host is a library run inside the runner, and reflection-based
    /// registries (subsystems, gizmo projectors) see only LOADED assemblies — this is what makes them complete.
    /// 📄 docs/DESIGN_Uniform_Gizmo_Membership.md §9.6 / §10.
    /// </summary>
    public static class DeploymentAssemblies
    {
        /// <summary>
        /// Our assemblies deliberately NOT pre-loaded. <c>Hrot.AI.Behaviors</c> goes into a collectible load context so
        /// <c>FbtAssemblyHotReloader</c> can unload and reload it; locking it into the default context would break that.
        /// </summary>
        public static readonly IReadOnlyList<string> Skipped = new[] { "Hrot.AI.Behaviors" };

        /// <summary>Whether <paramref name="assemblyName"/> is one of ours (the <c>Hrot.*</c> / <c>Fdp.*</c> families).</summary>
        public static bool IsOurs(string assemblyName) =>
            assemblyName.StartsWith("Hrot.", StringComparison.Ordinal) || assemblyName.StartsWith("Fdp.", StringComparison.Ordinal);

        /// <summary>
        /// Loads every one of our DLLs in <paramref name="directory"/> (default: the process's base directory) that is not
        /// loaded yet and not <see cref="Skipped"/>. Scans the folder rather than IL references: the compiler drops a
        /// <c>ProjectReference</c> no code uses statically, so <c>GetReferencedAssemblies()</c> misses e.g. <c>Hrot.CGF.dll</c>.
        /// Uses <c>Assembly.Load(AssemblyName)</c> so each lands in the default load context. Returns the names it loaded.
        /// </summary>
        public static IReadOnlyList<string> LoadAll(string? directory = null)
        {
            directory ??= AppDomain.CurrentDomain.BaseDirectory;
            var loaded = new HashSet<string>(
                AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name!), StringComparer.OrdinalIgnoreCase);
            var newlyLoaded = new List<string>();

            foreach (var file in Directory.GetFiles(directory, "*.dll"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!IsOurs(name) || Skipped.Contains(name, StringComparer.OrdinalIgnoreCase) || loaded.Contains(name)) continue;
                try
                {
                    Assembly.Load(new AssemblyName(name));
                    loaded.Add(name);
                    newlyLoaded.Add(name);
                }
                catch { /* not loadable here (e.g. a native or reference-only DLL) — ignore, as the runner always has */ }
            }
            return newlyLoaded;
        }

        /// <summary>
        /// The full names of the types in <paramref name="dllPath"/> that carry an attribute named
        /// <paramref name="attributeTypeName"/> — read from METADATA, without loading the assembly. ⭐ What lets a rail compare
        /// the DEPLOYED set with the REGISTERED one, instead of two answers read from the same loaded set.
        /// </summary>
        public static IReadOnlyList<string> TypesWithAttribute(string dllPath, string attributeTypeName)
        {
            var found = new List<string>();
            try
            {
                using var stream = File.OpenRead(dllPath);
                using var pe = new PEReader(stream);
                if (!pe.HasMetadata) return found;
                var md = pe.GetMetadataReader();
                foreach (var handle in md.TypeDefinitions)
                {
                    var type = md.GetTypeDefinition(handle);
                    foreach (var ah in type.GetCustomAttributes())
                    {
                        if (AttributeName(md, md.GetCustomAttribute(ah)) != attributeTypeName) continue;
                        string ns = md.GetString(type.Namespace);
                        string nm = md.GetString(type.Name);
                        found.Add(string.IsNullOrEmpty(ns) ? nm : ns + "." + nm);
                        break;
                    }
                }
            }
            catch (BadImageFormatException) { /* not a managed assembly */ }
            return found;
        }

        private static string? AttributeName(MetadataReader md, CustomAttribute attribute)
        {
            switch (attribute.Constructor.Kind)
            {
                case HandleKind.MemberReference:
                {
                    var parent = md.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
                    return parent.Kind == HandleKind.TypeReference
                        ? md.GetString(md.GetTypeReference((TypeReferenceHandle)parent).Name)
                        : null;
                }
                case HandleKind.MethodDefinition:
                {
                    var declaring = md.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType();
                    return md.GetString(md.GetTypeDefinition(declaring).Name);
                }
                default:
                    return null;
            }
        }
    }
}
