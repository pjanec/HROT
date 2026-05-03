using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Interfaces;
using Fdp.ModuleHost.Scheduling;

namespace Fdp.ModuleHost.Diagnostics
{
    /// <summary>
    /// Default implementation of <see cref="IArchitectureDiagnosticsService"/>.
    /// Wraps a <see cref="ModuleHostKernel"/> and extracts diagnostics into DTO snapshots.
    /// </summary>
    public sealed class ArchitectureDiagnosticsService : IArchitectureDiagnosticsService
    {
        private readonly Func<ModuleHostKernel?> _kernelGetter;

        /// <param name="kernelGetter">
        /// A delegate that returns the current kernel, or <see langword="null"/> when not yet available.
        /// </param>
        public ArchitectureDiagnosticsService(Func<ModuleHostKernel?> kernelGetter)
        {
            _kernelGetter = kernelGetter ?? throw new ArgumentNullException(nameof(kernelGetter));
        }

        /// <summary>Convenience overload for when the kernel is already available at construction.</summary>
        public ArchitectureDiagnosticsService(ModuleHostKernel kernel)
            : this(() => kernel ?? throw new ArgumentNullException(nameof(kernel)))
        {
        }

        /// <inheritdoc/>
        public ArchitectureSnapshotDto GetSnapshot()
        {
            var kernel = _kernelGetter();
            if (kernel == null)
                return new ArchitectureSnapshotDto();

            return new ArchitectureSnapshotDto
            {
                Modules     = BuildModuleRows(kernel),
                Systems     = BuildSystemRows(kernel),
                Translators = BuildTranslatorRows(kernel),
            };
        }

        private static IReadOnlyList<ModuleDiagnosticsDto> BuildModuleRows(ModuleHostKernel kernel)
        {
            return kernel.GetModuleDiagnostics()
                .Select(m => new ModuleDiagnosticsDto
                {
                    ModuleName       = m.ModuleName,
                    ModuleTypeName   = m.ModuleTypeName,
                    RunMode          = m.RunMode.ToString(),
                    DataStrategy     = m.DataStrategy.ToString(),
                    TargetFrequencyHz = m.TargetFrequencyHz,
                    LifecycleState   = m.LifecycleState.ToString(),
                    CircuitState     = m.CircuitState.ToString(),
                    ExecutionCount   = m.ExecutionCount,
                    FailureCount     = m.FailureCount,
                })
                .ToList();
        }

        private static IReadOnlyList<SystemDiagnosticsRow> BuildSystemRows(ModuleHostKernel kernel)
        {
            return kernel.SystemScheduler.GetAllProfileData()
                .SelectMany(kvp => kvp.Value.Select(item => new SystemDiagnosticsRow
                {
                    Phase      = kvp.Key.ToString(),
                    ModuleName = kernel.GetModuleNameForSystem(item.System),
                    Profile    = item.Profile,
                }))
                .ToList();
        }

        private static IReadOnlyList<TranslatorDiagnosticsDto> BuildTranslatorRows(ModuleHostKernel kernel)
        {
            var rows = new List<TranslatorDiagnosticsDto>();

            foreach (var system in kernel.SystemScheduler.GetAllSystems())
            {
                var translatorsProperty = system.GetType().GetProperty("Translators");
                if (translatorsProperty == null)
                    continue;

                if (translatorsProperty.GetValue(system) is not IEnumerable<INetworkTranslator> translators)
                    continue;

                if (system.GetType().Name.Contains("Cleanup"))
                    continue;

                foreach (var translator in translators)
                {
                    var profile = TryGetTranslatorProfile(system, translator)
                        ?? new SystemProfileData($"{translator.TopicName} [{(translator as IDescriptorTranslator)?.DescriptorOrdinal}]");

                    rows.Add(new TranslatorDiagnosticsDto
                    {
                        SystemName        = system.GetType().Name,
                        Direction         = translator.Direction.ToString(),
                        TopicName         = translator.TopicName,
                        DescriptorOrdinal = (translator as IDescriptorTranslator)?.DescriptorOrdinal ?? 0L,
                        Profile           = profile,
                        ReceivedSamples   = translator.ReceivedSampleCount,
                        SentSamples       = translator.SentSampleCount,
                    });
                }
            }

            return rows;
        }

        private static SystemProfileData? TryGetTranslatorProfile(object system, INetworkTranslator translator)
        {
            var method = system.GetType().GetMethod("GetTranslatorProfileData", new[] { typeof(INetworkTranslator) });
            if (method == null)
                return null;

            return method.Invoke(system, new object[] { translator }) as SystemProfileData;
        }
    }
}
