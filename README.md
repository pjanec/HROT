# IOS-IG-SimHost

Combined repository for the Hrot Image Generator (IG) mock and SimHost node.

---

## ⚠️ First-Time Setup — Required Before Building

### 1. Build Native CycloneDDS Libraries

**This step must be performed immediately after initial project cloning.** The managed `CycloneDDS.NET` package depends on native `.dll` files compiled from the CycloneDDS C submodule. Without them, DDS reader/writer calls will throw `DllNotFoundException` at runtime.

**Prerequisites:**
- CMake ≥ 3.20 in `PATH`
- Visual Studio 2019 / 2022 (C++ workload) — or equivalent MSVC build tools

**Run once from repo root:**

```powershell
.\FDP\ExtDeps\FastCycloneDds\build\native-win.ps1
```

This compiles the `cyclonedds` submodule and deposits the resulting binaries under
`FDP\ExtDeps\FastCycloneDds\artifacts\native\win-x64\`.

> **CI note:** The above script is idempotent — it is safe to re-run after updating the submodule.

### 2. Restore NuGet packages

```powershell
dotnet restore IOS-IG-SimHost.sln
```

### 3. Build solution

```powershell
dotnet build IOS-IG-SimHost.sln
```

---

## Projects

| Project | Description |
|---|---|
| `Hrot.IG` | IG Mock — Raylib-based Image Generator viewer node (DDS instance 300) |
| `Hrot.SimHost` | SimHost — simulation authority node |
| `Hrot.NED` | Shared DDS descriptor types (Hrot BDC SST) |
| `Hrot.Map.Common` | Shared map constants and gateway commands |
| `Hrot.Map.Definitions` | TKB entity type descriptors |

---

## Running

Start SimHost first (publishes `EntityMaster`, `WorldPos`, etc.), then start `Hrot.IG`.
Both processes communicate via CycloneDDS on domain 0.

```powershell
# Terminal 1 — SimHost
dotnet run --project Hrot.SimHost

# Terminal 2 — IG
dotnet run --project Hrot.IG
```
