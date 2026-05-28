using System;
using System.Reflection;
using Fdp.Core.Serialization;
using Fdp.Core.Serialization.Migrations.Adapters;

namespace Fdp.Core.Serialization.Migrations;

/// <summary>
/// Constructs the migration infrastructure for a host process. Each subsystem
/// (SimHost, CGF, IG, Editor, ClusterRunner) calls this once during startup,
/// passing format-specific registration callbacks.
/// </summary>
public static class MigrationBootstrap
{
    /// <summary>
    /// Builds the migration services bundle.
    /// </summary>
    /// <param name="registerFormats">
    /// A callback that receives the empty registry and registers all the
    /// document types this host process cares about.
    /// </param>
    /// <param name="storage">
    /// The storage backend. Use <see cref="FileSystemMigrationStorage"/>
    /// for production hosts; <see cref="InMemoryMigrationStorage"/> for tests.
    /// </param>
    /// <param name="engineVersionProvider">
    /// Returns the engine version string.
    /// </param>
    /// <param name="writerIdentifier">
    /// String identifying this writing tool, stored in <c>$meta.createdBy</c>
    /// on new files.
    /// </param>
    /// <remarks>
    /// The registry is sealed after registerFormats returns. Subsequent
    /// registration calls would throw.
    ///
    /// The journal format <c>"Fdp.MigrationJournal"</c> is auto-registered as
    /// passthrough at version 1.
    ///
    /// Internal because <see cref="IMigrationStorage"/> references the internal
    /// <c>UnknownsJournal</c> type; external callers should use
    /// <see cref="BuildForProduction"/>.
    /// </remarks>
    internal static MigrationServices Build(
        Action<MigrationRegistry> registerFormats,
        IMigrationStorage storage,
        Func<string> engineVersionProvider,
        string writerIdentifier)
    {
        if (registerFormats is null) throw new ArgumentNullException(nameof(registerFormats));
        if (storage is null) throw new ArgumentNullException(nameof(storage));
        if (engineVersionProvider is null) throw new ArgumentNullException(nameof(engineVersionProvider));
        if (writerIdentifier is null) throw new ArgumentNullException(nameof(writerIdentifier));

        var registry = new MigrationRegistry();

        // Auto-register the journal doc type as passthrough at version 1.
        registry.RegisterPassthroughDocType(FdpDocumentTypes.MigrationJournal, 1);

        // Let the caller register all host-specific document types.
        registerFormats(registry);

        // Seal so no further registrations are possible after Build returns.
        registry.Seal();

        var pipeline = new MigrationPipeline(registry);
        var readOnly = new ReadOnlyMigrationAdapter(pipeline);
        var persistent = new PersistentMigrationAdapter(pipeline, storage, engineVersionProvider, writerIdentifier);

        return new MigrationServices(registry, pipeline, readOnly, persistent);
    }

    /// <summary>
    /// Convenience overload that uses <see cref="FileSystemMigrationStorage"/>
    /// and reads <c>AssemblyInformationalVersionAttribute</c> from the calling
    /// assembly's <c>Fdp.Core</c> reference for the engine version.
    /// </summary>
    public static MigrationServices BuildForProduction(
        Action<MigrationRegistry> registerFormats,
        string writerIdentifier)
    {
        string version = ReadEngineVersion(typeof(MigrationBootstrap).Assembly);
        return Build(registerFormats, new FileSystemMigrationStorage(), () => version, writerIdentifier);
    }

    /// <summary>
    /// Reads the informational version string from an assembly.
    /// Returns "unknown" when the attribute is absent or its value is null.
    /// </summary>
    /// <remarks>
    /// Internal so that unit tests can supply a custom assembly that lacks
    /// the attribute, exercising the "unknown" fallback branch.
    /// </remarks>
    internal static string ReadEngineVersion(System.Reflection.Assembly assembly)
    {
        var infoAttr = (AssemblyInformationalVersionAttribute?)
            Attribute.GetCustomAttribute(
                assembly,
                typeof(AssemblyInformationalVersionAttribute));
        return infoAttr?.InformationalVersion ?? "unknown";
    }
}
