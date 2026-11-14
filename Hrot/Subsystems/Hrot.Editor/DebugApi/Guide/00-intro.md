# AI Debug & Test API — Agent Guide

You are driving an FDP simulation (the ClusterRunner — the editor, a node, or `--mode all`) over its **HTTP debug API**.
Every route below is plain HTTP on the process's port (default `http://localhost:8099`; ⚠ `localhost`, not `127.0.0.1`).
The process owns all the real logic; this guide teaches the mental model, the canonical workflows, and every route.

⭐ This text is served by the running process (`GET /help`) and printed before anything is started by
`Hrot.ClusterRunner --help-api` — both RENDERED from the route table, so it always describes the build you are talking to.
`GET /capabilities` adds, as JSON, what THIS host can do right now. How to START a process: `docs/RUNBOOK_Cluster_Debugging_Over_Http.md`.
