"""One cluster node per process, from Python — standard library only, runs on Windows and Linux (CE-3427, the scale rig on a many-core
Windows machine: RESUME_Backend_Large_Terrain_Perf §2).

The same commands as scripts/cluster-node.sh (start / status / get / post / kill / stop-all), which stays the Linux path for the shell
users; perf-stress.py and scale-rig.py call this module on Windows, where setsid / nohup / xvfb-run / pgrep / /proc do not exist.

    start(mode, port, base=0, domain=0)   -> "up: ..." once GET /status answers (or "NOT UP ...")
    get(port, route) / post(port, route, body)  -> the reply text (JSON), "" when nothing answers
    kill(port) / stop_all()               -> stop the node(s) this module started (pid files survive a separate invocation)
    pid(port)                             -> the node's process id (for dotnet-trace)
    log_path(mode, port)                  -> where its stdout/stderr went

⛔ Never kills by image name (taskkill /im dotnet.exe kills the build servers and the test host): only pids it wrote down, plus — for a
stop-all — processes whose COMMAND LINE runs Hrot.ClusterRunner.dll.
The runner: HROT_RUNNER_DLL, else the repo's Release, else Debug build. HROT_NODE_ARGS appends arguments to every node (as cluster-node.sh).
"""
import json, os, shutil, subprocess, sys, tempfile, time, urllib.error, urllib.request

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
STATE = os.path.join(tempfile.gettempdir(), "hrot-nodes")      # pid files + logs, shared between invocations
OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}))   # never through a proxy (RUNBOOK §2)
WINDOWS = os.name == "nt"
AFFINITY = {}   # port -> CPU mask (bit i = logical CPU i); set before start() to pin that node to its own cores (the scale rig's --pin)


def runner_dll():
    dll = os.environ.get("HROT_RUNNER_DLL")
    if dll: return dll
    for cfg in ("Release", "Debug"):
        p = os.path.join(REPO, "Hrot", "Runner", "Hrot.ClusterRunner", "bin", cfg, "net8.0", "Hrot.ClusterRunner.dll")
        if os.path.exists(p): return p
    raise SystemExit("Hrot.ClusterRunner.dll not found — build the runner or set HROT_RUNNER_DLL / pass --runner-dll")


def log_path(mode, port):
    return os.path.join(STATE, f"n-{mode}-{port}.log")


def _pid_file(port):
    return os.path.join(STATE, f"n-{port}.pid")


def http(method, port, route, body=None, timeout=20):
    """The reply body as text; "" when nothing answers. The HOSTNAME localhost — 127.0.0.1 404s every route (RUNBOOK §2)."""
    data = body.encode() if isinstance(body, str) else (json.dumps(body).encode() if body is not None else None)
    req = urllib.request.Request(f"http://localhost:{port}{route}", data=data, method=method, headers={"Content-Type": "application/json"})
    try:
        with OPENER.open(req, timeout=timeout) as r:
            return r.read().decode(errors="replace")
    except urllib.error.HTTPError as e:
        return e.read().decode(errors="replace")
    except (urllib.error.URLError, ConnectionError, TimeoutError, OSError):
        return ""


def get(port, route, timeout=20):
    return http("GET", port, route, timeout=timeout)


def post(port, route, body="{}", timeout=20):
    return http("POST", port, route, body or "{}", timeout=timeout)


def pid(port):
    try:
        return int(open(_pid_file(port)).read().strip())
    except (OSError, ValueError):
        return None


def start(mode, port, base=0, domain=0):
    os.makedirs(STATE, exist_ok=True)
    kill(port)   # a later start of the same port must not orphan the earlier node (CE-4029)
    args = ["--mode", mode, "--no-wait"] + ([] if str(base) == "auto" else ["-n", str(base)]) + ["-d", str(domain)]
    args += os.environ.get("HROT_NODE_ARGS", "").split()
    env = dict(os.environ, HROT_DEBUG_API_PORT=str(port))
    env.setdefault("HROT_HIDDEN_WINDOW", "1")   # CE-4386 — a windowed node (--windowed) never pops up on the operator's desktop; =0 shows it
    dll = runner_dll()
    log = open(log_path(mode, port), "wb")
    flags = (subprocess.CREATE_NEW_PROCESS_GROUP | subprocess.CREATE_NO_WINDOW) if WINDOWS else 0
    p = subprocess.Popen([shutil.which("dotnet") or "dotnet", dll] + args, cwd=os.path.dirname(dll), env=env, stdout=log,
                         stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL, creationflags=flags, start_new_session=not WINDOWS)
    log.close()
    open(_pid_file(port), "w").write(str(p.pid))
    if AFFINITY.get(int(port)):
        pin(p.pid, AFFINITY[int(port)])
    for _ in range(120):   # wait for the debug API (no fixed sleep)
        if '"ok"' in get(port, "/status", timeout=5):
            return f"up: {mode} port={port} base={base} domain={domain} pid={p.pid}"
        if p.poll() is not None:
            return f"EXITED ({p.returncode}): {mode} port={port} — see {log_path(mode, port)}"
        time.sleep(1)
    return f"NOT UP after 120 s: {mode} port={port} — see {log_path(mode, port)}"


def pin(pid_, mask):
    """Pin a process to the logical CPUs in MASK (Windows: SetProcessAffinityMask; Linux: os.sched_setaffinity)."""
    if WINDOWS:
        import ctypes
        k32 = ctypes.WinDLL("kernel32", use_last_error=True)
        k32.OpenProcess.restype = ctypes.c_void_p
        h = k32.OpenProcess(0x0200 | 0x0400, False, pid_)   # PROCESS_SET_INFORMATION | PROCESS_QUERY_INFORMATION
        ok = h and k32.SetProcessAffinityMask(ctypes.c_void_p(h), ctypes.c_size_t(mask))
        if h: k32.CloseHandle(ctypes.c_void_p(h))
        if not ok: print(f"  ⚠ could not pin pid {pid_} (error {ctypes.get_last_error()})")
    else:
        os.sched_setaffinity(pid_, {i for i in range(mask.bit_length()) if mask >> i & 1})


def split_cpus(ports, small=2):
    """Masks giving the FIRST port SMALL logical CPUs (the orchestrator: little work) and the rest equal shares of what is left."""
    n = os.cpu_count() or 1
    masks, at = {}, 0
    share = max(1, (n - small) // max(1, len(ports) - 1))
    for i, port in enumerate(ports):
        k = small if i == 0 else (share if i < len(ports) - 1 else n - at)
        masks[int(port)] = ((1 << k) - 1) << at
        at += k
    return masks


def _kill_pid(p):
    if WINDOWS:
        subprocess.run(["taskkill", "/F", "/T", "/PID", str(p)], capture_output=True)
    else:
        try: os.killpg(os.getpgid(p), 9)
        except (OSError, ProcessLookupError):
            try: os.kill(p, 9)
            except OSError: pass


def _alive(p):
    if WINDOWS:
        out = subprocess.run(["tasklist", "/FI", f"PID eq {p}", "/NH"], capture_output=True, text=True).stdout
        return str(p) in out
    try: os.kill(p, 0); return True
    except OSError: return False


def kill(port):
    p = pid(port)
    if p is None: return f"no node recorded on port {port}"
    _kill_pid(p)
    for _ in range(20):
        if not _alive(p): break
        time.sleep(0.5)
    try: os.remove(_pid_file(port))
    except OSError: pass
    return f"killed node on port {port}" if not _alive(p) else f"STILL RUNNING on port {port}: {p}"


def _runner_pids():
    """Every process whose command line runs Hrot.ClusterRunner.dll — by command line, never by image name."""
    if WINDOWS:
        ps = ("Get-CimInstance Win32_Process -Filter \"Name='dotnet.exe'\" | "
              "Where-Object { $_.CommandLine -like '*Hrot.ClusterRunner.dll*' } | ForEach-Object { $_.ProcessId }")
        out = subprocess.run(["powershell", "-NoProfile", "-Command", ps], capture_output=True, text=True).stdout
    else:
        out = subprocess.run(["pgrep", "-f", "ClusterRunner[.]dll"], capture_output=True, text=True).stdout
    return [int(x) for x in out.split() if x.isdigit()]


def stop_all():
    if os.path.isdir(STATE):
        for f in os.listdir(STATE):
            if f.startswith("n-") and f.endswith(".pid"):
                kill(f[2:-4])
    for p in _runner_pids():
        _kill_pid(p)
    return "stopped"


def node(cmd, *args):
    """cluster-node.sh's command line, as a function: start <mode> <port> [base] [domain] · status · get · post · kill · stop-all."""
    a = [str(x) for x in args]
    if cmd == "start":  return start(a[0], int(a[1]), a[2] if len(a) > 2 else 0, a[3] if len(a) > 3 else 0)
    if cmd == "status": return get(int(a[0]), "/status")
    if cmd == "get":    return get(int(a[0]), a[1])
    if cmd == "post":   return post(int(a[0]), a[1], a[2] if len(a) > 2 else "{}")
    if cmd == "kill":   return kill(int(a[0]))
    if cmd == "stop-all": return stop_all()
    raise ValueError(f"unknown command {cmd}")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__); sys.exit(2)
    print(node(sys.argv[1], *sys.argv[2:]))
