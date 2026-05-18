using System.Collections.Generic;
using Fdp.ModuleHost.Scheduling;

namespace Fdp.ModuleHost.Diagnostics
{
    /// <summary>
    /// Data transfer object mirroring <see cref="ModuleDiagnostics"/> for headless consumers.
    /// </summary>
    public sealed class ModuleDiagnosticsDto
    {
        public string ModuleName     { get; init; } = string.Empty;
        public string ModuleTypeName { get; init; } = string.Empty;
        public string RunMode        { get; init; } = string.Empty;
        public string DataStrategy   { get; init; } = string.Empty;
        public int    TargetFrequencyHz { get; init; }
        public string LifecycleState { get; init; } = string.Empty;
        public string CircuitState   { get; init; } = string.Empty;
        public int    ExecutionCount { get; init; }
        public int    FailureCount   { get; init; }
    }

    /// <summary>
    /// One row in the Systems table: the ECS system phase, the owning module name, and its profile.
    /// </summary>
    public sealed class SystemDiagnosticsRow
    {
        public string           Phase      { get; init; } = string.Empty;
        public string           ModuleName { get; init; } = string.Empty;
        public SystemProfileData Profile   { get; init; } = null!;
    }

    /// <summary>
    /// One row in the Translators table, flattened to primitives so the panel needs no kernel access.
    /// </summary>
    public sealed class TranslatorDiagnosticsDto
    {
        public string           SystemName       { get; init; } = string.Empty;
        public string           Direction        { get; init; } = string.Empty;
        public string           TopicName        { get; init; } = string.Empty;
        public long             DescriptorOrdinal { get; init; }
        public SystemProfileData Profile          { get; init; } = null!;
        public long             ReceivedSamples  { get; init; }
        public long             SentSamples      { get; init; }
    }

    /// <summary>
    /// A point-in-time snapshot of the architecture diagnostics data.
    /// Produced by <see cref="IArchitectureDiagnosticsService.GetSnapshot"/>.
    /// </summary>
    public sealed class ArchitectureSnapshotDto
    {
        public IReadOnlyList<ModuleDiagnosticsDto>  Modules     { get; init; } = System.Array.Empty<ModuleDiagnosticsDto>();
        public IReadOnlyList<SystemDiagnosticsRow>  Systems     { get; init; } = System.Array.Empty<SystemDiagnosticsRow>();
        public IReadOnlyList<TranslatorDiagnosticsDto> Translators { get; init; } = System.Array.Empty<TranslatorDiagnosticsDto>();
    }

    /// <summary>
    /// Headless service that extracts a snapshot of module/system/translator diagnostics
    /// from the running <see cref="ModuleHostKernel"/> without touching any UI or ImGui APIs.
    /// </summary>
    public interface IArchitectureDiagnosticsService
    {
        /// <summary>
        /// Returns a fresh point-in-time snapshot.  Allocates on every call — only call
        /// from the render thread at the UI frame rate.
        /// </summary>
        ArchitectureSnapshotDto GetSnapshot();
    }
}
