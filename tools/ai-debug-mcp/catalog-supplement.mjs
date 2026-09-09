/**
 * catalog-supplement.mjs — tools that are NOT backed by an HTTP endpoint.
 *
 * ⭐ Everything else in tool-catalog.mjs is GENERATED from `--mode dump-api` (HN-030), so it cannot drift
 *    from the routes. This file is the deliberate exception, and it is deliberately tiny.
 *
 * ⛔ `start_simulation` spawns the runner process from inside the MCP server. There is no endpoint to
 *    describe — the server is what creates the thing that would serve one. So it can never be route-derived,
 *    and a generator that pretended otherwise would be inventing an endpoint.
 *
 * ⚠ This file is the ONE part of the catalog that generation cannot police, which is exactly why
 *    test-catalog.mjs still reconciles the whole catalog against the dumped route table: a supplement entry
 *    that starts colliding with a real route, or names a path that does not exist, has to fail somewhere.
 *
 * ⭐ Keep it to tools that genuinely have no endpoint. Anything reachable over HTTP belongs on the route,
 *    as a RouteDoc in Hrot.Editor/DebugApi/DebugApiRouteDocs.cs.
 */

export const CATALOG_SUPPLEMENT = [
  {
    name: 'start_simulation',
    group: 'A — Lifecycle & status',
    summary: 'Launch the Hrot ClusterRunner with the AI Debug API enabled, in editor or cluster mode. Polls /status until ready.',
    http: null,
    params: [
      { name: 'runnerDll', type: 'string', required: false, description: 'Absolute path to Hrot.ClusterRunner.dll (overrides --runner-dll CLI arg)' },
      { name: 'port', type: 'number', required: false, description: 'Debug API port (overrides --port CLI arg). Default: 8099', default: 8099 },
      { name: 'mode', type: 'string', required: false, description: 'Runner mode: "editor" (one node, everything local) or a cluster mode such as "all" (orchestrator + simhost + ig + excon + cgf). Default: editor', default: 'editor' },
      { name: 'headless', type: 'boolean', required: false, description: 'Pass --headless to the runner. Editor mode only — see notes. Default: false', default: false },
    ],
    returns: '{ url, pid, mode }',
    notes: [
      'MCP-side lifecycle tool — no HTTP endpoint.',
      'runnerDll is required unless the server was started with --runner-dll.',
      'A cluster mode ("all") serves the same API but commands act in the currently selected perspective — call get_capabilities and switch_perspective.',
      'headless is REFUSED for a cluster mode: a panel publishes only when it draws, and the headless runner loop never draws, so every panel dump would come back empty. Launch it windowed (under Xvfb on Linux).',
    ],
    example: { args: { runnerDll: '/path/to/Hrot.ClusterRunner.dll', port: 8099, mode: 'all' }, gist: 'launch the whole cluster in one process on the default port' },
    hint: 'Required (if no --runner-dll on server): runnerDll (string). Optional: mode ("editor"|"all"), port, headless. Example: start_simulation({runnerDll:"/path/to/dll", mode:"all"})',
    manualVerify: false,
  },
];
