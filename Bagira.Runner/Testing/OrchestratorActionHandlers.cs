using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Bagira.BDC.SSTD.Orchestration;
using Bagira.Orchestrator;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Framework.Runner.Testing;
using FDP.Toolkit.Orchestration;
using Microsoft.Extensions.Logging;

namespace Bagira.Runner.Testing
{
    /// <summary>
    /// Lightweight test-harness component that makes an entity's X position advance each tick.
    /// Registered only by E2E test fixtures — never wired into a production boot path.
    /// ComponentId 219 is an unoccupied byte value in the test-reserved range (200-255).
    /// </summary>
    [Fdp.Kernel.ComponentId(219)]
    public struct MovingTestTag
    {
        /// <summary>Metres per second along the X axis.</summary>
        public float VelocityX;
    }
    /// <summary>
    /// Drives a DSM state transition or operation via <see cref="DrillMaster"/>.
    /// Action name: <c>"sysop"</c>.
    ///
    /// <para>
    /// Args:
    /// <list type="bullet">
    ///   <item><c>TargetState</c> (string or int) — DSMState name/value, or the special literal
    ///     <c>"TakeCheckpoint"</c> / <c>"ReplaySeek"</c>.</item>
    ///   <item><c>DrillId</c> (string, optional) — drill/session identifier included in payload.</item>
    ///   <item><c>ScenarioId</c> (string, optional) — scenario identifier included in payload.</item>
    ///   <item><c>TargetWallTicks</c> (long, optional) — required for <c>ReplaySeek</c>.</item>
    /// </list>
    /// </para>
    ///
    /// <para>Polls <paramref name="statusReader"/> until a <see cref="SysOpStatus"/> whose
    /// <c>RequestId</c> matches and whose <c>StatusCode</c> is not
    /// <c>OrchestrationStatusCode.InProgress</c> arrives (or timeout expires).</para>
    /// </summary>
    public sealed class SysopActionHandler : ITestActionHandler
    {
        private readonly DrillMaster _drillMaster;
        private readonly DdsReader<SysOpStatus> _statusReader;
        private readonly double _timeoutSeconds;
        private readonly ILogger _log;

        public string ActionName => "sysop";

        /// <summary>
        /// Creates a new handler.
        /// </summary>
        /// <param name="drillMaster">The DrillMaster that drives the cluster.</param>
        /// <param name="statusReader">Reader on the <c>SysOpStatus</c> DDS topic in the same domain.</param>
        /// <param name="log">Logger for tracing and error output.</param>
        /// <param name="timeoutSeconds">Maximum seconds to wait for operation completion.</param>
        public SysopActionHandler(
            DrillMaster drillMaster,
            DdsReader<SysOpStatus> statusReader,
            ILogger log,
            double timeoutSeconds = 10.0)
        {
            _drillMaster   = drillMaster   ?? throw new ArgumentNullException(nameof(drillMaster));
            _statusReader  = statusReader  ?? throw new ArgumentNullException(nameof(statusReader));
            _log           = log           ?? throw new ArgumentNullException(nameof(log));
            _timeoutSeconds = timeoutSeconds;
        }

        public async Task<object?> ExecuteAsync(Dictionary<string, object> args)
        {
            string targetStateStr = args.TryGetValue("TargetState", out var ts)
                ? Convert.ToString(ts) ?? string.Empty
                : string.Empty;

            string? drillId    = args.TryGetValue("DrillId",    out var di) ? Convert.ToString(di) : null;
            string? scenarioId = args.TryGetValue("ScenarioId", out var si) ? Convert.ToString(si) : null;
            long? targetWallTicks = args.TryGetValue("TargetWallTicks", out var twt)
                ? (long?)Convert.ToInt64(twt)
                : null;

            var requestId = Guid.NewGuid();
            SysOpRequest request;

            if (string.Equals(targetStateStr, "TakeCheckpoint", StringComparison.OrdinalIgnoreCase))
            {
                request = new SysOpRequest
                {
                    RequestId     = requestId,
                    OperationType = SysOpType.TakeCheckpoint,
                    PayloadJson   = string.Empty,
                };
            }
            else if (string.Equals(targetStateStr, "ReplaySeek", StringComparison.OrdinalIgnoreCase))
            {
                if (!targetWallTicks.HasValue)
                    throw new InvalidOperationException("sysop ReplaySeek requires TargetWallTicks argument.");

                request = new SysOpRequest
                {
                    RequestId     = requestId,
                    OperationType = SysOpType.ReplaySeek,
                    PayloadJson   = $"{{\"TargetWallTicks\":{targetWallTicks.Value}}}",
                };
            }
            else
            {
                // Parse as DSMState name or integer.
                DSMState targetState;
                if (int.TryParse(targetStateStr, out int intVal))
                {
                    targetState = (DSMState)intVal;
                }
                else if (!Enum.TryParse(targetStateStr, ignoreCase: true, out targetState))
                {
                    throw new ArgumentException(
                        $"sysop: cannot parse TargetState '{targetStateStr}' as DSMState name or integer.");
                }

                // Build payload JSON, optionally including DrillId and ScenarioId.
                var payloadDict = new Dictionary<string, object>
                {
                    ["TargetState"] = (int)targetState,
                };
                if (!string.IsNullOrEmpty(drillId))
                    payloadDict["DrillId"] = drillId!;
                if (!string.IsNullOrEmpty(scenarioId))
                    payloadDict["ScenarioId"] = scenarioId!;
                if (targetWallTicks.HasValue)
                    payloadDict["TargetWallTicks"] = targetWallTicks.Value;

                request = new SysOpRequest
                {
                    RequestId     = requestId,
                    OperationType = SysOpType.TransitionState,
                    PayloadJson   = JsonSerializer.Serialize(payloadDict),
                };
            }

            _log.LogDebug("sysop: enqueuing {OpType} request {Id}", request.OperationType, requestId);
            await _drillMaster.HandleSysOpRequestAsync(request).ConfigureAwait(false);

            // Poll until the matching SysOpStatus (non-InProgress) arrives or timeout.
            // Polling is extracted to a synchronous helper because DdsLoan is a ref struct
            // and cannot be held across an await in C# 12 (requires C# 13).
            var deadline = DateTime.UtcNow.AddSeconds(_timeoutSeconds);
            while (DateTime.UtcNow < deadline)
            {
                var (found, isError, errorMsg) = PollStatusOnce(requestId);
                if (found)
                {
                    if (isError)
                        throw new InvalidOperationException(errorMsg!);
                    _log.LogDebug("sysop: request {Id} succeeded", requestId);
                    return new Dictionary<string, object> { ["status"] = "success" };
                }
                await Task.Delay(20).ConfigureAwait(false);
            }

            throw new TimeoutException(
                $"sysop: timed out after {_timeoutSeconds}s waiting for SysOpStatus matching RequestId={requestId}");
        }

        // Synchronous helper: takes one snapshot from the reader and checks for a matching status.
        // Returns (found, isError, errorMessage). Extracted to avoid holding a ref struct across await.
        private (bool Found, bool IsError, string? ErrorMsg) PollStatusOnce(Guid requestId)
        {
            using var loan = _statusReader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                var data = sample.Data;
                if (data.RequestId != requestId) continue;
                if (data.StatusCode == OrchestrationStatusCode.InProgress) continue;

                if (OrchestrationStatusCode.IsError(data.StatusCode))
                {
                    return (true, true, $"sysop: request {requestId} failed with StatusCode={data.StatusCode}, " +
                                        $"ResultJson={data.ResultJson}");
                }

                _log.LogDebug("sysop: request {Id} completed with StatusCode={Code}", requestId, data.StatusCode);
                return (true, false, null);
            }
            return (false, false, null);
        }
    }

    /// <summary>
    /// Asserts the <see cref="EntityRepository.EntityCount"/> equals the expected value.
    /// Action name: <c>"assert_entity_count"</c>.
    ///
    /// <para>Args: <c>expected</c> (int) — the required entity count.</para>
    /// </summary>
    public sealed class AssertEntityCountActionHandler : ITestActionHandler
    {
        private readonly EntityRepository? _world;
        private readonly ILogger _log;

        public string ActionName => "assert_entity_count";

        public AssertEntityCountActionHandler(EntityRepository? world, ILogger log)
        {
            _world = world;
            _log   = log;
        }

        public Task<object?> ExecuteAsync(Dictionary<string, object> args)
        {
            int expected = args.TryGetValue("expected", out var ev)
                ? Convert.ToInt32(ev)
                : throw new ArgumentException("assert_entity_count requires 'expected' argument.");

            if (_world == null)
            {
                _log.LogWarning("assert_entity_count: no EntityRepository — skipping.");
                return Task.FromResult<object?>(new Dictionary<string, object> { ["entity_count"] = 0 });
            }

            int actual = _world.EntityCount;
            if (actual != expected)
            {
                throw new InvalidOperationException(
                    $"assert_entity_count FAILED: expected {expected} but got {actual}.");
            }

            _log.LogDebug("assert_entity_count: {Count} == {Expected} ✓", actual, expected);
            return Task.FromResult<object?>(new Dictionary<string, object> { ["entity_count"] = actual });
        }
    }

    /// <summary>
    /// Adds a <see cref="MovingTestTag"/> component to an entity, enabling it to be swept by
    /// <c>MovingEntitySystem</c> each tick.
    /// Action name: <c>"add_moving_tag"</c>.
    ///
    /// <para>Args:
    /// <list type="bullet">
    ///   <item><c>entity_id</c> (int) — resolved from <c>entity_ref</c> by the executor.</item>
    ///   <item><c>velocity_x</c> (float) — metres per second along the X axis.</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class AddMovingTagActionHandler : ITestActionHandler
    {
        private readonly EntityRepository? _world;
        private readonly ILogger _log;

        public string ActionName => "add_moving_tag";

        public AddMovingTagActionHandler(EntityRepository? world, ILogger log)
        {
            _world = world;
            _log   = log;
        }

        public Task<object?> ExecuteAsync(Dictionary<string, object> args)
        {
            if (_world == null)
            {
                _log.LogWarning("add_moving_tag: no EntityRepository — skipping.");
                return Task.FromResult<object?>(null);
            }

            int entityIdx = args.TryGetValue("entity_id", out var ve) ? Convert.ToInt32(ve) : -1;
            float velocityX = args.TryGetValue("velocity_x", out var vx)
                ? (float)Convert.ToDouble(vx)
                : 0f;

            var entity = _world.GetEntityByIndex(entityIdx);
            if (!_world.IsAlive(entity))
            {
                _log.LogWarning("add_moving_tag: entity {Id} is not alive.", entityIdx);
                return Task.FromResult<object?>(null);
            }

            if (_world.HasComponent<MovingTestTag>(entity))
            {
                ref var existing = ref _world.GetComponentRW<MovingTestTag>(entity);
                existing.VelocityX = velocityX;
            }
            else
            {
                _world.AddComponent(entity, new MovingTestTag { VelocityX = velocityX });
            }

            _log.LogDebug("add_moving_tag: entity {Id} VelocityX={V}", entityIdx, velocityX);
            return Task.FromResult<object?>(new Dictionary<string, object> { ["tagged"] = 1 });
        }
    }
}
