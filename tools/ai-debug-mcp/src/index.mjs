/**
 * ai-debug-mcp — Thin stdio MCP proxy for the Hrot ClusterRunner AI Debug HTTP API.
 *
 * Design: strictly 1:1 mapping of MCP tools ↔ HTTP endpoints (groups A–N, BATCHes 02–05).
 * No business logic — the API owns all semantics; this server relays the
 * {ok, data, error, awaited} envelope verbatim.
 *
 * Tools for not-yet-built endpoints (breakpoints, checkpoint, recording, logs, traces,
 * mutation) are intentionally absent — they will be added in their own batches as
 * those API endpoints land.
 *
 * Usage:
 *   Launch mode (server spawns runner):
 *     node src/index.mjs --runner-dll <path/to/Hrot.ClusterRunner.dll> --port <N> [--headless]
 *
 *   Attach mode (runner already running):
 *     node src/index.mjs --url http://localhost:<N>
 */

import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
} from '@modelcontextprotocol/sdk/types.js';
import { spawn } from 'node:child_process';
import { parseArgs } from 'node:util';

// ── Config parsing ──────────────────────────────────────────────────────────

const { values: args } = parseArgs({
  args: process.argv.slice(2),
  options: {
    url: { type: 'string' },
    'runner-dll': { type: 'string' },
    port: { type: 'string' },
    headless: { type: 'boolean', default: false },
  },
  strict: false,
});

// ── Process lifecycle state ─────────────────────────────────────────────────

/** The spawned runner child process (null in attach mode or before start_simulation). */
let runnerChild = null;
/** Base URL for the Debug API (set after launch or from --url). */
let baseUrl = args.url || null;

const LAUNCH_POLL_INTERVAL_MS = 1000;
const LAUNCH_TIMEOUT_MS = 60_000;
const GRACEFUL_KILL_TIMEOUT_MS = 10_000;

/**
 * Spawn the runner, poll /status until ready, set baseUrl, own the child.
 * @param {string} runnerDll  Path to Hrot.ClusterRunner.dll
 * @param {number} port       Debug API port
 * @param {boolean} headless  Pass --headless flag
 */
async function launchRunner(runnerDll, port, headless) {
  const dllArgs = [
    runnerDll,
    '-m', 'editor',
    '--debug-api',
    '--debug-api-port', String(port),
  ];
  if (headless) dllArgs.push('--headless');

  runnerChild = spawn('dotnet', dllArgs, {
    stdio: ['ignore', 'pipe', 'pipe'],
    windowsHide: true,
  });

  runnerChild.stdout.on('data', (d) => process.stderr.write(`[runner stdout] ${d}`));
  runnerChild.stderr.on('data', (d) => process.stderr.write(`[runner stderr] ${d}`));

  runnerChild.on('exit', (code, signal) => {
    process.stderr.write(`[runner] exited code=${code} signal=${signal}\n`);
    runnerChild = null;
  });

  baseUrl = `http://localhost:${port}`;

  // Poll until /status returns 200 or timeout
  const deadline = Date.now() + LAUNCH_TIMEOUT_MS;
  while (Date.now() < deadline) {
    try {
      const resp = await fetch(`${baseUrl}/status`, { signal: AbortSignal.timeout(2000) });
      if (resp.ok) {
        process.stderr.write(`[mcp] runner ready at ${baseUrl}\n`);
        return;
      }
    } catch {
      // not ready yet
    }
    await sleep(LAUNCH_POLL_INTERVAL_MS);
  }
  throw new Error(`Runner did not become ready within ${LAUNCH_TIMEOUT_MS / 1000}s`);
}

/**
 * Graceful→hard kill the launched runner.
 * 1. POST /shutdown (empty body so Content-Length is set)
 * 2. Wait up to GRACEFUL_KILL_TIMEOUT_MS
 * 3. SIGKILL if still alive
 */
async function killRunner() {
  if (!runnerChild) return;
  const child = runnerChild;

  // Graceful: POST /shutdown
  if (baseUrl) {
    try {
      await fetch(`${baseUrl}/shutdown`, {
        method: 'POST',
        body: '',           // explicit empty body → Content-Length: 0 (avoids 411)
        headers: { 'Content-Type': 'application/json' },
        signal: AbortSignal.timeout(5000),
      });
    } catch {
      // ignore — process may have already gone
    }
  }

  // Wait for process exit
  const exited = await Promise.race([
    new Promise((res) => child.on('exit', () => res(true))),
    sleep(GRACEFUL_KILL_TIMEOUT_MS).then(() => false),
  ]);

  if (!exited && child.exitCode === null) {
    process.stderr.write('[mcp] runner did not exit gracefully — sending SIGKILL\n');
    child.kill('SIGKILL');
  }
  runnerChild = null;
}

// Tear down runner on server exit
const teardown = async () => {
  await killRunner();
  process.exit(0);
};
process.on('SIGINT', teardown);
process.on('SIGTERM', teardown);
process.on('exit', () => {
  // Synchronous fallback: if child is still alive, kill it hard
  if (runnerChild && runnerChild.exitCode === null) {
    try { runnerChild.kill('SIGKILL'); } catch { /* ignore */ }
  }
});

// ── Generic API helper ──────────────────────────────────────────────────────

/**
 * Call a Debug API endpoint.
 *
 * Rules:
 * - GET: no body
 * - POST with body: JSON stringify + Content-Type: application/json
 * - POST with no body: send empty string so fetch sets Content-Length: 0 (avoids 411)
 *
 * Returns the parsed {ok, data, error, awaited} envelope verbatim on success.
 * Throws a structured MCP tool error on HTTP error or ok:false.
 */
async function callApi(method, path, body) {
  if (!baseUrl) throw new McpToolError('No runner URL configured. Use start_simulation first or pass --url.');

  const url = `${baseUrl}${path}`;
  const init = { method };

  if (method === 'POST') {
    if (body !== undefined && body !== null) {
      init.body = JSON.stringify(body);
      init.headers = { 'Content-Type': 'application/json' };
    } else {
      // Bodyless POST — must set Content-Length: 0 to avoid 411 from HttpListener
      init.body = '';
      init.headers = { 'Content-Type': 'application/json' };
    }
  }

  let resp;
  try {
    resp = await fetch(url, init);
  } catch (err) {
    throw new McpToolError(`Network error calling ${method} ${path}: ${err.message}`);
  }

  let envelope;
  try {
    envelope = await resp.json();
  } catch {
    throw new McpToolError(`Non-JSON response from ${method} ${path} (HTTP ${resp.status})`);
  }

  if (!resp.ok || envelope?.ok === false) {
    const msg = envelope?.error || `HTTP ${resp.status} from ${method} ${path}`;
    throw new McpToolError(msg, envelope);
  }

  return envelope;
}

/** Structured MCP tool error that surfaces the API error message. */
class McpToolError extends Error {
  constructor(message, envelope) {
    super(message);
    this.envelope = envelope;
  }
}

function sleep(ms) {
  return new Promise((res) => setTimeout(res, ms));
}

// ── MCP tool result helpers ─────────────────────────────────────────────────

function toolSuccess(envelope) {
  return {
    content: [{ type: 'text', text: JSON.stringify(envelope, null, 2) }],
  };
}

function toolError(message, envelope) {
  return {
    content: [{ type: 'text', text: JSON.stringify({ ok: false, error: message, ...(envelope || {}) }, null, 2) }],
    isError: true,
  };
}

// ── Tool definitions ────────────────────────────────────────────────────────

/**
 * Each tool is { name, description, inputSchema, handler(args) }.
 * handler should return a CallToolResult.
 * Tools are 1:1 with currently-implemented HTTP endpoints (Groups A–N, BATCHes 02–05).
 */
const TOOLS = [

  // ── Group A — Lifecycle & Status ──────────────────────────────────────────

  {
    name: 'start_simulation',
    description:
      'Launch the Hrot ClusterRunner in editor mode with the AI Debug API enabled. ' +
      'Polls /status until ready. MCP-side lifecycle tool — no HTTP endpoint.',
    inputSchema: {
      type: 'object',
      properties: {
        runnerDll: {
          type: 'string',
          description: 'Absolute path to Hrot.ClusterRunner.dll (overrides --runner-dll CLI arg)',
        },
        port: {
          type: 'number',
          description: 'Debug API port (overrides --port CLI arg). Default: 8099',
        },
        headless: {
          type: 'boolean',
          description: 'Pass --headless to the runner. Default: false',
        },
      },
    },
    async handler(toolArgs) {
      try {
        const dll = toolArgs.runnerDll || args['runner-dll'];
        if (!dll) throw new McpToolError('runnerDll is required (or pass --runner-dll to the server)');
        const port = toolArgs.port ?? (args.port ? Number(args.port) : 8099);
        const headless = toolArgs.headless ?? args.headless ?? false;
        await launchRunner(dll, port, headless);
        return toolSuccess({ ok: true, data: { url: baseUrl, pid: runnerChild?.pid } });
      } catch (err) {
        return toolError(err.message, err.envelope);
      }
    },
  },

  {
    name: 'stop_simulation',
    description:
      'Shut down the runner gracefully via POST /shutdown, then hard-kill if needed. ' +
      'MCP-side lifecycle tool — also calls the /shutdown HTTP endpoint.',
    inputSchema: { type: 'object', properties: {} },
    async handler() {
      try {
        // Call /shutdown first (envelope passthrough), then tear down child
        let envelope = { ok: true };
        try {
          envelope = await callApi('POST', '/shutdown', null);
        } catch (err) {
          // If runner is already gone that's fine; still try to kill
          if (!runnerChild) return toolSuccess({ ok: true, data: { note: 'runner already gone' } });
        }
        await killRunner();
        return toolSuccess(envelope);
      } catch (err) {
        return toolError(err.message, err.envelope);
      }
    },
  },

  {
    name: 'get_status',
    description: 'GET /status — runner liveness + sim state summary.',
    inputSchema: { type: 'object', properties: {} },
    async handler() {
      try { return toolSuccess(await callApi('GET', '/status')); }
      catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  // ── Group B — Queries ─────────────────────────────────────────────────────

  {
    name: 'list_entities',
    description:
      'GET /entities — list all entities with networkId, name, and component names. ' +
      'Optional query parameters: component (filter by component type), near (x,y,r spatial filter).',
    inputSchema: {
      type: 'object',
      properties: {
        component: { type: 'string', description: 'Filter: only entities that have this component type' },
        near: { type: 'string', description: 'Spatial filter: "x,y,r" (comma-separated floats)' },
      },
    },
    async handler(toolArgs) {
      try {
        const params = new URLSearchParams();
        if (toolArgs.component) params.set('component', toolArgs.component);
        if (toolArgs.near) params.set('near', toolArgs.near);
        const qs = params.toString() ? `?${params}` : '';
        return toolSuccess(await callApi('GET', `/entities${qs}`));
      } catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'get_entity',
    description: 'GET /entities/{networkId} — full component dump for one entity.',
    inputSchema: {
      type: 'object',
      required: ['networkId'],
      properties: {
        networkId: { type: 'number', description: 'Network entity ID (long)' },
      },
    },
    async handler(toolArgs) {
      try { return toolSuccess(await callApi('GET', `/entities/${toolArgs.networkId}`)); }
      catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'list_component_types',
    description: 'GET /components — enumerate registered ECS component types with field schemas.',
    inputSchema: { type: 'object', properties: {} },
    async handler() {
      try { return toolSuccess(await callApi('GET', '/components')); }
      catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'list_scenarios',
    description: 'GET /scenarios — list available scenarios by relative path.',
    inputSchema: { type: 'object', properties: {} },
    async handler() {
      try { return toolSuccess(await callApi('GET', '/scenarios')); }
      catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  // ── Group C — Event History ───────────────────────────────────────────────

  {
    name: 'get_event_history',
    description:
      'GET /events — query the diagnostic event history. ' +
      'Parameters: bus (world|orchestration, default world), type (event type filter), ' +
      'since (frame number), max (result limit, default 200).',
    inputSchema: {
      type: 'object',
      properties: {
        bus: { type: 'string', enum: ['world', 'orchestration'], description: 'Event bus to query' },
        type: { type: 'string', description: 'Filter by event type name' },
        since: { type: 'number', description: 'Return events since this frame number' },
        max: { type: 'number', description: 'Maximum events to return (default 200)' },
      },
    },
    async handler(toolArgs) {
      try {
        const params = new URLSearchParams();
        if (toolArgs.bus) params.set('bus', toolArgs.bus);
        if (toolArgs.type) params.set('type', toolArgs.type);
        if (toolArgs.since != null) params.set('since', String(toolArgs.since));
        if (toolArgs.max != null) params.set('max', String(toolArgs.max));
        const qs = params.toString() ? `?${params}` : '';
        return toolSuccess(await callApi('GET', `/events${qs}`));
      } catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  // ── Group D — Sim / Preview / Time Control ────────────────────────────────

  {
    name: 'get_sim_state',
    description: 'GET /sim/state — current sim state: isPaused, inPreview, totalTime, timeScale.',
    inputSchema: { type: 'object', properties: {} },
    async handler() {
      try { return toolSuccess(await callApi('GET', '/sim/state')); }
      catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'play',
    description: 'POST /sim/play — enter preview and/or resume if paused.',
    inputSchema: { type: 'object', properties: {} },
    async handler() {
      try { return toolSuccess(await callApi('POST', '/sim/play', null)); }
      catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'pause',
    description: 'POST /sim/pause — pause the simulation.',
    inputSchema: { type: 'object', properties: {} },
    async handler() {
      try { return toolSuccess(await callApi('POST', '/sim/pause', null)); }
      catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'step',
    description: 'POST /sim/step — advance simulation by N discrete steps (default 1).',
    inputSchema: {
      type: 'object',
      properties: {
        count: { type: 'number', description: 'Number of steps to advance (default 1)' },
      },
    },
    async handler(toolArgs) {
      try {
        const body = toolArgs.count != null ? { count: toolArgs.count } : {};
        return toolSuccess(await callApi('POST', '/sim/step', body));
      } catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'set_time_scale',
    description: 'POST /sim/timescale — set simulation time scale.',
    inputSchema: {
      type: 'object',
      required: ['scale'],
      properties: {
        scale: { type: 'number', description: 'Time scale factor (1.0 = real-time)' },
      },
    },
    async handler(toolArgs) {
      try { return toolSuccess(await callApi('POST', '/sim/timescale', { scale: toolArgs.scale })); }
      catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'enter_preview',
    description: 'POST /preview/enter — enter preview mode.',
    inputSchema: {
      type: 'object',
      properties: {
        startPaused: { type: 'boolean', description: 'Start preview in paused state' },
      },
    },
    async handler(toolArgs) {
      try {
        const body = toolArgs.startPaused != null ? { startPaused: toolArgs.startPaused } : {};
        return toolSuccess(await callApi('POST', '/preview/enter', body));
      } catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'stop_preview',
    description: 'POST /preview/exit — exit preview mode (rewinds the sim to pre-preview state).',
    inputSchema: { type: 'object', properties: {} },
    async handler() {
      try { return toolSuccess(await callApi('POST', '/preview/exit', null)); }
      catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  // ── Group E — Scenario Load ───────────────────────────────────────────────

  {
    name: 'load_scenario',
    description:
      'POST /scenario/load — load a scenario by name. ' +
      'Set waitForReady:true to block until the cluster reaches OperatingEdit.',
    inputSchema: {
      type: 'object',
      required: ['name'],
      properties: {
        name: { type: 'string', description: 'Scenario name (relative path)' },
        waitForReady: {
          type: 'boolean',
          description: 'Wait for cluster to reach OperatingEdit before returning',
        },
      },
    },
    async handler(toolArgs) {
      try {
        return toolSuccess(await callApi('POST', '/scenario/load', {
          name: toolArgs.name,
          waitForReady: toolArgs.waitForReady ?? false,
        }));
      } catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'save_scenario',
    description: 'POST /scenario/save — save the current authored world as a scenario.',
    inputSchema: {
      type: 'object',
      required: ['name'],
      properties: {
        name: { type: 'string', description: 'Scenario file name to save as' },
      },
    },
    async handler(toolArgs) {
      try {
        return toolSuccess(await callApi('POST', '/scenario/save', { name: toolArgs.name }));
      } catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  // ── Group F — Entity Commands ─────────────────────────────────────────────

  {
    name: 'list_commands',
    description: 'GET /commands — enumerate publishable FDP event types with field schemas.',
    inputSchema: { type: 'object', properties: {} },
    async handler() {
      try { return toolSuccess(await callApi('GET', '/commands')); }
      catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'send_entity_command',
    description:
      'POST /entities/command — publish an FDP event by type name. ' +
      'Set wait:true to attempt correlated-ack wait (awaited:false if sim not running).',
    inputSchema: {
      type: 'object',
      required: ['eventType'],
      properties: {
        eventType: { type: 'string', description: 'FDP event type name (e.g. MissionControlIntent)' },
        payload: { type: 'object', description: 'Event fields as JSON object' },
        wait: { type: 'boolean', description: 'Attempt to wait for correlated ack' },
      },
    },
    async handler(toolArgs) {
      try {
        return toolSuccess(await callApi('POST', '/entities/command', {
          eventType: toolArgs.eventType,
          payload: toolArgs.payload ?? {},
          wait: toolArgs.wait ?? false,
        }));
      } catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'spawn_entity',
    description: 'POST /entities/spawn — spawn an entity from a TKB type.',
    inputSchema: {
      type: 'object',
      required: ['tkbType'],
      properties: {
        tkbType: { type: 'number', description: 'TKB type ID (long)' },
        transform: {
          type: 'object',
          description: 'Transform: { position: {x,y,z}, rotation: {x,y,z,w} }',
        },
        components: { type: 'array', description: 'Additional component overrides' },
        attributesJson: {
          type: 'string',
          description: 'JSON string of attribute overrides (JsonAttributeCompiler patch)',
        },
      },
    },
    async handler(toolArgs) {
      try {
        const body = { tkbType: toolArgs.tkbType };
        if (toolArgs.transform != null) body.transform = toolArgs.transform;
        if (toolArgs.components != null) body.components = toolArgs.components;
        if (toolArgs.attributesJson != null) body.attributesJson = toolArgs.attributesJson;
        return toolSuccess(await callApi('POST', '/entities/spawn', body));
      } catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  // ── Group M — TKB Catalog ─────────────────────────────────────────────────

  {
    name: 'list_entity_types',
    description: 'GET /tkb/types — list entity types (TKB templates) with id, name, category, disType.',
    inputSchema: {
      type: 'object',
      properties: {
        category: { type: 'string', description: 'Filter by category path' },
      },
    },
    async handler(toolArgs) {
      try {
        const qs = toolArgs.category ? `?category=${encodeURIComponent(toolArgs.category)}` : '';
        return toolSuccess(await callApi('GET', `/tkb/types${qs}`));
      } catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'get_entity_type',
    description:
      'GET /tkb/types/{tkbType} — full TKB descriptor: mandatory components, ' +
      'child blueprints, DIS type, and descriptor DTOs.',
    inputSchema: {
      type: 'object',
      required: ['tkbType'],
      properties: {
        tkbType: { type: 'number', description: 'TKB type ID (long)' },
      },
    },
    async handler(toolArgs) {
      try { return toolSuccess(await callApi('GET', `/tkb/types/${toolArgs.tkbType}`)); }
      catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  // ── Group N — World / Coordinate Info ────────────────────────────────────

  {
    name: 'get_world_info',
    description:
      'GET /world/info — world metadata: geo origin, spatial grid extent. ' +
      'terrain and navmesh are null in editor mode.',
    inputSchema: { type: 'object', properties: {} },
    async handler() {
      try { return toolSuccess(await callApi('GET', '/world/info')); }
      catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'geo_to_local',
    description:
      'POST /world/geo-to-local — convert geographic coordinates to local ENU {x,y,z}. ' +
      'Optional headingDeg → rotation quaternion.',
    inputSchema: {
      type: 'object',
      required: ['lat', 'lon', 'alt'],
      properties: {
        lat: { type: 'number', description: 'Latitude (degrees)' },
        lon: { type: 'number', description: 'Longitude (degrees)' },
        alt: { type: 'number', description: 'Altitude (meters)' },
        headingDeg: { type: 'number', description: 'Optional heading (degrees CW from North) → rotation quaternion' },
      },
    },
    async handler(toolArgs) {
      try {
        const body = { lat: toolArgs.lat, lon: toolArgs.lon, alt: toolArgs.alt };
        if (toolArgs.headingDeg != null) body.headingDeg = toolArgs.headingDeg;
        return toolSuccess(await callApi('POST', '/world/geo-to-local', body));
      } catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'local_to_geo',
    description:
      'POST /world/local-to-geo — convert local ENU {x,y,z} to geographic coordinates. ' +
      'Optional rotation quaternion → headingDeg.',
    inputSchema: {
      type: 'object',
      required: ['x', 'y', 'z'],
      properties: {
        x: { type: 'number', description: 'Local X (meters East)' },
        y: { type: 'number', description: 'Local Y (meters Up)' },
        z: { type: 'number', description: 'Local Z (meters North)' },
        rotation: {
          type: 'object',
          description: 'Optional quaternion {x,y,z,w} → headingDeg in response',
          properties: {
            x: { type: 'number' }, y: { type: 'number' },
            z: { type: 'number' }, w: { type: 'number' },
          },
        },
      },
    },
    async handler(toolArgs) {
      try {
        const body = { x: toolArgs.x, y: toolArgs.y, z: toolArgs.z };
        if (toolArgs.rotation != null) body.rotation = toolArgs.rotation;
        return toolSuccess(await callApi('POST', '/world/local-to-geo', body));
      } catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  // ── Group G — Breakpoints (ADA-BATCH-07) ─────────────────────────────────

  {
    name: 'set_breakpoint',
    description:
      'POST /breakpoints — register a run-until-condition breakpoint. ' +
      'condition is a polymorphic SearchPredicateDto JSON object (use $type discriminator: ' +
      'Lifecycle, PropertyMatch, TransientEvent, Compound, Structural, SpatialBounding, etc.). ' +
      'Returns { breakpointId }.',
    inputSchema: {
      type: 'object',
      required: ['condition'],
      properties: {
        condition: {
          type: 'object',
          description: 'SearchPredicateDto with $type discriminator (e.g. {"$type":"Lifecycle","IdentifierType":"NameSubstring","TargetValue":"Alpha","NamePropertyPath":"Name"})',
        },
        filterNetworkId: {
          type: 'number',
          description: 'Optional: only trigger for this entity (network ID)',
        },
        occurrenceThreshold: {
          type: 'number',
          description: 'Number of hits before pausing (default 1)',
        },
        name: {
          type: 'string',
          description: 'Human-readable label for the breakpoint',
        },
      },
    },
    async handler(toolArgs) {
      try {
        const body = { condition: toolArgs.condition };
        if (toolArgs.filterNetworkId != null) body.filterNetworkId = toolArgs.filterNetworkId;
        if (toolArgs.occurrenceThreshold != null) body.occurrenceThreshold = toolArgs.occurrenceThreshold;
        if (toolArgs.name != null) body.name = toolArgs.name;
        return toolSuccess(await callApi('POST', '/breakpoints', body));
      } catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'list_breakpoints',
    description: 'GET /breakpoints — list all registered breakpoints with id, conditionSummary, enabled, occurrenceThreshold, hitCount, name.',
    inputSchema: { type: 'object', properties: {} },
    async handler() {
      try { return toolSuccess(await callApi('GET', '/breakpoints')); }
      catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'remove_breakpoint',
    description: 'DELETE /breakpoints/{id} — remove a breakpoint by its ID string (e.g. "BP#1").',
    inputSchema: {
      type: 'object',
      required: ['id'],
      properties: {
        id: { type: 'string', description: 'Breakpoint ID string (e.g. "BP#1" from set_breakpoint or list_breakpoints)' },
      },
    },
    async handler(toolArgs) {
      try { return toolSuccess(await callApi('DELETE', `/breakpoints/${encodeURIComponent(toolArgs.id)}`)); }
      catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'get_breakpoint_status',
    description:
      'GET /breakpoints/hits — current pause state and last breakpoint hit. ' +
      'Returns { isPaused, pausedTick, lastHit: { breakpointId, networkId } | null }.',
    inputSchema: { type: 'object', properties: {} },
    async handler() {
      try { return toolSuccess(await callApi('GET', '/breakpoints/hits')); }
      catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  // ── Group H — Checkpoint / Restore / Diff (ADA-BATCH-08) ─────────────────

  {
    name: 'checkpoint',
    description:
      'POST /checkpoint — take a single-slot RAM snapshot via IPreviewController.EnterPreviewMode(startPaused:true). ' +
      'Returns 409 if a live run is active. Returns 400 if already in preview/checkpointed. ' +
      'Single slot: mutually exclusive with /preview/enter.',
    inputSchema: { type: 'object', properties: {} },
    async handler() {
      try { return toolSuccess(await callApi('POST', '/checkpoint', null)); }
      catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'restore_checkpoint',
    description:
      'POST /checkpoint/restore — rewind the simulation to the checkpointed state via IPreviewController.ExitPreviewMode(). ' +
      'Returns 400 if no checkpoint is active.',
    inputSchema: { type: 'object', properties: {} },
    async handler() {
      try { return toolSuccess(await callApi('POST', '/checkpoint/restore', null)); }
      catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'capture_diff_baseline',
    description:
      'POST /diff/capture — serialize current entity states server-side and return a baselineId. ' +
      'Use before mutating the world, then call diff_state with the baselineId to see what changed. ' +
      'Optional entities array (networkId list) scopes which entities to capture (default: all).',
    inputSchema: {
      type: 'object',
      properties: {
        entities: {
          type: 'array',
          items: { type: 'number' },
          description: 'Optional list of networkIds to capture (default: all entities)',
        },
      },
    },
    async handler(toolArgs) {
      try {
        const body = {};
        if (toolArgs.entities != null) body.entities = toolArgs.entities;
        return toolSuccess(await callApi('POST', '/diff/capture', body));
      } catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'diff_state',
    description:
      'POST /diff/compare — compare a previously captured baseline against current entity state. ' +
      'Returns a per-entity diff tree showing only what changed (token-efficient). ' +
      'baselineId comes from capture_diff_baseline. ' +
      'Optional entities array scopes which entities to diff.',
    inputSchema: {
      type: 'object',
      required: ['baselineId'],
      properties: {
        baselineId: {
          type: 'string',
          description: 'Baseline ID from capture_diff_baseline (e.g. "BL#1")',
        },
        entities: {
          type: 'array',
          items: { type: 'number' },
          description: 'Optional list of networkIds to diff (default: all entities in baseline)',
        },
      },
    },
    async handler(toolArgs) {
      try {
        const body = { baselineId: toolArgs.baselineId };
        if (toolArgs.entities != null) body.entities = toolArgs.entities;
        return toolSuccess(await callApi('POST', '/diff/compare', body));
      } catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  // ── Group I — Recording + Replay (ADA-BATCH-10) ──────────────────────────

  {
    name: 'start_recording',
    description:
      'POST /recording/start — start recording. mode="preview" (revertible, EnterPreviewMode→PrepareRecordingAsync) ' +
      'or mode="live" (not supported in editor mode). ' +
      'Returns { recording:true, mode, fdpPath }. ' +
      'Mutually exclusive with checkpoint (both use the preview slot).',
    inputSchema: {
      type: 'object',
      properties: {
        mode: {
          type: 'string',
          enum: ['preview', 'live'],
          description: 'Recording mode: "preview" (revertible) or "live" (not supported in editor mode). Default: "preview"',
        },
      },
    },
    async handler(toolArgs) {
      try {
        return toolSuccess(await callApi('POST', '/recording/start', { mode: toolArgs.mode ?? 'preview' }));
      } catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'stop_recording',
    description:
      'POST /recording/stop — stop the active recording. ' +
      'For preview mode: finalizes BEFORE the exit rewind (hard ordering rule). ' +
      'Returns { recording:false, fdpPath }.',
    inputSchema: { type: 'object', properties: {} },
    async handler() {
      try { return toolSuccess(await callApi('POST', '/recording/stop', null)); }
      catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'load_replay',
    description:
      'POST /replay/load {fdpPath} — load a .fdp recording into an ISOLATED ReplayBrowserContext. ' +
      'Returns { loaded:true, fdpPath, totalFrames, currentFrame }. ' +
      'While replay is active, /replay/entities returns entities from the sandbox (not the live world).',
    inputSchema: {
      type: 'object',
      required: ['fdpPath'],
      properties: {
        fdpPath: { type: 'string', description: 'Absolute path to the .fdp recording file' },
      },
    },
    async handler(toolArgs) {
      try {
        return toolSuccess(await callApi('POST', '/replay/load', { fdpPath: toolArgs.fdpPath }));
      } catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'seek_replay',
    description:
      'POST /replay/seek {frame} — seek to a specific frame in the ISOLATED sandbox. ' +
      'Does NOT touch the live _world (isolation guarantee). ' +
      'Returns { frame, totalFrames }.',
    inputSchema: {
      type: 'object',
      required: ['frame'],
      properties: {
        frame: { type: 'number', description: 'Frame index to seek to (0-based)' },
      },
    },
    async handler(toolArgs) {
      try {
        return toolSuccess(await callApi('POST', '/replay/seek', { frame: toolArgs.frame }));
      } catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'step_replay',
    description:
      'POST /replay/step {dir} — step one frame forward or backward in the ISOLATED sandbox. ' +
      'Does NOT touch the live _world. ' +
      'Returns { stepped:bool, frame, totalFrames }.',
    inputSchema: {
      type: 'object',
      properties: {
        dir: {
          type: 'string',
          enum: ['forward', 'back'],
          description: 'Step direction: "forward" or "back". Default: "forward"',
        },
      },
    },
    async handler(toolArgs) {
      try {
        return toolSuccess(await callApi('POST', '/replay/step', { dir: toolArgs.dir ?? 'forward' }));
      } catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'get_replay_status',
    description:
      'GET /replay/status — replay sandbox status: replayActive, currentFrame, totalFrames.',
    inputSchema: { type: 'object', properties: {} },
    async handler() {
      try { return toolSuccess(await callApi('GET', '/replay/status')); }
      catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'list_replay_entities',
    description:
      'GET /replay/entities — list entities from the ISOLATED replay sandbox. ' +
      'Requires an active replay (call load_replay first). ' +
      'Returns same schema as list_entities but from the sandbox repo, NOT the live world.',
    inputSchema: { type: 'object', properties: {} },
    async handler() {
      try { return toolSuccess(await callApi('GET', '/replay/entities')); }
      catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'unload_replay',
    description:
      'POST /replay/unload — dispose the replay sandbox and return to live world queries.',
    inputSchema: { type: 'object', properties: {} },
    async handler() {
      try { return toolSuccess(await callApi('POST', '/replay/unload', null)); }
      catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  // ── Group J — Logs (ADA-BATCH-11) ────────────────────────────────────────

  {
    name: 'get_logs',
    description:
      'GET /logs — query the in-process log sinks (NLogMessageLogTarget + AiBehaviorLogTarget). ' +
      'Returns [{timestamp, level, logger, message}] sorted newest-first. ' +
      'All parameters optional. ' +
      'level = minimum severity (inclusive): Trace, Debug, Info, Warning, Error, Critical. ' +
      'logger = case-insensitive substring match on logger name. ' +
      'since = ISO-8601 timestamp; entries with timestamp >= since are included. ' +
      'max = upper bound on results (default 200). ' +
      'Read off-thread — no main-thread marshal required.',
    inputSchema: {
      type: 'object',
      properties: {
        level: {
          type: 'string',
          enum: ['Trace', 'Debug', 'Info', 'Warning', 'Error', 'Critical'],
          description: 'Minimum severity level (inclusive). Omit to return all levels.',
        },
        logger: {
          type: 'string',
          description: 'Filter by logger name substring (case-insensitive). Omit to return all loggers.',
        },
        since: {
          type: 'string',
          description: 'ISO-8601 timestamp. Only entries with timestamp >= since are returned.',
        },
        max: {
          type: 'number',
          description: 'Maximum number of entries to return (default 200).',
        },
      },
    },
    async handler(toolArgs) {
      try {
        const params = new URLSearchParams();
        if (toolArgs.level) params.set('level', toolArgs.level);
        if (toolArgs.logger) params.set('logger', toolArgs.logger);
        if (toolArgs.since) params.set('since', toolArgs.since);
        if (toolArgs.max != null) params.set('max', String(toolArgs.max));
        const qs = params.toString() ? `?${params}` : '';
        return toolSuccess(await callApi('GET', `/logs${qs}`));
      } catch (err) { return toolError(err.message, err.envelope); }
    },
  },
  // ── Group K — AI Behavior Traces (ADA-BATCH-12) ──────────────────────────

  {
    name: 'observe_trace',
    description:
      'POST /trace/observe — arm or disarm AI behavior trace buffer allocation for an entity. ' +
      'Must arm before get_entity_trace will return populated trace data.',
    inputSchema: {
      type: 'object',
      required: ['networkId', 'on'],
      properties: {
        networkId: { type: 'number', description: 'Network entity ID (long)' },
        on: { type: 'boolean', description: 'true to arm tracing, false to disarm' },
      },
    },
    async handler(toolArgs) {
      try {
        return toolSuccess(await callApi('POST', '/trace/observe', {
          networkId: toolArgs.networkId,
          on: toolArgs.on,
        }));
      } catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'get_entity_trace',
    description:
      'GET /entities/{networkId}/trace — extract AI behavior trace for an entity. ' +
      'Returns BTree active node path + history, HSM active leaves, or blueprint live state. ' +
      'Arm the entity with observe_trace first to populate trace data.',
    inputSchema: {
      type: 'object',
      required: ['networkId'],
      properties: {
        networkId: { type: 'number', description: 'Network entity ID (long)' },
      },
    },
    async handler(toolArgs) {
      try {
        return toolSuccess(await callApi('GET', `/entities/${toolArgs.networkId}/trace`));
      } catch (err) { return toolError(err.message, err.envelope); }
    },
  },


  // ── Group L — Live Mutation / Fault Injection (ADA-BATCH-13) ────────────────

  {
    name: 'get_attributes_schema',
    description:
      'GET /attributes/schema — return all patchable attribute paths and their JSON Schema. ' +
      'Use patch_attribute to apply a patch using these paths.',
    inputSchema: { type: 'object', properties: {} },
    async handler() {
      try { return toolSuccess(await callApi('GET', '/attributes/schema')); }
      catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'patch_attribute',
    description:
      'POST /entities/{networkId}/attribute — apply a JSON attribute patch to an entity. ' +
      'Authority-aware; unregistered keys are silently ignored (no error). ' +
      'patchJson may be a nested JSON object like {"Name":"Alpha"} or a JSON string. ' +
      'Returns the updated entity dump on success.',
    inputSchema: {
      type: 'object',
      required: ['networkId', 'patchJson'],
      properties: {
        networkId: { type: 'number', description: 'Network entity ID (long)' },
        patchJson: {
          description: 'Patch as a JSON object {"Name":"Alpha"} or as a JSON string',
        },
      },
    },
    async handler(toolArgs) {
      try {
        // Accept patchJson as either a nested object or a string.
        // Pass it directly — the server handles both forms.
        return toolSuccess(await callApi('POST', `/entities/${toolArgs.networkId}/attribute`, {
          patchJson: toolArgs.patchJson,
        }));
      } catch (err) { return toolError(err.message, err.envelope); }
    },
  },

  {
    name: 'edit_component',
    description:
      'POST /entities/{networkId}/component — StructEdit escape hatch for arbitrary component fields. ' +
      'Opens a StructEdit session, applies the patch fields, validates via IComponentValidator, ' +
      'and writes the result back to ECS. ' +
      'Invalid values → 400, component unchanged. ' +
      'For fields registered in the attribute schema, prefer patch_attribute.',
    inputSchema: {
      type: 'object',
      required: ['networkId', 'componentType', 'patch'],
      properties: {
        networkId: { type: 'number', description: 'Network entity ID (long)' },
        componentType: { type: 'string', description: 'ECS component type name (e.g. "EntityInfo", "SimTransform")' },
        patch: {
          type: 'object',
          description: 'JSON object with field names and new values to apply to the component',
        },
      },
    },
    async handler(toolArgs) {
      try {
        return toolSuccess(await callApi('POST', `/entities/${toolArgs.networkId}/component`, {
          componentType: toolArgs.componentType,
          patch: toolArgs.patch,
        }));
      } catch (err) { return toolError(err.message, err.envelope); }
    },
  },
];

// ── MCP Server setup ────────────────────────────────────────────────────────

const server = new Server(
  { name: 'ai-debug-mcp', version: '0.1.0' },
  { capabilities: { tools: {} } },
);

server.setRequestHandler(ListToolsRequestSchema, async () => ({
  tools: TOOLS.map((t) => ({
    name: t.name,
    description: t.description,
    inputSchema: t.inputSchema,
  })),
}));

server.setRequestHandler(CallToolRequestSchema, async (request) => {
  const tool = TOOLS.find((t) => t.name === request.params.name);
  if (!tool) {
    return toolError(`Unknown tool: ${request.params.name}`);
  }
  return tool.handler(request.params.arguments || {});
});

// ── Entry point ─────────────────────────────────────────────────────────────

const transport = new StdioServerTransport();
await server.connect(transport);
process.stderr.write(`[mcp] ai-debug-mcp started (${TOOLS.length} tools)\n`);
