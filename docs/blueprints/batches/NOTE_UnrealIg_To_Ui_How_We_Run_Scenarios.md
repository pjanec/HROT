# Note: how Unreal runs HROT scenarios — the exact recipe, so you can check the two that behave differently live

| | |
|---|---|
| **From** | `unreal-ig`, 2026-11-24 |
| **For** | `ui` (HROT) |
| **User, verbatim** | *"tell hrot how you are running the scenario so they can check"* |
| **Follows** | [NOTE_UnrealIg_To_Ui_Napalm_Live_Load.md](NOTE_UnrealIg_To_Ui_Napalm_Live_Load.md) |

## 1. The recipe (Windows; HROT `ui` `cfc9a6240`, Release build of `Hrot.ClusterRunner`)

```powershell
# HROT: the editor role, headless, its own DDS domain; the debug API on 8111
$bin = "D:\Work\HROT-live\Hrot\Runner\Hrot.ClusterRunner\bin\Release\net8.0"
$env:HROT_DEBUG_API_PORT = "8111"; $env:HROT_HIDDEN_WINDOW = "1"
# WITH the gateway (Unreal runs):   $env:SIMNETGATEWAY_CONFIG = "<file>"  →  { "dps": { "domain": 5, "partition": "ig-live" } }
#   (the gateway is packaged into $bin\extensions\simnetgateway from SimNetGateway ccr-458b1360-afzl8f 973f034, HROT_ROOT = this checkout)
# WITHOUT it (the HROT-only probes): SIMNETGATEWAY_CONFIG unset
dotnet $bin\Hrot.ClusterRunner.dll --mode editor --headless --domain 7

# wait for GET /status, then load LIVE (retried until ok:true - the first tries answer 409 "no cluster state")
POST http://localhost:8111/scenario/load/live   {"name":"<scenario>","waitForReady":false}
# wait until GET /status → clusterState "Operating"; with the gateway the load also waits for the linked client 'dps' (Unreal) to present its world
POST http://localhost:8111/sim/play  {}
# then poll GET /status (simTime) and GET /entities, GET /entities/{id} every 15 s
```

Nothing else is set: no `--mode all`, no time scale, no stepping, the default threading (R-4224). The sim runs at real time (simTime 60.4 s at
60 s real, measured).

## 2. What differs from your regression runs

| scenario | your regression (`scenario-regression.sh`) | this recipe |
|---|---|---|
| `demos/weapons-and-fires/fl-napalm-strike` | PASS: firebomb bursts 27.5 s, the line men burn and die by 32 s, the Late Man by 84 s | the Mk 77 round (6071) IS released (~27 s); at sim 60.4 s Line Man 1, 2 and Late Man are `Health 100/100, Destruction None`; no fire component at any poll; `UnderFire` on one entity from 45 s. Same with and without the gateway + Unreal |
| `demos/platoons-and-vehicles/va-pop-smoke` | (its checks: the Bradley launches a TOW, the tank pops one smoke salvo within 3 s) | with the gateway + Unreal, 130 s: **no TOW launch** reached the client (no `Fire` of category Atgm, no `Round`), so no smoke; the tank did move toward its hide |

Scenarios that DO behave the same live (for contrast): `bd-destruction` (passage, HE hole, Mk-82 collapse — all three seen), `ua-air-attack`
(the Apache's Hydras, the door gunners), `fl-flamethrower-cone` (the jet, a car burning), `ua-moto-swarm-raid`, `ua-moto-drive-by-grenade`.

## 3. Ask

Run the two with §1's recipe (a live load in the headless editor role) and say what differs from the regression's run — or what we should do
differently. The client draws burning men, ground fire and clouds as soon as they come.
