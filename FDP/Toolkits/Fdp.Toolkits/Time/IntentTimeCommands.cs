using System;
using Fdp.Core;
using Fdp.Toolkit.Time.Domain;

namespace Fdp.Toolkit.Time
{
    /// <summary>
    /// <see cref="ITimeCommands"/> that publishes intents onto an event bus and nothing else.
    ///
    /// <para>This is the whole point of `T4`: the caller states what it wants, and whoever is
    /// responsible for the clock on this node decides how to honour it. On a node that hosts the
    /// master, <c>MasterSyncController.Update()</c> drains the intent; on a node that does not,
    /// <c>ClusterOpEgressTranslator</c> drains it and sends a <c>ClusterOpRequest</c> to the
    /// orchestrator. Same call, same intent, two drainers chosen by the node's role — and the
    /// caller does not need to know which it is.</para>
    ///
    /// <para><b>The bus must be the one the drainer reads.</b> Publishing onto a bus nobody drains
    /// fails silently — <c>ReadManaged</c> on the wrong bus returns an empty list, with no exception
    /// and no log. That was the editor's `T3` defect. Pass the bus that carries the orchestration
    /// registry, which is also the bus the node's time controller was constructed on.</para>
    /// </summary>
    public sealed class IntentTimeCommands : ITimeCommands
    {
        private readonly FdpEventBus _bus;
        private readonly float       _fixedStepSeconds;

        /// <param name="bus">The bus the node's time controller (or its egress translator) drains.</param>
        /// <param name="fixedStepSeconds">
        /// What one tick of <see cref="StepOneTick"/> is worth. Defaults to
        /// <see cref="Controllers.TimeConfig.FixedDeltaSeconds"/> (60 Hz).
        /// </param>
        public IntentTimeCommands(FdpEventBus bus, float fixedStepSeconds = 1.0f / 60.0f)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            if (fixedStepSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(fixedStepSeconds),
                    "a step must advance time; a zero or negative step is not a step.");
            _fixedStepSeconds = fixedStepSeconds;
        }

        /// <inheritdoc />
        public void Pause() => _bus.PublishManaged(new PauseTimeIntent());

        /// <inheritdoc />
        public void Resume() => _bus.PublishManaged(new ResumeTimeIntent());

        /// <inheritdoc />
        public void StepOneTick() =>
            _bus.PublishManaged(new StepTimeIntent { DeltaSeconds = _fixedStepSeconds });

        /// <inheritdoc />
        public void SetTimeScale(float scale)
        {
            if (scale < 0f)
                throw new ArgumentOutOfRangeException(nameof(scale), "time scale cannot be negative.");
            _bus.PublishManaged(new SetTimeScaleIntent { TimeScale = scale });
        }
    }
}
