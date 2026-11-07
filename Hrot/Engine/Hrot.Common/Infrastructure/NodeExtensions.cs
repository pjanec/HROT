using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using Fdp.Core;

namespace Hrot.Common.Infrastructure;

/// <summary>
/// ⭐ CE-4293 (R-4158) — a product built OUTSIDE HROT's sources contributes an ordinary <see cref="INodeCapability"/> to every node
/// the shared bootstrap composes, by shipping an assembly in <c>extensions/&lt;name&gt;/</c>. It needs a public parameterless constructor.
/// 📄 docs/DESIGN_Node_Extensions.md.
/// </summary>
public interface INodeExtension
{
    /// <summary>Identity for the report and the log.</summary>
    string Name { get; }

    /// <summary>The capability this extension adds to the node described by <paramref name="context"/>; <see langword="null"/> composes nothing (not for this role, not configured).</summary>
    INodeCapability? CreateCapability(NodeExtensionContext context);
}

/// <summary>What an extension is told about the node being composed. <see cref="Folder"/> is the extension's own <c>extensions/&lt;name&gt;</c> folder (empty when asked for no folder).</summary>
/// <param name="Role">The node's declared role at the <c>capabilities</c> boot step.</param>
/// <param name="Offline">The node has no DDS stack (<c>HrotNodeConfig.Headless</c>).</param>
/// <param name="HostName">The host's subsystem name (<c>HrotNodeConfig.SubsystemName</c>, e.g. "Editor").</param>
/// <param name="Folder">The extension's own folder, for configuration files.</param>
public sealed record NodeExtensionContext(NodeRole Role, bool Offline, string HostName, string Folder = "");

/// <summary>
/// The build stamp of an extension assembly: the informational version of the <c>Hrot.Common</c> it was compiled against.
/// Emitted automatically by <c>Hrot/Engine/Hrot.Common/Hrot.NodeExtension.targets</c>. A mismatch with the running <c>Hrot.Common</c> refuses that extension.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class BuiltAgainstHrotAttribute : Attribute
{
    public BuiltAgainstHrotAttribute(string version) => Version = version;
    public string Version { get; }
}

/// <summary>
/// ⭐ CE-4293 — loads the extensions once per process and adds their capabilities to a node's list. Every refusal is ONE line in
/// <see cref="Report"/> and on stderr; no <c>extensions/</c> folder means nothing and no error. Startup only: no per-frame code.
/// </summary>
public static class NodeExtensions
{
    /// <summary>The folder (beside the binaries) holding one subfolder per extension.</summary>
    public const string FolderName = "extensions";

    private sealed record Entry(INodeExtension Extension, string Folder);

    private static readonly object Gate = new();
    private static readonly HashSet<string> LoadedBases = new(StringComparer.Ordinal);
    private static readonly HashSet<string> ExtensionFolders = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<string> ReportLines = new();
    private static Entry[] _entries = Array.Empty<Entry>();
    private static string[] _report = Array.Empty<string>();
    private static bool _resolverHooked;

    /// <summary>The extensions found, in folder-name then type-name order.</summary>
    public static IReadOnlyList<INodeExtension> Discovered => Array.ConvertAll(_entries, e => e.Extension);

    /// <summary>One line per refusal; empty when everything loaded.</summary>
    public static IReadOnlyList<string> Report => _report;

    /// <summary>The informational version of the running <c>Hrot.Common</c> (carries the commit), the value a stamp must equal.</summary>
    public static string RunningVersion { get; } =
        typeof(NodeExtensions).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

    private static bool IsShared(string fileNameWithoutExtension) =>
        fileNameWithoutExtension.StartsWith("Hrot.", StringComparison.OrdinalIgnoreCase)
        || fileNameWithoutExtension.StartsWith("Fdp.", StringComparison.OrdinalIgnoreCase)
        || fileNameWithoutExtension.StartsWith("CycloneDDS", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Loads every <c>&lt;baseDirectory&gt;/extensions/&lt;name&gt;/</c> by path into the default load context. Idempotent per base directory
    /// and thread-safe: a second call for the same directory does nothing.
    /// </summary>
    public static void LoadAll(string? baseDirectory = null)
    {
        string root = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);
        lock (Gate)
        {
            if (!LoadedBases.Add(root)) return;
            string dir = Path.Combine(root, FolderName);
            if (!Directory.Exists(dir)) return;
            HookResolver();
            string[] folders = Directory.GetDirectories(dir);
            Array.Sort(folders, StringComparer.Ordinal);
            foreach (string folder in folders)
                LoadFolder(folder);
        }
    }

    private static void LoadFolder(string folder)
    {
        string name = Path.GetFileName(folder);
        string[] dlls = Directory.GetFiles(folder, "*.dll");
        Array.Sort(dlls, StringComparer.Ordinal);

        foreach (string dll in dlls)
            if (IsShared(Path.GetFileNameWithoutExtension(dll)))
            {
                Refuse($"extension '{name}': refused - its folder ships the shared assembly '{Path.GetFileName(dll)}'. Hrot.*, Fdp.* and CycloneDDS* come from the host and are never shipped (a second copy would be a second type identity).");
                return;
            }

        var stamped = new List<(string Path, string Stamp)>();
        foreach (string dll in dlls)
            if (ReadStamp(dll) is { } stamp) stamped.Add((dll, stamp));
        if (stamped.Count == 0)
        {
            Refuse($"extension '{name}': refused - no assembly in the folder carries [assembly: BuiltAgainstHrot(...)].");
            return;
        }
        foreach (var (path, stamp) in stamped)
            if (!string.Equals(stamp, RunningVersion, StringComparison.Ordinal))
            {
                Refuse($"extension '{name}': refused - '{Path.GetFileName(path)}' was built against Hrot.Common '{stamp}' but this process runs '{RunningVersion}'. Rebuild the extension.");
                return;
            }

        ExtensionFolders.Add(Path.GetFullPath(folder));
        foreach (var (path, _) in stamped)
        {
            Assembly assembly;
            try { assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path)); }
            catch (Exception e)
            {
                Refuse($"extension '{name}': '{Path.GetFileName(path)}' failed to load - {e.GetType().Name}: {e.Message}");
                continue;
            }
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e)
            {
                Refuse($"extension '{name}': '{Path.GetFileName(path)}' types failed to load - {e.LoaderExceptions.FirstOrDefault()?.Message ?? e.Message}");
                types = e.Types.Where(t => t != null).ToArray()!;
            }
            var candidates = types.Where(t => t.IsClass && !t.IsAbstract && typeof(INodeExtension).IsAssignableFrom(t))
                                  .OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();
            if (candidates.Count == 0)
                Refuse($"extension '{name}': '{Path.GetFileName(path)}' contains no INodeExtension type.");
            foreach (Type t in candidates)
                Add(t, folder);
        }
    }

    /// <summary>Builds one extension type through its public parameterless constructor; every failure is a report line.</summary>
    internal static void Add(Type type, string folder)
    {
        lock (Gate)
        {
            if (type.GetConstructor(Type.EmptyTypes) is not { } ctor)
            {
                Refuse($"extension type '{type.FullName}': refused - it has no public parameterless constructor.");
                return;
            }
            INodeExtension instance;
            try { instance = (INodeExtension)ctor.Invoke(null); }
            catch (Exception e)
            {
                Refuse($"extension type '{type.FullName}': refused - its constructor threw {(e is TargetInvocationException { InnerException: { } i } ? i : e).GetType().Name}: {(e.InnerException ?? e).Message}");
                return;
            }
            _entries = _entries.Append(new Entry(instance, folder)).ToArray();
        }
    }

    /// <summary>
    /// <paramref name="host"/> with the extensions' capabilities appended LAST, in discovery order. Returns <paramref name="host"/> itself
    /// (same instance) when nothing is appended. A throwing extension, a capability key already present (host or another extension), a
    /// feature id already declared, or a need the role did not declare (<paramref name="declaredResources"/>, when given) is refused with one report line.
    /// </summary>
    public static IReadOnlyList<INodeCapability> CapabilitiesFor(
        NodeExtensionContext context, IReadOnlyList<INodeCapability> host, IReadOnlyCollection<string>? declaredResources = null)
    {
        Entry[] entries = _entries;
        if (entries.Length == 0) return host;

        List<INodeCapability>? result = null;
        var keys = new HashSet<string>(host.Select(c => c.Key), StringComparer.Ordinal);
        var features = new HashSet<string>(host.SelectMany(c => c.Features).Select(f => f.Id), StringComparer.Ordinal);
        foreach (Entry entry in entries)
        {
            INodeCapability? capability;
            try { capability = entry.Extension.CreateCapability(context with { Folder = entry.Folder }); }
            catch (Exception e)
            {
                Refuse($"extension '{entry.Extension.Name}': CreateCapability threw {e.GetType().Name}: {e.Message}");
                continue;
            }
            if (capability is null) continue;
            if (keys.Contains(capability.Key))
            {
                Refuse($"extension '{entry.Extension.Name}': refused - capability key '{capability.Key}' is already composed on this node.");
                continue;
            }
            string? clash = capability.Features.Select(f => f.Id).FirstOrDefault(features.Contains);
            if (clash != null)
            {
                Refuse($"extension '{entry.Extension.Name}': refused - feature '{clash}' is already declared on this node.");
                continue;
            }
            string? undeclared = declaredResources == null ? null : capability.Needs.FirstOrDefault(n => !declaredResources.Contains(n));
            if (undeclared != null)
            {
                Refuse($"extension '{entry.Extension.Name}': refused - it needs resource '{undeclared}', which this node's role does not declare.");
                continue;
            }
            keys.Add(capability.Key);
            foreach (var f in capability.Features) features.Add(f.Id);
            (result ??= new List<INodeCapability>(host)).Add(capability);
        }
        return result ?? host;
    }

    private static void Refuse(string line)
    {
        lock (Gate)
        {
            if (ReportLines.Contains(line)) return;   // one line per refusal, however many nodes boot in the process
            ReportLines.Add(line);
            _report = ReportLines.ToArray();
        }
        Console.Error.WriteLine("[Extensions] " + line);
    }

    /// <summary>Test seam: forgets what was discovered and which directories were scanned (already loaded assemblies stay loaded).</summary>
    internal static void ResetForTests()
    {
        lock (Gate)
        {
            LoadedBases.Clear();
            ReportLines.Clear();
            _entries = Array.Empty<Entry>();
            _report = Array.Empty<string>();
        }
    }

    // An unresolved name asked for by an extension assembly is served from THAT extension's folder only, and never for a shared name.
    private static void HookResolver()
    {
        if (_resolverHooked) return;
        _resolverHooked = true;
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            string? location = args.RequestingAssembly?.Location;
            if (string.IsNullOrEmpty(location)) return null;
            string folder = Path.GetDirectoryName(location)!;
            lock (Gate) { if (!ExtensionFolders.Contains(folder)) return null; }
            string simple = new AssemblyName(args.Name).Name ?? "";
            if (IsShared(simple)) return null;
            string candidate = Path.Combine(folder, simple + ".dll");
            return File.Exists(candidate) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate) : null;
        };
    }

    // The stamp is read from metadata, so a mismatching extension is refused WITHOUT being loaded.
    private static string? ReadStamp(string dllPath)
    {
        try
        {
            using var stream = File.OpenRead(dllPath);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) return null;
            MetadataReader md = pe.GetMetadataReader();
            foreach (CustomAttributeHandle h in md.GetAssemblyDefinition().GetCustomAttributes())
            {
                CustomAttribute attribute = md.GetCustomAttribute(h);
                if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
                var parent = md.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
                if (parent.Kind != HandleKind.TypeReference
                    || md.GetString(md.GetTypeReference((TypeReferenceHandle)parent).Name) != nameof(BuiltAgainstHrotAttribute)) continue;
                BlobReader blob = md.GetBlobReader(attribute.Value);
                if (blob.ReadUInt16() != 1) return null;   // prolog
                return blob.ReadSerializedString();
            }
        }
        catch (BadImageFormatException) { /* a native or reference-only dll */ }
        return null;
    }
}
