# Phase 1 Detailed Design

## Schema, NAS Layout, and Publish Gate

*Companion to Architecture Design Document Revision 2 and API Specification* *Targets `Phase 1` from §21 of the architecture doc* *C# / .NET 8 · Windows · May 2026*

------

## 1. Phase Scope

### 1.1 In Scope

Phase 1 delivers the static, off-line side of the system — what exists on the NAS and how it gets there. No master, no agents, no SignalR, no transfer.

Deliverables:

1. **Manifest schema** covering all four downward data categories
2. **NAS directory layout** with atomic-operation rules
3. **Publish gate protocol** — what makes a version "real"
4. **Publish CLI tool** (`syncpub`) — command surface, internal pipeline, error handling
5. **Reference manifest validator** — schema + semantic rules
6. **Acceptance tests** — what "Phase 1 done" means

### 1.2 Out of Scope (deferred to later phases)

- Master agent and its REST surface (Phase 2)
- Site config, agent slices, SignalR (Phase 3)
- All transfer engines and downloading (Phases 4, 5, 7)
- Activation on nodes (Phases 6, 9)
- Recordings (Phase 8)
- Garbage collection (Phase 11)

### 1.3 Definition of Done

After Phase 1 the operator can:

- Place source files in a directory and run a single CLI command to publish a versioned bundle to the NAS
- Open the NAS and see a consistent, immutable bundle version with a manifest, a gate, and the data files
- Re-verify any published version's integrity offline (no network involvement)
- List, inspect, and re-verify all published versions of all bundles

The output of Phase 1 is the input contract that all later phases consume.

------

## 2. Manifest Schema

The manifest is the single source of truth describing a bundle version. It is written into the version directory and is the authoritative description consumed by master, relays, and agents in later phases.

### 2.1 Common Fields

Present on every manifest regardless of category:

| Field             | Type    | Notes                                                        |
| ----------------- | ------- | ------------------------------------------------------------ |
| `manifestVersion` | int     | Schema version. Currently `1`. Increment on breaking changes. |
| `bundleId`        | string  | Stable id of the bundle. ASCII letters, digits, `-`, `_`, `.`, max 128 chars. |
| `version`         | string  | Version string. Filename-safe (see §2.6). Max 128 chars.     |
| `dataCategory`    | enum    | `RuntimeAsset` | `Config` | `Dataset` | `ChunkedHugeFile`    |
| `container`       | enum    | `zip` | `none`                                               |
| `publishedAt`     | string  | ISO8601 UTC timestamp, e.g. `2026-05-17T12:00:00Z`           |
| `groupHash`       | string  | Single representative hash for the bundle (see §2.5). Lowercase hex SHA-256, prefix `sha256:`. |
| `totalBytes`      | long    | Total on-disk size on the NAS (archive size for zip; file size for raw). |
| `fileCount`       | int     | Logical file count. For zip: entries inside the zip. For raw: always 1. |
| `activation`      | object  | Activation directive (see §2.7).                             |
| `description`     | string? | Optional human-readable description.                         |
| `customTags`      | object? | Optional opaque key-value pairs for publisher metadata.      |

### 2.2 Zip-Variant Fields (RuntimeAsset, Config, Dataset)

Additional fields when `container == "zip"`:

| Field                     | Type   | Notes                                                        |
| ------------------------- | ------ | ------------------------------------------------------------ |
| `archive`                 | object | The zip file metadata (see below).                           |
| `archive.name`            | string | Filename of the zip within the version directory, e.g. `bundle.zip`. |
| `archive.size`            | long   | Size of the zip file in bytes.                               |
| `archive.sha256`          | string | SHA-256 of the zip file contents. Lowercase hex, prefix `sha256:`. |
| `files`                   | array  | List of files inside the zip.                                |
| `files[].relativePath`    | string | Path within the zip. Forward slashes only. No `..` segments. |
| `files[].size`            | long   | Uncompressed file size.                                      |
| `files[].hash`            | string | SHA-256 of the uncompressed file content.                    |
| `files[].compressionMode` | enum   | `store` | `deflate`                                          |

### 2.3 Raw-Variant Fields (ChunkedHugeFile)

Additional fields when `container == "none"`:

| Field             | Type   | Notes                                                      |
| ----------------- | ------ | ---------------------------------------------------------- |
| `file`            | object | The raw file metadata.                                     |
| `file.name`       | string | Filename within the version directory, e.g. `dataset.zip`. |
| `file.size`       | long   | File size in bytes.                                        |
| `chunks`          | array  | Per-chunk metadata, in order from offset 0.                |
| `chunks[].index`  | int    | Zero-based chunk index.                                    |
| `chunks[].offset` | long   | Byte offset within the file.                               |
| `chunks[].size`   | int    | Chunk size in bytes (last chunk may be smaller).           |
| `chunks[].sha256` | string | SHA-256 of the chunk's bytes.                              |

No per-file hash is recorded for the raw variant — whole-file hashing is deferred to on-demand recovery operations (architecture doc §14.2, Level 6).

### 2.4 Examples

**RuntimeAsset (zip):**

```json
{
  "manifestVersion": 1,
  "bundleId": "TerrainTextures",
  "version": "v42",
  "dataCategory": "RuntimeAsset",
  "container": "zip",
  "publishedAt": "2026-05-17T12:00:00Z",
  "groupHash": "sha256:7a2f...",
  "totalBytes": 1073741824,
  "fileCount": 1247,
  "activation": {
    "mode": "atomic-directory-swap"
  },
  "archive": {
    "name": "bundle.zip",
    "size": 1073741824,
    "sha256": "sha256:7a2f..."
  },
  "files": [
    {
      "relativePath": "grass/grass_a.dds",
      "size": 4194304,
      "hash": "sha256:aabb...",
      "compressionMode": "store"
    },
    {
      "relativePath": "manifest_inner.txt",
      "size": 256,
      "hash": "sha256:ccdd...",
      "compressionMode": "deflate"
    }
  ]
}
```

**Config (zip):**

```json
{
  "manifestVersion": 1,
  "bundleId": "ScenarioA-Config",
  "version": "r12345",
  "dataCategory": "Config",
  "container": "zip",
  "publishedAt": "2026-05-17T12:00:00Z",
  "groupHash": "sha256:9b3c...",
  "totalBytes": 524288,
  "fileCount": 17,
  "activation": {
    "mode": "atomic-directory-swap",
    "targetPath": "Data/Configs/ScenarioA"
  },
  "archive": {
    "name": "bundle.zip",
    "size": 524288,
    "sha256": "sha256:9b3c..."
  },
  "files": [
    {
      "relativePath": "scenario.json",
      "size": 81920,
      "hash": "sha256:eeff...",
      "compressionMode": "deflate"
    }
  ]
}
```

**ChunkedHugeFile (raw):**

```json
{
  "manifestVersion": 1,
  "bundleId": "TerrainDatabase",
  "version": "2026-05-17-001",
  "dataCategory": "ChunkedHugeFile",
  "container": "none",
  "publishedAt": "2026-05-17T12:00:00Z",
  "groupHash": "sha256:e3d1...",
  "totalBytes": 549755813888,
  "fileCount": 1,
  "activation": {
    "mode": "in-place",
    "targetPath": "Data/TerrainDatabase/dataset.zip"
  },
  "file": {
    "name": "dataset.zip",
    "size": 549755813888
  },
  "chunks": [
    { "index": 0,    "offset": 0,           "size": 67108864, "sha256": "sha256:1111..." },
    { "index": 1,    "offset": 67108864,    "size": 67108864, "sha256": "sha256:2222..." },
    { "index": 8191, "offset": 549688705024, "size": 67108864, "sha256": "sha256:zzzz..." }
  ]
}
```

### 2.5 `groupHash` Computation

A single representative hash for the version, enabling fast fleet-wide equality checks without re-reading bundle contents.

For zip variant:

```
groupHash = archive.sha256
```

For raw variant (Merkle-style root over chunk hashes):

```
groupHash = SHA256( concat( chunks[0].sha256_bytes, chunks[1].sha256_bytes, ... ) )
```

Where `sha256_bytes` is the 32-byte raw digest (not hex string). The concatenation order is by `chunks[].index` ascending.

### 2.6 Filename Safety Rules

Both `bundleId` and `version` end up as directory names on disk. Rules:

- ASCII only
- Letters, digits, hyphen `-`, underscore `_`, period `.`
- Length 1–128 characters
- Cannot start with `.` (no hidden directories)
- Cannot be `_draft`, `_publishing`, `manifests`, `versions`, `latest.json`, `published.json` (reserved)
- Case-sensitive in the manifest; case-preserved on Windows (which is case-insensitive in lookup — see §3.3)

Regex: `^[A-Za-z0-9_.-]{1,128}$` with the additional rules above.

### 2.7 `activation` Object

| Field        | Type   | Required when | Notes                                                        |
| ------------ | ------ | ------------- | ------------------------------------------------------------ |
| `mode`       | enum   | always        | `atomic-directory-swap` | `in-place` | `cooperative-hot-swap` |
| `targetPath` | string | optional      | Relative path under the agent's app data root. If omitted, agent uses `{appDataRoot}/{bundleId}/active`. Forward slashes; no `..`. |

Mode semantics (full details deferred to Phase 6 and 9 design docs):

- `atomic-directory-swap` — agent extracts the zip, then repoints a directory symlink/junction.
- `in-place` — agent writes the file directly to `targetPath`, replacing the existing file. App must guarantee no open handles at activation time.
- `cooperative-hot-swap` — agent waits for `SignalSafeWindow` before swap. Otherwise identical to `atomic-directory-swap`.

### 2.8 Hash Format

All hashes in the manifest use the format `sha256:` followed by lowercase hex digest (64 hex chars). Example: `sha256:a3f5b9c7d2e1...`. The prefix is required to allow future algorithm changes.

### 2.9 JSON Encoding

- UTF-8, no BOM
- LF line endings (not CRLF) for stable hashing if the manifest itself is ever hashed
- Property order: stable, alphabetical within each object, to make manifest re-generation byte-identical for unchanged content

------

## 3. NAS Directory Layout

### 3.1 Top-Level Structure

```
/NAS/SyncRoot/
  bundles/
    {bundleId}/
      versions/
        {version}/
          manifest.json
          published.json
          bundle.zip                 (zip variant) OR
          {filename}                 (raw variant)
      latest.json
  recordings/
    {sessionId}/
      _session.json
      {logicalNodeId}.zip
  _draft/
    {bundleId}/
      {version}/                     work in progress; agents never read
  _trash/
    {timestamp}-{bundleId}-{version}/    deleted versions, retained briefly for safety
```

### 3.2 Files Inside `versions/{version}/`

| File             | Purpose                                                  | Atomicity                                                    |
| ---------------- | -------------------------------------------------------- | ------------------------------------------------------------ |
| `manifest.json`  | The bundle manifest as defined in §2                     | Written before `published.json`; never modified after gate is written |
| `published.json` | The publish gate. Its existence makes the version valid. | Written *last*, atomically (write to temp + rename)          |
| `bundle.zip`     | The bundle data (zip variant)                            | Written before manifest; immutable after gate                |
| `{filename}`     | The bundle data (raw variant), name from `file.name`     | Written before manifest; immutable after gate                |

### 3.3 `published.json` Format

Tiny pointer file. Its presence is the publish signal.

```json
{
  "bundleId":    "TerrainTextures",
  "version":     "v42",
  "publishedAt": "2026-05-17T12:00:00Z",
  "manifestSha256": "sha256:f4a2..."
}
```

`manifestSha256` is the SHA-256 of the bytes of `manifest.json` in the same directory. This lets any reader verify the manifest has not been tampered with after publication without recomputing all file hashes.

### 3.4 `latest.json` Format

Per-bundle pointer to the current latest published version. Written by the master in Phase 2, not by the publish CLI. In Phase 1 the CLI does not write this file.

```json
{
  "bundleId":  "TerrainTextures",
  "latest":    "v42",
  "updatedAt": "2026-05-17T12:00:30Z"
}
```

Note for Phase 1: tooling reads `latest.json` if present; absence is not an error (Phase 1 alone never writes it).

### 3.5 Reserved Paths

The following names are reserved and the CLI refuses to use them as `bundleId` or `version`:

```
_draft, _publishing, _trash, _session.json, published.json, manifest.json,
manifests, versions, latest.json, recordings, bundles
```

### 3.6 Case Sensitivity

Windows file systems are case-insensitive in lookup but case-preserving in display. The CLI:

- Treats `bundleId` and `version` as **case-sensitive** in the manifest
- Refuses to publish `bundleId` "Foo" if a directory `foo` already exists (case-conflict detected)
- Same rule for `version` within a bundle

### 3.7 UNC vs Local Paths

The CLI accepts the NAS root as either:

- UNC path: `\\nas\sync` (when CLI runs on a remote machine)
- Local path: `D:\Sync` (when CLI runs on the NAS / master machine)

Internally the CLI uses `System.IO.Path` operations and resolves both consistently.

### 3.8 Atomic Operation Requirements

| Operation                                                   | Mechanism                                                    | Why                                                     |
| ----------------------------------------------------------- | ------------------------------------------------------------ | ------------------------------------------------------- |
| Move `_draft/{bundleId}/{version}/` → `versions/{version}/` | `Directory.Move` (or `MoveFile` Win32)                       | Single-directory atomic rename within the same volume   |
| Write `published.json`                                      | Write to `published.json.tmp`, then `File.Move(tmp, "published.json", overwrite: false)` | Atomic rename ensures readers never see partial content |
| Move deleted version to `_trash/`                           | `Directory.Move`                                             | Atomic; reversible briefly                              |

The `_draft/` and `versions/` directories must be on the same volume for `Directory.Move` to be atomic. The CLI verifies this at startup.

------

## 4. Publish Flow

### 4.1 Sequence

```
┌─────────┐                                  ┌─────┐                     ┌────────┐
│ Builder │                                  │ CLI │                     │  NAS   │
└────┬────┘                                  └──┬──┘                     └────┬───┘
     │                                          │                             │
     │ syncpub publish ...                      │                             │
     │─────────────────────────────────────────>│                             │
     │                                          │                             │
     │                                          │ validate args               │
     │                                          │ check NAS reachable         │
     │                                          │────────────────────────────>│
     │                                          │                             │
     │                                          │ enumerate source files      │
     │                                          │ compute hashes (parallel)   │
     │                                          │                             │
     │                                          │ create _draft/{bId}/{ver}/  │
     │                                          │────────────────────────────>│
     │                                          │                             │
     │                                          │ pack zip (or copy raw)      │
     │                                          │ write file(s)               │
     │                                          │────────────────────────────>│
     │                                          │                             │
     │                                          │ verify on-NAS hashes        │
     │                                          │  match in-memory manifest   │
     │                                          │<────────────────────────────│
     │                                          │                             │
     │                                          │ write manifest.json         │
     │                                          │────────────────────────────>│
     │                                          │                             │
     │                                          │ atomic Directory.Move:      │
     │                                          │   _draft/{...} → versions/  │
     │                                          │────────────────────────────>│
     │                                          │                             │
     │                                          │ atomic write of             │
     │                                          │   published.json            │
     │                                          │   (gate is now open)        │
     │                                          │────────────────────────────>│
     │                                          │                             │
     │                                          │ (optional)                  │
     │                                          │ POST /api/bundles/{id}/     │
     │                                          │      versions to master     │
     │                                          │                             │
     │ result: success(version, groupHash)      │                             │
     │<─────────────────────────────────────────│                             │
```

### 4.2 Atomicity Guarantees

The system relies on three atomic operations:

1. **Directory rename** of the draft to its final versioned location. After this point, the version data is in its final place but the gate is not yet open.
2. **File rename** of `published.json.tmp` to `published.json`. This is the gate-open moment. Before this, no reader treats the version as valid.
3. **Crash recovery is implicit**: if the CLI crashes at any point, the worst that happens is a stale `_draft/` directory or a version directory without a gate. Both are harmless — no reader treats them as published.

### 4.3 Failure Handling

| Failure Point                          | Recovery                                                     |
| -------------------------------------- | ------------------------------------------------------------ |
| During hash computation                | Nothing on NAS yet. Re-run CLI.                              |
| During `_draft/` population            | Stale `_draft/` left on NAS. `syncpub clean-drafts` removes drafts older than 24h. |
| During directory move                  | If move failed, draft remains; re-run. If move succeeded but CLI crashed before gate, version directory exists without gate — agents ignore it. `syncpub clean-orphans` lists/removes such directories. |
| During gate write                      | If `.tmp` exists but rename failed, re-run will re-create and rename. |
| After gate, during master notification | Gate is open; readers can see the version. Re-run with `--notify-only` to retry notification. |

### 4.4 Idempotency

Running `syncpub publish` twice with the same `(bundleId, version, source-dir)` and unchanged source files produces a byte-identical manifest (because of stable property ordering and deterministic zip packing — see §6.5) and detects the version already exists, reporting success without re-uploading.

If the source files have changed (any hash differs), the CLI fails with "version already published with different content; use a new version string".

------

## 5. Publish CLI: `syncpub`

### 5.1 Command Surface

```
syncpub publish <bundleId> <version>
    [--source-dir <path>]            required
    [--category <cat>]               required: RuntimeAsset|Config|Dataset|ChunkedHugeFile
    [--nas-root <path>]              required, UNC or local
    [--activation <mode>]            optional, defaults per category (§5.4)
    [--target-path <path>]           optional, activation target override
    [--container <type>]             optional, defaults per category
    [--chunk-size <size>]            optional, ChunkedHugeFile only, default 64MB
    [--compression <mode>]           optional, zip variant: store|deflate|auto (§5.5)
    [--description <text>]           optional
    [--tag <key>=<value>]            optional, repeatable, becomes customTags
    [--master-url <url>]             optional, notify master after publish
    [--auto-register]                optional, forwarded to master
    [--parallel <N>]                 optional, parallel hash workers, default = CPU count
    [--dry-run]                      validate, don't write
    [--force]                        overwrite existing version (refused unless content identical)

syncpub verify <bundleId> <version>
    [--nas-root <path>]              required
    [--level <1|2|3|4|5>]            verification depth, default 4

syncpub list
    [--nas-root <path>]              required
    [--bundle <bundleId>]            optional filter

syncpub info <bundleId> <version>
    [--nas-root <path>]              required

syncpub clean-drafts
    [--nas-root <path>]              required
    [--older-than <duration>]        optional, default 24h
    [--dry-run]                      optional

syncpub clean-orphans
    [--nas-root <path>]              required
    [--dry-run]                      optional

syncpub notify-only <bundleId> <version>
    [--nas-root <path>]              required
    [--master-url <url>]             required
    [--auto-register]                optional
```

### 5.2 Examples

Publish runtime assets:

```
syncpub publish TerrainTextures v42 \
  --source-dir D:\build\textures\v42 \
  --category RuntimeAsset \
  --nas-root \\nas\sync \
  --activation atomic-directory-swap
```

Publish a config bundle, notify the master:

```
syncpub publish ScenarioA-Config r12345 \
  --source-dir D:\build\configs\scenarioA \
  --category Config \
  --nas-root \\nas\sync \
  --target-path Data/Configs/ScenarioA \
  --master-url http://master.local:8080 \
  --description "Scenario A config rev 12345"
```

Publish a half-terabyte pre-zipped dataset:

```
syncpub publish TerrainDatabase 2026-05-17-001 \
  --source-dir D:\build\terrain \
  --category ChunkedHugeFile \
  --nas-root \\nas\sync \
  --chunk-size 64MB \
  --activation in-place \
  --target-path Data/TerrainDatabase/dataset.zip \
  --parallel 16
```

Verify a published version's integrity:

```
syncpub verify TerrainTextures v42 --nas-root \\nas\sync --level 4
```

### 5.3 Internal Pipeline

```
parse args
  → resolve & validate inputs (paths, names, category)
  → check NAS reachable, writable
  → check reserved names
  → detect existing version (idempotent path or refuse)

scan source
  → enumerate files
  → compute file hashes (parallel, --parallel)
  → for ChunkedHugeFile: compute chunk hashes
  → build in-memory Manifest object

build draft
  → mkdir _draft/{bundleId}/{version}/
  → write archive (zip variant) OR copy raw file (ChunkedHugeFile)
  → on-disk verification: re-hash what was written, compare to manifest

finalize
  → write manifest.json into draft
  → Directory.Move(_draft/{bId}/{ver}, versions/{ver})

gate
  → write published.json.tmp
  → File.Move(.tmp → published.json)

optional notify
  → POST /api/bundles/{bId}/versions [?autoRegister=true]
  → on failure: log, exit with code reflecting notification failure but publish success

report
  → print bundle/version/groupHash/sizes
```

### 5.4 Defaults Per Category

| Category        | `container` | `activation.mode`     | `compression` | `chunkSize` |
| --------------- | ----------- | --------------------- | ------------- | ----------- |
| RuntimeAsset    | zip         | atomic-directory-swap | auto          | n/a         |
| Config          | zip         | atomic-directory-swap | deflate       | n/a         |
| Dataset         | zip         | atomic-directory-swap | deflate       | n/a         |
| ChunkedHugeFile | none        | in-place              | n/a           | 64MB        |

### 5.5 Compression Mode `auto`

When `--compression auto` (the default for RuntimeAsset):

- Files with extensions in the *already-compressed* list use `store` (no compression): `.zip .gz .7z .rar .png .jpg .jpeg .webp .dds .bc7 .bc3 .ktx2 .ogg .opus .mp3 .mp4 .webm .h264 .h265`
- All other files use `deflate` at `CompressionLevel.Optimal`

The list is configurable via a sidecar file `syncpub.compression.json` next to the source directory, or via `--compression-rules <file>`.

### 5.6 Exit Codes

| Code | Meaning                                          |
| ---- | ------------------------------------------------ |
| 0    | Success                                          |
| 1    | Argument or input validation error               |
| 2    | Source files unreadable                          |
| 3    | NAS unreachable or write-denied                  |
| 4    | Version already exists with different content    |
| 5    | On-NAS verification failed (write corruption)    |
| 6    | Atomic rename failed                             |
| 10   | Publish succeeded but master notification failed |
| 99   | Unhandled exception                              |

### 5.7 Logging

The CLI logs to stderr by default. `--log-file <path>` writes to a file in addition. Log format:

```
2026-05-17T12:00:00.123Z INFO  publish.start bundleId=TerrainTextures version=v42
2026-05-17T12:00:01.456Z INFO  scan.complete fileCount=1247 totalBytes=1073741824 elapsed=1.3s
2026-05-17T12:00:34.789Z INFO  pack.complete archiveSize=1073741824 sha256=7a2f...
2026-05-17T12:00:35.012Z INFO  gate.open bundleId=TerrainTextures version=v42
```

------

## 6. Code Sketches

### 6.1 Project Layout

```
SyncSystem.sln
  src/
    SyncSystem.Manifest/            (library)
      BundleManifest.cs
      ManifestBuilder.cs
      ManifestValidator.cs
      ManifestSerializer.cs
      HashFormat.cs
    SyncSystem.Publish/             (library)
      Publisher.cs
      DraftDirectory.cs
      GateWriter.cs
      NasPaths.cs
      CompressionPolicy.cs
    SyncSystem.Publish.Cli/         (exe, "syncpub")
      Program.cs
      Commands/
        PublishCommand.cs
        VerifyCommand.cs
        ListCommand.cs
        InfoCommand.cs
        CleanDraftsCommand.cs
        CleanOrphansCommand.cs
        NotifyOnlyCommand.cs
  test/
    SyncSystem.Manifest.Tests/
    SyncSystem.Publish.Tests/
    SyncSystem.Publish.Cli.Tests/
```

### 6.2 Core DTOs

```csharp
namespace SyncSystem.Manifest;

public sealed record BundleManifest
{
    public int ManifestVersion { get; init; } = 1;
    public required string BundleId { get; init; }
    public required string Version { get; init; }
    public required DataCategory DataCategory { get; init; }
    public required ContainerKind Container { get; init; }
    public required DateTimeOffset PublishedAt { get; init; }
    public required string GroupHash { get; init; }
    public required long TotalBytes { get; init; }
    public required int FileCount { get; init; }
    public required ActivationDirective Activation { get; init; }
    public string? Description { get; init; }
    public IReadOnlyDictionary<string, string>? CustomTags { get; init; }

    // Zip variant:
    public ArchiveInfo? Archive { get; init; }
    public IReadOnlyList<FileEntry>? Files { get; init; }

    // Raw variant:
    public FileInfo? File { get; init; }
    public IReadOnlyList<ChunkEntry>? Chunks { get; init; }
}

public enum DataCategory { RuntimeAsset, Config, Dataset, ChunkedHugeFile }
public enum ContainerKind { Zip, None }

public sealed record ActivationDirective(
    ActivationMode Mode,
    string? TargetPath = null);

public enum ActivationMode { AtomicDirectorySwap, InPlace, CooperativeHotSwap }

public sealed record ArchiveInfo(string Name, long Size, string Sha256);

public sealed record FileEntry(
    string RelativePath,
    long Size,
    string Hash,
    CompressionMode CompressionMode);

public enum CompressionMode { Store, Deflate }

public sealed record FileInfo(string Name, long Size);

public sealed record ChunkEntry(int Index, long Offset, int Size, string Sha256);
```

### 6.3 Manifest Validator

```csharp
namespace SyncSystem.Manifest;

public static class ManifestValidator
{
    private static readonly Regex IdRegex = new(@"^[A-Za-z0-9_.-]{1,128}$", RegexOptions.Compiled);
    private static readonly HashSet<string> ReservedNames = new(StringComparer.Ordinal)
    {
        "_draft", "_publishing", "_trash", "_session.json", "published.json",
        "manifest.json", "manifests", "versions", "latest.json", "recordings", "bundles"
    };

    public static ValidationResult Validate(BundleManifest m)
    {
        var errors = new List<string>();

        if (m.ManifestVersion != 1)
            errors.Add($"Unsupported manifestVersion: {m.ManifestVersion}");

        if (!IsValidId(m.BundleId)) errors.Add($"Invalid bundleId: '{m.BundleId}'");
        if (!IsValidId(m.Version))  errors.Add($"Invalid version: '{m.Version}'");

        if (m.PublishedAt.Offset != TimeSpan.Zero)
            errors.Add("publishedAt must be UTC");

        ValidateHashFormat(m.GroupHash, "groupHash", errors);

        switch (m.Container)
        {
            case ContainerKind.Zip:
                ValidateZipVariant(m, errors);
                break;
            case ContainerKind.None:
                ValidateRawVariant(m, errors);
                break;
        }

        // Activation consistency
        if (m.Activation.Mode == ActivationMode.InPlace &&
            m.Container == ContainerKind.Zip)
            errors.Add("Activation mode 'in-place' is incompatible with zip container");

        return new ValidationResult(errors);
    }

    private static bool IsValidId(string id) =>
        IdRegex.IsMatch(id) && !id.StartsWith('.') && !ReservedNames.Contains(id);

    private static void ValidateHashFormat(string hash, string field, List<string> errors)
    {
        if (!hash.StartsWith("sha256:", StringComparison.Ordinal) || hash.Length != 7 + 64)
            errors.Add($"Invalid {field} format: must be 'sha256:' + 64 lowercase hex chars");
        // ... hex validation ...
    }

    private static void ValidateZipVariant(BundleManifest m, List<string> errors)
    {
        if (m.Archive is null)        errors.Add("Zip container requires 'archive' field");
        if (m.Files is null || m.Files.Count == 0)
            errors.Add("Zip container requires non-empty 'files' array");
        if (m.File is not null)       errors.Add("Zip container must not have 'file' field");
        if (m.Chunks is not null)     errors.Add("Zip container must not have 'chunks' field");

        if (m.Files is not null && m.Files.Count != m.FileCount)
            errors.Add($"fileCount ({m.FileCount}) != files.length ({m.Files.Count})");

        // Per-file validation
        if (m.Files is not null)
        {
            foreach (var f in m.Files)
            {
                if (f.RelativePath.Contains("..") || f.RelativePath.StartsWith('/'))
                    errors.Add($"Invalid relativePath: '{f.RelativePath}'");
                ValidateHashFormat(f.Hash, $"files[{f.RelativePath}].hash", errors);
            }
        }
    }

    private static void ValidateRawVariant(BundleManifest m, List<string> errors)
    {
        if (m.File is null)           errors.Add("Raw container requires 'file' field");
        if (m.Chunks is null || m.Chunks.Count == 0)
            errors.Add("Raw container requires non-empty 'chunks' array");
        if (m.Archive is not null)    errors.Add("Raw container must not have 'archive' field");
        if (m.Files is not null)      errors.Add("Raw container must not have 'files' field");
        if (m.FileCount != 1)         errors.Add("Raw container requires fileCount=1");

        // Chunks must be contiguous starting at offset 0
        if (m.Chunks is not null)
        {
            long expectedOffset = 0;
            for (int i = 0; i < m.Chunks.Count; i++)
            {
                var c = m.Chunks[i];
                if (c.Index != i) errors.Add($"Chunk index mismatch at position {i}: expected {i}, got {c.Index}");
                if (c.Offset != expectedOffset)
                    errors.Add($"Chunk {i} offset mismatch: expected {expectedOffset}, got {c.Offset}");
                if (c.Size <= 0) errors.Add($"Chunk {i} has non-positive size");
                ValidateHashFormat(c.Sha256, $"chunks[{i}].sha256", errors);
                expectedOffset += c.Size;
            }

            if (m.File is not null && expectedOffset != m.File.Size)
                errors.Add($"Chunks total ({expectedOffset}) != file.size ({m.File.Size})");
        }
    }
}

public sealed record ValidationResult(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}
```

### 6.4 Manifest Builder

```csharp
namespace SyncSystem.Manifest;

public sealed class ManifestBuilder
{
    private readonly int _parallelHashWorkers;

    public ManifestBuilder(int parallelHashWorkers) => _parallelHashWorkers = parallelHashWorkers;

    public async Task<BundleManifest> BuildZipAsync(
        string bundleId,
        string version,
        DataCategory category,
        DirectoryInfo sourceDir,
        ActivationDirective activation,
        CompressionPolicy compression,
        string? description,
        IReadOnlyDictionary<string, string>? tags,
        CancellationToken ct)
    {
        var files = EnumerateFiles(sourceDir);
        var entries = await HashFilesAsync(files, sourceDir, compression, ct);
        var packed = await PackZipAsync(sourceDir, entries, ct);   // returns archive path + size + sha256

        return new BundleManifest
        {
            BundleId       = bundleId,
            Version        = version,
            DataCategory   = category,
            Container      = ContainerKind.Zip,
            PublishedAt    = DateTimeOffset.UtcNow,
            GroupHash      = packed.Sha256,
            TotalBytes     = packed.Size,
            FileCount      = entries.Count,
            Activation     = activation,
            Description    = description,
            CustomTags     = tags,
            Archive        = new ArchiveInfo(packed.Name, packed.Size, packed.Sha256),
            Files          = entries
        };
    }

    public async Task<BundleManifest> BuildRawAsync(
        string bundleId,
        string version,
        FileInfo sourceFile,
        ActivationDirective activation,
        int chunkSizeBytes,
        string? description,
        IReadOnlyDictionary<string, string>? tags,
        CancellationToken ct)
    {
        var chunks = await HashChunksAsync(sourceFile, chunkSizeBytes, ct);
        var groupHash = ComputeMerkleRoot(chunks);

        return new BundleManifest
        {
            BundleId       = bundleId,
            Version        = version,
            DataCategory   = DataCategory.ChunkedHugeFile,
            Container      = ContainerKind.None,
            PublishedAt    = DateTimeOffset.UtcNow,
            GroupHash      = groupHash,
            TotalBytes     = sourceFile.Length,
            FileCount      = 1,
            Activation     = activation,
            Description    = description,
            CustomTags     = tags,
            File           = new Manifest.FileInfo(sourceFile.Name, sourceFile.Length),
            Chunks         = chunks
        };
    }

    private static string ComputeMerkleRoot(IReadOnlyList<ChunkEntry> chunks)
    {
        using var sha = SHA256.Create();
        foreach (var c in chunks)
        {
            var bytes = HashFormat.HexToBytes(c.Sha256);   // 32 raw bytes
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return HashFormat.Format(sha.Hash!);
    }

    // EnumerateFiles, HashFilesAsync, PackZipAsync, HashChunksAsync: standard implementations.
    // Hash computation parallelized via Parallel.ForEachAsync with _parallelHashWorkers.
}
```

### 6.5 Deterministic Zip Packing

For idempotent re-publish (§4.4), the zip archive must be byte-identical for unchanged inputs. Achieved by:

- Files added to the zip in sorted order by `relativePath` (ordinal)
- File timestamps fixed to a constant (e.g. 2000-01-01T00:00:00Z)
- No global zip comment
- Stable ZIP central directory ordering
- Compression level fixed (`Optimal` for deflate)

In .NET 8, `ZipArchive` with explicit `ZipArchiveEntry.LastWriteTime` set and entries added in deterministic order is sufficient. The implementation uses a custom small wrapper.

### 6.6 Gate Writer

```csharp
namespace SyncSystem.Publish;

public sealed class GateWriter
{
    public async Task WriteAsync(string versionDir, BundleManifest manifest, CancellationToken ct)
    {
        var manifestPath = Path.Combine(versionDir, "manifest.json");
        var manifestBytes = await File.ReadAllBytesAsync(manifestPath, ct);
        var manifestSha = HashFormat.Format(SHA256.HashData(manifestBytes));

        var gate = new
        {
            bundleId        = manifest.BundleId,
            version         = manifest.Version,
            publishedAt     = manifest.PublishedAt.ToString("O"),
            manifestSha256  = manifestSha
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(gate, ManifestSerializer.Options);
        var tmpPath  = Path.Combine(versionDir, "published.json.tmp");
        var finalPath = Path.Combine(versionDir, "published.json");

        await File.WriteAllBytesAsync(tmpPath, json, ct);

        // Atomic rename. If destination exists, fail (gate must not be overwritten silently).
        File.Move(tmpPath, finalPath, overwrite: false);
    }
}
```

### 6.7 Publisher (top-level orchestration)

```csharp
namespace SyncSystem.Publish;

public sealed class Publisher
{
    private readonly ManifestBuilder _builder;
    private readonly GateWriter _gateWriter;
    private readonly INasPaths _paths;
    private readonly ILogger<Publisher> _log;

    public async Task<PublishResult> PublishAsync(PublishRequest req, CancellationToken ct)
    {
        ManifestValidator.ValidateRequest(req).ThrowIfInvalid();

        // Detect existing version
        var versionDir = _paths.VersionDir(req.BundleId, req.Version);
        if (Directory.Exists(versionDir))
        {
            return await HandleExistingVersion(versionDir, req, ct);
        }

        // Build manifest from source
        var manifest = req.DataCategory == DataCategory.ChunkedHugeFile
            ? await _builder.BuildRawAsync(/* ... */, ct)
            : await _builder.BuildZipAsync(/* ... */, ct);

        ManifestValidator.Validate(manifest).ThrowIfInvalid();

        // Stage in _draft/
        var draftDir = _paths.DraftDir(req.BundleId, req.Version);
        await StageDraftAsync(draftDir, manifest, req, ct);
        await VerifyOnDiskAsync(draftDir, manifest, ct);

        // Atomic move draft → versions/
        Directory.Move(draftDir, versionDir);

        // Open the gate
        await _gateWriter.WriteAsync(versionDir, manifest, ct);

        _log.LogInformation("Gate open: {Bundle}/{Version} groupHash={Hash}",
            req.BundleId, req.Version, manifest.GroupHash);

        return new PublishResult(manifest, versionDir);
    }

    private async Task<PublishResult> HandleExistingVersion(string versionDir, PublishRequest req, CancellationToken ct)
    {
        var existing = await ReadManifestAsync(versionDir, ct);
        var fresh    = await BuildManifestAsync(req, ct);   // recompute from source

        if (existing.GroupHash == fresh.GroupHash)
        {
            _log.LogInformation("Version {Bundle}/{Version} already published, content identical — idempotent success",
                req.BundleId, req.Version);
            return new PublishResult(existing, versionDir, IdempotentReplay: true);
        }

        if (req.Force)
        {
            // _trash existing, retry
            await MoveToTrashAsync(versionDir, ct);
            return await PublishAsync(req, ct);
        }

        throw new VersionConflictException(req.BundleId, req.Version,
            existing.GroupHash, fresh.GroupHash);
    }
}

public sealed record PublishRequest(
    string BundleId,
    string Version,
    DataCategory DataCategory,
    ContainerKind Container,
    DirectoryInfo SourceDir,
    ActivationDirective Activation,
    CompressionPolicy Compression,
    int? ChunkSizeBytes,
    string? Description,
    IReadOnlyDictionary<string, string>? Tags,
    bool Force,
    int ParallelHashWorkers);

public sealed record PublishResult(
    BundleManifest Manifest,
    string VersionDirectory,
    bool IdempotentReplay = false);
```

### 6.8 CLI Entry Point

Built on `System.CommandLine` (a current standard for .NET CLI tooling). One sub-command per operation:

```csharp
// Program.cs
var root = new RootCommand("syncpub — sync system publish CLI");
root.Add(PublishCommand.Build());
root.Add(VerifyCommand.Build());
root.Add(ListCommand.Build());
root.Add(InfoCommand.Build());
root.Add(CleanDraftsCommand.Build());
root.Add(CleanOrphansCommand.Build());
root.Add(NotifyOnlyCommand.Build());
return await root.InvokeAsync(args);
```

Each command builds the appropriate Publisher / Verifier / etc., wires logging, and translates exceptions to exit codes per §5.6.

------

## 7. Validation: Schema and Semantic Rules

### 7.1 Schema Rules (mechanical, checked by validator)

- All required fields present
- All enums in allowed set
- All hashes match `^sha256:[0-9a-f]{64}$`
- `bundleId` and `version` match §2.6 rules
- `publishedAt` parses as ISO8601 with `Z` (UTC) suffix
- `manifestVersion == 1`

### 7.2 Cross-Field Semantic Rules

- `container == "zip"` ⟺ `archive` and `files` present, `file` and `chunks` absent
- `container == "none"` ⟺ `file` and `chunks` present, `archive` and `files` absent
- `files.length == fileCount` (zip)
- `fileCount == 1` (raw)
- Chunks contiguous from offset 0, sum of sizes == `file.size`
- `activation.mode == "in-place"` ⇒ `container == "none"` *(in-place activation only meaningful for raw files in this phase)*
- All `files[].relativePath` distinct
- No `relativePath` contains `..` or starts with `/`
- `groupHash == archive.sha256` (zip) or `groupHash == merkle(chunks)` (raw)

### 7.3 On-Disk Verification (verify command, level 4)

After reading manifest, re-hash every file (or every chunk for raw) and compare to recorded hashes. Failure = corruption.

For huge files, level 3 (chunk hashes only) is the default routine verification; level 6 (whole-file hash) is on-demand and not run by default.

------

## 8. Acceptance Tests

The acceptance criteria for Phase 1 are codified as automated tests.

### 8.1 Manifest Tests

| Test                                                         | Pass condition                                               |
| ------------------------------------------------------------ | ------------------------------------------------------------ |
| Build manifest from a 100-file directory                     | Resulting manifest validates, `fileCount == 100`, hashes match recomputed hashes |
| Build manifest from a 0-file directory                       | Build fails with clear error                                 |
| Build raw manifest from a 1 MB file with 64 KB chunks        | Resulting manifest validates, 16 chunks, offsets and sizes correct, groupHash matches Merkle root |
| Round-trip serialize/deserialize                             | Output byte-identical to input                               |
| Validate a manifest with a `..` in relativePath              | Validation fails with specific error                         |
| Validate a manifest with mismatched fileCount and files.length | Validation fails                                             |
| Validate a manifest with non-contiguous chunks               | Validation fails                                             |

### 8.2 Publish Tests

| Test                                                         | Pass condition                                               |
| ------------------------------------------------------------ | ------------------------------------------------------------ |
| Publish a small RuntimeAsset bundle to a temp NAS dir        | `versions/v1/` exists, contains `bundle.zip`, `manifest.json`, `published.json`. Gate is the newest mtime in the dir. |
| Publish twice with same content                              | Second run reports idempotent success; on-disk state unchanged after second run |
| Publish twice with different content, same version           | Second run fails with exit code 4                            |
| Publish twice with different content, `--force`              | First version moved to `_trash/`, new version published      |
| Crash simulation: kill CLI between manifest write and gate write | Re-run succeeds; orphan version dir cleaned by `clean-orphans` |
| Crash simulation: kill CLI between zip write and manifest write | Re-run succeeds; stale draft cleaned by `clean-drafts`       |
| Publish a ChunkedHugeFile manifest for a 1 GB file with 64 MB chunks | 16 chunks, all hash verified, in-place activation valid      |
| Reserved name as bundleId (e.g. `_draft`)                    | Refused at argument parsing                                  |

### 8.3 Verify Tests

| Test                                                         | Pass condition                                              |
| ------------------------------------------------------------ | ----------------------------------------------------------- |
| Verify a clean published version, level 4                    | All hashes match, exit 0                                    |
| Corrupt one byte in `bundle.zip`, verify                     | Exit non-zero, error names the file and the mismatched hash |
| Corrupt one byte in a chunk's range for a raw bundle, verify level 3 | Identifies exactly which chunk failed                       |
| Tamper with `manifest.json` after publish, verify            | `published.json`'s `manifestSha256` mismatch reported       |

### 8.4 List & Info Tests

| Test                                          | Pass condition                                               |
| --------------------------------------------- | ------------------------------------------------------------ |
| List bundles in an empty NAS                  | Empty result, exit 0                                         |
| List after publishing 3 versions of 2 bundles | All 6 versions appear, grouped by bundle                     |
| Info on an existing version                   | Prints manifest summary with groupHash, fileCount, totalBytes, activation mode |
| Info on a non-existent version                | Exit code 1 with clear error                                 |

### 8.5 Concurrency Tests

| Test                                                         | Pass condition                                               |
| ------------------------------------------------------------ | ------------------------------------------------------------ |
| Two `syncpub publish` instances writing different versions of the same bundle concurrently | Both succeed; both `versions/` dirs present; both gates open |
| Two `syncpub publish` instances writing the *same* version concurrently | One succeeds, one fails (lost race on directory move or gate write); failure is reported clearly |

------

## 9. Deferred Items (carry into later phases)

These appear in this design but their implementations are deferred:

- **Master notification** (`POST /api/bundles/{id}/versions`): CLI implements the call; master implementation is Phase 2.
- **`latest.json` update**: written by master, not the CLI. Phase 2 implements it.
- **Site config integration**: the CLI is stand-alone in Phase 1 — does not read site config. In later phases the master may invoke `syncpub` or the publisher library.
- **Cooperative-hot-swap defaults**: declared in manifests but not exercised until Phase 6.
- **Bundle registry pre-existence check**: in Phase 1 the CLI does not consult the master's registry; `--auto-register` is forwarded blindly. Once Phase 2 lands, the CLI can pre-validate.

------

## 10. Implementation Sequence

A suggested order within Phase 1, each step independently testable:

1. **DTOs and serializer** (`SyncSystem.Manifest` library). Round-trip tests pass.
2. **ManifestValidator**. Unit tests cover all rules in §2 and §7.
3. **ManifestBuilder — zip variant**. Build manifests from real directories; hash output is correct.
4. **ManifestBuilder — raw variant**. Build manifests from large files; chunking math correct.
5. **CompressionPolicy and deterministic zip packing**. Byte-identical re-pack of unchanged input.
6. **DraftDirectory + atomic move + GateWriter**. Acceptance test 8.2 row 1 passes.
7. **Publisher (top-level orchestration)**. Idempotency and conflict handling.
8. **CLI commands** built on top, one per command. Exit codes per §5.6.
9. **Verify command** and integrity tests (§8.3).
10. **Clean-drafts / clean-orphans** maintenance commands.
11. **Notify-only command and HTTP client** (stub master in tests).

After step 11, Phase 1 is complete and Phase 2 can begin building the master on top.





# Phase 2 Detailed Design

## Master Skeleton, Agent State Machine, Stub Transfer

*Companion to Architecture Design Document Revision 2, API Specification, and Phase 1 Design* *Targets `Phase 2` from §21 of the architecture doc* *C# / .NET 8 · ASP.NET Core · SignalR · SQLite · May 2026*

------

## 1. Phase Scope

### 1.1 What Phase 2 Adds On Top of Phase 1

Phase 1 produced the static NAS-side artifact: published bundle versions with manifests and gates. Phase 2 puts living processes around it.

Deliverables:

1. **Master process** (`SyncMaster`) — ASP.NET Core, single host, single binary
   - SignalR Hub at `/hubs/sync` for agent connections
   - REST API subset (bundle registry, version registration, deploy, intents, status, messages)
   - Data plane HTTP at `/content/bundles/...` (serves directly from NAS in Phase 2; no caching layer yet)
   - In-memory state with periodic JSON snapshot
2. **Agent process** (`SyncAgent`) — Windows service
   - SignalR client with automatic reconnect
   - Local state in SQLite (`agent.db`)
   - Agent state machine driving per-bundle lifecycle
   - Stub transfer engine: plain HTTP GET, single file at a time, no resume
   - Atomic activation (directory swap for zip, in-place for raw)
3. **Operator monitoring page** at `/` on the master — read-only HTML + JS that polls `GET /api/status` every 5 seconds. Shows fleet table.
4. **End-to-end happy path**:
   - Publish via Phase 1 CLI →
   - Master sees the new version →
   - Operator calls `POST /api/deploy` targeting specific agents →
   - Agents transfer, verify, activate →
   - Operator UI shows `Active`

### 1.2 Explicitly Deferred

| Concern                                         | Deferred to |
| ----------------------------------------------- | ----------- |
| `logicalNodeId` mapping, `POST /api/membership` | Phase 3     |
| Site config distribution, `ConfigUpdate`        | Phase 3     |
| Cascading via relays                            | Phase 5     |
| Cooperative safe-window                         | Phase 6     |
| Chunked huge-file transfer with resume          | Phase 7     |
| Recordings                                      | Phase 8     |
| Two-phase commit, rollback automation           | Phase 9     |
| Fleet sync window, `FleetSyncMode` gate         | Phase 10    |
| GC                                              | Phase 11    |

In Phase 2, targeting is by `agentId` only. Bundle `defaultScope` exists in the registry but is purely informational — it does not auto-trigger deployment.

### 1.3 Definition of Done

After Phase 2 an operator can:

- Register a bundle, observe the master accepting it
- Publish a version with the Phase 1 CLI, observe the master recording it
- Issue a deploy targeting 3 specific agents, watch all 3 transition through `Queued → Transferring → Verifying → Staged → Activating → Active`
- See the result on the operator monitoring page
- Restart any agent at any point in the flow and have it resume cleanly
- Restart the master and see fleet state reconstruct from agent reports plus the snapshot

------

## 2. Process Architecture

### 2.1 Master Process Layout

```
SyncMaster (ASP.NET Core Web Host, single process)
  Program.cs — Kestrel + DI wiring

  Endpoints
    /hubs/sync               SignalR Hub
    /api/...                 REST control plane (minimal-API or controllers)
    /content/bundles/...     Data plane, static file middleware
    /                        Operator monitoring page (static HTML + JS)

  Background services
    StateSnapshotService     periodic JSON snapshot (every 60s + on critical events)
    IntentDispatcher         delivers Pending intents to online agents on connect

  In-process state
    IBundleRegistry          bundle definitions
    IPublishedVersionStore   per-bundle version records
    IIntentRepository        all intents (Pending, Executing, Complete, ...)
    IFleetState              connected agents and their reported state
    IOperatorMessageQueue    warnings and errors

  Persistence
    master-state.json        atomically written; reloaded on startup
    Location: configurable, default C:\ProgramData\SyncMaster\master-state.json

  Config (Phase 2 minimal)
    appsettings.json
      "Nas": { "Root": "\\\\nas\\sync" }     UNC or local
      "Snapshot": { "Path": "...", "IntervalSeconds": 60 }
      "Kestrel": { "Endpoints": { ... } }
```

### 2.2 Agent Process Layout

```
SyncAgent (Windows Service, .NET 8)
  Program.cs — Generic Host + Worker

  Hosted services
    SyncAgentWorker          owns the SignalR connection, dispatches commands
    StateMachineRunner       drives per-bundle state transitions
    SnapshotReporter         periodic ReportStatus to master

  Local state
    agent.db                 SQLite, location C:\ProgramData\SyncAgent\
    manifests/               cached manifest.json per (bundleId, version)
    staging/                 in-progress downloads
    versions/                installed versions
    active/                  junction → current active version per bundle

  Config (Phase 2 minimal)
    agent.json
      "AgentId": "SIM-03"               default: machine hostname
      "MasterUrl": "http://master.local:8080"
      "DataRoot": "C:\\ProgramData\\SyncAgent"
      "Capabilities": ["render", "physics"]
      "AppDataRoot": "C:\\AppData"      where activation targetPaths resolve
```

### 2.3 Component Boundaries

```
┌──────────────────────────────────────────────────────────────────────────┐
│                                Master                                    │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌─────────────────┐  │
│  │  Hub Layer  │  │  REST Layer │  │ Data Plane  │  │  Operator UI    │  │
│  │  SyncHub    │  │  Endpoints  │  │  /content   │  │  static + poll  │  │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘  └────────┬────────┘  │
│         │                │                │                  │           │
│         └────────┬───────┴────────┬───────┴──────────────────┘           │
│                  │                │                                      │
│           ┌──────▼─────────┬──────▼──────────┐                           │
│           │  Orchestrator  │  Intent Store   │                           │
│           │   FleetState   │   Repository    │                           │
│           └──────┬─────────┴──────┬──────────┘                           │
│                  │                │                                      │
│                  └────────┬───────┘                                      │
│                           ▼                                              │
│                    ┌────────────────┐                                    │
│                    │   Snapshot     │  master-state.json                 │
│                    │   (atomic)     │                                    │
│                    └────────────────┘                                    │
└────────────┬───────────────────┬───────────────────────────────┬─────────┘
             │                   │                               │
   SignalR over WebSocket   HTTP GET /content/...         HTTP GET /api/...
             │                   │                               │
┌────────────▼───────────────────▼────────────┐         ┌────────▼────────┐
│                  Agent                       │         │   Operator UI    │
│                                              │         │   (browser)      │
│  ┌─────────────────────────────────────────┐ │         └──────────────────┘
│  │  SignalR Client (auto-reconnect)         │ │
│  │  StateMachineRunner                      │ │
│  │  TransferEngine (stub: HTTP GET)         │ │
│  │  Verifier                                │ │
│  │  Activator                               │ │
│  └─────────────────────────────────────────┘ │
│  ┌─────────────────────────────────────────┐ │
│  │  agent.db (SQLite)                       │ │
│  │  versions/, staging/, active/            │ │
│  └─────────────────────────────────────────┘ │
└──────────────────────────────────────────────┘
```

------

## 3. SignalR Hub and Connection Lifecycle

### 3.1 Hub Methods (Phase 2 subset)

**Agent → Master**

| Method         | Signature                                          | Purpose                                                      |
| -------------- | -------------------------------------------------- | ------------------------------------------------------------ |
| `Register`     | `Register(RegisterRequest req) → RegisterResponse` | Called on connect / reconnect. Master rebuilds agent state and replies. |
| `ReportStatus` | `ReportStatus(StatusReport report) → void`         | Per-bundle state updates during transfer. Throttled to every 5% or 2s on the agent side. |
| `AckCommand`   | `AckCommand(CommandAck ack) → void`                | Acknowledges a previously received command.                  |

**Master → Agent**

| Method           | Signature                     | Purpose                                                 |
| ---------------- | ----------------------------- | ------------------------------------------------------- |
| `ReceiveCommand` | `ReceiveCommand(Command cmd)` | Dispatch a deploy / activate / cancel / verify command. |

DTOs:

```csharp
public sealed record RegisterRequest(
    string AgentId,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<InstalledVersion> CurrentVersions);

public sealed record InstalledVersion(
    string BundleId,
    string Version,
    string GroupHash,
    DateTimeOffset InstalledAt,
    BundleState State);                   // Active, Staged, etc.

public sealed record RegisterResponse(
    string AssignedAgentId,
    DateTimeOffset ServerTime,
    IReadOnlyList<Command> ReplayedCommands);  // pending intents addressed to this agent

public sealed record StatusReport(
    string BundleId,
    BundleState State,
    string? Version,
    int? ProgressPct,
    string? ErrorDetail);

public sealed record Command(
    string CommandId,                     // = intentId for traceability
    CommandAction Action,                 // Stage | Activate | Cancel | Verify
    string? BundleId,
    string? Version,
    string? SourceUrl);

public enum CommandAction { Stage, Activate, Cancel, Verify }

public sealed record CommandAck(
    string CommandId,
    AckResult Result,                     // Received, Complete, Failed, NotApplicable
    string? ErrorDetail);
```

### 3.2 Connection Lifecycle

**Agent startup:**

```
1. Agent service starts
2. Load agent.db; reconstruct in-memory state
3. Build HubConnection with WithAutomaticReconnect()
4. Start HubConnection
5. Wait for Connected state, then call Register(...)
6. Receive RegisterResponse with ReplayedCommands
7. Dispatch each ReplayedCommand to the state machine
   (commands are idempotent on CommandId)
```

**Master receives Register:**

```
1. Look up or create AgentRecord by AgentId
2. Mark presence = Online; record ConnectionId
3. Update agent's reported InstalledVersions
4. Reconcile: detect "unknown bundle reported by agent" → warn to message queue
5. Query Intents for this agent in state {Pending, Executing}
6. Compose ReplayedCommands list
7. Return RegisterResponse
8. Trigger snapshot (within the next snapshot interval)
```

**Master disconnect handling:**

```
OnDisconnectedAsync(exception):
  1. Mark agent presence = Unreachable
  2. If any intent for this agent was in Executing state and not Ack'd as Complete,
     leave it in Executing — it will be re-delivered on reconnect (idempotent)
  3. Write warning to operator message queue if intents were in-flight
```

**SignalR reconnection:**

`HubConnectionBuilder.WithAutomaticReconnect()` defaults to retry after 0, 2, 10, 30 seconds, then gives up. In Phase 2 we override to retry indefinitely with backoff capped at 60 seconds:

```csharp
new HubConnectionBuilder()
    .WithUrl(masterUrl)
    .WithAutomaticReconnect(new IndefiniteRetryPolicy())
    .Build();

public sealed class IndefiniteRetryPolicy : IRetryPolicy
{
    public TimeSpan? NextRetryDelay(RetryContext ctx) =>
        TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, Math.Min(6, ctx.PreviousRetryCount))));
}
```

### 3.3 Idempotency Rules

- `Register` is idempotent. Repeated calls within a connection are accepted; later state replaces earlier.
- `ReceiveCommand` is idempotent on `CommandId`. The agent records each accepted `CommandId` in `PendingIntent` table; duplicates are detected and Ack'd as `Complete` (if already done) or ignored (if currently executing).
- `ReportStatus` is fire-and-forget. The master accepts the latest; older arriving out-of-order are ignored via the report's implicit ordering (most recent wins for a given `BundleId`).
- `AckCommand` is informational. Repeated acks are merged.

------

## 4. REST Endpoints in Phase 2

The full surface is documented in the API spec. Phase 2 implements the subset below. Other endpoints exist as 404 placeholders or are not routed.

| Method | Path                                            | Purpose                                                    |
| ------ | ----------------------------------------------- | ---------------------------------------------------------- |
| POST   | `/api/bundles`                                  | Register bundle                                            |
| GET    | `/api/bundles`                                  | List bundles                                               |
| GET    | `/api/bundles/{bundleId}`                       | Bundle detail                                              |
| PUT    | `/api/bundles/{bundleId}`                       | Update bundle                                              |
| DELETE | `/api/bundles/{bundleId}`                       | Deregister bundle                                          |
| POST   | `/api/bundles/{bundleId}/versions`              | Publish notification (from CLI)                            |
| POST   | `/api/deploy`                                   | Request deployment (target.type = `Agent` only in Phase 2) |
| GET    | `/api/intents`                                  | List intents                                               |
| GET    | `/api/intents/{intentId}`                       | Intent detail                                              |
| DELETE | `/api/intents/{intentId}`                       | Cancel intent                                              |
| POST   | `/api/intents/{intentId}/retry`                 | Retry failed intent                                        |
| GET    | `/api/status`                                   | Full fleet state                                           |
| GET    | `/api/status/{agentId}`                         | Single agent state                                         |
| GET    | `/api/messages`                                 | List operator messages                                     |
| DELETE | `/api/messages/{messageId}`                     | Dismiss message                                            |
| GET    | `/content/bundles/{bundleId}/{version}/{*path}` | Bundle bytes (data plane)                                  |

`POST /api/deploy` in Phase 2:

- Only accepts `target: { "type": "Agent", "agentIds": [...] }`. Other targeting types return 400.
- Creates one Pending sub-intent per resolved agent.
- For online agents, immediately dispatches `ReceiveCommand`. For offline agents, the intent stays Pending until reconnect.

`POST /api/bundles/{bundleId}/versions` in Phase 2:

- Validates bundle exists (or auto-registers with `?autoRegister=true`)
- Reads `manifest.json` from the NAS path
- Validates the manifest passes Phase 1 validation
- Records `PublishedVersion(bundleId, version, publishedAt, groupHash)` in master state
- Triggers snapshot
- Returns 201
- Does **not** auto-deploy. Deployment is operator-driven via `POST /api/deploy`.

------

## 5. Bundle Registry & Version Registration

### 5.1 In-Memory Shape

```csharp
public sealed class BundleRegistry : IBundleRegistry
{
    private readonly ConcurrentDictionary<string, BundleDefinition> _defs = new();
    private readonly ConcurrentDictionary<string, List<PublishedVersion>> _versions = new();
    private readonly ISnapshotWriter _snapshot;
    private readonly ILogger<BundleRegistry> _log;

    public Task<BundleDefinition> RegisterAsync(BundleDefinition def, CancellationToken ct)
    {
        if (!_defs.TryAdd(def.BundleId, def))
            throw new ConflictException($"Bundle {def.BundleId} already registered");
        _versions.TryAdd(def.BundleId, new List<PublishedVersion>());
        _snapshot.MarkDirty();
        return Task.FromResult(def);
    }

    public Task<PublishedVersion> RecordVersionAsync(
        string bundleId, BundleManifest manifest, CancellationToken ct)
    {
        if (!_defs.ContainsKey(bundleId))
            throw new NotFoundException($"Bundle {bundleId} not registered");

        var list = _versions[bundleId];
        lock (list)
        {
            var existing = list.FirstOrDefault(v => v.Version == manifest.Version);
            if (existing is not null)
            {
                if (existing.GroupHash != manifest.GroupHash)
                    throw new ConflictException("Version exists with different content");
                return Task.FromResult(existing);   // idempotent
            }

            var pv = new PublishedVersion(
                BundleId:    bundleId,
                Version:     manifest.Version,
                PublishedAt: manifest.PublishedAt,
                GroupHash:   manifest.GroupHash,
                ManifestPath: $"bundles/{bundleId}/versions/{manifest.Version}/manifest.json"
            );
            list.Add(pv);
            _snapshot.MarkDirty();
            return Task.FromResult(pv);
        }
    }

    // ... other methods
}

public sealed record BundleDefinition(
    string BundleId,
    DataCategory DataCategory,
    DeploymentScope DefaultScope,
    ActivationMode ActivationMode,
    int RetentionCount,
    TimeSpan StaleAfter,
    int ChunkSizeBytes);

public sealed record PublishedVersion(
    string BundleId,
    string Version,
    DateTimeOffset PublishedAt,
    string GroupHash,
    string ManifestPath);
```

### 5.2 Registration Flow End-to-End

```
syncpub publish ... --master-url http://master.local
  │
  │ HTTP POST /api/bundles/TerrainTextures/versions
  │   body: { "version": "v42", "manifestPath": "...", "publishedAt": "..." }
  │
  ▼
Master (BundleVersionEndpoint)
  1. Validate input shape (400 if malformed)
  2. Look up BundleDefinition by bundleId
       - if missing and ?autoRegister=true: synthesize from category defaults
       - if missing and not autoRegister: 404
  3. Resolve manifestPath against Nas.Root, read file
  4. Run Phase 1 ManifestValidator on the read manifest
       - if invalid: 400 with details
  5. Cross-check manifest.bundleId matches URL bundleId (400 if mismatch)
  6. Cross-check manifest.version matches body.version
  7. registry.RecordVersionAsync(bundleId, manifest, ct)
       - idempotent: returns existing PublishedVersion if content matches
  8. Trigger immediate snapshot (critical event)
  9. Return 201 with PublishedVersion summary
```

The master does **not** download or cache bundle contents at registration time. It only reads the manifest. Content stays on the NAS until an agent requests it via `GET /content/...`, at which point the master streams from NAS to agent.

------

## 6. Persistence

### 6.1 Master JSON Snapshot

Single file `master-state.json`. Atomic write: serialize to `master-state.json.tmp`, then `File.Move(.tmp, ., overwrite: true)`.

Schema:

```json
{
  "schemaVersion": 1,
  "snapshotAt":    "2026-05-17T12:00:00Z",
  "bundles": [
    {
      "bundleId":       "TerrainTextures",
      "dataCategory":   "RuntimeAsset",
      "defaultScope":   { "type": "Fleet" },
      "activationMode": "atomic-directory-swap",
      "retentionCount": 3,
      "staleAfter":     "24:00:00",
      "chunkSize":      67108864,
      "createdAt":      "2026-05-01T08:00:00Z"
    }
  ],
  "publishedVersions": [
    {
      "bundleId":     "TerrainTextures",
      "version":      "v42",
      "publishedAt":  "2026-05-15T11:00:00Z",
      "groupHash":    "sha256:7a2f...",
      "manifestPath": "bundles/TerrainTextures/versions/v42/manifest.json"
    }
  ],
  "intents": [
    {
      "intentId":       "11111111-...",
      "kind":           "Deploy",
      "agentId":        "SIM-03",
      "bundleId":       "TerrainTextures",
      "version":        "v42",
      "state":          "Pending",
      "createdAt":      "2026-05-17T10:00:00Z",
      "updatedAt":      "2026-05-17T10:00:00Z",
      "deadline":       null,
      "history": [
        { "at": "2026-05-17T10:00:00Z", "state": "Pending" }
      ]
    }
  ],
  "messages": [
    {
      "messageId": "msg-...",
      "severity":  "warning",
      "title":     "...",
      "detail":    "...",
      "createdAt": "2026-05-18T10:01:00Z"
    }
  ]
}
```

**Snapshot triggers:**

- Periodic, every 60 seconds (configurable)
- On critical events: bundle registered, version recorded, intent created, intent state change to terminal, message enqueued

The `StateSnapshotService` debounces: critical events set a dirty flag; the writer fires either immediately if the dirty flag has been set for more than 1 second, or at the next periodic tick, whichever comes first. Ensures bursts of intent updates don't write the file 100 times per second.

**Agent state is NOT in the snapshot.** Reconstructed from agent `Register` reports on startup. Master keeps an in-memory `FleetState` indexed by `agentId` that is purely volatile.

### 6.2 Master Startup

```
1. Load master-state.json (or create empty if missing)
2. Validate schemaVersion
3. Hydrate BundleRegistry, IntentRepository, MessageQueue, etc.
4. Start Kestrel
5. Start StateSnapshotService
6. Wait for agents to connect via Register
7. Agents arriving in Register provide currentVersions[] which populates FleetState
```

### 6.3 Agent SQLite Schema

```sql
CREATE TABLE BundleState (
    BundleId         TEXT PRIMARY KEY,
    State            TEXT NOT NULL,           -- Unknown, Outdated, Queued, ..., Active
    CurrentVersion   TEXT,                    -- the version currently Active
    TargetVersion    TEXT,                    -- the version we're working toward
    ProgressPct      INTEGER,
    UpdatedAt        TEXT NOT NULL
);

CREATE TABLE InstalledVersion (
    BundleId         TEXT NOT NULL,
    Version          TEXT NOT NULL,
    InstalledAt      TEXT NOT NULL,
    GroupHash        TEXT NOT NULL,
    LocalPath        TEXT NOT NULL,           -- versions/{bundleId}/{version}
    PRIMARY KEY (BundleId, Version)
);

CREATE TABLE PendingIntent (
    CommandId        TEXT PRIMARY KEY,        -- == master's intentId
    Kind             TEXT NOT NULL,           -- Stage, Activate, Cancel, Verify
    BundleId         TEXT,
    Version          TEXT,
    SourceUrl        TEXT,
    Payload          TEXT NOT NULL,           -- full JSON for forward compat
    ReceivedAt       TEXT NOT NULL,
    State            TEXT NOT NULL            -- Received, Executing, Done, Failed
);

CREATE TABLE TransferJob (
    JobId            TEXT PRIMARY KEY,        -- agent-generated GUID
    CommandId        TEXT NOT NULL,
    BundleId         TEXT NOT NULL,
    Version          TEXT NOT NULL,
    SourceUrl        TEXT NOT NULL,
    State            TEXT NOT NULL,           -- Queued, Transferring, Transferred, Failed
    BytesTotal       INTEGER,
    BytesDownloaded  INTEGER,
    StartedAt        TEXT,
    CompletedAt      TEXT,
    LastError        TEXT
);

CREATE TABLE VerificationResult (
    BundleId         TEXT NOT NULL,
    Version          TEXT NOT NULL,
    Level            INTEGER NOT NULL,
    Success          INTEGER NOT NULL,
    Detail           TEXT,
    VerifiedAt       TEXT NOT NULL,
    PRIMARY KEY (BundleId, Version, Level)
);

CREATE TABLE FailedFile (
    BundleId         TEXT NOT NULL,
    Version          TEXT NOT NULL,
    RelativePath     TEXT NOT NULL,
    ExpectedHash     TEXT NOT NULL,
    ActualHash       TEXT,
    Reason           TEXT,
    DetectedAt       TEXT NOT NULL,
    PRIMARY KEY (BundleId, Version, RelativePath, DetectedAt)
);

CREATE TABLE ActivationHistory (
    Id               INTEGER PRIMARY KEY AUTOINCREMENT,
    BundleId         TEXT NOT NULL,
    FromVersion      TEXT,
    ToVersion        TEXT NOT NULL,
    Result           TEXT NOT NULL,            -- Success, Failed, RolledBack
    ActivatedAt      TEXT NOT NULL
);

CREATE INDEX IX_PendingIntent_State ON PendingIntent(State);
CREATE INDEX IX_TransferJob_State   ON TransferJob(State);
```

`agent.db` uses WAL mode for crash safety: `PRAGMA journal_mode=WAL`. All writes are within transactions.

### 6.4 Agent Startup

```
1. Open agent.db (create schema if missing)
2. Reconcile filesystem with InstalledVersion table:
   - For each row, verify versions/{bundleId}/{version}/ exists
   - For each versions/{bundleId}/{version}/ dir, ensure it's in the table
3. Reconcile active/ junctions against table
4. Build initial RegisterRequest.CurrentVersions from InstalledVersion + BundleState
5. Connect to master, call Register
6. Apply replayed commands sequentially, each producing PendingIntent rows
```

------

## 7. Agent State Machine

### 7.1 State Diagram (Phase 2 Subset)

```
                       ┌──────────┐
                       │ Unknown  │  initial state, never registered for this bundle
                       └────┬─────┘
                            │ master sends Stage command
                            ▼
                       ┌──────────┐
            ┌──────────│  Queued  │ ◄────────────┐
            │          └────┬─────┘              │ retry
            │               │ start              │
            │               ▼                    │
            │          ┌──────────────┐          │
            │          │ Transferring │          │
            │          └────┬───┬─────┘          │
            │       fail   │   │ success         │
            │   ┌──────────┘   └─────────┐       │
            │   ▼                        ▼       │
            │ ┌─────────────────┐  ┌──────────┐  │
            │ │ TransferFailed  │──┤Transferred│ │
            │ └────────┬────────┘  └────┬─────┘  │
            │          │                │        │
            │          │            verify       │
            │          │                ▼        │
            │          │           ┌──────────┐  │
            │          │           │Verifying │  │
            │          │           └────┬───┬─┘  │
            │          │       fail  │   │ pass  │
            │          │ ┌───────────┘   └───┐   │
            │          │ ▼                   ▼   │
            │          │┌────────────────┐ ┌─────────┐
            │          ││VerificationFail│ │ Staged  │
            │          │└────────┬───────┘ └────┬────┘
            │          │         │              │ master sends Activate
            └──────────┴─────────┘              ▼
                                          ┌─────────────────┐
                                          │ ReadyToActivate │
                                          └────────┬────────┘
                                                   │ activate
                                                   ▼
                                          ┌────────────┐
                                          │ Activating │
                                          └─────┬──┬───┘
                                       fail │      │ success
                                  ┌─────────┘      └─────────┐
                                  ▼                          ▼
                          ┌──────────────────┐         ┌──────────┐
                          │ActivationFailed  │         │  Active  │
                          └──────────────────┘         └──────────┘
                                  │
                          (Phase 2: stay in failed state;
                          operator-driven retry via API)
```

`NotApplicable`, `Corrupt`, `RollbackPending`, `RolledBack`, `AwaitingSafeWindow`, `ActivationPending` exist in the architecture model but are not exercised by Phase 2's stub transfer engine. They are valid state column values; the machine simply does not transition into them in Phase 2.

### 7.2 Transition Triggers

| From                 | To                   | Trigger                                                      |
| -------------------- | -------------------- | ------------------------------------------------------------ |
| `Unknown`            | `Queued`             | `ReceiveCommand(Stage, bundleId, version, sourceUrl)` for an unknown bundle |
| any                  | `Queued`             | `ReceiveCommand(Stage, ...)` for a different version than currently Active |
| `Queued`             | `Transferring`       | `TransferEngine.Start` succeeds in initial HTTP request      |
| `Transferring`       | `Transferred`        | HTTP body fully read, written to staging                     |
| `Transferring`       | `TransferFailed`     | HTTP error, IO error, connection drop                        |
| `TransferFailed`     | `Queued`             | Auto-retry after backoff (3 attempts: 1s, 5s, 30s)           |
| `TransferFailed`     | terminal             | After max retries, an operator message is written; state stays Failed |
| `Transferred`        | `Verifying`          | Immediate, automatic                                         |
| `Verifying`          | `Staged`             | All per-file hashes match manifest                           |
| `Verifying`          | `VerificationFailed` | Any file hash mismatch                                       |
| `VerificationFailed` | `Queued`             | Auto-retry once; second failure terminal                     |
| `Staged`             | `ReadyToActivate`    | Immediate, automatic                                         |
| `ReadyToActivate`    | `Activating`         | `ReceiveCommand(Activate, bundleId, version)`                |
| `Activating`         | `Active`             | Activation succeeds                                          |
| `Activating`         | `ActivationFailed`   | Activation throws                                            |

### 7.3 Persistence Per Transition

Every transition writes to `BundleState`:

- `State` column updated
- `UpdatedAt` set to now
- `ProgressPct` updated during `Transferring`

Plus a `ReportStatus` SignalR call to the master, throttled to every 5 % progress or every 2 seconds, whichever comes first.

### 7.4 Idempotency on Restart

The agent's `agent.db` records `BundleState` at all times. On restart:

- `Queued`: re-dispatch the transfer
- `Transferring`: re-dispatch (Phase 2 has no chunked resume; just restart)
- `Transferred`: re-dispatch verification
- `Verifying`: re-dispatch verification
- `Staged` / `ReadyToActivate`: wait for next `Activate` command
- `Activating`: re-attempt activation (atomic operations make this safe)
- `Active`: nothing to do

The `PendingIntent` table holds the `CommandId` for in-flight work. On restart, the runner reloads pending intents and re-applies them.

------

## 8. Stub Transfer Engine

### 8.1 What "Stub" Means

Phase 2 uses the simplest possible transfer:

- Single HTTP GET per bundle (downloads the whole `bundle.zip` or single huge file)
- No chunking, no resume, no byte-range
- No relays — direct from master's `/content/bundles/...`
- No retry within a single transfer attempt; the state machine handles retry by going back to `Queued`

For huge files (ChunkedHugeFile category), Phase 2 also downloads as a single GET — it works but is slow and resume-unsafe. Phase 7 replaces this with chunked download.

### 8.2 Interface

```csharp
public interface ITransferEngine
{
    string MethodId { get; }
    Task<TransferResult> ExecuteAsync(
        TransferJob job,
        BundleManifest manifest,
        IProgress<TransferProgress> progress,
        CancellationToken ct);
}

public sealed record TransferJob(
    string JobId,
    string CommandId,
    string BundleId,
    string Version,
    string SourceUrl,                 // e.g. http://master.local/content/bundles/X/v42/bundle.zip
    string StagingPath);              // local destination

public sealed record TransferResult(
    bool Success,
    long BytesTransferred,
    string? ErrorDetail);

public sealed record TransferProgress(long BytesDownloaded, long? BytesTotal);
```

### 8.3 Stub Implementation

```csharp
public sealed class DirectHttpStubEngine : ITransferEngine
{
    public string MethodId => "DirectHttpStub";

    private readonly HttpClient _http;
    private readonly ILogger<DirectHttpStubEngine> _log;

    public async Task<TransferResult> ExecuteAsync(
        TransferJob job, BundleManifest manifest,
        IProgress<TransferProgress> progress, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(job.SourceUrl,
                HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength;
            Directory.CreateDirectory(Path.GetDirectoryName(job.StagingPath)!);

            await using var src = await response.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(job.StagingPath);

            var buffer = new byte[81920];
            long copied = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                copied += read;
                progress.Report(new TransferProgress(copied, total));
            }

            return new TransferResult(true, copied, null);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Transfer failed for {Bundle}/{Version}",
                job.BundleId, job.Version);
            return new TransferResult(false, 0, ex.Message);
        }
    }
}
```

After the engine returns success, the runner moves to `Verifying`.

### 8.4 Data Plane URL Resolution

The master's `/content/bundles/{bundleId}/{version}/{*path}` route maps directly to NAS:

```csharp
app.MapGet("/content/bundles/{bundleId}/{version}/{*path}",
    async (string bundleId, string version, string path,
           INasPaths paths, HttpContext ctx) =>
{
    var nasPath = paths.Resolve(bundleId, version, path);
    if (!File.Exists(nasPath))
        return Results.NotFound();

    return Results.File(nasPath, "application/octet-stream",
        enableRangeProcessing: true);   // free byte-range for future use
});
```

In Phase 2 the agent ignores range support and just downloads the whole file. Range processing is enabled here so it's available for Phases 5 and 7 without changing the master.

------

## 9. Operator Monitoring Page

### 9.1 Minimum Viable

A single static page served at `/`, plus client-side JavaScript polling `GET /api/status` every 5 seconds.

`wwwroot/index.html`:

```html
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>SyncMaster · Fleet</title>
  <style>
    body { font: 14px system-ui, sans-serif; margin: 1rem; }
    table { border-collapse: collapse; width: 100%; }
    th, td { padding: 4px 8px; border-bottom: 1px solid #ddd; text-align: left; }
    .state-Active { color: #080; }
    .state-Transferring { color: #06f; }
    .state-TransferFailed, .state-VerificationFailed, .state-ActivationFailed { color: #c00; }
    .presence-Online { color: #080; }
    .presence-Unreachable { color: #999; }
  </style>
</head>
<body>
  <h1>SyncMaster · Fleet</h1>
  <p id="updated"></p>
  <table id="fleet">
    <thead>
      <tr><th>Agent</th><th>Presence</th><th>Last Seen</th><th>Bundles</th></tr>
    </thead>
    <tbody></tbody>
  </table>
  <h2>Recent Intents</h2>
  <table id="intents">
    <thead>
      <tr><th>Intent</th><th>Kind</th><th>Agent</th><th>Bundle</th><th>State</th><th>Progress</th></tr>
    </thead>
    <tbody></tbody>
  </table>
  <h2>Messages</h2>
  <ul id="messages"></ul>
  <script src="app.js"></script>
</body>
</html>
```

`wwwroot/app.js`:

```javascript
async function refresh() {
  const [status, intents, messages] = await Promise.all([
    fetch('/api/status').then(r => r.json()),
    fetch('/api/intents?limit=50').then(r => r.json()),
    fetch('/api/messages?dismissed=false&limit=20').then(r => r.json())
  ]);

  document.getElementById('updated').textContent =
    `Updated ${new Date().toLocaleTimeString()}`;

  const fleetBody = document.querySelector('#fleet tbody');
  fleetBody.innerHTML = status.agents.map(a => `
    <tr>
      <td>${a.agentId}</td>
      <td class="presence-${a.presence}">${a.presence}</td>
      <td>${a.lastSeen ?? '—'}</td>
      <td>${Object.entries(a.bundles).map(([id, s]) =>
        `<span class="state-${s.state}">${id}: ${s.state}${s.version ? ' @ ' + s.version : ''}${s.progressPct != null ? ` (${s.progressPct}%)` : ''}</span>`
      ).join(' · ')}</td>
    </tr>
  `).join('');

  const intentsBody = document.querySelector('#intents tbody');
  intentsBody.innerHTML = intents.items.map(i => `
    <tr>
      <td><code>${i.intentId.slice(0, 8)}</code></td>
      <td>${i.kind}</td>
      <td>${i.agentId}</td>
      <td>${i.bundleId} @ ${i.version}</td>
      <td class="state-${i.state}">${i.state}</td>
      <td>${i.progressPct ?? '—'}</td>
    </tr>
  `).join('');

  document.getElementById('messages').innerHTML = messages.items.map(m =>
    `<li><strong>${m.severity}</strong>: ${m.title} — ${m.detail}</li>`
  ).join('');
}

refresh();
setInterval(refresh, 5000);
```

Deliberately ugly, fully read-only, no framework. Phase 3 grows controls (cancel intent, trigger deploy). Phase 4 onwards can upgrade to SignalR push for live updates.

------

## 10. Intent Reliability and Reconnection

### 10.1 Happy Path

```
operator POST /api/deploy {target=Agent,SIM-03; bundle=X; version=v42}
  → master creates Intent #i1 (Pending, agent=SIM-03)
  → master finds SIM-03 connected, sends ReceiveCommand(i1, Stage, X, v42, url)
  → master updates Intent #i1 → Executing
  → agent receives, stores in PendingIntent, ack Received
  → agent state machine: Unknown → Queued → Transferring → ... → Active
  → agent sends ReportStatus updates along the way
  → final: agent sends AckCommand(i1, Complete)
  → master updates Intent #i1 → Complete
```

### 10.2 Agent Disconnects Mid-Transfer

```
[Agent at state=Transferring, 40% complete]
[Agent process dies or network drops]
master OnDisconnectedAsync(SIM-03): presence=Unreachable
  Intent #i1 stays in Executing
[SignalR reconnect after backoff]
[Agent process restarts; loads agent.db; finds BundleState row in Transferring]
agent.SignalR.Connected → calls Register(SIM-03, ..., currentVersions=[...])
master Register handler:
  finds Intent #i1 (Executing, agent=SIM-03)
  composes ReplayedCommands = [Command(i1, Stage, X, v42, url)]
  returns RegisterResponse with replay
agent receives, dispatches to state machine
state machine sees existing BundleState in Transferring:
  → restart transfer (Phase 2: from scratch; Phase 7+ would resume)
...continues to Active
```

### 10.3 Master Restarts Mid-Transfer

```
[Master process dies]
[Agents detect disconnect via heartbeat / read timeout]
[Agents continue with whatever transfer is in flight if it's still going]
  - If transfer was direct from master, the GET request fails when master dies →
    state machine moves Transferring → TransferFailed → retries → Queued
  - Agent SignalR client tries to reconnect (indefinite backoff)
[Master restarts]
  Loads master-state.json → BundleRegistry, IntentRepository, MessageQueue rehydrated
  Starts SignalR Hub
[Agents reconnect]
  agent.Register(...): master rebuilds FleetState
  Pending and Executing intents for this agent are replayed
[State machine resumes]
```

### 10.4 Intent Cancellation

```
operator DELETE /api/intents/i1
master:
  if state == Pending → mark Cancelled (no command was sent; nothing to undo)
  if state == Executing → ?
```

In Phase 2, cancelling an Executing intent is best-effort:

- Master sets state = Cancelled.
- Master sends `ReceiveCommand(Cancel, bundleId)` to the agent.
- Agent's runner checks: if its current job for this bundle matches the cancelled intent, it aborts the transfer or rolls back staging.
- Agent acks Cancel with `AckResult.Complete`.

If the agent is offline, the intent is marked Cancelled immediately and a `Cancel` command is queued in `ReplayedCommands` for the next reconnect. If the agent has already moved to `Active` by then, the Cancel is a no-op.

------

## 11. Code Sketches

### 11.1 Master Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();

builder.Services.Configure<NasOptions>(builder.Configuration.GetSection("Nas"));
builder.Services.Configure<SnapshotOptions>(builder.Configuration.GetSection("Snapshot"));

builder.Services.AddSingleton<INasPaths, NasPaths>();
builder.Services.AddSingleton<IBundleRegistry, BundleRegistry>();
builder.Services.AddSingleton<IIntentRepository, IntentRepository>();
builder.Services.AddSingleton<IFleetState, FleetState>();
builder.Services.AddSingleton<IOperatorMessageQueue, OperatorMessageQueue>();
builder.Services.AddSingleton<ISnapshotWriter, SnapshotWriter>();
builder.Services.AddSingleton<IIntentDispatcher, IntentDispatcher>();

builder.Services.AddHostedService<StateSnapshotService>();
builder.Services.AddHostedService<StartupRehydrationService>();

var app = builder.Build();

app.UseDefaultFiles();              // serves /index.html for /
app.UseStaticFiles();               // serves /wwwroot/*

app.MapHub<SyncHub>("/hubs/sync");

// REST endpoints
app.MapBundleEndpoints();
app.MapIntentEndpoints();
app.MapStatusEndpoints();
app.MapMessageEndpoints();
app.MapDeployEndpoint();

// Data plane
app.MapDataPlaneEndpoints();

app.Run();
```

### 11.2 SignalR Hub

```csharp
public sealed class SyncHub : Hub
{
    private readonly IFleetState _fleet;
    private readonly IIntentRepository _intents;
    private readonly IBundleRegistry _registry;
    private readonly IOperatorMessageQueue _messages;
    private readonly ILogger<SyncHub> _log;

    public async Task<RegisterResponse> Register(RegisterRequest req)
    {
        var connectionId = Context.ConnectionId;

        _fleet.RegisterAgent(req.AgentId, connectionId, req.Capabilities, req.CurrentVersions);

        // Reconcile unknown bundles reported by agent
        foreach (var iv in req.CurrentVersions)
        {
            if (!await _registry.ExistsAsync(iv.BundleId, Context.ConnectionAborted))
            {
                _messages.Enqueue(new OperatorMessage(
                    Severity.Warning,
                    "Agent reports unknown bundle",
                    $"Agent {req.AgentId} reports {iv.BundleId}@{iv.Version}; bundle not registered."));
            }
        }

        var pending = await _intents.GetPendingForAgentAsync(req.AgentId, Context.ConnectionAborted);
        var replay = pending.Select(i => i.ToCommand()).ToList();

        _log.LogInformation("Agent {AgentId} registered; replaying {Count} commands",
            req.AgentId, replay.Count);

        return new RegisterResponse(req.AgentId, DateTimeOffset.UtcNow, replay);
    }

    public Task ReportStatus(StatusReport report)
    {
        var agentId = _fleet.GetAgentIdForConnection(Context.ConnectionId);
        if (agentId is null) return Task.CompletedTask;
        _fleet.UpdateBundleStatus(agentId, report);
        return Task.CompletedTask;
    }

    public async Task AckCommand(CommandAck ack)
    {
        var agentId = _fleet.GetAgentIdForConnection(Context.ConnectionId);
        if (agentId is null) return;

        switch (ack.Result)
        {
            case AckResult.Received:
                await _intents.UpdateStateAsync(ack.CommandId, IntentState.Executing, Context.ConnectionAborted);
                break;
            case AckResult.Complete:
                await _intents.UpdateStateAsync(ack.CommandId, IntentState.Complete, Context.ConnectionAborted);
                break;
            case AckResult.Failed:
                await _intents.UpdateStateAsync(ack.CommandId, IntentState.Failed, Context.ConnectionAborted);
                _messages.Enqueue(new OperatorMessage(
                    Severity.Error, "Intent failed",
                    $"Agent {agentId} reported failure on intent {ack.CommandId}: {ack.ErrorDetail}"));
                break;
            case AckResult.NotApplicable:
                await _intents.UpdateStateAsync(ack.CommandId, IntentState.Complete, Context.ConnectionAborted);
                break;
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var agentId = _fleet.GetAgentIdForConnection(Context.ConnectionId);
        if (agentId is not null)
        {
            _fleet.MarkUnreachable(agentId);
            _log.LogInformation("Agent {AgentId} disconnected", agentId);
        }
        await base.OnDisconnectedAsync(exception);
    }
}
```

### 11.3 Agent State Machine Runner

```csharp
public sealed class StateMachineRunner
{
    private readonly IAgentDb _db;
    private readonly IHubProxy _hub;            // wraps HubConnection
    private readonly ITransferEngine _transfer;
    private readonly IVerifier _verifier;
    private readonly IActivator _activator;
    private readonly ILogger<StateMachineRunner> _log;

    private readonly Channel<Command> _inbox =
        Channel.CreateUnbounded<Command>(new() { SingleReader = true });

    public ValueTask EnqueueAsync(Command cmd) => _inbox.Writer.WriteAsync(cmd);

    public async Task RunAsync(CancellationToken ct)
    {
        await foreach (var cmd in _inbox.Reader.ReadAllAsync(ct))
        {
            try
            {
                await HandleAsync(cmd, ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Command handling failed: {CommandId}", cmd.CommandId);
                await _hub.AckAsync(new CommandAck(cmd.CommandId, AckResult.Failed, ex.Message));
            }
        }
    }

    private async Task HandleAsync(Command cmd, CancellationToken ct)
    {
        // Idempotency: if we've already processed this CommandId to Done state, ack and return
        if (await _db.IsCommandDoneAsync(cmd.CommandId, ct))
        {
            await _hub.AckAsync(new CommandAck(cmd.CommandId, AckResult.Complete, null));
            return;
        }

        await _db.UpsertPendingIntentAsync(cmd, ct);
        await _hub.AckAsync(new CommandAck(cmd.CommandId, AckResult.Received, null));

        switch (cmd.Action)
        {
            case CommandAction.Stage:    await HandleStageAsync(cmd, ct); break;
            case CommandAction.Activate: await HandleActivateAsync(cmd, ct); break;
            case CommandAction.Cancel:   await HandleCancelAsync(cmd, ct); break;
            case CommandAction.Verify:   await HandleVerifyAsync(cmd, ct); break;
        }
    }

    private async Task HandleStageAsync(Command cmd, CancellationToken ct)
    {
        await _db.SetBundleStateAsync(cmd.BundleId!, BundleState.Queued, cmd.Version, ct);
        await _hub.ReportStatusAsync(cmd.BundleId!, BundleState.Queued, cmd.Version);

        var attempts = 0;
        const int maxAttempts = 3;

        while (true)
        {
            attempts++;
            await _db.SetBundleStateAsync(cmd.BundleId!, BundleState.Transferring, cmd.Version, ct);
            await _hub.ReportStatusAsync(cmd.BundleId!, BundleState.Transferring, cmd.Version, 0);

            var stagingPath = _paths.StagingPath(cmd.BundleId!, cmd.Version!);
            var job = new TransferJob(
                JobId:        Guid.NewGuid().ToString(),
                CommandId:    cmd.CommandId,
                BundleId:     cmd.BundleId!,
                Version:      cmd.Version!,
                SourceUrl:    cmd.SourceUrl!,
                StagingPath:  stagingPath);

            var progress = new Progress<TransferProgress>(p =>
            {
                if (p.BytesTotal is long total && total > 0)
                {
                    var pct = (int)(100 * p.BytesDownloaded / total);
                    _ = _hub.ReportStatusAsync(cmd.BundleId!, BundleState.Transferring, cmd.Version, pct);
                }
            });

            var manifest = await ReadManifestAsync(cmd, ct);   // separate small GET for manifest.json
            var result = await _transfer.ExecuteAsync(job, manifest, progress, ct);

            if (result.Success)
            {
                await _db.SetBundleStateAsync(cmd.BundleId!, BundleState.Transferred, cmd.Version, ct);
                break;
            }

            await _db.SetBundleStateAsync(cmd.BundleId!, BundleState.TransferFailed, cmd.Version, ct);
            if (attempts >= maxAttempts)
            {
                await _hub.AckAsync(new CommandAck(cmd.CommandId, AckResult.Failed,
                    $"Transfer failed after {maxAttempts} attempts: {result.ErrorDetail}"));
                return;
            }
            await Task.Delay(BackoffFor(attempts), ct);
        }

        // Verifying
        await _db.SetBundleStateAsync(cmd.BundleId!, BundleState.Verifying, cmd.Version, ct);
        await _hub.ReportStatusAsync(cmd.BundleId!, BundleState.Verifying, cmd.Version);

        var manifest2 = await ReadManifestAsync(cmd, ct);
        var verify = await _verifier.VerifyAsync(manifest2,
            _paths.StagingPath(cmd.BundleId!, cmd.Version!), VerificationMode.FullHash, ct);

        if (!verify.Success)
        {
            await _db.SetBundleStateAsync(cmd.BundleId!, BundleState.VerificationFailed, cmd.Version, ct);
            await _hub.AckAsync(new CommandAck(cmd.CommandId, AckResult.Failed,
                $"Verification failed: {verify.Detail}"));
            return;
        }

        // Move from staging → versions/
        Directory.Move(
            _paths.StagingPath(cmd.BundleId!, cmd.Version!),
            _paths.VersionPath(cmd.BundleId!, cmd.Version!));

        await _db.RecordInstalledVersionAsync(cmd.BundleId!, cmd.Version!, manifest2.GroupHash, ct);
        await _db.SetBundleStateAsync(cmd.BundleId!, BundleState.Staged, cmd.Version, ct);
        await _db.SetBundleStateAsync(cmd.BundleId!, BundleState.ReadyToActivate, cmd.Version, ct);
        await _hub.ReportStatusAsync(cmd.BundleId!, BundleState.ReadyToActivate, cmd.Version);

        // Phase 2: Stage command finishes here, returning Complete.
        // Activation requires a separate Activate command.
        await _hub.AckAsync(new CommandAck(cmd.CommandId, AckResult.Complete, null));
        await _db.MarkCommandDoneAsync(cmd.CommandId, ct);
    }

    private async Task HandleActivateAsync(Command cmd, CancellationToken ct)
    {
        var state = await _db.GetBundleStateAsync(cmd.BundleId!, ct);
        if (state.State != BundleState.ReadyToActivate)
        {
            await _hub.AckAsync(new CommandAck(cmd.CommandId, AckResult.Failed,
                $"Cannot activate from state {state.State}"));
            return;
        }

        await _db.SetBundleStateAsync(cmd.BundleId!, BundleState.Activating, cmd.Version, ct);
        await _hub.ReportStatusAsync(cmd.BundleId!, BundleState.Activating, cmd.Version);

        var result = await _activator.ActivateAsync(cmd.BundleId!, cmd.Version!, ct);

        if (result.Success)
        {
            await _db.SetBundleStateAsync(cmd.BundleId!, BundleState.Active, cmd.Version, ct);
            await _hub.ReportStatusAsync(cmd.BundleId!, BundleState.Active, cmd.Version);
            await _hub.AckAsync(new CommandAck(cmd.CommandId, AckResult.Complete, null));
        }
        else
        {
            await _db.SetBundleStateAsync(cmd.BundleId!, BundleState.ActivationFailed, cmd.Version, ct);
            await _hub.AckAsync(new CommandAck(cmd.CommandId, AckResult.Failed, result.ErrorDetail));
        }
        await _db.MarkCommandDoneAsync(cmd.CommandId, ct);
    }

    private static TimeSpan BackoffFor(int attempt) =>
        attempt switch { 1 => TimeSpan.FromSeconds(1),
                         2 => TimeSpan.FromSeconds(5),
                         _ => TimeSpan.FromSeconds(30) };
}
```

### 11.4 Atomic Activation (Directory Swap)

```csharp
public sealed class DirectorySwapActivator : IActivator
{
    private readonly IAgentPaths _paths;
    private readonly ILogger<DirectorySwapActivator> _log;

    public async Task<ActivationResult> ActivateAsync(string bundleId, string version, CancellationToken ct)
    {
        var manifest = await ReadManifestAsync(bundleId, version, ct);

        if (manifest.Container == ContainerKind.Zip)
        {
            // Extract zip into versions/{bundleId}/{version}/extracted/
            var extractedPath = _paths.ExtractedPath(bundleId, version);
            var zipPath       = _paths.ArchivePath(bundleId, version, manifest.Archive!.Name);
            if (!Directory.Exists(extractedPath))
                ZipFile.ExtractToDirectory(zipPath, extractedPath);
        }

        // Repoint junction
        var targetRoot = _paths.ActiveJunction(bundleId);
        var newTarget = manifest.Container == ContainerKind.Zip
            ? _paths.ExtractedPath(bundleId, version)
            : _paths.RawFilePath(bundleId, version, manifest.File!.Name);

        try
        {
            await JunctionWriter.RepointAsync(targetRoot, newTarget);
        }
        catch (Exception ex)
        {
            return new ActivationResult(false, ex.Message);
        }

        return new ActivationResult(true, null);
    }
}
```

`JunctionWriter` uses `CreateSymbolicLink` Win32 API or `mklink /J` shell-out; the rename trick (create new junction at temp name + atomic rename over old) gives atomic activation. Details deferred to implementation.

### 11.5 Intent Dispatcher

When a new Intent is created (via `POST /api/deploy`), the dispatcher delivers immediately if the agent is online:

```csharp
public sealed class IntentDispatcher : IIntentDispatcher
{
    private readonly IHubContext<SyncHub> _hub;
    private readonly IFleetState _fleet;
    private readonly IIntentRepository _intents;

    public async Task DispatchAsync(Intent intent, CancellationToken ct)
    {
        var connId = _fleet.GetConnectionId(intent.AgentId);
        if (connId is null) return;     // agent offline; will replay on next Register

        var cmd = intent.ToCommand();
        await _hub.Clients.Client(connId).SendAsync("ReceiveCommand", cmd, ct);
        await _intents.UpdateStateAsync(intent.IntentId, IntentState.Executing, ct);
    }
}
```

### 11.6 Agent Hub Proxy

```csharp
public sealed class HubProxy : IHubProxy
{
    private readonly HubConnection _conn;
    private readonly StateMachineRunner _runner;
    private readonly ILogger<HubProxy> _log;

    public HubProxy(IOptions<AgentOptions> opts, StateMachineRunner runner, ILogger<HubProxy> log)
    {
        _runner = runner;
        _log = log;

        _conn = new HubConnectionBuilder()
            .WithUrl($"{opts.Value.MasterUrl}/hubs/sync")
            .WithAutomaticReconnect(new IndefiniteRetryPolicy())
            .Build();

        _conn.On<Command>("ReceiveCommand", async cmd =>
        {
            _log.LogDebug("Received command {Id} {Action}", cmd.CommandId, cmd.Action);
            await _runner.EnqueueAsync(cmd);
        });

        _conn.Reconnected += async _ =>
        {
            await ReregisterAsync(CancellationToken.None);
        };
    }

    public async Task StartAsync(AgentOptions opts, IReadOnlyList<InstalledVersion> current, CancellationToken ct)
    {
        await _conn.StartAsync(ct);
        var resp = await _conn.InvokeAsync<RegisterResponse>("Register",
            new RegisterRequest(opts.AgentId, opts.Capabilities, current), ct);

        foreach (var cmd in resp.ReplayedCommands)
            await _runner.EnqueueAsync(cmd);
    }

    public Task ReportStatusAsync(string bundleId, BundleState state, string? version, int? pct = null) =>
        _conn.InvokeAsync("ReportStatus", new StatusReport(bundleId, state, version, pct, null));

    public Task AckAsync(CommandAck ack) =>
        _conn.InvokeAsync("AckCommand", ack);
}
```

------

## 12. Acceptance Tests

The test inventory for "Phase 2 done":

### 12.1 Master Unit Tests

| Test                                                         | Pass condition                               |
| ------------------------------------------------------------ | -------------------------------------------- |
| `POST /api/bundles` with valid body                          | 201, bundle in registry                      |
| `POST /api/bundles` with duplicate id                        | 409                                          |
| `POST /api/bundles/{id}/versions` for unregistered bundle    | 404                                          |
| `POST /api/bundles/{id}/versions ?autoRegister=true` for unregistered | 201, bundle auto-created                     |
| `POST /api/bundles/{id}/versions` twice with same content    | 201 then 200 (idempotent)                    |
| `POST /api/bundles/{id}/versions` twice with different content | 201 then 409                                 |
| Snapshot write then process restart                          | All registered bundles and intents recovered |

### 12.2 Agent Unit Tests

| Test                                                 | Pass condition                                               |
| ---------------------------------------------------- | ------------------------------------------------------------ |
| Empty agent.db on startup                            | Schema created, RegisterRequest.CurrentVersions = []         |
| InstalledVersion row but no versions/ directory      | Reconciler flags inconsistency, writes to operator queue via next Register |
| ReceiveCommand(Stage) idempotent: same command twice | Second call ack'd Complete without re-running transfer       |
| Transfer succeeds → state Active                     | All state transitions persisted in BundleState; final InstalledVersion row exists |
| Transfer fails 3 times → state TransferFailed        | LastError recorded; AckCommand(Failed) sent                  |
| Verification mismatch → state VerificationFailed     | FailedFile row recorded; one retry; second failure terminal  |

### 12.3 End-to-End

| Test                                    | Pass condition                                               |
| --------------------------------------- | ------------------------------------------------------------ |
| Single agent, single bundle, happy path | After deploy, state reaches Active on agent and master sees it |
| 3 agents, fleet deploy                  | All 3 reach Active within reasonable time                    |
| Agent killed mid-transfer               | On restart and reconnect, transfer restarts; reaches Active  |
| Master killed mid-transfer              | On restart, agent reconnects; intent state matches; transfer completes |
| Cancel pending intent (agent offline)   | Intent marked Cancelled; agent on reconnect does not receive the command |
| Cancel executing intent (agent online)  | Agent receives Cancel, aborts current transfer, ack Complete |

### 12.4 Operator UI

| Test                                | Pass condition                                               |
| ----------------------------------- | ------------------------------------------------------------ |
| Fresh open after 3 agents connected | Table shows 3 rows with `Online`                             |
| Mid-transfer state                  | Bundle column shows `Transferring (NN%)` and updates every 5s |
| Intent failure                      | Messages section shows the error                             |

------

## 13. Implementation Sequence

A suggested order within Phase 2. Each step independently testable.

1. **DTOs** in a shared library (`SyncSystem.Contracts`): `RegisterRequest`, `Command`, `StatusReport`, `CommandAck`, `BundleDefinition`, `PublishedVersion`, etc. Phase 1's `BundleManifest` is reused.
2. **Master state primitives** (`SyncSystem.Master.State`): `BundleRegistry`, `IntentRepository`, `FleetState`, `OperatorMessageQueue` — all in-memory, no persistence yet. Unit tests.
3. **Snapshot writer**: serialize state to JSON, load back. Atomic write. Test by snapshotting, mutating, reloading.
4. **`StateSnapshotService`**: hosted service, debounced writes. Test with critical-event triggers.
5. **REST endpoints — bundle registry** (no SignalR yet). `POST /api/bundles`, `GET`, `PUT`, `DELETE`. Tests against HTTP.
6. **REST endpoint — `POST /api/bundles/{id}/versions`**. Reads manifest from NAS, validates, records. Tests with Phase 1's CLI as input.
7. **Data plane endpoint** `/content/bundles/{*}` mapping to NAS. Test with curl.
8. **SignalR Hub `SyncHub`** with `Register`, `ReportStatus`, `AckCommand` methods. No `ReceiveCommand` dispatch yet.
9. **Agent skeleton**: Windows service, agent.db schema, HubProxy connecting to master with Register. Agents appear in `/api/status` after connect.
10. **`POST /api/deploy`** with `Agent` target. Creates intents; `IntentDispatcher` sends `ReceiveCommand`. Test against an agent that just acks.
11. **Stub `DirectHttpStubEngine`** in the agent. Downloads `bundle.zip` from master into staging.
12. **`StateMachineRunner`** with the Phase 2 state subset. Test with a real transfer.
13. **`Verifier`** implementation: re-hash each file in the extracted zip, compare to manifest. Test with intentional corruption.
14. **`DirectorySwapActivator`**: extract zip, repoint junction, atomic. Test on a real Windows directory.
15. **Activate command path** end-to-end: agent reaches `Active`, master sees `Complete`.
16. **Operator UI**: static HTML + JS polling. Verify the table populates correctly.
17. **Restart resilience tests** from §12.3.

After step 17, Phase 2 is complete. Phase 3 can build site config and identity mapping on top.

------

## 14. Open Questions for Implementation

A few decisions that can be deferred but should be made before Phase 2 starts coding:

- **Manifest fetch protocol on agent side**: in §11.3 the runner calls `ReadManifestAsync(cmd, ct)` separately from the bundle.zip download. The simplest implementation is a small HTTP GET against `/content/bundles/{id}/{ver}/manifest.json` before the main transfer. Confirm this matches your taste, or push it into the transfer engine as a two-step operation.
- **Activation path target for zip bundles in Phase 2**: the agent extracts zip into `versions/{bundleId}/{version}/extracted/` and points `active/{bundleId}` at that. The app data root for the consuming app is separate (`AppDataRoot` in agent.json). If you want a junction in the app's data tree (e.g. `C:\AppData\TerrainTextures` → agent's versions dir), that needs an extra step. Confirm preference.
- **In-place activation in Phase 2**: ChunkedHugeFile category uses `in-place` activation. Phase 2 will write the downloaded file directly to `activation.targetPath` (or default location). Confirm this is acceptable for the file types in question — there should be no app process holding open file handles during activation.
- **Operator UI hosting**: served from `wwwroot/` under the master process. No separate web server. OK?
- **Logging destination**: in Phase 2, both master and agent log to console (and Windows Event Log for service). No structured log aggregation. Defer log shipping to a later phase. OK?

These are minor; flag preferences and the build can proceed.



# Phase 3 Detailed Design

## Site Configuration, Identity Mapping, Group Membership

*Companion to Architecture Design Document Revision 2, API Specification, Phase 1 and Phase 2 Designs* *Targets `Phase 3` from §21 of the architecture doc* *C# / .NET 8 · ASP.NET Core · SignalR · May 2026*

------

## 1. Phase Scope

### 1.1 What Phase 3 Adds On Top of Phase 2

Phase 2 delivered fleet visibility and direct deployment to specific agents. Phase 3 introduces the topology layer above it: who is where, who serves which logical node, who belongs to which group, and the configuration that ties it all together.

Deliverables:

1. **Site configuration system**
   - Canonical JSON file on master (single source of truth for topology, identity, defaults, and app pass-through)
   - `POST /api/config/reload` endpoint
   - Per-agent slice computation
   - `ConfigUpdate` SignalR push (master → agent) on reload
   - Agent local cache of its slice for restart resilience
   - `bundles.ensured` mechanism (config-declared bundles that must always exist)
2. **Two-tier identity**
   - `agentId` ↔ `logicalNodeIds` mapping in site config
   - Master-side resolution of `logicalNodeId → agentId`
   - API extended: `POST /api/deploy` accepts `target.type ∈ { Fleet, Agent, LogicalNode, Group, Capability }`
3. **Group membership**
   - `POST /api/membership` endpoint
   - Per-agent `currentGroupId` state
   - Auto-deploy on membership change for Group-scoped bundles
4. **Operator UI**
   - Segments as visual grouping
   - Logical nodes shown per agent
   - Capabilities, current group, and any topology data from site config visible

### 1.2 Explicitly Deferred

| Concern                                                 | Deferred to |
| ------------------------------------------------------- | ----------- |
| Relay agents as data-plane HTTP servers within segments | Phase 5     |
| Cooperative safe-window activation                      | Phase 6     |
| Chunked transfer / huge file resume                     | Phase 7     |
| Recordings                                              | Phase 8     |
| Two-phase commit, rollback automation                   | Phase 9     |
| Auto-deploy on publish, fleet sync window               | Phase 10    |
| Garbage collection                                      | Phase 11    |

Site config in Phase 3 declares segments and identifies which agent in each segment is the relay. The *relay role* is described and persisted — but **relays in Phase 3 still serve no traffic**. The master remains the only data-plane source. Phase 5 lights up the data-plane HTTP role on relays.

`operational` fields like `fleetSyncWindow`, `diskWatermarkPercent`, `sessionQueueDepth`, and `agentRetention` exist in the config schema for forward compatibility but are not yet consumed in Phase 3.

### 1.3 Definition of Done

After Phase 3 an operator can:

- Edit `site-config.json` on the master, call `POST /api/config/reload`, and see every affected agent receive its updated slice within seconds
- Issue a deploy targeting logical nodes (`target.type: "LogicalNode"`) and see the master resolve to the correct agents
- Issue a deploy targeting a capability (`target.type: "Capability"`) and have it land on every agent that declares that capability in config
- Call `POST /api/membership` to put an agent in a group; for any Group-scoped bundle the agent is missing, the master creates Pending intents automatically
- See the fleet on the operator UI organised by segment, with logical nodes, capabilities, and group membership visible per agent

------

## 2. Site Configuration System

### 2.1 File Location and Format

A single JSON file on the master.

- Default path: `C:\ProgramData\SyncMaster\site-config.json`

- Override via `appsettings.json`:

  ```json
  { "SiteConfig": { "Path": "D:/conf/site-config.json" } }
  ```

- UTF-8, no BOM; LF line endings preferred but CRLF accepted

- Hot-reloadable via `POST /api/config/reload`

The master does not watch the file for changes — reload is explicit. This matches the operator-driven model (file edit, then API call to reload) and avoids partial-read races during a multi-line edit.

### 2.2 Schema

```json
{
  "schemaVersion": 1,
  "topology": {
    "master":   { "agentId": "MASTER-01", "dataPlaneUrl": "http://10.0.0.1:8080" },
    "nas":      { "uncPath": "\\\\nas\\sync", "localPath": "D:/sync" },
    "segments": [
      { "segmentId": "seg-A", "relayAgentId": "REL-A1", "description": "Primary lab" },
      { "segmentId": "seg-B", "relayAgentId": "REL-B1", "description": "East annex" }
    ],
    "agents": [
      { "agentId": "MASTER-01", "hostname": "master.local", "segmentId": "seg-A",
        "capabilities": [], "isRelay": false, "isMaster": true, "logicalNodeIds": [] },
      { "agentId": "REL-A1",   "hostname": "relay-a.local", "segmentId": "seg-A",
        "capabilities": ["relay"], "isRelay": true,  "logicalNodeIds": [] },
      { "agentId": "SIM-03",   "hostname": "sim03.local",  "segmentId": "seg-A",
        "capabilities": ["render", "physics"], "isRelay": false, "logicalNodeIds": [42, 43] },
      { "agentId": "SIM-04",   "hostname": "sim04.local",  "segmentId": "seg-B",
        "capabilities": ["render"], "isRelay": false, "logicalNodeIds": [44] }
    ]
  },
  "bundles": {
    "ensured": [
      {
        "bundleId":       "GlobalAITables",
        "dataCategory":   "Dataset",
        "defaultScope":   { "type": "Fleet" },
        "activationMode": "atomic-directory-swap",
        "retentionCount": 5,
        "staleAfter":     "24h",
        "chunkSize":      "64MB"
      }
    ]
  },
  "categoryDefaults": {
    "RuntimeAsset":    { "chunkSize": "64MB", "verifyMode": "ChunkHash", "staleAfter": "24h" },
    "ChunkedHugeFile": { "chunkSize": "64MB", "verifyMode": "ChunkHash", "staleAfter": "24h" },
    "Config":          { "verifyMode": "FullHash", "staleAfter": "1h" },
    "Dataset":         { "verifyMode": "FullHash", "staleAfter": "12h" },
    "Recording":       { "chunkSize": "64MB", "compressionLevel": "Optimal" }
  },
  "operational": {
    "fleetSyncWindow":     "01:00-05:00",
    "diskWatermarkPercent": 10,
    "sessionQueueDepth":    5,
    "agentRetention": {
      "bundles":    { "keepActiveAndPrevious": true, "keepLastN": 2 },
      "recordings": { "keepLastNSessions": 5 }
    }
  },
  "appSettings": {
    "_comment": "Opaque pass-through to consuming app via the agent",
    "renderQuality": "high",
    "physicsTimestep": "0.016"
  }
}
```

### 2.3 Field Reference

**`topology.master`** — declares which `agentId` is the master agent and the URL relays/nodes use for the data plane.

**`topology.nas`** — both `uncPath` and `localPath` may be set:

- If `localPath` is set and resolves to an existing directory on the master machine, the master uses local filesystem access (no SMB).
- Otherwise the master uses `uncPath` via SMB.
- The master may use both: `localPath` for itself, `uncPath` exposed to other tooling (like the publish CLI on a separate build machine).

**`topology.segments[]`** — one entry per network segment. `relayAgentId` must match an agent's `agentId`. In Phase 3 this is recorded but not yet used for traffic routing.

**`topology.agents[]`** — every machine in the system, including the master and relays.

| Field            | Type     | Notes                                                        |
| ---------------- | -------- | ------------------------------------------------------------ |
| `agentId`        | string   | Unique. Must match the agent's bootstrap config.             |
| `hostname`       | string   | Informational; not used for connectivity.                    |
| `segmentId`      | string   | Must reference `segments[].segmentId`.                       |
| `capabilities`   | string[] | Free-form labels. Used for capability-scoped targeting. Empty array is valid. |
| `isRelay`        | bool     | If true, this agent is the relay for its segment. Phase 5 activates the role. |
| `isMaster`       | bool     | If true, this agent is co-located with the master process. Affects NAS access optimization. Defaults to false. |
| `logicalNodeIds` | int[]    | Integer IDs the consuming app uses on this machine. Empty for master/relay machines. |

**`bundles.ensured[]`** — bundles guaranteed to exist after every config reload. Same shape as the body of `POST /api/bundles`. See §2.5 for behaviour.

**`categoryDefaults`** — per-data-category defaults applied when a bundle definition omits the corresponding field. The master fills in missing values when registering a bundle. Existing registrations are not retroactively rewritten.

**`operational`** — reserved for later phases. Master validates the schema but does not consume these fields in Phase 3.

**`appSettings`** — free-form object. Forwarded to every agent's slice verbatim. The agent forwards it to the consuming app via the local IPC channel between agent and app (which is itself a separate, out-of-band concern — Phase 3 just makes the data available to the agent process).

### 2.4 Validation Rules

Master validates on load and on reload. On reload, validation runs before any state change. Old config remains in effect if new config is invalid.

| Rule                                                         | Failure mode                     |
| ------------------------------------------------------------ | -------------------------------- |
| `schemaVersion == 1`                                         | 400, "Unsupported schemaVersion" |
| All `agentId` values unique                                  | 400, lists duplicates            |
| All `logicalNodeId` values unique across all agents          | 400, lists duplicates            |
| Every `agents[].segmentId` references a defined segment      | 400, lists missing               |
| Every `segments[].relayAgentId` references an existing `agentId` with `isRelay: true` | 400, lists mismatches            |
| Exactly one agent has `isMaster: true`, and it matches `topology.master.agentId` | 400                              |
| `bundles.ensured[].bundleId` follows filename safety rules (§2.6 of Phase 1 design) | 400                              |
| `categoryDefaults` keys are valid data categories            | 400, lists invalid               |
| Durations parse (`"24h"`, `"1h"`, `"30m"`)                   | 400                              |
| Sizes parse (`"64MB"`, `"1GB"`)                              | 400                              |

A non-blocking warning is written to the operator message queue for each of:

- Capabilities referenced by registered bundles (via `defaultScope.capabilityFilter`) that no agent declares
- Connected agents whose `agentId` is not present in `topology.agents[]`

### 2.5 `bundles.ensured[]` Semantics

On every successful reload:

1. For each `ensured` entry, check the bundle registry.
2. If the bundle does **not** exist: register it using the entry. Operator message queue gets an `info` message: "Bundle X auto-registered from site config."
3. If the bundle **does** exist with byte-identical shape: no action.
4. If the bundle exists with a *different* shape: log to operator message queue as a `warning`; **do not** modify the existing registration. The operator must resolve via `PUT /api/bundles/{bundleId}` or by editing the config to match.

Removing an entry from `ensured[]` does **not** remove the bundle from the registry. Deletion is explicit via `DELETE /api/bundles/{bundleId}`.

### 2.6 Bootstrap: First Run With Empty Config

If `site-config.json` is missing on master startup:

- Master logs: "site-config.json not found; running with empty topology"
- All agents that connect are accepted; each gets an empty slice (no segment, no capabilities, no logical nodes)
- Deployment by `agentId` or `Fleet` works
- Deployment by `LogicalNode`, `Group`, or `Capability` returns 404 (unable to resolve)
- Operator UI shows an "Unassigned" pseudo-segment containing the connected agents
- Operator message queue carries a `warning`: "No site config loaded; topology features disabled."

Operator creates the file and calls `POST /api/config/reload` to bring topology online.

### 2.7 Reload Flow

```
POST /api/config/reload
  ├─ read file from configured path
  ├─ parse JSON
  ├─ validate schema (§2.4)
  │     fail → 400, body lists errors; old config still in effect
  ├─ apply bundles.ensured (§2.5)
  ├─ compute new slices for all agents
  ├─ diff old slices vs new slices
  ├─ atomic swap: in-memory SiteConfig replaced
  ├─ for each agent with changed slice:
  │     if online: SignalR ConfigUpdate(agentSlice)
  │     if offline: change will be delivered on next Register
  ├─ trigger immediate state snapshot
  └─ 200 with diff summary
```

Atomicity: the in-memory `SiteConfig` reference is replaced under a single lock. Slice computations and ConfigUpdate sends happen after the swap. Concurrent reads during the swap see either the old or the new value — never a partial.

### 2.8 Per-Agent Slice

The slice sent to each agent. Computed from the canonical config:

```json
{
  "agentId":        "SIM-03",
  "segmentId":      "seg-A",
  "isRelay":        false,
  "isMaster":       false,
  "capabilities":   ["render", "physics"],
  "logicalNodeIds": [42, 43],
  "dataSource": {
    "type":         "Master",
    "url":          "http://master.local:8080"
  },
  "appDataRoot":    "C:\\AppData",
  "categoryDefaults": {
    "RuntimeAsset":    { "chunkSize": "64MB", "verifyMode": "ChunkHash", "staleAfter": "24h" },
    "Config":          { "verifyMode": "FullHash", "staleAfter": "1h" }
  },
  "ensuredBundles": [
    { "bundleId": "GlobalAITables", "dataCategory": "Dataset" }
  ],
  "appSettings": {
    "renderQuality":   "high",
    "physicsTimestep": "0.016"
  },
  "configVersion":  "2026-05-17T12:00:00Z"
}
```

`dataSource.type` is always `"Master"` in Phase 3. In Phase 5 it becomes `"Relay"` with the relay's URL when the agent should pull from its segment relay.

`configVersion` is the slice's source timestamp — used by agents to detect "I'm running on a stale slice" if the local cache and master's reply diverge after reconnect.

The slice deliberately omits `master.dataPlaneUrl` and `topology` — agents need only what's relevant to themselves. The master URL is in agent bootstrap config; the data-plane URL is derived from it (or replaced by the relay URL in Phase 5).

`appDataRoot` is currently fixed in agent bootstrap config (`agent.json`). It is *not* in site config; tell me if you'd want to move it.

### 2.9 Slice Caching On Agent

Agent persists its current slice locally:

- Path: `{DataRoot}/agent-config-cache.json`
- Atomic write (temp + rename)
- Written whenever a `ConfigUpdate` or `Register` response delivers a new slice
- Read on agent startup, before connecting to master
- The cache lets the agent run for restart if the master is briefly unavailable on cold start (e.g. ordering during reboot of the whole rack); see §10.2 for behavior when cache and master eventually disagree

------

## 3. Two-Tier Identity

### 3.1 Identity Model

- `agentId` — the machine/process identity. SignalR client identity. One per computer.
- `logicalNodeId` — the integer the consuming application uses. Many per agent.

Mapping lives in `topology.agents[].logicalNodeIds`. Master holds the inverse map `logicalNodeId → agentId` in memory; it is rebuilt on every config reload.

### 3.2 Resolution Rules

- A single `logicalNodeId` belongs to exactly one `agentId`. Enforced by validation.
- Multiple `logicalNodeId`s may map to the same `agentId`.
- An `agentId` with zero `logicalNodeIds` is valid (e.g. master, relay, non-simulating utility machines).

### 3.3 API Resolution

The API spec already documents this; Phase 3 implements it.

**`POST /api/deploy` with `target.type == "LogicalNode"`:**

```json
{
  "intentId":  "...",
  "bundleId":  "TerrainTextures",
  "version":   "v42",
  "target":    { "type": "LogicalNode", "logicalNodeIds": [42, 43, 44] },
  "priority":  "Normal"
}
```

Resolution:

1. Each `logicalNodeId` resolves to its `agentId` via the mapping.
2. The resulting agent set is deduplicated. `[42, 43]` both mapping to `SIM-03` becomes `{SIM-03}`.
3. One sub-intent per unique resolved `agentId`. Each sub-intent records `appliesToLogicalNodes: [42, 43]` for traceability.
4. If any `logicalNodeId` is unknown, the entire deploy fails with 404 — body lists which ids didn't resolve. No sub-intents created. Caller fixes the request and retries.

**`POST /api/membership` accepts either `agentId` or `logicalNodeId`:**

If `logicalNodeId` given, master resolves to `agentId`. If both given and inconsistent, 400. Membership is stored per `agentId`.

**Status responses include both identities:**

```json
{
  "agentId":         "SIM-03",
  "logicalNodeIds":  [42, 43],
  "presence":        "Online",
  ...
}
```

### 3.4 Safe-Window Resolution (Forward Reference)

`POST /api/safe-window` accepts a `logicalNodeId`. The master resolves to its `agentId`, then ANDs the safe-window flags across **all** logical nodes mapped to that agent before sending `SignalSafeWindow` to the agent. Phase 6 implements this fully; Phase 3 lays the resolution groundwork.

------

## 4. Group Membership

### 4.1 Model

- Each agent has at most one `currentGroupId` (nullable).
- Groups are identified by free-form string `groupId`.
- The master holds membership in memory and snapshots it.
- Membership changes trigger `DesiredState` recomputation for the affected agent.

### 4.2 `POST /api/membership`

Body:

```json
{
  "agentId":       "SIM-03",         // or logicalNodeId
  "logicalNodeId": 42,                // resolves to agentId
  "groupId":       "session-2026-05-17-A"
}
```

To clear membership: `"groupId": null`.

Response (`200 OK`):

```json
{
  "agentId":          "SIM-03",
  "previousGroupId":  null,
  "currentGroupId":   "session-2026-05-17-A",
  "triggeredIntents": ["aaaa-...", "bbbb-..."]
}
```

`triggeredIntents` lists intents created by `DesiredState` recomputation in response to this membership change.

Errors:

- `404` — unknown agent or logical node
- `400` — both `agentId` and `logicalNodeId` provided and inconsistent
- `409` — agent is already in another group; body lists current group
  - Caller must clear first (`{"groupId": null}`) before joining a new one

### 4.3 Auto-Deploy Logic

When an agent's `currentGroupId` becomes `G`:

```
for each bundle B in registry:
  if B.defaultScope.type == "Group" and B.defaultScope.groupId == G:
    latest = B.latestPublishedVersion
    if latest is null: continue                  // no published version yet
    if agent.activeVersionOf(B) != latest:
      create Intent(Deploy, agentId, B.bundleId, latest)
```

This is the **only** automatic-deploy trigger in Phase 3. New publishes do **not** auto-deploy in this phase — that lands in Phase 10 with the fleet sync scheduler.

When `currentGroupId` becomes `null`:

- No automatic cleanup of bundles. Anything currently Active stays Active.
- Phase 11 GC eventually evicts unreferenced versions if disk pressure warrants.

### 4.4 Conflict Policy

Two simultaneous membership requests for the same agent: the master serializes via a per-agent lock. The first wins; the second sees the post-first state and either succeeds (clearing then setting) or fails (already in another group).

The session manager calling this endpoint is expected to clear before re-joining; the system does not auto-clear.

------

## 5. SignalR Hub Additions

### 5.1 New Method: `ConfigUpdate` (Master → Agent)

```csharp
public sealed record ConfigUpdateMessage(
    AgentSlice Slice,
    string ConfigVersion);
```

Sent on:

- A reload that produced a changed slice for this agent
- The first `Register` after master startup (if slice has changed since cache)

Agent behaviour:

- Apply slice to in-memory state
- Atomically write to `agent-config-cache.json`
- Forward `appSettings` to the consuming app via the local IPC channel (out of scope here)
- If `categoryDefaults` changed mid-operation: in-flight intents continue with the values they were dispatched with; new intents use updated values

The agent does **not** acknowledge `ConfigUpdate` with a separate hub method. The next `ReportStatus` from the agent carries an implicit "I've applied configVersion X" via an additional field:

```csharp
public sealed record StatusReport(
    string BundleId,
    BundleState State,
    string? Version,
    int? ProgressPct,
    string? ErrorDetail,
    string? AppliedConfigVersion);     // new in Phase 3
```

Master uses `AppliedConfigVersion` to track which agents have caught up to which config version. Reported on the UI.

### 5.2 Updated Method: `Register` Response

`RegisterResponse` gains a slice field:

```csharp
public sealed record RegisterResponse(
    string AssignedAgentId,
    DateTimeOffset ServerTime,
    AgentSlice Slice,                              // new
    IReadOnlyList<Command> ReplayedCommands);
```

The slice in the response is authoritative — overrides whatever the agent had cached. The agent immediately writes it to `agent-config-cache.json`.

------

## 6. REST Endpoints Added in Phase 3

| Method | Path                 | Purpose                                      |
| ------ | -------------------- | -------------------------------------------- |
| `POST` | `/api/config/reload` | Reload site config                           |
| `GET`  | `/api/config`        | Return the canonical config (for inspection) |
| `POST` | `/api/membership`    | Set or clear group membership                |
| `GET`  | `/api/groups`        | List currently populated groups and members  |

Existing endpoints extended:

- `POST /api/deploy` — `target.type` now supports `LogicalNode`, `Group`, `Capability`
- `GET /api/status` — agent objects include `logicalNodeIds`, `capabilities`, `segmentId`, `currentGroupId`, `appliedConfigVersion`
- `GET /api/status/{agentId}` — same additions

`GET /api/config` returns the canonical config as currently loaded — useful for verifying what the master is actually using vs what's on disk:

```json
{
  "configVersion":  "2026-05-17T12:00:00Z",
  "loadedFrom":     "C:\\ProgramData\\SyncMaster\\site-config.json",
  "config": { /* full schema */ }
}
```

`GET /api/groups` shows live membership:

```json
{
  "groups": [
    {
      "groupId": "session-2026-05-17-A",
      "memberAgents": [
        { "agentId": "SIM-03", "logicalNodeIds": [42, 43] },
        { "agentId": "SIM-04", "logicalNodeIds": [44] }
      ]
    }
  ]
}
```

------

## 7. Persistence Updates

### 7.1 Master Snapshot Additions

`master-state.json` gains:

```json
{
  "schemaVersion": 2,                          // bumped from Phase 2's 1
  "snapshotAt":    "...",
  "bundles":          [ ... ],
  "publishedVersions":[ ... ],
  "intents":          [ ... ],
  "messages":         [ ... ],
  "membership": [
    { "agentId": "SIM-03", "currentGroupId": "session-A", "since": "2026-05-17T10:00:00Z" }
  ]
}
```

`SiteConfig` is **not** snapshotted — it lives in its own file (`site-config.json`) and is reloaded on startup by reading that file. If the file is missing on startup, master starts with empty topology (§2.6).

Migration from Phase 2's `schemaVersion: 1`: master detects, treats `membership` as empty, writes back as v2 at next snapshot.

### 7.2 Agent Local Files

In addition to Phase 2's `agent.db`, `manifests/`, `staging/`, `versions/`, `active/`:

- `agent-config-cache.json` — last received slice, written atomically.

`agent.json` (bootstrap, set at install time) shrinks to the minimum:

```json
{
  "AgentId":    "SIM-03",                       // optional; defaults to hostname
  "MasterUrl":  "http://master.local:8080",
  "DataRoot":   "C:\\ProgramData\\SyncAgent",
  "AppDataRoot": "C:\\AppData"
}
```

Capabilities, segment assignment, logical nodes, category defaults, and app settings come from the slice. The agent's local config has only what it needs to connect.

------

## 8. Targeting Resolution

Full resolution logic for every `target.type` value:

| `target.type` | Resolution to agent set                                      |
| ------------- | ------------------------------------------------------------ |
| `Fleet`       | Every `agentId` in `topology.agents[]` minus `isMaster: true` and `isRelay: true` agents (these don't run app workloads in Phase 3) |
| `Agent`       | `target.agentIds` directly. Each must exist in `topology.agents[]` or be a currently-connected agent (the "Unassigned" pseudo-segment case from §2.6). 404 if any missing. |
| `LogicalNode` | Map each `target.logicalNodeIds[]` to its `agentId` via the master's mapping. Deduplicate. 404 if any missing. |
| `Group`       | All agents whose `currentGroupId == target.groupId`. Empty set is valid (no error). |
| `Capability`  | All agents where `target.capabilityFilter ∈ agent.capabilities`. Empty set is valid. |

After resolution, the deploy creates one sub-intent per unique agent. Each sub-intent's body records `appliesToLogicalNodes` (or `appliesToGroup`, etc.) for traceability.

`Fleet` excludes `isRelay` and `isMaster` agents because they don't run the consuming app and shouldn't receive runtime asset / config deployments. They can still be targeted explicitly by `Agent` if needed.

------

## 9. Operator UI Updates

Phase 2's flat agent table becomes a segment-grouped view. Same polling-every-5s approach.

### 9.1 Page Structure

```
┌────────────────────────────────────────────────────────────────────────┐
│ SyncMaster · Fleet                                  [Updated 12:00:05] │
│                                                                        │
│ Config: 2026-05-17T12:00:00Z  loaded from C:\ProgramData\...           │
│                                                                        │
│ ▼ Segment seg-A — Primary lab — relay REL-A1                          │
│   Agent          Presence  Logical Nodes  Capabilities    Group       │
│   MASTER-01      Online    —              —               —           │
│   REL-A1         Online    —              relay           —           │
│   SIM-03         Online    42, 43         render,physics  session-A   │
│      └── Bundles: TerrainTextures:Active@v42, AITables:Transferring  │
│                                                                        │
│ ▼ Segment seg-B — East annex — relay REL-B1                          │
│   ...                                                                  │
│                                                                        │
│ ▼ Unassigned (3 agents)                                                │
│   ...                                                                  │
│                                                                        │
│ Groups                                                                 │
│   session-2026-05-17-A:  SIM-03, SIM-04                                │
│                                                                        │
│ Recent Intents                                                         │
│   ...                                                                  │
│                                                                        │
│ Messages                                                               │
│   warning: bundle GlobalAITables shape mismatch with config            │
│   info:    bundle GlobalAITables auto-registered from site config      │
└────────────────────────────────────────────────────────────────────────┘
```

Still HTML + vanilla JS polling `GET /api/status` plus `GET /api/groups` and `GET /api/config`. Three parallel fetches every 5 seconds; results rendered.

### 9.2 Read-Only

Still no controls in the UI. Cancel-intent, deploy, and membership remain operator-issued through curl or a script. Adding interactive controls is deferred to a later phase (or to a separate operator-tool project).

------

## 10. Code Sketches

### 10.1 Site Config Loader

```csharp
public interface ISiteConfigStore
{
    SiteConfig Current { get; }
    AgentSlice ComputeSlice(string agentId);
    Task<ReloadResult> ReloadAsync(CancellationToken ct);
    event EventHandler<ConfigChangedEventArgs>? Changed;
}

public sealed class SiteConfigStore : ISiteConfigStore
{
    private readonly string _path;
    private readonly ILogger<SiteConfigStore> _log;
    private readonly IBundleRegistry _registry;
    private readonly IOperatorMessageQueue _messages;
    private SiteConfig _current = SiteConfig.Empty;
    private readonly object _swapLock = new();

    public SiteConfig Current => Volatile.Read(ref _current);

    public async Task<ReloadResult> ReloadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path))
            return new ReloadResult(success: true, configVersion: _current.ConfigVersion, isEmpty: true);

        var bytes = await File.ReadAllBytesAsync(_path, ct);
        SiteConfig newConfig;
        try
        {
            newConfig = SiteConfigSerializer.Deserialize(bytes);
        }
        catch (JsonException ex)
        {
            return ReloadResult.Failure($"JSON parse error: {ex.Message}");
        }

        var validation = SiteConfigValidator.Validate(newConfig);
        if (!validation.IsValid)
            return ReloadResult.Failure(validation.Errors);

        // Apply ensured bundles BEFORE swap — failures should not change config state
        foreach (var entry in newConfig.Bundles.Ensured)
        {
            await ApplyEnsuredAsync(entry, ct);
        }

        SiteConfig old;
        lock (_swapLock)
        {
            old = _current;
            _current = newConfig with { ConfigVersion = DateTimeOffset.UtcNow };
        }

        var diff = SiteConfigDiff.Compute(old, _current);
        Changed?.Invoke(this, new ConfigChangedEventArgs(diff));

        _log.LogInformation("Site config reloaded. {Diff}", diff.Summary);
        return new ReloadResult(success: true, configVersion: _current.ConfigVersion, diff: diff);
    }

    public AgentSlice ComputeSlice(string agentId)
    {
        var cfg = Current;
        var agent = cfg.Topology.Agents.FirstOrDefault(a => a.AgentId == agentId);
        if (agent is null)
            return AgentSlice.Empty(agentId, cfg.ConfigVersion);

        return new AgentSlice
        {
            AgentId        = agent.AgentId,
            SegmentId      = agent.SegmentId,
            IsRelay        = agent.IsRelay,
            IsMaster       = agent.IsMaster,
            Capabilities   = agent.Capabilities,
            LogicalNodeIds = agent.LogicalNodeIds,
            DataSource     = new DataSource("Master", cfg.Topology.Master.DataPlaneUrl),
            CategoryDefaults = cfg.CategoryDefaults,
            EnsuredBundles = cfg.Bundles.Ensured
                .Select(e => new EnsuredBundleHint(e.BundleId, e.DataCategory))
                .ToList(),
            AppSettings    = cfg.AppSettings,
            ConfigVersion  = cfg.ConfigVersion
        };
    }

    private async Task ApplyEnsuredAsync(BundleDefinition entry, CancellationToken ct)
    {
        var existing = await _registry.GetAsync(entry.BundleId, ct);
        if (existing is null)
        {
            await _registry.RegisterAsync(entry, ct);
            _messages.Enqueue(OperatorMessage.Info(
                "Bundle auto-registered from site config",
                $"Bundle {entry.BundleId} auto-registered from ensured list."));
        }
        else if (!BundleDefinitionComparer.ShapeEqual(existing, entry))
        {
            _messages.Enqueue(OperatorMessage.Warning(
                "ensured bundle shape mismatch",
                $"Bundle {entry.BundleId} is in ensured list with a different shape than the registry. Resolve via PUT /api/bundles/{entry.BundleId} or align the config."));
        }
    }
}
```

### 10.2 Identity Resolver

```csharp
public interface IIdentityResolver
{
    string? ResolveAgentId(int logicalNodeId);
    IReadOnlyList<int> GetLogicalNodes(string agentId);
    AgentResolutionResult ResolveTarget(DeployTarget target);
}

public sealed class IdentityResolver : IIdentityResolver
{
    private readonly ISiteConfigStore _config;
    private readonly IFleetState _fleet;

    public string? ResolveAgentId(int logicalNodeId)
    {
        var cfg = _config.Current;
        foreach (var a in cfg.Topology.Agents)
            if (a.LogicalNodeIds.Contains(logicalNodeId))
                return a.AgentId;
        return null;
    }

    public AgentResolutionResult ResolveTarget(DeployTarget target)
    {
        var cfg = _config.Current;
        return target.Type switch
        {
            DeployTargetType.Fleet =>
                Success(cfg.Topology.Agents
                    .Where(a => !a.IsMaster && !a.IsRelay)
                    .Select(a => a.AgentId)),

            DeployTargetType.Agent =>
                ResolveAgents(target.AgentIds, cfg),

            DeployTargetType.LogicalNode =>
                ResolveLogicalNodes(target.LogicalNodeIds, cfg),

            DeployTargetType.Group =>
                Success(_fleet.GetAgentsInGroup(target.GroupId)),

            DeployTargetType.Capability =>
                Success(cfg.Topology.Agents
                    .Where(a => a.Capabilities.Contains(target.CapabilityFilter))
                    .Select(a => a.AgentId)),

            _ => AgentResolutionResult.BadRequest($"Unknown target type {target.Type}")
        };
    }

    private AgentResolutionResult ResolveLogicalNodes(IReadOnlyList<int> ids, SiteConfig cfg)
    {
        var resolved = new HashSet<string>();
        var missing = new List<int>();
        foreach (var id in ids)
        {
            var agentId = ResolveAgentId(id);
            if (agentId is null) missing.Add(id);
            else resolved.Add(agentId);
        }
        return missing.Count > 0
            ? AgentResolutionResult.NotFound($"Unknown logicalNodeIds: {string.Join(',', missing)}")
            : AgentResolutionResult.Success(resolved);
    }
}
```

### 10.3 Membership Service

```csharp
public sealed class MembershipService
{
    private readonly IFleetState _fleet;
    private readonly IIdentityResolver _resolver;
    private readonly IBundleRegistry _registry;
    private readonly IIntentRepository _intents;
    private readonly IIntentDispatcher _dispatcher;
    private readonly ISnapshotWriter _snapshot;

    public async Task<MembershipResult> SetMembershipAsync(MembershipRequest req, CancellationToken ct)
    {
        var agentId = req.AgentId ?? _resolver.ResolveAgentId(req.LogicalNodeId!.Value);
        if (agentId is null) throw new NotFoundException("Unknown agent or logical node");

        using var _ = await _fleet.LockAgentAsync(agentId, ct);

        var current = _fleet.GetCurrentGroup(agentId);

        if (req.GroupId is not null && current is not null && current != req.GroupId)
            throw new ConflictException($"Agent {agentId} currently in group {current}");

        _fleet.SetCurrentGroup(agentId, req.GroupId);

        var triggered = new List<string>();
        if (req.GroupId is not null)
        {
            var bundlesForGroup = await _registry.GetBundlesForGroupAsync(req.GroupId, ct);
            var agentState = _fleet.GetAgent(agentId);

            foreach (var bundle in bundlesForGroup)
            {
                var latest = await _registry.GetLatestVersionAsync(bundle.BundleId, ct);
                if (latest is null) continue;

                var activeOnAgent = agentState.GetActiveVersion(bundle.BundleId);
                if (activeOnAgent == latest.Version) continue;

                var intent = await _intents.CreateAsync(IntentRequest.Deploy(
                    agentId, bundle.BundleId, latest.Version,
                    appliesToGroup: req.GroupId), ct);
                triggered.Add(intent.IntentId);
                await _dispatcher.DispatchAsync(intent, ct);
            }
        }

        _snapshot.MarkDirty();

        return new MembershipResult(agentId, current, req.GroupId, triggered);
    }
}
```

### 10.4 Hub Method Wiring

```csharp
public sealed class SyncHub : Hub
{
    // ... fields from Phase 2 ...
    private readonly ISiteConfigStore _config;

    public async Task<RegisterResponse> Register(RegisterRequest req)
    {
        // ... Phase 2 registration logic ...

        var slice = _config.ComputeSlice(req.AgentId);
        var pending = await _intents.GetPendingForAgentAsync(req.AgentId, Context.ConnectionAborted);
        var replay  = pending.Select(i => i.ToCommand()).ToList();

        return new RegisterResponse(req.AgentId, DateTimeOffset.UtcNow, slice, replay);
    }
}

// Wiring: subscribe to ISiteConfigStore.Changed to fan out ConfigUpdate
public sealed class ConfigPushService : BackgroundService
{
    private readonly ISiteConfigStore _config;
    private readonly IHubContext<SyncHub> _hub;
    private readonly IFleetState _fleet;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _config.Changed += async (sender, args) =>
        {
            foreach (var agentId in args.Diff.AgentsWithChangedSlices)
            {
                var connId = _fleet.GetConnectionId(agentId);
                if (connId is null) continue;
                var slice = _config.ComputeSlice(agentId);
                await _hub.Clients.Client(connId)
                    .SendAsync("ConfigUpdate",
                        new ConfigUpdateMessage(slice, _config.Current.ConfigVersion.ToString("O")),
                        CancellationToken.None);
            }
        };
        return Task.CompletedTask;
    }
}
```

### 10.5 Agent Slice Application

```csharp
public sealed class AgentSliceManager
{
    private readonly string _cachePath;
    private readonly ILogger<AgentSliceManager> _log;
    private AgentSlice _current = AgentSlice.Empty();

    public AgentSlice Current => Volatile.Read(ref _current);

    public AgentSlice LoadFromCache()
    {
        if (!File.Exists(_cachePath)) return _current;
        try
        {
            _current = JsonSerializer.Deserialize<AgentSlice>(File.ReadAllBytes(_cachePath))!;
            _log.LogInformation("Loaded slice from cache, configVersion={V}", _current.ConfigVersion);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to load slice cache; starting with empty");
        }
        return _current;
    }

    public async Task ApplyAsync(AgentSlice slice, CancellationToken ct)
    {
        Volatile.Write(ref _current, slice);
        var tmp = _cachePath + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(slice);
        await File.WriteAllBytesAsync(tmp, bytes, ct);
        File.Move(tmp, _cachePath, overwrite: true);
        _log.LogInformation("Applied new slice configVersion={V}", slice.ConfigVersion);

        // Forward appSettings to the consuming app (local IPC, out of scope here)
    }
}
```

### 10.6 Hub Methods on Agent Side

```csharp
public sealed class HubProxy
{
    // ... Phase 2 setup ...

    public HubProxy(...)
    {
        // ... Phase 2 ...

        _conn.On<ConfigUpdateMessage>("ConfigUpdate", async msg =>
        {
            await _sliceManager.ApplyAsync(msg.Slice, CancellationToken.None);
        });
    }

    public async Task ReportStatusAsync(string bundleId, BundleState state, string? version, int? pct = null)
    {
        await _conn.InvokeAsync("ReportStatus", new StatusReport(
            BundleId: bundleId, State: state, Version: version, ProgressPct: pct,
            ErrorDetail: null, AppliedConfigVersion: _sliceManager.Current.ConfigVersion));
    }
}
```

------

## 11. Acceptance Tests

### 11.1 Site Config

| Test                                                     | Pass condition                                               |
| -------------------------------------------------------- | ------------------------------------------------------------ |
| Reload with valid config that adds a new agent           | New agent visible in `GET /api/config`; if connected, receives slice via ConfigUpdate |
| Reload with invalid JSON                                 | 400; old config still in effect; verify `GET /api/config` unchanged |
| Reload with duplicate `agentId`                          | 400; specific error in body                                  |
| Reload with `logicalNodeId` mapped to two agents         | 400                                                          |
| Reload that adds an ensured bundle missing from registry | Bundle auto-registered; info message in queue                |
| Reload that adds an ensured bundle with shape conflict   | Warning in queue; registry unchanged                         |
| Reload that removes an agent from config                 | Connected agent still visible in fleet; warning in queue     |
| Master startup with missing site config                  | Empty topology; agents connect to "Unassigned"; warning in queue |
| Master startup with malformed site config                | Master starts with empty topology; error message in queue with details |

### 11.2 Identity Resolution

| Test                                                         | Pass condition                                               |
| ------------------------------------------------------------ | ------------------------------------------------------------ |
| `POST /api/deploy` with `LogicalNode [42, 43]` both on SIM-03 | One sub-intent for SIM-03 with `appliesToLogicalNodes: [42, 43]` |
| `POST /api/deploy` with unknown `logicalNodeId`              | 404; lists the unknown ids; no intents created               |
| `POST /api/deploy` with `Capability "render"`                | Sub-intents for every agent with `render` capability         |
| `POST /api/deploy` with `Capability` matching no agent       | 200 with empty `resolvedAgents`; no intents created          |
| `POST /api/deploy` with `Fleet`                              | Excludes master and relay agents                             |

### 11.3 Membership

| Test                                                     | Pass condition                                               |
| -------------------------------------------------------- | ------------------------------------------------------------ |
| Set membership for an agent currently in no group        | 200; `currentGroupId` set; auto-deploy intents created for Group-scoped bundles |
| Set membership for an agent already in another group     | 409                                                          |
| Clear membership and re-set                              | Works; intents created on re-set                             |
| Set membership via `logicalNodeId`                       | Resolves to agent; behaves same as agent-targeted membership |
| Set membership where the group has no associated bundles | 200 with empty `triggeredIntents`                            |
| `GET /api/groups` after several memberships              | Shows all current groupings                                  |

### 11.4 SignalR `ConfigUpdate`

| Test                                                         | Pass condition                                               |
| ------------------------------------------------------------ | ------------------------------------------------------------ |
| Online agent, reload changes its slice                       | Agent receives ConfigUpdate; `agent-config-cache.json` is rewritten; next ReportStatus carries new `AppliedConfigVersion` |
| Offline agent, reload changes its slice                      | No SignalR message sent; on next connect, Register response carries new slice |
| Reload that does not change agent's slice                    | No ConfigUpdate sent to that agent                           |
| Agent restart with valid cached slice and master unreachable | Agent runs on cached slice; reconnects when master available |

### 11.5 Operator UI

| Test                                                         | Pass condition                                    |
| ------------------------------------------------------------ | ------------------------------------------------- |
| Open UI with 3 segments populated                            | Each segment renders as a section with its agents |
| Agent in an unassigned segment (not in config but connected) | Shows in "Unassigned" pseudo-segment              |
| Agent in a group                                             | Group cell shows groupId                          |
| Bundle in Transferring state                                 | Cell shows state and percent, updates on poll     |
| `appliedConfigVersion` divergence between two agents         | Both shown with their respective versions         |

------

## 12. Implementation Sequence

1. **DTOs for site config**: `SiteConfig`, `AgentSlice`, `ConfigUpdateMessage`, all immutable records. Round-trip JSON tests.
2. **`SiteConfigValidator`**. Cover all rules in §2.4. Tests with malformed inputs.
3. **`SiteConfigStore` — load, validate, hold in memory**. No reload yet. Tests using stub registry.
4. **`POST /api/config/reload`** wired to store. Tests against stub registry and message queue.
5. **`bundles.ensured` application**. Tests for new bundle, existing matching, existing mismatch.
6. **`AgentSlice` computation**. Tests verifying slices contain correct subsets.
7. **`SiteConfigStore.Changed` event + `ConfigPushService`**. Tests that online agents get ConfigUpdate; offline agents don't.
8. **Agent: `AgentSliceManager` with cache**. Tests for cache write/read, atomicity, missing-cache fallback.
9. **Hub `ConfigUpdate` handler on agent**. Tests against a stub master.
10. **`Register` response carrying slice**. Tests for first-connect, reconnect with stale cache, etc.
11. **`IdentityResolver`**. Tests for all five target types.
12. **`POST /api/deploy` extended targets**. Integration tests for each target type.
13. **`MembershipService`**. Tests for set, clear, conflict, auto-deploy.
14. **`POST /api/membership`** wired to service. HTTP-level tests.
15. **`GET /api/groups`** and `GET /api/config`. Smoke tests.
16. **Operator UI updates**. Segment grouping, group panel, applied config version display.
17. **End-to-end Phase 3 acceptance tests** (§11).

After step 17, Phase 3 is complete. Phase 4 introduces the real `DirectHttp` transfer engine and the master's pull cache on top of this topology.

------

## 13. Open Questions for Implementation

A few decisions that should be made before Phase 3 coding starts:

- **`appDataRoot` location**: currently in agent bootstrap (`agent.json`). Tell me if you'd want to move it into site config so the operator can override per-agent. My default is to keep it in bootstrap — it's machine-specific and rarely changes.
- **Slice push timing under heavy reload**: when a reload changes 100 agents' slices, the master sends 100 ConfigUpdate messages. SignalR handles fan-out, but tell me if you want any rate limiting or batching. My default: send all at once; SignalR's per-connection queue handles backpressure.
- **`isRelay` and `isMaster` exclusion from `Fleet` target**: I've defined `Fleet` to exclude them. If you have a use case for fleet-wide deploys that *include* utility machines (operator may want `GlobalAITables` everywhere), this is wrong. Alternative: include them by default; let `target.exclude` filter. Confirm.
- **`Group` target with no matching agents**: I've defined as success-with-empty-result, not 404. The session manager creating a group before any agent has joined should not error. Confirm.
- **`POST /api/membership` with `logicalNodeId`**: membership is set per agent. If `logicalNodeId 42` and `logicalNodeId 43` both map to `SIM-03`, calling membership for one sets the agent's group; calling for the other while still in the same group is a no-op. Setting for `logicalNodeId 43` to a *different* group raises 409. Confirm semantics.
- **Removal of an agent from site config when it's currently in a group**: my current behaviour is "leave the agent's membership intact; emit a warning". Alternative: clear membership on removal. The first is safer (no surprise side effects from config edits); the second is cleaner. Confirm.

These are minor; flag preferences and the build can proceed.



# Phase 4 Detailed Design

## DirectHttp Transfer, Master Pull Cache, Zip Extraction

*Companion to Architecture Design Document Revision 2, API Specification, Phases 1–3 Designs* *Targets `Phase 4` from §21 of the architecture doc* *C# / .NET 8 · ASP.NET Core · May 2026*

------

## 1. Phase Scope

### 1.1 What Phase 4 Adds On Top of Phase 3

Phase 2's stub transfer (single HTTP GET, no resume) is replaced with the real `DirectHttp` engine. A pull cache appears on the master, mediating between the NAS and HTTP-serving downstream. Zip extraction and per-file verification move out of the activator into the staging pipeline, where they belong.

Deliverables:

1. **Master pull cache** — on-disk store of bundle bytes the master has fetched from NAS. Sits between NAS (SMB or local FS) and the data plane HTTP endpoint. Sentinel-based atomicity (no `bundles/{id}/{version}/` exists in cache unless complete).
2. **`DirectHttp` transfer engine on the agent** — proper HTTP semantics with byte-range resume, configurable timeouts, retry policy, and progress reporting.
3. **Stream-extract-verify pipeline** — agent extracts zip while computing per-file hashes, refusing the staged version on the first mismatch. Single pass over the bytes.
4. **Master data plane upgraded** — concurrent-request coalescing (multiple simultaneous requests for the same uncached version queue behind one fill); byte-range serving from cache.
5. **End-to-end on a single segment** — the master's segment, with no relay involvement. Agents in other segments may still connect and receive intents, but they pull from the master over whatever network path is available (no segment-relay optimisation yet).

### 1.2 Explicitly Deferred

| Concern                                                  | Deferred to |
| -------------------------------------------------------- | ----------- |
| Per-segment relay agents serving downstream traffic      | Phase 5     |
| Cooperative safe-window activation                       | Phase 6     |
| Chunked huge-file transfer with multi-chunk parallelism  | Phase 7     |
| Recordings                                               | Phase 8     |
| Two-phase commit and automated rollback                  | Phase 9     |
| Auto-deploy on publish, fleet sync window                | Phase 10    |
| Garbage collection of cache, staging, installed versions | Phase 11    |

In Phase 4 the agent's slice (from Phase 3) declares `dataSource.type: "Master"` for every agent regardless of segment. Phase 5 introduces `"Relay"` for non-master-segment agents.

For huge ChunkedHugeFile bundles (single files up to ~0.5 TB) Phase 4 will *function* — the cache fills the file once, the agent downloads it once — but resume is per-byte-range only (no chunk-level parallelism or chunk-level retry granularity). Phase 7 brings the proper chunked engine.

### 1.3 Definition of Done

After Phase 4 an operator can:

- Publish a zipped bundle via the Phase 1 CLI, deploy it to 10 agents in the master segment, and watch all 10 transfer via real HTTP with progress visible in the operator UI
- Kill an agent's network mid-transfer, restore connectivity, and see the agent resume from where it left off (no full re-download)
- Kill the master mid-cache-fill, restart it, and see the next agent request trigger a fresh fill (no half-cached file served)
- Watch the master cache fill once and serve 10 concurrent agents from the cached bytes (NAS sees 1 SMB session, not 10)
- See per-file hash failures detected and reported clearly (corruption stops at the agent, never reaches `Active`)

------

## 2. Master Pull Cache

### 2.1 Purpose

The cache sits between the NAS (slow, SMB-limited) and the data-plane HTTP endpoint (fast, fan-out). It gives Phase 4 three properties:

1. **Single NAS read per bundle version**, regardless of how many agents request it.
2. **Atomicity**: agents never see a partial cache entry. Either the version is fully cached or it's not visible in the cache at all.
3. **Concurrent fan-out**: many simultaneous agents pull from the cached copy in parallel, served by Kestrel's static-file pipeline.

### 2.2 Directory Layout

```
{CacheRoot}/                                       configurable, default C:\ProgramData\SyncMaster\cache
  bundles/
    {bundleId}/
      {version}/                                   present only when fully cached
        manifest.json
        bundle.zip                                 (zip-container bundles)
        {filename}                                 (raw bundles, name from manifest.file.name)
  _fill/
    {bundleId}/
      {version}/                                   in-progress fills (atomic-renamed on completion)
        ...
  _trash/
    {timestamp}-{bundleId}-{version}/              evicted entries kept briefly
```

The presence of `bundles/{bundleId}/{version}/` implies "fully cached and ready to serve." No sentinel file is needed inside the directory because the directory's *existence* is itself the sentinel — the atomic `Directory.Move` from `_fill/{...}/` to `bundles/{...}/` is the commit step.

### 2.3 Fill State Machine

```
NotCached  ── fill request ──>  Filling  ── success ──>  Cached
                                  │
                                  └─ failure ──>  NotCached  (with retry counter)
```

The cache coordinator holds the state for each `(bundleId, version)` pair in memory:

```csharp
public enum CacheFillState { NotCached, Filling, Cached, Failed }

public sealed record CacheEntry(
    string BundleId,
    string Version,
    CacheFillState State,
    long? BytesTotal,
    long BytesPulled,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    Task<CacheResult> FillTask,        // shared by all waiters during Filling
    int FailedAttempts);
```

On master startup:

- Scan `bundles/` directory — every `{bundleId}/{version}/` directory present is `Cached`
- Anything in `_fill/` is leftover from a crash — clean up on startup
- Anything in `_trash/` older than 24h is cleaned up on startup; younger entries left alone

### 2.4 Fill Operation

```
fill(bundleId, version):
  1. acquire entry lock for (bundleId, version)
  2. if state == Cached: release lock, return success
  3. if state == Filling: release lock, await existing FillTask
  4. set state = Filling, set FillTask = new TCS, release lock
  5. perform fill:
     a. mkdir _fill/{bundleId}/{version}/
     b. resolve NAS source path (uncPath or localPath)
     c. copy manifest.json from NAS to _fill/.../manifest.json
     d. read manifest, determine archive or raw file
     e. copy bundle.zip (or raw file) from NAS to _fill/.../
        - block copy with progress reporting
        - SMB connection reused if open; otherwise opened on demand
        - bytes pulled tracked for ReportStatus-equivalent metrics
     f. verify on-disk archive.sha256 matches manifest.archive.sha256
        (or for raw: skip whole-file hash; chunk-level verification is the agent's job)
     g. atomic rename _fill/{bundleId}/{version}/ → bundles/{bundleId}/{version}/
     h. set state = Cached, complete FillTask
  6. on any failure:
     - cleanup _fill/{bundleId}/{version}/
     - set state = Failed, increment FailedAttempts, FillTask faults
     - if FailedAttempts < MaxFillRetries: schedule retry
     - else: enqueue OperatorMessage and leave state = Failed
```

Step (e)'s block copy uses `FileStream` async APIs with a generous buffer (e.g. 4 MB). On Windows, when source and destination are on the same volume, `File.Copy` is fastest; for cross-volume / SMB paths, manual buffered stream copy is used.

### 2.5 Concurrent Fill Coordination

When the second agent requests an uncached version while a fill is in flight, the coordinator returns the same `FillTask`. Both await the same operation. When the task completes, both proceed to serve. No double-fill ever happens for a given `(bundleId, version)`.

A global semaphore limits parallel fills:

```csharp
private readonly SemaphoreSlim _fillSlots;   // capacity = config.Cache.MaxConcurrentFills, default 4
```

This caps NAS pressure during a burst of new-version requests. The semaphore is acquired before step (5) and released after step (5h) or on failure.

### 2.6 Failure Retry Policy

| Attempt     | Delay before retry                   |
| ----------- | ------------------------------------ |
| 1 (initial) | —                                    |
| 2           | 5 seconds                            |
| 3           | 30 seconds                           |
| 4+          | 5 minutes, then state stays `Failed` |

When state is `Failed`, agent requests for that version return HTTP 503 with `Retry-After: 300`. The agent's state machine treats this as `TransferFailed → Queued` with its own backoff.

On master restart, `FailedAttempts` resets to 0.

### 2.7 Atomicity Guarantees

- The cache's `bundles/{bundleId}/{version}/` directory either contains a complete, validated copy or doesn't exist at all.
- The atomic `Directory.Move` from `_fill/` to `bundles/` is the commit point. Before it, no observer treats the version as cached.
- If the master crashes during step (5a)–(5g), `_fill/` is left in an inconsistent state. Master startup cleans up `_fill/` entirely. Any fill that was in flight is forgotten; the next agent request restarts it.
- `_fill/` and `bundles/` must be on the same volume for the rename to be atomic. The cache root validates this at startup.

### 2.8 Cache Skip for Master-Co-Located NAS (Optional Optimization)

When the master is co-located with the NAS and `nasRoot` resolves to a local path, fill is just a local file copy — typically fast (single-digit GB/s). The cache is still useful because:

- It decouples the cache layout from the NAS layout (publish-gate semantics live on NAS; cache layout serves HTTP)
- It allows the master to serve files even if NAS becomes briefly unavailable
- Phase 5 relays will pull from the cache regardless

Phase 4 does **not** implement a "skip cache when local" optimisation. Every served file goes through the cache. Tell me if you'd want this skip behind a config flag.

------

## 3. Master Data Plane

### 3.1 Endpoint Behaviour

```
GET /content/bundles/{bundleId}/{version}/{*path}

Resolution steps:
  1. Look up CacheEntry for (bundleId, version).
     - State Cached:     proceed to step 2.
     - State Filling:    await FillTask completion; on success proceed to step 2; on failure return 503.
     - State NotCached:  trigger fill; await; on success proceed; on failure return 503.
     - State Failed:     return 503 with Retry-After.
  2. Resolve path within {CacheRoot}/bundles/{bundleId}/{version}/.
  3. If file not found, 404.
  4. Stream via Results.File(path, enableRangeProcessing: true).
```

A request for `/content/bundles/{bundleId}/{version}/manifest.json` triggers fill if needed — same coordinator handles it. The manifest is part of the cache entry; serving the manifest implies the cache entry exists.

### 3.2 Byte-Range Support

ASP.NET Core's `Results.File(path, contentType, enableRangeProcessing: true)` handles RFC 7233 byte-range requests natively:

```http
GET /content/bundles/TerrainTextures/v42/bundle.zip HTTP/1.1
Range: bytes=536870912-

HTTP/1.1 206 Partial Content
Content-Type: application/octet-stream
Content-Range: bytes 536870912-1073741823/1073741824
Content-Length: 536870912
ETag: "sha256-7a2f..."
Accept-Ranges: bytes
```

`ETag` is derived from the manifest's `archive.sha256` (or `file.sha256` for raw bundles); stable across master restarts because it's content-addressed.

### 3.3 Concurrent Request Fan-Out

Once a version is `Cached`, requests are independent: Kestrel's static-file pipeline streams the same file to N parallel clients. The OS file cache helps; for very hot files, kernel-level read-ahead handles concurrency efficiently.

Phase 4 imposes no per-version concurrency limit on serving. Phase 5 may revisit when relays appear.

### 3.4 Cache Status Endpoint (Operator Visibility)

```
GET /api/cache/status

Response (200):
{
  "cacheRoot":  "C:\\ProgramData\\SyncMaster\\cache",
  "diskFreeGB": 423,
  "diskTotalGB": 1024,
  "entries": [
    { "bundleId": "TerrainTextures", "version": "v42", "state": "Cached",
      "bytesTotal": 1073741824, "completedAt": "2026-05-17T10:00:00Z" },
    { "bundleId": "AITables",       "version": "v18", "state": "Filling",
      "bytesTotal": 524288, "bytesPulled": 262144, "startedAt": "2026-05-17T12:01:00Z" }
  ]
}
```

This is read-only and informational. Eviction control is a Phase 11 concern.

------

## 4. DirectHttp Transfer Engine

### 4.1 Interface (Unchanged from Phase 2)

The `ITransferEngine` interface is the same. The Phase 4 implementation replaces the Phase 2 stub:

```csharp
public sealed class DirectHttpEngine : ITransferEngine
{
    public string MethodId => "DirectHttp";

    private readonly HttpClient _http;
    private readonly DirectHttpOptions _options;
    private readonly ILogger<DirectHttpEngine> _log;

    public async Task<TransferResult> ExecuteAsync(
        TransferJob job,
        BundleManifest manifest,
        IProgress<TransferProgress> progress,
        CancellationToken ct) { /* §4.3 */ }
}

public sealed record DirectHttpOptions(
    int BufferSizeBytes = 1 << 20,      // 1 MB
    TimeSpan RequestTimeout = default,  // default 30 minutes (huge files)
    TimeSpan ReadTimeout = default,     // default 60 seconds
    int MaxConnectionsPerServer = 4);
```

The `HttpClient` is a singleton, configured via `IHttpClientFactory` with a Kestrel-friendly handler:

```csharp
services.AddHttpClient<DirectHttpEngine>(c =>
{
    c.Timeout = TimeSpan.FromMinutes(30);
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    MaxConnectionsPerServer = 4,
    EnableMultipleHttp2Connections = true,
    AutomaticDecompression = DecompressionMethods.None,   // bundles already compressed
});
```

### 4.2 Resume Semantics

The agent tracks bytes received in `TransferJob.BytesDownloaded` (already in the SQLite schema from Phase 2). On transfer start:

- If `BytesDownloaded > 0` and the file at `StagingPath` exists and has size matching `BytesDownloaded`, agent sends `Range: bytes={BytesDownloaded}-`
- Otherwise agent sends no Range header (full download from scratch)

On 200 response, agent overwrites the staging file from byte 0. On 206 response, agent appends from current EOF. On 416 response (range not satisfiable, usually because cache changed), agent deletes the staging file and restarts from scratch.

### 4.3 Transfer Implementation

```csharp
public async Task<TransferResult> ExecuteAsync(
    TransferJob job,
    BundleManifest manifest,
    IProgress<TransferProgress> progress,
    CancellationToken ct)
{
    var staging = new FileInfo(job.StagingPath);
    var existingSize = staging.Exists ? staging.Length : 0;
    var resume = existingSize > 0 && existingSize == job.BytesDownloaded;

    using var request = new HttpRequestMessage(HttpMethod.Get, job.SourceUrl);
    if (resume) request.Headers.Range = new RangeHeaderValue(existingSize, null);

    using var response = await _http.SendAsync(request,
        HttpCompletionOption.ResponseHeadersRead, ct);

    if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
    {
        staging.Delete();
        return new TransferResult(false, 0, "Range not satisfiable; staging cleared");
    }
    if (!response.IsSuccessStatusCode)
        return new TransferResult(false, 0, $"HTTP {(int)response.StatusCode}");

    var contentLength = response.Content.Headers.ContentLength;
    var total = resume ? existingSize + contentLength : contentLength;

    Directory.CreateDirectory(staging.DirectoryName!);
    var mode = resume ? FileMode.Append : FileMode.Create;
    await using var dst = new FileStream(staging.FullName, mode,
        FileAccess.Write, FileShare.None, _options.BufferSizeBytes, useAsync: true);
    await using var src = await response.Content.ReadAsStreamAsync(ct);

    var buffer = new byte[_options.BufferSizeBytes];
    long pulled = existingSize;
    int read;
    var lastReport = Stopwatch.StartNew();

    while ((read = await src.ReadAsync(buffer, ct)) > 0)
    {
        await dst.WriteAsync(buffer.AsMemory(0, read), ct);
        pulled += read;

        if (lastReport.Elapsed >= TimeSpan.FromMilliseconds(500))
        {
            progress.Report(new TransferProgress(pulled, total));
            lastReport.Restart();
        }
    }
    await dst.FlushAsync(ct);

    progress.Report(new TransferProgress(pulled, total));
    return new TransferResult(true, pulled - existingSize, null);
}
```

The progress callback is throttled to every 500 ms to avoid spamming SignalR. The state machine runner does its own 5%-or-2s throttle on top.

### 4.4 Error Categories

| HTTP / IO outcome                               | Treated as                                             |
| ----------------------------------------------- | ------------------------------------------------------ |
| 200 / 206 success                               | Success                                                |
| 404                                             | Permanent fail; do not retry; record in operator queue |
| 503 with Retry-After                            | Transient; honour Retry-After; retry up to N times     |
| 5xx other                                       | Transient; retry with backoff                          |
| 416 Range Not Satisfiable                       | Stale local file; delete staging, restart              |
| `IOException` mid-stream                        | Transient; retry                                       |
| `TaskCanceledException` (timeout)               | Transient; retry                                       |
| `OperationCanceledException` (caller cancelled) | Abort; no retry                                        |

Retries are managed by the state machine, not the engine. The engine reports `TransferResult(Success: false, ErrorDetail: ...)` and the runner decides whether to re-queue.

### 4.5 Manifest Fetch

The manifest is small (KB range). Fetched via a separate GET before the bundle.zip / raw-file fetch:

```csharp
private async Task<BundleManifest> FetchManifestAsync(string sourceBase, CancellationToken ct)
{
    var manifestUrl = $"{sourceBase}/manifest.json";
    using var response = await _http.GetAsync(manifestUrl, ct);
    response.EnsureSuccessStatusCode();
    var bytes = await response.Content.ReadAsByteArrayAsync(ct);
    return ManifestSerializer.Deserialize(bytes);
}
```

The agent compares the fetched manifest against any locally cached copy (under `agent/manifests/{bundleId}/{version}.json`) and caches it for restart resilience. If the agent restarts mid-transfer, the manifest is already on disk.

------

## 5. Zip Extraction with Per-File Verification

### 5.1 Stream-Extract-Verify

Phase 2 verified per-file hashes by re-reading every file after extraction. Phase 4 hashes each file's bytes as they are written during extraction — a single pass.

```csharp
public sealed class ZipExtractVerifier
{
    public async Task<VerificationResult> ExtractAndVerifyAsync(
        string zipPath,
        BundleManifest manifest,
        string targetDir,
        IProgress<ExtractionProgress> progress,
        CancellationToken ct)
    {
        // 1. Verify the zip's overall hash first (fail fast on transport corruption)
        var actualZipHash = await ComputeFileSha256Async(zipPath, ct);
        if (actualZipHash != manifest.Archive!.Sha256)
            return VerificationResult.Failed(
                $"Zip hash mismatch: expected {manifest.Archive.Sha256}, got {actualZipHash}");

        Directory.CreateDirectory(targetDir);

        // 2. Open the zip and extract each entry, hashing on the fly
        using var archive = ZipFile.OpenRead(zipPath);
        var manifestByPath = manifest.Files!.ToDictionary(f => f.RelativePath, StringComparer.Ordinal);

        if (archive.Entries.Count != manifest.Files.Count)
            return VerificationResult.Failed(
                $"Zip entry count {archive.Entries.Count} != manifest fileCount {manifest.Files.Count}");

        var extracted = 0;
        var totalBytes = manifest.Files.Sum(f => f.Size);
        long bytesWritten = 0;

        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();

            if (!manifestByPath.TryGetValue(entry.FullName, out var fileEntry))
                return VerificationResult.Failed($"Entry {entry.FullName} not in manifest");

            var destPath = Path.Combine(targetDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            // Stream-copy with hash accumulation
            using var sha = SHA256.Create();
            await using var srcStream = entry.Open();
            await using var dstStream = File.Create(destPath);

            var buffer = new byte[1 << 20];
            int read;
            while ((read = await srcStream.ReadAsync(buffer, ct)) > 0)
            {
                sha.TransformBlock(buffer, 0, read, null, 0);
                await dstStream.WriteAsync(buffer.AsMemory(0, read), ct);
                bytesWritten += read;
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

            var actualHash = HashFormat.Format(sha.Hash!);
            if (actualHash != fileEntry.Hash)
                return VerificationResult.Failed(
                    $"File {entry.FullName}: expected {fileEntry.Hash}, got {actualHash}");

            extracted++;
            if (extracted % 100 == 0 || extracted == archive.Entries.Count)
                progress.Report(new ExtractionProgress(extracted, archive.Entries.Count, bytesWritten, totalBytes));
        }

        return VerificationResult.Success;
    }
}
```

### 5.2 Failure Handling

On any per-file hash mismatch, extraction halts. The partially extracted `targetDir` is left intact for forensics but is not considered a valid staging — the state machine transitions to `VerificationFailed`. The runner moves staging to `_failed/` and clears it; on retry, the transfer starts fresh.

For raw (`ChunkedHugeFile`) bundles, no extraction is performed. The raw file is verified via the manifest's `chunks[].sha256` once Phase 7 is in. In Phase 4, raw bundle verification reads each chunk sequentially and compares against `chunks[].sha256` — slow but correct. This is a Phase-4-acceptable approach until Phase 7 introduces parallel chunked verification.

### 5.3 Updated Local Layout

```
{DataRoot}/
  staging/
    {bundleId}/
      {version}/
        bundle.zip              (or raw file)        downloaded but not yet verified
  versions/
    {bundleId}/
      {version}/
        manifest.json
        bundle.zip              (kept for re-extraction if needed)
        extracted/              extracted contents, verified
          ...
  active/
    {bundleId}/  →  versions/{bundleId}/{version}/extracted   (junction)
```

After extraction succeeds:

- staging/{bundleId}/{version}/bundle.zip → versions/{bundleId}/{version}/bundle.zip (move)
- versions/{bundleId}/{version}/extracted/  already populated by the extractor
- manifest.json copied alongside

Keeping `bundle.zip` after extraction is intentional: if the consumer somehow corrupts `extracted/`, the agent can re-extract from the local zip without re-downloading. Costs disk space; Phase 11 GC can remove zips for non-active versions.

For raw bundles, there is no extraction. The raw file lives at `versions/{bundleId}/{version}/{filename}`.

------

## 6. Agent State Machine Updates

### 6.1 Changes From Phase 2

Phase 2's pipeline:

```
Stage command:
  Queued → Transferring → Transferred → Verifying → Staged → ReadyToActivate
  (Verifying did per-file hash re-read)

Activate command:
  ReadyToActivate → Activating (extract zip + repoint junction) → Active
```

Phase 4's pipeline:

```
Stage command:
  Queued → Transferring → Transferred → Verifying → Staged → ReadyToActivate
  (Verifying does: zip hash check + stream-extract-verify)

Activate command:
  ReadyToActivate → Activating (just repoint junction, already extracted) → Active
```

The state names are unchanged. The work behind `Verifying` and `Activating` shifts.

### 6.2 Transition Triggers

| From                                    | To                   | Trigger                                                      |
| --------------------------------------- | -------------------- | ------------------------------------------------------------ |
| `Unknown` / any                         | `Queued`             | `ReceiveCommand(Stage)` for a version not currently Active   |
| `Queued`                                | `Transferring`       | Engine starts, opens HTTP request                            |
| `Transferring`                          | `Transferred`        | Engine returns success                                       |
| `Transferring`                          | `TransferFailed`     | Engine returns failure                                       |
| `Transferred`                           | `Verifying`          | Automatic                                                    |
| `Verifying`                             | `Staged`             | Zip hash matches + all per-file hashes match (or raw chunks match) |
| `Verifying`                             | `VerificationFailed` | Any mismatch                                                 |
| `Staged`                                | `ReadyToActivate`    | Automatic                                                    |
| `ReadyToActivate`                       | `Activating`         | `ReceiveCommand(Activate)`                                   |
| `Activating`                            | `Active`             | Junction repointed successfully                              |
| `Activating`                            | `ActivationFailed`   | Junction operation failed                                    |
| `TransferFailed` / `VerificationFailed` | `Queued`             | Auto-retry with backoff (1s, 5s, 30s); after third, terminal |

### 6.3 Resume on Restart

Agent restart scenarios:

| Was in state                             | After restart                                                |
| ---------------------------------------- | ------------------------------------------------------------ |
| `Queued`                                 | Re-issue transfer request                                    |
| `Transferring` (partial file on disk)    | Resume via Range header — see §4.2                           |
| `Transferred`                            | Move forward to `Verifying` (zip is on disk, ready)          |
| `Verifying` (partial extraction on disk) | Delete `versions/{bundleId}/{version}/extracted/` and re-extract from `staging/{...}/bundle.zip` |
| `Staged` / `ReadyToActivate`             | Wait for Activate command                                    |
| `Activating`                             | Re-attempt junction repoint (idempotent for already-pointed-correctly) |
| `Active`                                 | Nothing to do                                                |

The `TransferJob.BytesDownloaded` row in SQLite is updated every 500 ms during transfer. Worst-case loss on a hard crash is ~500 ms of progress.

------

## 7. Configuration Additions

### 7.1 Master `appsettings.json`

```json
{
  "Nas": {
    "UncPath":   "\\\\nas\\sync",
    "LocalPath": "D:/sync"
  },
  "Cache": {
    "Root":                    "C:/ProgramData/SyncMaster/cache",
    "MaxConcurrentFills":      4,
    "FillBufferSizeBytes":     4194304,
    "MaxFillRetries":          3,
    "TrashRetentionHours":     24
  },
  "Snapshot":  { ... },
  "SiteConfig": { ... }
}
```

`MaxConcurrentFills` caps how many parallel fills the master will run. Default 4. Tune per NAS bandwidth.

`MaxFillRetries` matches §2.6's schedule of 1+3 attempts.

### 7.2 Agent `agent.json`

No new fields strictly required — the slice from site config (Phase 3) carries `dataSource.url`. The agent's local config remains:

```json
{
  "AgentId":     "SIM-03",
  "MasterUrl":   "http://master.local:8080",
  "DataRoot":    "C:/ProgramData/SyncAgent",
  "AppDataRoot": "C:/AppData",
  "Transfer": {
    "BufferSizeBytes":      1048576,
    "RequestTimeoutMinutes": 30,
    "MaxRetries":            3
  }
}
```

`Transfer` block tunes the `DirectHttpEngine`. The defaults work; expose for advanced cases.

------

## 8. Code Sketches

### 8.1 Cache Coordinator

```csharp
public interface ICacheCoordinator
{
    Task<string> EnsureCachedAsync(string bundleId, string version, CancellationToken ct);
    CacheEntry? Get(string bundleId, string version);
    IReadOnlyList<CacheEntry> ListEntries();
}

public sealed class CacheCoordinator : ICacheCoordinator
{
    private readonly INasReader _nas;
    private readonly CacheOptions _opts;
    private readonly SemaphoreSlim _fillSlots;
    private readonly ConcurrentDictionary<(string, string), CacheEntryHandle> _entries = new();
    private readonly ILogger<CacheCoordinator> _log;

    public async Task<string> EnsureCachedAsync(string bundleId, string version, CancellationToken ct)
    {
        var key = (bundleId, version);
        var handle = _entries.GetOrAdd(key, _ => new CacheEntryHandle(bundleId, version));

        await using var _ = await handle.Lock.AcquireAsync(ct);

        if (handle.State == CacheFillState.Cached)
            return handle.CachedDirectory!;

        if (handle.State == CacheFillState.Filling)
        {
            // Another waiter is filling. Release the lock and await its task.
            var task = handle.FillTask!;
            return await task;          // throws if fill fails
        }

        // State is NotCached or Failed; start a new fill.
        handle.State = CacheFillState.Filling;
        var tcs = new TaskCompletionSource<string>();
        handle.FillTask = tcs.Task;

        _ = Task.Run(async () => await ExecuteFillAsync(handle, tcs, CancellationToken.None));

        return await tcs.Task;
    }

    private async Task ExecuteFillAsync(CacheEntryHandle handle, TaskCompletionSource<string> tcs, CancellationToken ct)
    {
        await _fillSlots.WaitAsync(ct);
        try
        {
            var fillDir = Path.Combine(_opts.Root, "_fill", handle.BundleId, handle.Version);
            var finalDir = Path.Combine(_opts.Root, "bundles", handle.BundleId, handle.Version);

            if (Directory.Exists(fillDir)) Directory.Delete(fillDir, true);
            Directory.CreateDirectory(fillDir);

            // Pull manifest
            await _nas.CopyAsync(
                _nas.ManifestPath(handle.BundleId, handle.Version),
                Path.Combine(fillDir, "manifest.json"), ct);

            var manifest = ManifestSerializer.Deserialize(
                File.ReadAllBytes(Path.Combine(fillDir, "manifest.json")));

            // Pull payload
            if (manifest.Container == ContainerKind.Zip)
            {
                await _nas.CopyAsync(
                    _nas.ArchivePath(handle.BundleId, handle.Version, manifest.Archive!.Name),
                    Path.Combine(fillDir, manifest.Archive.Name), ct);

                // Verify zip hash matches manifest
                var hash = await Sha256.ComputeFileAsync(Path.Combine(fillDir, manifest.Archive.Name), ct);
                if (hash != manifest.Archive.Sha256)
                    throw new InvalidDataException($"NAS zip hash mismatch: expected {manifest.Archive.Sha256}, got {hash}");
            }
            else
            {
                await _nas.CopyAsync(
                    _nas.RawFilePath(handle.BundleId, handle.Version, manifest.File!.Name),
                    Path.Combine(fillDir, manifest.File.Name), ct);
                // Whole-file hash for raw bundles is on-demand only; skip here.
            }

            // Atomic commit
            Directory.CreateDirectory(Path.GetDirectoryName(finalDir)!);
            Directory.Move(fillDir, finalDir);

            handle.State = CacheFillState.Cached;
            handle.CachedDirectory = finalDir;
            handle.CompletedAt = DateTimeOffset.UtcNow;
            tcs.SetResult(finalDir);
            _log.LogInformation("Cached {Bundle}/{Version}", handle.BundleId, handle.Version);
        }
        catch (Exception ex)
        {
            handle.State = CacheFillState.Failed;
            handle.FailedAttempts++;
            handle.FillTask = null;
            tcs.SetException(ex);
            _log.LogError(ex, "Cache fill failed for {Bundle}/{Version}", handle.BundleId, handle.Version);
        }
        finally
        {
            _fillSlots.Release();
        }
    }
}

internal sealed class CacheEntryHandle
{
    public AsyncLock Lock { get; } = new();
    public CacheFillState State { get; set; } = CacheFillState.NotCached;
    public Task<string>? FillTask { get; set; }
    public string? CachedDirectory { get; set; }
    public int FailedAttempts { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string BundleId { get; }
    public string Version  { get; }

    public CacheEntryHandle(string bundleId, string version) { BundleId = bundleId; Version = version; }
}
```

### 8.2 NAS Reader

```csharp
public interface INasReader
{
    string ManifestPath(string bundleId, string version);
    string ArchivePath(string bundleId, string version, string archiveName);
    string RawFilePath(string bundleId, string version, string fileName);
    Task CopyAsync(string srcPath, string dstPath, CancellationToken ct);
}

public sealed class NasReader : INasReader
{
    private readonly NasOptions _opts;
    public NasReader(IOptions<NasOptions> opts) => _opts = opts.Value;

    private string Root => !string.IsNullOrEmpty(_opts.LocalPath) && Directory.Exists(_opts.LocalPath)
        ? _opts.LocalPath : _opts.UncPath;

    public string ManifestPath(string bundleId, string version) =>
        Path.Combine(Root, "bundles", bundleId, "versions", version, "manifest.json");

    public string ArchivePath(string bundleId, string version, string archiveName) =>
        Path.Combine(Root, "bundles", bundleId, "versions", version, archiveName);

    public string RawFilePath(string bundleId, string version, string fileName) =>
        Path.Combine(Root, "bundles", bundleId, "versions", version, fileName);

    public async Task CopyAsync(string srcPath, string dstPath, CancellationToken ct)
    {
        const int BufferSize = 4 * 1024 * 1024;
        await using var src = new FileStream(srcPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        await using var dst = new FileStream(dstPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
        await src.CopyToAsync(dst, BufferSize, ct);
    }
}
```

For SMB paths, the OS handles connection pooling. The same `FileStream` API works transparently. If the SMB session needs explicit creation (e.g. with credentials), use `NetUse.AddConnection(...)` at master startup. Phase 4 assumes already-authenticated SMB via the master service account.

### 8.3 Data Plane Endpoint

```csharp
app.MapGet("/content/bundles/{bundleId}/{version}/{**path}", async (
    string bundleId,
    string version,
    string path,
    ICacheCoordinator cache,
    HttpContext ctx) =>
{
    try
    {
        var cachedDir = await cache.EnsureCachedAsync(bundleId, version, ctx.RequestAborted);
        var fullPath = Path.Combine(cachedDir, path.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(fullPath))
            return Results.NotFound();

        var contentType = path.EndsWith(".json") ? "application/json" : "application/octet-stream";

        return Results.File(
            fullPath,
            contentType,
            enableRangeProcessing: true,
            entityTag: ComputeEtag(cachedDir, path));
    }
    catch (InvalidDataException ex)
    {
        // Hash mismatch during fill
        return Results.Problem(ex.Message, statusCode: 500);
    }
    catch (Exception)
    {
        return Results.StatusCode(503);   // Retry-After could be added
    }
});
```

### 8.4 Updated State Machine Verifier

```csharp
public sealed class StateMachineRunner
{
    // ... fields from Phase 2 ...
    private readonly IZipExtractVerifier _extractor;

    private async Task HandleStageAsync(Command cmd, CancellationToken ct)
    {
        // ... §11.3 of Phase 2 design through Transferring ...

        // After Transferred:
        await _db.SetBundleStateAsync(cmd.BundleId!, BundleState.Verifying, cmd.Version, ct);
        await _hub.ReportStatusAsync(cmd.BundleId!, BundleState.Verifying, cmd.Version);

        var manifest = await ReadManifestAsync(cmd, ct);
        var stagingZip = _paths.StagingPath(cmd.BundleId!, cmd.Version!, manifest.Archive?.Name ?? manifest.File!.Name);
        var versionDir = _paths.VersionDir(cmd.BundleId!, cmd.Version!);

        if (manifest.Container == ContainerKind.Zip)
        {
            var extractedDir = Path.Combine(versionDir, "extracted");
            Directory.CreateDirectory(extractedDir);

            var verifyProgress = new Progress<ExtractionProgress>(p =>
                _ = _hub.ReportStatusAsync(cmd.BundleId!, BundleState.Verifying, cmd.Version,
                    (int)(100 * p.BytesWritten / Math.Max(1, p.BytesTotal))));

            var result = await _extractor.ExtractAndVerifyAsync(
                stagingZip, manifest, extractedDir, verifyProgress, ct);

            if (!result.Success)
            {
                await _db.SetBundleStateAsync(cmd.BundleId!, BundleState.VerificationFailed, cmd.Version, ct);
                await _hub.AckAsync(new CommandAck(cmd.CommandId, AckResult.Failed, result.Detail));
                return;
            }
        }
        else
        {
            // Raw bundle: verify chunk hashes
            var result = await _chunkVerifier.VerifyAsync(stagingZip, manifest, ct);
            if (!result.Success)
            {
                await _db.SetBundleStateAsync(cmd.BundleId!, BundleState.VerificationFailed, cmd.Version, ct);
                await _hub.AckAsync(new CommandAck(cmd.CommandId, AckResult.Failed, result.Detail));
                return;
            }
        }

        // Move staging zip / raw into versions/
        File.Move(stagingZip, Path.Combine(versionDir, Path.GetFileName(stagingZip)));

        // Copy the manifest alongside
        await File.WriteAllBytesAsync(
            Path.Combine(versionDir, "manifest.json"),
            ManifestSerializer.Serialize(manifest), ct);

        await _db.RecordInstalledVersionAsync(cmd.BundleId!, cmd.Version!, manifest.GroupHash, ct);
        await _db.SetBundleStateAsync(cmd.BundleId!, BundleState.Staged, cmd.Version, ct);
        await _db.SetBundleStateAsync(cmd.BundleId!, BundleState.ReadyToActivate, cmd.Version, ct);
        await _hub.ReportStatusAsync(cmd.BundleId!, BundleState.ReadyToActivate, cmd.Version);

        await _hub.AckAsync(new CommandAck(cmd.CommandId, AckResult.Complete, null));
        await _db.MarkCommandDoneAsync(cmd.CommandId, ct);
    }

    private async Task HandleActivateAsync(Command cmd, CancellationToken ct)
    {
        // Phase 4: just repoint the junction
        var manifest = await ReadManifestAsync(cmd, ct);
        var newTarget = manifest.Container == ContainerKind.Zip
            ? Path.Combine(_paths.VersionDir(cmd.BundleId!, cmd.Version!), "extracted")
            : Path.Combine(_paths.VersionDir(cmd.BundleId!, cmd.Version!), manifest.File!.Name);

        await _db.SetBundleStateAsync(cmd.BundleId!, BundleState.Activating, cmd.Version, ct);
        await _hub.ReportStatusAsync(cmd.BundleId!, BundleState.Activating, cmd.Version);

        try
        {
            await JunctionWriter.RepointAsync(_paths.ActiveJunction(cmd.BundleId!), newTarget);
            await _db.SetBundleStateAsync(cmd.BundleId!, BundleState.Active, cmd.Version, ct);
            await _hub.ReportStatusAsync(cmd.BundleId!, BundleState.Active, cmd.Version);
            await _hub.AckAsync(new CommandAck(cmd.CommandId, AckResult.Complete, null));
        }
        catch (Exception ex)
        {
            await _db.SetBundleStateAsync(cmd.BundleId!, BundleState.ActivationFailed, cmd.Version, ct);
            await _hub.AckAsync(new CommandAck(cmd.CommandId, AckResult.Failed, ex.Message));
        }
        await _db.MarkCommandDoneAsync(cmd.CommandId, ct);
    }
}
```

------

## 9. Acceptance Tests

### 9.1 Master Cache

| Test                                           | Pass condition                                               |
| ---------------------------------------------- | ------------------------------------------------------------ |
| First GET for an uncached version              | Triggers fill; agent receives full bytes after fill          |
| Second GET for same version mid-fill           | Both requests await the same FillTask; both receive bytes after one fill |
| Master restart with partial `_fill/` directory | Cleanup on startup; next GET triggers fresh fill             |
| NAS file deleted before fill completes         | Fill fails; entry marked Failed; subsequent GET returns 503  |
| 20 agents request same version concurrently    | NAS sees one read; all agents served from cache              |
| Cache fill of 1 GB bundle                      | Completes; `bundles/X/v1/` exists; `_fill/X/v1/` does not    |
| GET with Range header on cached version        | 206 response with correct Content-Range                      |
| GET with invalid Range                         | 416                                                          |

### 9.2 DirectHttp Engine

| Test                                            | Pass condition                                               |
| ----------------------------------------------- | ------------------------------------------------------------ |
| Full download                                   | Bytes received match manifest archive.size; hash matches archive.sha256 |
| Network kill mid-download                       | Agent records BytesDownloaded; on retry, Range header used; resume completes |
| Master restart mid-download                     | Agent's GET fails; retry succeeds after master recovery      |
| Master 503 with Retry-After                     | Agent honours the delay before next attempt                  |
| Master 404                                      | Permanent failure; logged to operator queue; no infinite retry |
| Manifest hash mismatch in cache (corrupted NAS) | Fill fails; agent's GET returns 503; agent retries; persistent failure surfaces as TransferFailed |

### 9.3 Extract-Verify Pipeline

| Test                                | Pass condition                                               |
| ----------------------------------- | ------------------------------------------------------------ |
| Healthy bundle                      | Extraction completes; per-file hashes all match; state reaches Staged |
| Corrupted zip (zip hash mismatch)   | Fail at first hash check; state VerificationFailed; staging cleared on retry |
| Single file corrupted inside zip    | Detect on that file; halt extraction; report which file mismatched |
| Zip has extra entry not in manifest | Fail with "entry X not in manifest"                          |
| Zip missing an entry from manifest  | Fail with mismatch on entry count                            |
| Restart mid-extraction              | On startup, `extracted/` deleted and re-extracted            |

### 9.4 End-to-End

| Test                                                         | Pass condition                                               |
| ------------------------------------------------------------ | ------------------------------------------------------------ |
| Single agent, 100 MB bundle                                  | Reaches Active in < 30s on local network                     |
| 10 agents in master segment, single bundle                   | All reach Active; NAS sees one read; total time roughly equal to single-agent time + agent extraction time |
| 10 agents, one with intermittent network                     | The interrupted agent resumes via Range; reaches Active eventually |
| Agent restart during Transferring                            | After restart, transfer resumes from BytesDownloaded; reaches Active |
| Master restart during agent's Verifying                      | Agent's local state intact; on next Activate command, junction repoint succeeds |
| Bundle re-published as same version (idempotent) with same content | No fill churn; cache hit on next agent request               |

------

## 10. Implementation Sequence

1. **`INasReader`** with local + UNC path resolution. Tests for both.
2. **Cache directory layout helpers** (`ICachePaths`). Tests for path resolution and atomicity preconditions.
3. **`CacheCoordinator` — single-threaded happy path**. Tests for fill, idempotency, error states.
4. **Concurrent fill coalescing**. Tests with 10 parallel requests for same key.
5. **Master startup cache cleanup** (purge `_fill/`, scan `bundles/`). Tests with leftover directories.
6. **Data plane endpoint** using cache coordinator. Tests including Range requests, 416, 404, 503.
7. **`/api/cache/status`** endpoint. Smoke test.
8. **`DirectHttpEngine` on agent — no resume yet**. Replace stub. End-to-end test of one bundle.
9. **Resume support**: agent records BytesDownloaded; engine sends Range header; tested by killing connection mid-transfer.
10. **`ZipExtractVerifier`**. Tests with intact, corrupted-zip, corrupted-file, missing-entry, extra-entry scenarios.
11. **Update state machine: `Verifying` now extracts + verifies**. Tests for state transitions.
12. **Update state machine: `Activating` now just repoints**. Tests for fast activation.
13. **Raw bundle chunk verification** (sequential, slow but correct). Tests with corrupted chunks.
14. **End-to-end Phase 4 acceptance tests** (§9.4).

After step 14, Phase 4 is complete. Phase 5 introduces relays serving downstream agents while keeping everything in Phase 4 working in the master's segment.

------

## 11. Open Questions for Implementation

A few minor decisions:

- **Cache skip for local-NAS master**: I left the cache mandatory even when NAS is on the same machine as the master. Tell me if you'd want a `Cache.SkipForLocalNas: true` flag — small implementation, lets master serve directly from NAS file paths when co-located. My default is keep-it-uniform.
- **`bundle.zip` retained in `versions/` after extraction**: agents keep the zip alongside `extracted/`. Lets the agent re-extract without re-downloading. Costs ~2× disk per version. Alternative: delete the zip after successful extraction. Confirm preference.
- **Raw bundle whole-file verification in Phase 4**: my plan is sequential chunk-by-chunk hash verification using the manifest's `chunks[]`. Phase 7 will parallelise. Tell me if Phase 4 should skip chunk verification entirely for raw bundles (and rely on Phase 7's proper verifier later), or do the sequential pass now. My default is do-it-correctly-now.
- **Master cache eviction in Phase 4**: GC is Phase 11. In the meantime, an operator can purge `cache/bundles/{id}/{version}/` by hand if needed. A manual `POST /api/cache/evict?bundleId=X&version=v42` endpoint would help. Add now or wait for Phase 11?
- **HTTP/2 vs HTTP/1.1 for data plane**: Kestrel supports both. HTTP/2 multiplexes nicely, HTTP/1.1 has wider tooling familiarity. I've configured the agent's `HttpClient` with `EnableMultipleHttp2Connections = true`; Kestrel defaults to negotiating. No explicit choice forced. Confirm OK.

These are minor; flag preferences and the build can proceed.





# Phase 5 Detailed Design

## Cascading via Segment Relays

*Companion to Architecture Design Document Revision 2, API Specification, Phases 1–4 Designs* *Targets `Phase 5` from §21 of the architecture doc* *C# / .NET 8 · ASP.NET Core · May 2026*

------

## 1. Phase Scope

### 1.1 What Phase 5 Adds On Top of Phase 4

Phase 4 introduced the real `DirectHttp` engine and the master's pull cache, with all agents pulling directly from the master. Phase 5 makes the cascade real: each segment gets a designated relay that pulls from the master and serves nodes in its segment.

Deliverables:

1. **Relay role activation** — agents flagged `isRelay: true` in site config start a data-plane HTTP listener and run their own pull cache, structurally identical to the master's.
2. **Cascade fill path** — relays pull from master; non-relay agents in non-master segments pull from their segment's relay. Master is still the only SMB client to the NAS.
3. **Presence-aware slice computation** — agent slices include `dataSource.url` that points to the segment's relay when the relay is `Online`, falling back to the master when the relay is `Unreachable`. ConfigUpdate fires on presence changes.
4. **Proactive cache warming** — when the master dispatches Stage commands to agents in a non-master segment, it also sends a `CacheWarm` command to the segment's relay, in parallel. Eliminates the first-agent-waits-double-latency penalty.
5. **Relay cache GC** — disk-watermark-driven LRU eviction running on relays. The same code is wired but disabled on the master until Phase 11.
6. **CAL validation procedure** — operational checklist confirming that the NAS sees exactly one SMB session under load and that nodes never speak SMB.

### 1.2 Explicitly Deferred

| Concern                                                   | Deferred to |
| --------------------------------------------------------- | ----------- |
| Cooperative safe-window activation                        | Phase 6     |
| Chunked huge-file transfer with parallel chunks           | Phase 7     |
| Recordings                                                | Phase 8     |
| Two-phase commit, automated rollback                      | Phase 9     |
| Fleet sync window scheduler                               | Phase 10    |
| Master pull-cache GC, agent local GC, NAS GC, dry-run API | Phase 11    |
| Two relays per segment, cross-segment failover            | not planned |

The cache GC built in this phase runs only on relays. The same cache coordinator code is used by the master, but the master's eviction policy is "keep everything" until Phase 11.

### 1.3 Definition of Done

After Phase 5 an operator can:

- Designate `REL-A1` as the relay for segment `seg-A` in site config, run `POST /api/config/reload`, and see `REL-A1` start serving content
- Issue a fleet deploy targeting 100 agents across 5 segments; the NAS sees exactly 1 SMB session throughout, each inter-segment link transfers each bundle once, each relay caches and serves to ~20 local agents in parallel
- Take a relay down mid-deploy; affected segment's agents transparently fall back to direct master pull (slice updated within a few seconds); bring relay back up; agents migrate back to relay-pull on the next deploy
- Fill a relay's cache past its disk watermark; LRU eviction frees space; in-flight serves never get cut off
- Run the CAL validation script and see all four assertions pass

------

## 2. Topology Activation

### 2.1 Site Config Additions

Per-agent `dataPlaneUrl` is required for any agent with `isRelay: true`. Validation enforces:

```json
{
  "agentId":       "REL-A1",
  "hostname":      "relay-a.local",
  "segmentId":     "seg-A",
  "capabilities":  ["relay"],
  "isRelay":       true,
  "isMaster":      false,
  "dataPlaneUrl":  "http://relay-a.local:8080",
  "logicalNodeIds": []
}
```

For the master agent, `dataPlaneUrl` is already declared in `topology.master.dataPlaneUrl`. Phase 5 adds the rule: if the master's segment has no other relay (i.e. `segments[].relayAgentId == topology.master.agentId`), the master acts as its segment's relay using its own `dataPlaneUrl`.

Validation rules added in Phase 5:

| Rule                                                         | Failure |
| ------------------------------------------------------------ | ------- |
| Every `segments[].relayAgentId` references an agent with `isRelay: true` OR `isMaster: true` | 400     |
| Every agent with `isRelay: true` has a non-empty `dataPlaneUrl` | 400     |
| `dataPlaneUrl` parses as an absolute HTTP URL                | 400     |
| Each segment has exactly one designated relay                | 400     |
| An agent is not both `isRelay` and `isMaster` (mutually exclusive in this design) | 400     |

### 2.2 Slice Becomes Presence-Aware

In Phase 3 the slice's `dataSource.url` was statically `master.dataPlaneUrl`. In Phase 5 it depends on:

1. Whether this agent is itself the master or a relay
2. The current presence (`Online` vs `Unreachable`) of the segment's designated relay

Computation:

```
ComputeDataSource(agent):
  if agent.isMaster or agent.isRelay:
    return ("Master", master.dataPlaneUrl)              // always pull from master
  segment = config.segments[agent.segmentId]
  relayAgent = config.agents[segment.relayAgentId]
  if relayAgent.agentId == master.agentId:
    return ("Master", master.dataPlaneUrl)              // master serves its own segment
  relayPresence = fleet.GetPresence(relayAgent.agentId)
  if relayPresence == Online:
    return ("Relay", relayAgent.dataPlaneUrl)
  else:
    return ("Master", master.dataPlaneUrl)              // failover
```

Slice diffs are computed against the previous slice. If `dataSource.url` changed, a `ConfigUpdate` is pushed to that agent.

### 2.3 New ConfigUpdate Triggers

In Phase 3, `ConfigUpdate` fired only on site-config reload. In Phase 5 it also fires on:

- Relay agent connects (`OnConnectedAsync`) → slices for non-relay agents in its segment recompute; affected agents receive ConfigUpdate
- Relay agent disconnects (`OnDisconnectedAsync`) → same recomputation; slices flip back to master
- Master agent's `FleetState.UpdatePresence` raises an event picked up by the `ConfigPushService` from Phase 3

The service throttles: presence changes within a 2-second window are coalesced. A relay flapping (rapid up/down) doesn't generate a storm of slice updates.

### 2.4 What the Agent Does With dataSource.url

The agent's transfer engine reads `dataSource.url` at the moment of each transfer attempt:

```
sourceBase = agentSlice.DataSource.Url           // resolved fresh per attempt
manifestUrl = $"{sourceBase}/content/bundles/{bundleId}/{version}/manifest.json"
archiveUrl  = $"{sourceBase}/content/bundles/{bundleId}/{version}/bundle.zip"
```

The `Stage` command no longer carries `SourceUrl`. This is a small but real change to the SignalR contract — see §9.1.

### 2.5 Bootstrap Order

If the master starts before any relays connect, slices for all agents temporarily route to master (relay is `Unreachable`). As each relay comes online and registers, the master:

1. Marks relay `Online`
2. Recomputes slices for non-relay agents in the relay's segment
3. Pushes ConfigUpdate to each affected online agent

For an offline agent (in any segment), the next `Register` carries the current (correct) slice. No catch-up logic needed.

------

## 3. Master → Relay → Node Flow

### 3.1 Fill Cascade

```
First agent in segment-A requests bundle.zip:
  agent.GET http://relay-a.local:8080/content/bundles/X/v42/bundle.zip
    └─ relay's CacheCoordinator: NotCached
       └─ fill via IUpstreamReader (HTTP from master):
          relay.GET http://master.local:8080/content/bundles/X/v42/bundle.zip
            └─ master's CacheCoordinator: NotCached
               └─ fill via INasReader (SMB or local FS):
                  master copies bundle from NAS into master's cache
                  master atomic-renames _fill → bundles/X/v42/
            └─ master serves byte stream to relay
          relay receives byte stream, writes to relay's _fill/X/v42/
          relay atomic-renames to bundles/X/v42/
       └─ relay serves byte stream to agent

Subsequent agents in segment-A:
  agent.GET http://relay-a.local:8080/content/bundles/X/v42/bundle.zip
    └─ relay's CacheCoordinator: Cached
    └─ stream served from relay's local file
```

The relay's cache structure is identical to the master's (§2.2 of Phase 4):

```
{RelayCacheRoot}/
  bundles/{bundleId}/{version}/         present iff fully cached
    manifest.json
    bundle.zip                          (or raw filename)
  _fill/{bundleId}/{version}/           in-progress, atomic-renamed on complete
  _trash/{timestamp}-{id}-{version}/    evicted entries kept briefly
```

### 3.2 Bundle Source Abstraction

The cache coordinator is shared between master and relay. The source differs:

```csharp
public interface IBundleSource
{
    string ManifestUrl(string bundleId, string version);          // for logging
    Task CopyManifestAsync(string bundleId, string version, string dstPath, CancellationToken ct);
    Task CopyArchiveAsync(string bundleId, string version, string archiveName, string dstPath,
                          IProgress<long>? progress, CancellationToken ct);
    Task CopyRawAsync(string bundleId, string version, string fileName, string dstPath,
                      IProgress<long>? progress, CancellationToken ct);
}

public sealed class NasBundleSource : IBundleSource { /* master uses this; SMB or local FS */ }
public sealed class MasterBundleSource : IBundleSource { /* relay uses this; HTTP GET from master */ }
```

`NasBundleSource` is the Phase 4 `INasReader` adapted to the new interface. `MasterBundleSource` is new in Phase 5 and uses the same `HttpClient` as the agent's `DirectHttpEngine`, with the same byte-range resume behaviour.

### 3.3 Concurrent Fill Coalescing — Still Works

Each cache coordinator (master's, relay's) maintains its own `(bundleId, version) → CacheEntryHandle` map. The Phase 4 coalescing semantics carry forward without change:

- 10 agents request the same uncached version from relay-A simultaneously
- relay-A's coordinator: one fill task; all 10 await it
- That fill task issues exactly one GET to the master
- Master's coordinator: one fill task; relay-A's request awaits it
- Master's fill: exactly one SMB read

End-to-end: 1 NAS read, 1 master→relay HTTP transfer, 10 relay→agent HTTP transfers, 0 redundant fetches.

### 3.4 In-Flight Coalescing Across Master and Relay

A subtler case: relay-A is in the middle of pulling from master when relay-B (different segment) also starts pulling the same bundle from master. Both relays issue GETs to the master.

Master's coordinator coalesces: one fill task across both relays. Both await the same task. NAS still sees only one SMB read.

The coalescing works regardless of how many layers of pulling exist (NAS → master → relay → relay-of-relay → agent if such a topology existed). Each layer's coordinator collapses concurrent requests for the same key.

------

## 4. Relay Agent Process

### 4.1 Process Shape

The relay agent runs the same executable as a regular agent, distinguished by its slice's `isRelay: true` flag. On slice apply, the agent starts or stops the data-plane HTTP listener as a hosted service.

```
SyncAgent  (Windows Service, .NET 8)
  Generic Host
    SyncAgentWorker            owns the SignalR connection
    StateMachineRunner         drives per-bundle state machine
    SnapshotReporter           periodic ReportStatus

    [new in Phase 5, conditional]
    RelayDataPlaneHost         hosted service, started iff isRelay=true in slice
      Kestrel listener on dataPlaneUrl's port
      RelayCacheCoordinator    (IBundleSource = MasterBundleSource)
      Endpoint: GET /content/bundles/{bundleId}/{version}/{*path}
      Endpoint: GET /api/cache/status (informational, same as master's)

  Local state
    agent.db                   SQLite (Phase 2)
    versions/, staging/, active/   (Phases 2, 4)
    [new in Phase 5, only on relay agents]
    relay-cache/               separate from versions/; serves to segment
      bundles/
      _fill/
      _trash/
```

The relay's `versions/` directory holds bundles the relay machine itself uses locally (typically empty — a relay has no app workload). The `relay-cache/` directory holds bundles the relay serves to its segment.

The two directories are deliberately separate. If a relay does happen to be using a bundle locally, the same bundle gets a second copy in `relay-cache/`. Acceptable in Phase 5 — Phase 11 GC may add cross-tree deduplication later.

### 4.2 Slice-Driven Listener Lifecycle

```csharp
public sealed class RelayDataPlaneHost : BackgroundService
{
    private readonly IAgentSliceManager _slice;
    private readonly IServiceProvider _services;
    private WebApplication? _runningHost;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _slice.SliceChanged += async (_, args) => await ReconcileAsync(args.NewSlice, ct);
        await ReconcileAsync(_slice.Current, ct);
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
    }

    private async Task ReconcileAsync(AgentSlice slice, CancellationToken ct)
    {
        var shouldRun = slice.IsRelay && Uri.TryCreate(slice.DataPlaneUrl, UriKind.Absolute, out _);

        if (shouldRun && _runningHost is null)
        {
            _runningHost = BuildHost(slice.DataPlaneUrl!);
            await _runningHost.StartAsync(ct);
            _log.LogInformation("Relay data plane started on {Url}", slice.DataPlaneUrl);
        }
        else if (!shouldRun && _runningHost is not null)
        {
            await _runningHost.StopAsync(ct);
            _runningHost = null;
            _log.LogInformation("Relay data plane stopped");
        }
        // url change while running: stop + restart
        else if (shouldRun && _runningHost is not null && CurrentUrl != slice.DataPlaneUrl)
        {
            await _runningHost.StopAsync(ct);
            _runningHost = BuildHost(slice.DataPlaneUrl!);
            await _runningHost.StartAsync(ct);
        }
    }
}
```

Note: when `isRelay` is the agent's own flag in its slice, the agent itself becomes a relay. This is set at site-config time and rarely changes.

### 4.3 Listener Configuration

Port is derived from `dataPlaneUrl`. The relay binds to all interfaces (`0.0.0.0:{port}`) by default. If finer control is needed, an explicit binding setting can be added to `agent.json`:

```json
{
  "Relay": {
    "BindAddress": "0.0.0.0",
    "MaxConcurrentFills": 2
  }
}
```

`MaxConcurrentFills` is the per-relay analogue of the master's setting. Default 2 (smaller than master's 4 because a single relay typically pulls fewer distinct bundles than the master).

------

## 5. Proactive Cache Warming

### 5.1 The Problem

Lazy fill alone forces the first agent in each segment to wait through two cache fills (master's + relay's). For a 10 GB bundle over a 1 Gbps inter-segment link, that's ~80 seconds before the first byte reaches the first agent. The next 19 agents see warm cache and complete quickly.

For Phase 5, proactive warming sends a hint to the relay at the same moment the agent Stage commands go out, so by the time agents request bytes, the relay's fill is well underway (often complete).

### 5.2 Mechanism: `CacheWarm` Command

A new `CommandAction` value. The command tells a relay to ensure a bundle version is cached. It does not transition any state on the relay agent — it's purely a cache operation:

```csharp
public enum CommandAction
{
    Stage,
    Activate,
    Cancel,
    Verify,
    CacheWarm                     // new in Phase 5; sent to relays only
}
```

Command shape:

```json
{
  "commandId":   "warm-...",
  "action":      "CacheWarm",
  "bundleId":    "TerrainTextures",
  "version":     "v42"
}
```

The relay handler treats the command as an instruction to call `relayCache.EnsureCachedAsync(bundleId, version)`. On completion, the relay sends `AckCommand(commandId, Complete)`. On fill failure, `Failed` with detail.

### 5.3 When the Master Sends Warm Commands

When a Deploy intent resolves to a set of agents:

```
foreach agent in resolvedAgents:
  if agent.segment == master.segment:
    // master is the relay for this segment; cache fills naturally via agent request
    skip
  else:
    relayId = config.segments[agent.segment].relayAgentId
    pendingWarms.Add(relayId)

foreach relayId in pendingWarms.Distinct():
  intent = CreateIntent(kind: CacheWarm, agentId: relayId, bundleId, version)
  Dispatch(intent)                                // parallel with Stage dispatches
```

The CacheWarm intents are sent in parallel with the Stage intents. Agents and their segment's relay race to start the fill — but the relay almost always wins because:

- The relay's CacheWarm command produces an immediate fill kickoff
- The agent's Stage command goes through `Queued → Transferring → starts HTTP`, which has a small startup latency
- And even if the agent's request arrives at the relay before the relay's fill starts, the relay's coordinator coalesces them onto the same fill task — no harm done

### 5.4 Operator Visibility

`GET /api/intents` includes CacheWarm intents alongside Deploy intents. They show up in the operator UI as a separate "kind". Useful for diagnosing why a particular segment is slow (warm fill stalled).

CacheWarm intents are short-lived — they complete as soon as the relay's cache has the bundle. They don't transition node-agent state. Failure of a CacheWarm doesn't prevent agent Stage from succeeding (the agent's request will fall back to master via failover if the relay can't serve).

------

## 6. Failover and Recovery

### 6.1 Relay Goes Offline

```
master detects relay-A disconnect via SignalR OnDisconnectedAsync
  → FleetState.MarkUnreachable("REL-A1")
  → presence event fires
  → ConfigPushService recomputes slices for agents in seg-A
  → slices change: dataSource.url shifts from relay's URL to master's URL
  → ConfigUpdate pushed to each agent in seg-A
```

Throttle: presence changes within 2 seconds are coalesced into one slice push. Avoids churn when a relay briefly blips.

### 6.2 In-Flight Transfer When Slice Changes

Agent's transfer engine is in the middle of pulling from the (now-offline) relay:

- The HTTP connection drops (relay process died) or hangs (network partition)
- `IOException` or timeout fires; engine returns `TransferResult(Success: false, ...)`
- State machine: `Transferring → TransferFailed → Queued` after backoff
- On retry, agent reads its slice fresh; sees new `dataSource.url` pointing to master
- Retry succeeds against master

The agent never needs special "failover" logic. The slice update + the natural retry loop handle it.

### 6.3 Resume Across Failover

The Phase 4 byte-range resume works across the failover:

- Agent had downloaded 600 MB from relay before the relay went offline
- `BytesDownloaded = 600 MB` recorded in SQLite
- On retry against master, agent sends `Range: bytes=600000000-`
- Master serves from its cache, supports byte ranges, completes the download

The staged-file content from before failover is reused. No re-download of the first 600 MB.

### 6.4 Relay Comes Back Online

```
relay-A reconnects via SignalR
  → master FleetState marks Online
  → ConfigPushService recomputes slices for seg-A agents
  → slices flip back: dataSource.url goes from master back to relay
  → ConfigUpdate pushed to seg-A agents
```

In-flight transfers continue against master (no preemption). The next new transfer in seg-A uses the relay again. Lazy re-warming: the relay's cache may be cold; the first new request triggers fill.

### 6.5 Cache State After Relay Restart

A relay restart with intact `relay-cache/` directory keeps all cached bundles. The cache coordinator's startup scan rebuilds the in-memory entry handles from on-disk presence:

- For every `bundles/{bundleId}/{version}/` directory, mark `Cached`
- Delete any `_fill/{bundleId}/{version}/` (leftover from crash)
- Delete `_trash/*` older than `TrashRetentionHours`

The relay rejoins SignalR, gets a slice with the same role, and starts serving without re-pulling anything that was already cached.

### 6.6 Master Restart

Behaviour from Phase 4 unchanged for the master's own cache. New for Phase 5: relays see the master disconnect. While master is down:

- Relays can still serve already-cached bundles to nodes (relay's cache is a local file system, not dependent on master being up)
- Relays cannot fill new bundles (upstream gone)
- Nodes pulling cached bundles succeed; pulling uncached bundles fails on the relay

When master comes back, relays' SignalR connections re-establish. Pending Stage and CacheWarm commands replay through `Register`'s `ReplayedCommands` field. Operation resumes.

------

## 7. Relay Cache GC

### 7.1 Eviction Policy

When a fill completes, the relay's coordinator checks total disk usage in the cache:

```
if cacheBytes > targetBytes (= MaxCacheGB * 1GB * 0.9):
  evictLruUntilUnder(targetBytes - safetyMargin)
```

LRU is tracked by `CacheEntry.LastAccessedAt`, updated on every successful serve (range or full).

Skip rules — never evict an entry where any of these hold:

- `State != Cached` (Filling entries already protected by their fill lock)
- `ActiveReadCount > 0` (track via refcount during serves; decremented on response complete)
- Entry was first cached less than `MinCacheAgeMinutes` ago (default 5 minutes — protects against just-now-warmed entries being evicted under tight watermarks)

### 7.2 Active Read Refcount

A small wrapper around the file response stream:

```csharp
public sealed class CountingFileResult : IResult
{
    private readonly string _path;
    private readonly CacheEntryHandle _handle;

    public async Task ExecuteAsync(HttpContext ctx)
    {
        Interlocked.Increment(ref _handle.ActiveReads);
        try
        {
            await Results.File(_path, "application/octet-stream",
                enableRangeProcessing: true).ExecuteAsync(ctx);
        }
        finally
        {
            Interlocked.Decrement(ref _handle.ActiveReads);
            _handle.LastAccessedAt = DateTimeOffset.UtcNow;
        }
    }
}
```

This is a Phase 5 addition to the data-plane endpoint.

### 7.3 Eviction Process

```csharp
private async Task EvictIfOversizedAsync()
{
    var candidates = _entries.Values
        .Where(e => e.State == CacheFillState.Cached
                 && e.ActiveReads == 0
                 && DateTimeOffset.UtcNow - e.CompletedAt > _options.MinCacheAge)
        .OrderBy(e => e.LastAccessedAt)
        .ToList();

    var currentBytes = ComputeCurrentDiskUsage();
    foreach (var entry in candidates)
    {
        if (currentBytes <= _options.TargetBytes) break;

        var bundleDir = Path.Combine(_options.Root, "bundles", entry.BundleId, entry.Version);
        var trashDir = Path.Combine(_options.Root, "_trash",
            $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{entry.BundleId}-{entry.Version}");

        Directory.Move(bundleDir, trashDir);                  // atomic rename, makes entry invisible
        _entries.TryRemove((entry.BundleId, entry.Version), out _);
        currentBytes -= entry.TotalBytes;

        _log.LogInformation("Evicted {Bundle}/{Version} ({Bytes} bytes)",
            entry.BundleId, entry.Version, entry.TotalBytes);
    }

    if (currentBytes > _options.TargetBytes)
    {
        _messages.Enqueue(OperatorMessage.Warning(
            "Cache eviction insufficient",
            $"Cache still {currentBytes} bytes after eviction; consider increasing MaxCacheGB."));
    }
}
```

### 7.4 Trash Cleanup

A periodic hosted service sweeps `_trash/` every hour:

```csharp
foreach (var dir in Directory.GetDirectories(trashRoot))
{
    var age = DateTimeOffset.UtcNow - Directory.GetCreationTimeUtc(dir);
    if (age > _options.TrashRetentionHours)
        Directory.Delete(dir, recursive: true);
}
```

Trash retention is configurable (default 24h). The window gives an operator time to recover an erroneously-evicted bundle by manually moving it back from `_trash/` to `bundles/`.

### 7.5 Where Eviction Runs

| Layer                  | GC enabled in Phase 5?                                       |
| ---------------------- | ------------------------------------------------------------ |
| Relay's `relay-cache/` | Yes                                                          |
| Master's `cache/`      | Code present; enabled only if `Cache.GcEnabled: true` in `appsettings.json` (default false) |
| Agent's `versions/`    | No — Phase 11                                                |
| NAS                    | No — Phase 11                                                |

The master's cache GC is identical code, gated by a config flag. Phase 11 turns it on by default and adds the dry-run / run API.

### 7.6 Configuration

```json
{
  "Cache": {
    "Root":                "C:/ProgramData/SyncAgent/relay-cache",
    "MaxCacheGB":          200,
    "WatermarkPercent":    90,
    "MinCacheAgeMinutes":  5,
    "TrashRetentionHours": 24,
    "GcEnabled":           true
  }
}
```

For the master, the same options live under `appsettings.json`'s `Cache:` section. The `GcEnabled` flag is the difference (false on master in Phase 5, true on relays).

------

## 8. CAL Validation

### 8.1 What CAL Validation Means

Windows CAL counts. The design's central topological claim is:

- **At most 1 SMB session to the NAS** (the master's, persistent), regardless of fleet size or activity
- **0 SMB sessions from any relay or node to anywhere**
- **All inter-machine bundle transport is HTTP**

Phase 5 includes a validation procedure to confirm this empirically before declaring the system production-ready.

### 8.2 Procedure

Run during a representative load (e.g. a fleet deploy of a 10 GB bundle to 100 agents across 5 segments):

#### Test 1 — NAS SMB session count

On the NAS host (Windows file server), run:

```powershell
Get-SmbSession | Where-Object { $_.ClientUserName -like '*master*' } | Measure-Object
```

**Expected**: exactly 1 session from the master service account, throughout the deploy. The session count should not grow with fleet size.

If the NAS is a COTS appliance, use its administrative interface to verify connection count (or use `Get-SmbSession` on a Windows machine acting as a SMB client probe).

#### Test 2 — No SMB sessions from nodes

On a sample of regular agent machines (5–10 across all segments), run:

```powershell
Get-SmbConnection | Where-Object { $_.ServerName -like '*nas*' }
```

**Expected**: empty result. No SMB connections to the NAS from any node.

#### Test 3 — No SMB sessions from relays

On every relay machine, same command:

```powershell
Get-SmbConnection | Where-Object { $_.ServerName -like '*nas*' }
```

**Expected**: empty result. Relays speak HTTP to the master, never SMB to the NAS.

#### Test 4 — HTTP traffic shape

On the master host, observe Kestrel access logs during the deploy. Verify:

- GET requests to `/content/bundles/...` from relay IPs only (during cascade)
- Direct GET requests from node IPs only when their relay is `Unreachable` (failover case)

On each relay host, observe its Kestrel access logs:

- GET requests to `/content/bundles/...` from node IPs in the relay's segment

#### Test 5 — Cache hit ratio

Use `GET /api/cache/status` on master and each relay. After the deploy:

- Master cache: should contain the deployed bundle, fetched once from NAS
- Each relay cache: should contain the deployed bundle, fetched once from master
- All cache entries should show `State: Cached`

### 8.3 Operational Acceptance Sign-Off

The system passes Phase 5 CAL validation when:

| Assertion                        | Pass criterion                                               |
| -------------------------------- | ------------------------------------------------------------ |
| NAS SMB session count under load | exactly 1                                                    |
| Node→NAS SMB connections         | 0 across all sampled nodes                                   |
| Relay→NAS SMB connections        | 0 across all relays                                          |
| Master→relay HTTP traffic        | Visible during deploy, sized roughly = bundle size × number of segments |
| Relay→node HTTP traffic          | Visible during deploy, sized roughly = bundle size × nodes-per-segment |

Document the numbers in an operator runbook entry. Re-run after any topology change.

### 8.4 What If Validation Fails

Most common cause: a relay or node is misconfigured to access the NAS directly (e.g. an old SMB mapping left in place from manual testing). Run `net use` on the offending machine and remove stale mappings.

A subtler cause: the master service running under an account that also has interactive sessions on the NAS share. Disable interactive logins for the master service account.

------

## 9. SignalR and Persistence Changes

### 9.1 `Stage` Command Loses `SourceUrl`

```csharp
public sealed record Command(
    string CommandId,
    CommandAction Action,
    string? BundleId,
    string? Version);
    // SourceUrl removed in Phase 5; agent uses slice
```

The agent looks up its slice each transfer attempt:

```csharp
var sourceBase = _slice.Current.DataSource.Url;
var manifestUrl = $"{sourceBase}/content/bundles/{cmd.BundleId}/{cmd.Version}/manifest.json";
```

If `dataSource.url` is empty or invalid (uncommon — slice should always carry one), the agent reports the Stage intent `Failed` with detail "no valid dataSource in slice".

### 9.2 New `CacheWarm` Command Action

Per §5.2. The relay agent's StateMachineRunner gains a handler:

```csharp
case CommandAction.CacheWarm:
    await HandleCacheWarmAsync(cmd, ct);
    break;

private async Task HandleCacheWarmAsync(Command cmd, CancellationToken ct)
{
    try
    {
        await _relayCache.EnsureCachedAsync(cmd.BundleId!, cmd.Version!, ct);
        await _hub.AckAsync(new CommandAck(cmd.CommandId, AckResult.Complete, null));
    }
    catch (Exception ex)
    {
        await _hub.AckAsync(new CommandAck(cmd.CommandId, AckResult.Failed, ex.Message));
    }
    await _db.MarkCommandDoneAsync(cmd.CommandId, ct);
}
```

Note that `CacheWarm` does not interact with the `BundleState` table — it operates on the cache, not on the agent's per-bundle state.

### 9.3 Intent Kind Expansion

`Intent.Kind` gains `CacheWarm`:

```csharp
public enum IntentKind { Deploy, Activate, Cancel, Verify, CacheWarm }
```

CacheWarm intents are first-class: persisted in the master snapshot, visible via `GET /api/intents`, can be cancelled by operator (which is rarely useful but supported).

### 9.4 Snapshot Schema Bump

`master-state.json` schemaVersion goes from Phase 3's 2 to 3, adding `CacheWarm` to the legal `intents[].kind` values. Migration: silent — old snapshots load fine.

------

## 10. Operator UI Updates

### 10.1 New Information

The Phase 3 UI showed agents grouped by segment. Phase 5 adds, per segment:

- The relay's identity and presence
- The relay's data-plane URL
- The relay's cache size (from `GET /api/cache/status` on the relay)
- Number of cached bundles
- Any CacheWarm intents in flight

### 10.2 New Endpoint Aggregation

The operator UI polls `GET /api/status` (existing) plus a new `GET /api/relays/status`:

```json
{
  "relays": [
    {
      "agentId":        "REL-A1",
      "segmentId":      "seg-A",
      "dataPlaneUrl":   "http://relay-a.local:8080",
      "presence":       "Online",
      "cache": {
        "diskFreeGB":    73,
        "diskTotalGB":   200,
        "entryCount":    4,
        "totalCachedGB": 117
      },
      "currentFills": [
        { "bundleId": "TerrainTextures", "version": "v42", "bytesPulled": 854624256, "bytesTotal": 1073741824 }
      ]
    }
  ]
}
```

The master collects this by querying each relay's `/api/cache/status` endpoint over HTTP, periodically (every 10 seconds), and caches the results. The operator UI gets the cached collection.

If a relay's status query fails, its entry shows `presence: "Unreachable"` and the cache fields are null.

------

## 11. Acceptance Tests

### 11.1 Topology Activation

| Test                                                    | Pass condition                                               |
| ------------------------------------------------------- | ------------------------------------------------------------ |
| Designate `REL-A1` as relay in site config; reload      | `REL-A1`'s slice has `isRelay: true`; HTTP listener starts on declared port |
| Reload changing relay's `dataPlaneUrl`                  | Relay's listener stops on old port, starts on new port       |
| Reload with `isRelay: false` for previously-relay agent | Listener stops; cache files left in place                    |
| Reload with two relays for one segment                  | 400 validation error; old config still in effect             |

### 11.2 Slice and Failover

| Test                                                    | Pass condition                                        |
| ------------------------------------------------------- | ----------------------------------------------------- |
| Agent in seg-A with relay online                        | Slice's `dataSource.url == relay-a.local:8080`        |
| Agent in seg-A with relay offline                       | Slice's `dataSource.url == master.local:8080`         |
| Relay flips Online → Offline                            | Affected agents receive ConfigUpdate within 5 seconds |
| Relay flaps (Online → Offline → Online within 1 second) | Coalesced into 0 or 1 ConfigUpdate per affected agent |
| Agent in master's own segment                           | Slice's `dataSource.url == master.local:8080` always  |

### 11.3 Cascade Fill

| Test                                                         | Pass condition                                               |
| ------------------------------------------------------------ | ------------------------------------------------------------ |
| First agent in seg-A requests bundle                         | NAS sees 1 SMB read; master cache fills; relay cache fills; agent receives bytes |
| 20 agents in seg-A request same bundle simultaneously        | NAS sees 1 SMB read; relay cache fills once; 20 agents served from relay's cache |
| Two relays request same bundle from master simultaneously    | NAS sees 1 SMB read; master cache fills once; both relays receive bytes |
| Agent's request to relay arrives while relay's fill is in flight | Agent's request awaits fill; receives bytes after fill completes |

### 11.4 Proactive Warming

| Test                                                         | Pass condition                                               |
| ------------------------------------------------------------ | ------------------------------------------------------------ |
| Deploy to 20 agents in 1 non-master segment                  | CacheWarm intent sent to that segment's relay; relay cache fills; first agent's request hits warm cache |
| Deploy to 100 agents across 5 segments                       | 4 CacheWarm intents (one per non-master segment); all relays warm in parallel; first agent in each segment sees warm cache |
| Deploy where CacheWarm fails on one relay (e.g. master temporarily unreachable from relay) | Other segments' deploys continue; affected segment's agents fall back to master via slice failover |

### 11.5 Cache GC

| Test                                               | Pass condition                                               |
| -------------------------------------------------- | ------------------------------------------------------------ |
| Fill relay cache past `WatermarkPercent`           | LRU eviction runs; cache size returns to under target        |
| Eviction never evicts entry with `ActiveReads > 0` | While 10 agents are downloading bundle X, evicting X is skipped |
| `MinCacheAge` protects just-warmed entries         | Recently filled entries (under 5 min old) skipped in eviction |
| Trash retention                                    | Entries in `_trash/` older than `TrashRetentionHours` deleted |
| Recovery from `_trash/`                            | Operator manually moves a directory from `_trash/` back to `bundles/`; subsequent requests serve from it |

### 11.6 CAL Validation

| Test                                                         | Pass condition                |
| ------------------------------------------------------------ | ----------------------------- |
| `Get-SmbSession` on NAS during deploy                        | Exactly 1 session from master |
| `Get-SmbConnection` on sample of nodes                       | Empty result                  |
| `Get-SmbConnection` on each relay                            | Empty result                  |
| Kestrel access logs on relay show only intra-segment node IPs | Verified                      |

------

## 12. Implementation Sequence

1. **`IBundleSource` abstraction**. Extract Phase 4's NAS reader behind the new interface. Master uses `NasBundleSource`.
2. **`MasterBundleSource`** — HTTP GET from master, with byte-range resume internally. Tests using a stub master.
3. **Make `CacheCoordinator` source-agnostic**. Refactor to take `IBundleSource` in the constructor. Existing master tests still pass.
4. **Site-config validation additions** (§2.1). Tests for invalid topology.
5. **Slice computation with presence** (§2.2). Tests for online/offline cases, master-segment, master-co-located.
6. **Presence change → slice diff → ConfigUpdate**. Tests via simulated relay connect/disconnect.
7. **`RelayDataPlaneHost` hosted service** — slice-driven Kestrel start/stop. Tests by mutating slice in process.
8. **Cache endpoint on relay** — reuses master's `MapGet /content/...` shape with relay's coordinator. Tests with real HTTP.
9. **`CacheWarm` command and intent kind**. Tests for dispatch, ack, persistence.
10. **Master sends CacheWarm in `POST /api/deploy`** when intent resolves to non-master-segment agents. Tests verify CacheWarm dispatch alongside Stage dispatches.
11. **`CountingFileResult`** for active-read refcount during serves. Tests verify refcount increments/decrements correctly.
12. **LRU eviction in `CacheCoordinator`** with skip rules. Unit tests under tight watermark.
13. **Trash cleanup hosted service**. Test with old directories.
14. **`GET /api/cache/status` on relay**, identical to master's. Smoke test.
15. **Master's `GET /api/relays/status`** aggregator. Tests with mocked relay responses.
16. **Operator UI update** — per-segment relay panel.
17. **End-to-end Phase 5 acceptance tests** (§11).
18. **CAL validation procedure** (§8.2) executed by operator with documentation captured in runbook.

After step 18, Phase 5 is complete. Phase 6 (cooperative safe-window) builds on top without further topology changes.

------

## 13. Open Questions for Implementation

A few decisions to lock before coding:

- **Master cache GC enable flag in Phase 5**: I've put the code behind `Cache.GcEnabled` (default false on master, true on relays). Tell me if you'd rather enable it on master too in Phase 5 — same code, same behaviour, no real downside other than the master needing tuned `MaxCacheGB`. My default is "wait for Phase 11 and the dry-run API."
- **CacheWarm intent visibility**: I've made CacheWarm a first-class intent kind, visible in `/api/intents` and the operator UI alongside Deploy intents. Some operators may find the UI noisier this way; alternative is to make CacheWarm an internal master-side state not exposed as intents. My default is "expose it" — debugging beats tidiness.
- **`MinCacheAgeMinutes` default of 5**: protects against thrashing under tight watermarks. Set lower (1 minute) for very high churn; higher (15 min) for stable workloads. Reasonable default? Tell me.
- **Relay cache deduplication with `versions/`**: when a relay is using a bundle locally AND serving it, two copies on disk. I've left it as-is; tell me if you'd want a symlink-based dedup. My default is "accept the duplication, revisit if disk becomes painful."
- **Failover detection latency**: I've throttled presence-change-driven ConfigUpdate to a 2-second coalescing window. Faster (instant) means more churn during flapping; slower (10s) means longer time before agents migrate to master after a relay dies. 2s is a defensible default; flag if you'd prefer different.
- **CacheWarm cancellation**: an operator can `DELETE /api/intents/{warmIntentId}` to cancel a CacheWarm. The relay's cache fill is cancelled and the fill state goes to NotCached. Agents requesting the bundle from that relay will trigger a fresh fill. Confirm acceptable.

These are minor; flag preferences and the build can proceed.



# Phase 6 Detailed Design

## Cooperative Safe-Window Activation

*Companion to Architecture Design Document Revision 2, API Specification, Phases 1–5 Designs* *Targets `Phase 6` from §21 of the architecture doc* *C# / .NET 8 · ASP.NET Core · May 2026*

------

## 1. Phase Scope

### 1.1 What Phase 6 Adds On Top of Phase 5

Phase 5 finished the data-plane cascade: bundles transfer reliably from NAS through master through relays to agents, regardless of how big the fleet is. Phase 6 introduces *when* the activation actually happens — under app control, with the master arbitrating across multiple logical nodes that share an agent.

Deliverables:

1. **`POST /api/safe-window` endpoint** — the consuming app declares whether a given `logicalNodeId` is currently using a given bundle.
2. **AND-fan-in semantics on the master** — when N logical nodes share one agent, the agent's swap is gated on *all* of them reporting safe.
3. **`SignalSafeWindow` SignalR push (master → agent)** — fires the moment the AND becomes true while the agent is in `AwaitingSafeWindow` for that bundle.
4. **`AwaitingSafeWindow` agent state** — a real state in the per-bundle state machine, entered when the activation mode is `cooperative-hot-swap`.
5. **`GET /api/safe-window/pending`** — lets the app discover which bundles on a given logical node are currently waiting on a safe-window signal.
6. **Operator UI panel** showing per-(logicalNode, bundle) window flags and AND-eligibility per agent.

### 1.2 Explicitly Deferred

| Concern                                                     | Deferred to |
| ----------------------------------------------------------- | ----------- |
| Chunked huge-file transfer with parallel chunks             | Phase 7     |
| Recordings                                                  | Phase 8     |
| Two-phase commit, coordinated activation across many agents | Phase 9     |
| Fleet sync window scheduler                                 | Phase 10    |
| Garbage collection beyond Phase 5 relay-cache GC            | Phase 11    |

Phase 6 does **not** introduce coordinated multi-agent activation (that's Phase 9). The activation gate here is *per agent* (with multiple logical nodes), not *across* agents.

### 1.3 Definition of Done

After Phase 6 an operator can:

- Publish a bundle with `activation.mode: "cooperative-hot-swap"` and deploy it to agents
- See affected agents reach `AwaitingSafeWindow` rather than activating immediately
- Have the app post `windowOpen: true` for each logical node and watch the master fire `SignalSafeWindow` to the agent at the exact moment the AND becomes true
- Observe that an agent with two logical nodes (42, 43) won't swap until *both* report safe
- See the operator UI list the pending safe-window intents and identify which logical nodes are still holding back the AND
- Have an in-flight `AwaitingSafeWindow` intent survive both master and agent restarts

------

## 2. Cooperative Hot-Swap Flow

The full sequence for a bundle declared with `activation.mode: "cooperative-hot-swap"`:

```
                                                       ┌────────────────────────────────────────┐
                                                       │  preconditions                         │
                                                       │  • bundle X v43 published              │
                                                       │  • agent SIM-03 hosts logical nodes 42,43│
                                                       │  • manifest.activation.mode = coop      │
                                                       └────────────────────────────────────────┘

  Operator                Master                                Agent SIM-03                App (LN 42)    App (LN 43)
     │                       │                                       │                          │              │
     │ POST /api/deploy      │                                       │                          │              │
     │──────────────────────>│                                       │                          │              │
     │                       │ create Deploy intent                  │                          │              │
     │                       │ ReceiveCommand(Stage, X, v43) ───────>│                          │              │
     │                       │                                       │ Queued → Transferring →  │              │
     │                       │                                       │   Transferred → Verifying│              │
     │                       │                                       │   → Staged → ReadyToActivate            │
     │                       │ ReceiveCommand(Activate, X, v43) ────>│                          │              │
     │                       │                                       │ inspect manifest.mode    │              │
     │                       │                                       │   → cooperative-hot-swap │              │
     │                       │                                       │ ReadyToActivate →        │              │
     │                       │                                       │   AwaitingSafeWindow     │              │
     │                       │<── ReportStatus(X, AwaitingSafeWindow)│                          │              │
     │                       │ update master state                   │                          │              │
     │                       │   _awaiting[(SIM-03, X)] = true       │                          │              │
     │                       │ AND check                             │                          │              │
     │                       │   flags(42,X) = false (unreported)    │                          │              │
     │                       │   flags(43,X) = false (unreported)    │                          │              │
     │                       │   → AND = false; no signal sent yet   │                          │              │
     │                       │                                       │                          │              │
     │                       │                                       │ (app discovers pending via polling)     │
     │                       │                                       │                          │ GET /api/safe-window/pending?logicalNodeId=42
     │                       │<──────────────────────────────────────────────────────────────── │              │
     │                       │ response: pending = [{X, v43}]        │                          │              │
     │                       │                                       │                          │              │
     │                       │                                       │ (app finishes using X on LN 42)         │
     │                       │                                       │                          │              │
     │                       │ POST /api/safe-window                                            │              │
     │                       │   { LN: 42, bundle: X, windowOpen: true }                        │              │
     │                       │<───────────────────────────────────────────────────────────────  │              │
     │                       │ flags(42,X) = true                    │                          │              │
     │                       │ AND check: flags(43,X) still false    │                          │              │
     │                       │   → AND = false; respond with reason  │                          │              │
     │                       │ response: agentSwapEligible=false, reason="LN 43 has windowOpen=false"
     │                       │ ──────────────────────────────────────────────────────────────── │              │
     │                       │                                                                                 │
     │                       │ POST /api/safe-window                                                           │
     │                       │   { LN: 43, bundle: X, windowOpen: true }                                       │
     │                       │<──────────────────────────────────────────────────────────────────────────────  │
     │                       │ flags(43,X) = true                    │                                         │
     │                       │ AND check: both true → AND = true     │                                         │
     │                       │ SignalSafeWindow(X, true) ───────────>│                                         │
     │                       │ response: agentSwapEligible=true      │                                         │
     │                       │ ──────────────────────────────────────────────────────────────────────────────> │
     │                       │                                       │ AwaitingSafeWindow →     │              │
     │                       │                                       │   Activating             │              │
     │                       │                                       │ atomic junction repoint  │              │
     │                       │                                       │ Activating → Active      │              │
     │                       │<── ReportStatus(X, Active, v43)       │                          │              │
     │                       │ _awaiting[(SIM-03, X)] = false        │                          │              │
     │                       │ AckCommand(Activate, Complete)        │                          │              │
     │                       │ intent → Complete                     │                          │              │
     │                       │                                       │                          │              │
     │                       │ (next time apps want to swap a new version, they re-signal)      │              │
```

Key invariants in this flow:

- The `SignalSafeWindow` from master to agent is sent **at most once per activation**, the moment the AND becomes true.
- The agent only acts on `SignalSafeWindow(true)`. A subsequent `SignalSafeWindow(false)` after activation has begun is ignored — atomic swap is fast (junction repoint, milliseconds), and races between signal-true and agent action are accepted.
- After activation completes, the per-(logicalNode, bundle) window flags are **not reset**. The flags represent the app's *current* usage view, not a per-activation token.

------

## 3. Safe-Window Model

### 3.1 Level-Triggered State

The safe-window flag for `(logicalNodeId, bundleId)` is a level-triggered boolean reflecting the app's current view: "logical node N is **not** currently using bundle B, so a swap is safe right now."

- `true` ≈ safe to swap
- `false` ≈ in use, do not swap
- *(unreported)* ≈ treated as `false` for AND purposes

The app's job is to keep this flag accurate. The pattern is the same as a watchdog: the app continuously asserts its state. Master acts on the AND whenever conditions warrant.

App responsibilities (out-of-scope for the sync system, but worth noting):

| App event                                                    | Signal to send                                              |
| ------------------------------------------------------------ | ----------------------------------------------------------- |
| App starts up                                                | `windowOpen: true` for all bundles it isn't currently using |
| App begins using bundle B on LN N                            | `windowOpen: false` for `(N, B)`                            |
| App finishes using bundle B on LN N (between scenarios, end of session, idle) | `windowOpen: true` for `(N, B)`                             |
| Periodic heartbeat (optional but recommended)                | re-signal current state for all `(N, B)` pairs              |

The heartbeat matters for master restarts: master holds the flag map in memory only (see §3.3). After a master restart, the app's next heartbeat restores the map.

### 3.2 Identity Resolution Reminder

Per Phase 3, the app speaks `logicalNodeId`. Master resolves to `agentId` via the site-config mapping (`topology.agents[].logicalNodeIds`). All AND-checks run over the set of logical nodes mapped to a given agent.

If a `logicalNodeId` posts a safe-window flag but isn't in site config (no resolution), the master returns 404. The flag is not recorded.

### 3.3 Master State: Ephemeral

Master maintains two in-memory tables:

```csharp
// (logicalNodeId, bundleId) → bool
ConcurrentDictionary<(int, string), bool> _windowFlags;

// (agentId, bundleId) → bool: is the agent in AwaitingSafeWindow for this bundle?
ConcurrentDictionary<(string, string), bool> _agentAwaiting;
```

Neither is persisted in the master snapshot. Rationale:

- `_windowFlags` reflects the app's *current* view. After master restart, master cannot know what the app currently thinks — the app must re-assert. Persisting stale flags could mistakenly green-light a swap when the app's actual state has changed.
- `_agentAwaiting` is rebuilt from agent state reports on reconnect. Each agent reports its current per-bundle state including `AwaitingSafeWindow`; master populates the table.

The persisted `Intent` (which carries the `AwaitingSafeWindow` activation request) survives restart in the snapshot, so the operator-visible "what's pending" view doesn't lose its records. The ephemeral tables are just the runtime accelerators on top.

### 3.4 Activation Modes Recap

From the manifest's `activation.mode`:

| Mode                    | Used by                                                      | Safe-window? |
| ----------------------- | ------------------------------------------------------------ | ------------ |
| `atomic-directory-swap` | RuntimeAsset, Config, Dataset bundles where app tolerates abrupt swap | No           |
| `in-place`              | ChunkedHugeFile bundles where overwrite is OK                | No           |
| `cooperative-hot-swap`  | RuntimeAsset bundles where app needs an idle window          | **Yes**      |

Phase 6 makes `cooperative-hot-swap` real. The other two activation modes behave identically to Phase 4.

------

## 4. Master-Side Coordination

### 4.1 SafeWindowCoordinator

The new component on the master:

```csharp
public interface ISafeWindowCoordinator
{
    Task<SafeWindowResult> SetWindowAsync(int logicalNodeId, string bundleId, bool open, CancellationToken ct);
    Task NotifyAwaitingAsync(string agentId, string bundleId, CancellationToken ct);
    Task NotifyNotAwaitingAsync(string agentId, string bundleId, CancellationToken ct);
    SafeWindowSnapshot GetSnapshot();
}

public sealed record SafeWindowResult(
    string AgentId,
    bool AgentSwapEligible,
    string? AgentSwapEligibleReason,
    bool SignalSent);

public sealed record SafeWindowSnapshot(
    IReadOnlyDictionary<(int, string), bool> WindowFlags,
    IReadOnlyDictionary<(string, string), bool> AgentAwaiting);
```

### 4.2 Implementation

```csharp
public sealed class SafeWindowCoordinator : ISafeWindowCoordinator
{
    private readonly IIdentityResolver _identity;
    private readonly IHubContext<SyncHub> _hub;
    private readonly IFleetState _fleet;
    private readonly ConcurrentDictionary<(int, string), bool> _windowFlags = new();
    private readonly ConcurrentDictionary<(string, string), bool> _agentAwaiting = new();
    private readonly ILogger<SafeWindowCoordinator> _log;

    public async Task<SafeWindowResult> SetWindowAsync(int logicalNodeId, string bundleId, bool open, CancellationToken ct)
    {
        var agentId = _identity.ResolveAgentId(logicalNodeId)
            ?? throw new NotFoundException($"Unknown logicalNodeId {logicalNodeId}");

        _windowFlags[(logicalNodeId, bundleId)] = open;

        var (allOpen, blockingNodes) = ComputeAnd(agentId, bundleId);
        var signalSent = false;

        if (_agentAwaiting.GetValueOrDefault((agentId, bundleId), false) && allOpen)
        {
            await SendSignalAsync(agentId, bundleId, windowOpen: true, ct);
            signalSent = true;
            _log.LogInformation("SignalSafeWindow sent: {AgentId}/{BundleId}", agentId, bundleId);
        }

        var reason = allOpen ? null
            : $"Blocked by logicalNodes [{string.Join(',', blockingNodes)}] (windowOpen=false or unreported)";

        return new SafeWindowResult(agentId, allOpen, reason, signalSent);
    }

    public async Task NotifyAwaitingAsync(string agentId, string bundleId, CancellationToken ct)
    {
        _agentAwaiting[(agentId, bundleId)] = true;

        var (allOpen, _) = ComputeAnd(agentId, bundleId);
        if (allOpen)
        {
            await SendSignalAsync(agentId, bundleId, windowOpen: true, ct);
            _log.LogInformation("SignalSafeWindow sent on Awaiting: {AgentId}/{BundleId}", agentId, bundleId);
        }
    }

    public Task NotifyNotAwaitingAsync(string agentId, string bundleId, CancellationToken ct)
    {
        _agentAwaiting[(agentId, bundleId)] = false;
        return Task.CompletedTask;
    }

    private (bool AllOpen, List<int> Blocking) ComputeAnd(string agentId, string bundleId)
    {
        var logicalNodes = _identity.GetLogicalNodes(agentId);
        var blocking = new List<int>();

        foreach (var ln in logicalNodes)
        {
            if (!_windowFlags.GetValueOrDefault((ln, bundleId), false))
                blocking.Add(ln);
        }
        return (blocking.Count == 0, blocking);
    }

    private async Task SendSignalAsync(string agentId, string bundleId, bool windowOpen, CancellationToken ct)
    {
        var connId = _fleet.GetConnectionId(agentId);
        if (connId is null) return;            // agent offline; signal will be re-evaluated on Register

        var msg = new SignalSafeWindowMessage(bundleId, windowOpen);
        await _hub.Clients.Client(connId).SendAsync("SignalSafeWindow", msg, ct);
    }

    public SafeWindowSnapshot GetSnapshot() =>
        new(new ReadOnlyDictionary<(int, string), bool>(_windowFlags.ToDictionary(kv => kv.Key, kv => kv.Value)),
            new ReadOnlyDictionary<(string, string), bool>(_agentAwaiting.ToDictionary(kv => kv.Key, kv => kv.Value)));
}
```

### 4.3 Wiring Points

`SafeWindowCoordinator` is called from:

- **`POST /api/safe-window` endpoint** — calls `SetWindowAsync` on every request.
- **`SyncHub.ReportStatus`** — when the agent reports `BundleState.AwaitingSafeWindow`, calls `NotifyAwaitingAsync`. When the agent reports any other state, calls `NotifyNotAwaitingAsync`.
- **`SyncHub.OnConnectedAsync` (Register)** — after Register, the master iterates over the agent's `currentVersions[]` and calls Notify based on per-bundle state.

This last point matters for master restarts: the master rebuilds `_agentAwaiting` from agent state reports.

### 4.4 What Happens On Master Restart

```
Master restarts.
  _windowFlags = empty
  _agentAwaiting = empty
  Intents persist via snapshot (including any Activate intents in Executing state with
  agent state AwaitingSafeWindow).

Agents reconnect, call Register:
  Agent SIM-03's Register payload includes (X, AwaitingSafeWindow, v43).
  Master calls SafeWindowCoordinator.NotifyAwaitingAsync(SIM-03, X).
  _agentAwaiting now has (SIM-03, X) = true.
  ComputeAnd returns false because _windowFlags is empty.
  No signal sent yet.

App heartbeats:
  POST /api/safe-window for each (logicalNode, bundle) it's currently safe on.
  _windowFlags repopulates.
  When AND becomes true for any (agentId, bundleId) where _agentAwaiting is true,
  the signal fires.
```

The continuity hinges on the app's heartbeat. If the app *only* signals on state changes (edge-triggered), an app that has already signaled `true` before the master restart will leave the master in a state where the agent waits indefinitely. Hence the recommendation in §3.1: **periodic heartbeats are recommended**, not just edge-triggered signals.

------

## 5. AND Semantics — Edge Cases

### 5.1 Agent With No Logical Nodes

Some agents (master, relays) have empty `logicalNodeIds`. They never receive cooperative-hot-swap bundles in normal operation (the master and relay agents don't run the consuming app workload).

If for some reason an agent with zero logical nodes does receive a cooperative-hot-swap bundle, the AND over an empty set is *vacuously true*. The master sends `SignalSafeWindow(true)` immediately on `NotifyAwaitingAsync`. This is intentional: no logical nodes can object, so no gating is needed.

### 5.2 Single Logical Node

The common case. AND over `{42}` is just `_windowFlags[(42, X)]`. No surprise.

### 5.3 Multiple Logical Nodes — All Report

Agent has `[42, 43, 44]`. Each app process posts independently. Master collects three flags. AND fires only when all three are `true`.

### 5.4 Multiple Logical Nodes — One Never Reports

If logical node 44 never sends a `POST /api/safe-window`, the AND stays `false` forever. The intent ages, eventually crosses `staleAfter`, and an operator warning is written to the message queue.

The operator may:

- Investigate why LN 44's app isn't signaling (it might be down, in a long-running scenario, etc.)
- Cancel the activation intent (`DELETE /api/intents/{id}`) — agent moves back to `ReadyToActivate` for the next Activate command
- Adjust the deployment (e.g. remove LN 44 from the agent's logical nodes if it shouldn't have been there)

There is no auto-cancellation. The architecture's principle (no hard deadlines) holds.

### 5.5 Concurrent Safe-Window POSTs

Two apps post simultaneously for the same agent's logical nodes. The `ConcurrentDictionary` updates are atomic; AND is recomputed under each request. If both flags flip to `true` very close in time, both compute AND as `true`, both attempt to send the signal. The agent is idempotent — receiving two `SignalSafeWindow(true)` for the same bundle is a no-op on the second.

A more subtle race: between the first AND-true computation and the signal dispatch, the SignalR hub queues the message. If a second request arrives during this window and also fires, two messages reach the agent. The agent's state machine ignores the second (it has already started activating). No harm.

### 5.6 Bundle Has No Open Intent

If the app posts `{ LN: 42, bundle: X, windowOpen: true }` but no Activate intent for bundle X exists on any agent, the flag is recorded but no signal fires. Future activation will benefit from the pre-set flag. Idempotent.

### 5.7 What If Window Flips False After Signal Sent But Before Activation Completes?

App says `(42, X, true)`. Master sends signal. Agent starts activating. App now says `(42, X, false)`.

Master receives the new flag. Recomputes AND (now false). But `_agentAwaiting[(SIM-03, X)]` was already cleared (agent moved past `AwaitingSafeWindow`).

Master action: nothing. The signal was already sent and consumed. Activation is in flight and completes. After activation, the agent reports `Active`, and `_agentAwaiting[(SIM-03, X)] = false` is reaffirmed.

The race is accepted. The activation is atomic (junction repoint = milliseconds); the app's window between "I'm idle" and "I'm not idle" is much longer in practice, and the swap completes within the idle period.

### 5.8 Mode Mismatch

Two scenarios:

1. **Bundle declared `cooperative-hot-swap` but the app never signals.** The intent waits indefinitely (modulo staleness warning). This is the normal operating condition for a bundle that wasn't intended for this mode but got published with it accidentally. Operator cancels the intent.
2. **Bundle declared `atomic-directory-swap` but app \*does\* signal.** The signal is recorded in `_windowFlags`. It has no effect because the agent's activation path skips `AwaitingSafeWindow` for non-cooperative modes. The flag persists harmlessly.

------

## 6. Agent State Machine Updates

### 6.1 New State: `AwaitingSafeWindow`

The Phase 4 state machine's `Activating` step now branches by mode:

```
         ┌────────────────┐
         │ReadyToActivate │
         └────────┬───────┘
                  │ ReceiveCommand(Activate, X, v43)
                  │ inspect manifest.activation.mode
                  ▼
            ┌─────────┐
            │  mode?  │
            └────┬────┘
   cooperative-hot-swap │  atomic-directory-swap / in-place
                  ▼               ▼
   ┌──────────────────────┐  ┌──────────┐
   │  AwaitingSafeWindow  │  │Activating│
   └──────┬───────────────┘  └────┬─────┘
          │                       │
          │ SignalSafeWindow(true)│
          ▼                       ▼
       ┌──────────┐            ┌──────┐
       │Activating│ ─────────> │Active│
       └──────────┘            └──────┘
```

### 6.2 New Transition Triggers

| From                 | To                   | Trigger                                                      |
| -------------------- | -------------------- | ------------------------------------------------------------ |
| `ReadyToActivate`    | `AwaitingSafeWindow` | `ReceiveCommand(Activate)` + manifest mode == `cooperative-hot-swap` |
| `AwaitingSafeWindow` | `Activating`         | `SignalSafeWindow(bundleId, windowOpen=true)` received       |
| `AwaitingSafeWindow` | `ReadyToActivate`    | `ReceiveCommand(Cancel)` for the current Activate intent (operator-initiated) |
| `AwaitingSafeWindow` | unchanged            | `SignalSafeWindow(bundleId, windowOpen=false)` received (no-op, log only) |

### 6.3 Agent-Side Code Changes

`StateMachineRunner` gains:

```csharp
private async Task HandleActivateAsync(Command cmd, CancellationToken ct)
{
    var state = await _db.GetBundleStateAsync(cmd.BundleId!, ct);
    if (state.State != BundleState.ReadyToActivate)
    {
        await _hub.AckAsync(new CommandAck(cmd.CommandId, AckResult.Failed,
            $"Cannot activate from state {state.State}"));
        return;
    }

    var manifest = await ReadManifestAsync(cmd, ct);

    if (manifest.Activation.Mode == ActivationMode.CooperativeHotSwap)
    {
        await _db.SetBundleStateAsync(cmd.BundleId!, BundleState.AwaitingSafeWindow, cmd.Version, ct);
        await _hub.ReportStatusAsync(cmd.BundleId!, BundleState.AwaitingSafeWindow, cmd.Version);
        // Don't Ack yet. The intent stays Executing; we Ack when we actually activate.
        // Record the pending command so we can resume on a signal.
        await _db.RecordPendingActivateAsync(cmd.CommandId, cmd.BundleId!, cmd.Version!, ct);
        return;
    }

    // Phase 4 non-cooperative path
    await PerformActivationAsync(cmd, ct);
}

public async Task SignalSafeWindowAsync(string bundleId, bool windowOpen, CancellationToken ct)
{
    if (!windowOpen) return;  // we only act on true

    var pending = await _db.GetPendingActivateAsync(bundleId, ct);
    if (pending is null) return;  // not awaiting

    var cmd = pending.Command;
    await _db.SetBundleStateAsync(cmd.BundleId!, BundleState.Activating, cmd.Version, ct);
    await _hub.ReportStatusAsync(cmd.BundleId!, BundleState.Activating, cmd.Version);

    await PerformActivationAsync(cmd, ct);
}

private async Task PerformActivationAsync(Command cmd, CancellationToken ct)
{
    var result = await _activator.ActivateAsync(cmd.BundleId!, cmd.Version!, ct);

    if (result.Success)
    {
        await _db.SetBundleStateAsync(cmd.BundleId!, BundleState.Active, cmd.Version, ct);
        await _hub.ReportStatusAsync(cmd.BundleId!, BundleState.Active, cmd.Version);
        await _hub.AckAsync(new CommandAck(cmd.CommandId, AckResult.Complete, null));
    }
    else
    {
        await _db.SetBundleStateAsync(cmd.BundleId!, BundleState.ActivationFailed, cmd.Version, ct);
        await _hub.AckAsync(new CommandAck(cmd.CommandId, AckResult.Failed, result.ErrorDetail));
    }
    await _db.MarkCommandDoneAsync(cmd.CommandId, ct);
}
```

### 6.4 New SQLite Table

```sql
CREATE TABLE PendingActivate (
    BundleId   TEXT PRIMARY KEY,
    Version    TEXT NOT NULL,
    CommandId  TEXT NOT NULL,
    EnteredAt  TEXT NOT NULL
);
```

One row per bundle currently in `AwaitingSafeWindow`. Cleared when the agent transitions out (to `Activating` on signal, or to `ReadyToActivate` on cancel).

### 6.5 Agent Restart Behaviour

```
Agent restarts.
  Load agent.db.
  BundleState shows X = AwaitingSafeWindow.
  PendingActivate has row (X, v43, commandId-X).

Agent connects, calls Register:
  currentVersions[] includes (X, AwaitingSafeWindow, v43).
  Master receives Register; Notify SafeWindowCoordinator that SIM-03 is awaiting X.
  SafeWindowCoordinator re-evaluates AND, sends signal if appropriate.

If AND was already true and master had previously sent signal:
  The previous signal was lost (agent had crashed mid-signal).
  Re-evaluation triggers a new send.
  Agent receives, transitions to Activating, activates, reports Active.

If AND is false:
  Agent waits as before. App must continue signaling.
```

The agent's `SignalSafeWindow` handler is idempotent: receiving the signal when not in `AwaitingSafeWindow` is a no-op.

------

## 7. SignalR Hub Additions

### 7.1 New Method: `SignalSafeWindow` (Master → Agent)

```csharp
public sealed record SignalSafeWindowMessage(
    string BundleId,
    bool WindowOpen);
```

Agent registers handler:

```csharp
_conn.On<SignalSafeWindowMessage>("SignalSafeWindow", async msg =>
{
    await _runner.SignalSafeWindowAsync(msg.BundleId, msg.WindowOpen, CancellationToken.None);
});
```

Master sends via `IHubContext<SyncHub>.Clients.Client(connId).SendAsync("SignalSafeWindow", msg)`.

### 7.2 Updated `ReportStatus` Wiring

When `ReportStatus` arrives on the hub:

```csharp
public async Task ReportStatus(StatusReport report)
{
    var agentId = _fleet.GetAgentIdForConnection(Context.ConnectionId);
    if (agentId is null) return;

    _fleet.UpdateBundleStatus(agentId, report);

    // Phase 6 addition: hook into safe-window coordinator
    if (report.State == BundleState.AwaitingSafeWindow)
        await _safeWindow.NotifyAwaitingAsync(agentId, report.BundleId, Context.ConnectionAborted);
    else
        await _safeWindow.NotifyNotAwaitingAsync(agentId, report.BundleId, Context.ConnectionAborted);
}
```

### 7.3 Updated `Register` Handler

After Register processes replayed commands, it also primes the safe-window coordinator:

```csharp
public async Task<RegisterResponse> Register(RegisterRequest req)
{
    // ... Phase 5 register logic ...

    foreach (var iv in req.CurrentVersions)
    {
        if (iv.State == BundleState.AwaitingSafeWindow)
            await _safeWindow.NotifyAwaitingAsync(req.AgentId, iv.BundleId, Context.ConnectionAborted);
    }

    // ... return slice and replayed commands ...
}
```

------

## 8. REST Endpoints

### 8.1 `POST /api/safe-window`

Per the API spec §9.1. Phase 6 implements it.

Request:

```json
{
  "logicalNodeId": 42,
  "bundleId":      "TerrainTextures",
  "windowOpen":    true
}
```

Response (`200 OK`):

```json
{
  "logicalNodeId":            42,
  "bundleId":                 "TerrainTextures",
  "windowOpen":               true,
  "agentId":                  "SIM-03",
  "agentSwapEligible":        false,
  "agentSwapEligibleReason":  "Blocked by logicalNodes [43] (windowOpen=false or unreported)",
  "signalSent":               false
}
```

`signalSent` is a new field, useful for debugging. `true` indicates the master fired `SignalSafeWindow` in response to this call (vs. just recording state).

Errors:

- `404` — unknown `logicalNodeId`
- `400` — malformed body

### 8.2 `GET /api/safe-window/pending`

New endpoint for the app to discover pending bundle activations on its logical nodes.

Query parameters:

- `logicalNodeId` (required) — int

Response (`200 OK`):

```json
{
  "logicalNodeId":  42,
  "agentId":        "SIM-03",
  "pending": [
    {
      "bundleId":          "TerrainTextures",
      "version":           "v43",
      "manifestActivationMode": "cooperative-hot-swap",
      "enteredAwaitingAt": "2026-05-17T10:00:00Z",
      "stalenessSeconds":  234,
      "currentWindowFlags": {
        "42": true,
        "43": false
      },
      "agentSwapEligible": false
    }
  ]
}
```

`currentWindowFlags` shows all logical nodes mapped to this agent and their reported flags for this bundle. Lets the app see whether it's the one blocking the swap.

Errors:

- `404` — unknown `logicalNodeId`

### 8.3 `GET /api/safe-window/status`

New endpoint, primarily for the operator UI. Returns the full master-side coordinator state:

Response:

```json
{
  "asOf":  "2026-05-17T10:05:00Z",
  "agents": [
    {
      "agentId":        "SIM-03",
      "logicalNodeIds": [42, 43],
      "awaiting": [
        {
          "bundleId":          "TerrainTextures",
          "version":           "v43",
          "windowFlags":       { "42": true, "43": false },
          "agentSwapEligible": false,
          "enteredAwaitingAt": "2026-05-17T10:00:00Z"
        }
      ]
    }
  ]
}
```

Operator-facing; not designed for app polling. The per-(logicalNode) pending endpoint is the app's interface.

------

## 9. Operator UI Updates

The Phase 5 UI gains a "Safe-Window Activity" panel listing every (agent, bundle) currently in `AwaitingSafeWindow`:

```
Safe-Window Activity                                          [Updated 10:05:12]

Agent      Bundle             Version    Entered    Stale   Blocking LN
SIM-03     TerrainTextures    v43        10:00:00   5 min    43 (windowOpen=false)
SIM-07     AITables           v18        09:55:00  10 min    81, 82 (unreported)
```

"Stale" turns red after the bundle's `staleAfter` threshold. Operator can click an entry to see all logical-node flags.

The panel is fed by `GET /api/safe-window/status` polled at the same 5s interval as the rest of the UI.

------

## 10. Acceptance Tests

### 10.1 Single-Logical-Node Cases

| Test                                                         | Pass condition                                               |
| ------------------------------------------------------------ | ------------------------------------------------------------ |
| Agent has 1 logical node; app posts windowOpen=true after AwaitingSafeWindow | SignalSafeWindow fires immediately; agent activates          |
| App posts windowOpen=true *before* Activate command issued   | Flag recorded; no signal fires; when Activate later arrives, agent goes from ReadyToActivate to Activating directly via the AwaitingSafeWindow → signal path |
| App posts windowOpen=false then true                         | Only true triggers signal                                    |
| App posts windowOpen=true twice                              | Idempotent; signal fires once (per AwaitingSafeWindow entry) |
| App posts for unknown logicalNodeId                          | 404                                                          |
| App posts for unknown bundleId (not registered)              | 200 with flag recorded; no harm                              |
| Cooperative bundle deploys; app never signals                | Intent stays in AwaitingSafeWindow; staleness warning appears after configured threshold |

### 10.2 Multi-Logical-Node AND Cases

| Test                                                        | Pass condition                                               |
| ----------------------------------------------------------- | ------------------------------------------------------------ |
| Agent has 2 logical nodes; one app posts true               | response.agentSwapEligible=false; reason names the other LN; no signal fires |
| Both apps post true (close in time)                         | One signal fires after both flags set                        |
| Both apps post true; intent reaches AwaitingSafeWindow last | Signal fires on Register/NotifyAwaiting (master sees flags ready) |
| One app posts true, second posts false, third resumes true  | Signal fires only after final flag flip                      |
| Three logical nodes; only two of three apps report          | Intent stays in AwaitingSafeWindow                           |
| Agent with 0 logical nodes receives cooperative bundle      | AND vacuously true; signal fires immediately                 |

### 10.3 State Machine

| Test                                                      | Pass condition                                            |
| --------------------------------------------------------- | --------------------------------------------------------- |
| atomic-directory-swap bundle on cooperative-capable agent | Activates immediately; AwaitingSafeWindow never entered   |
| in-place bundle on cooperative-capable agent              | Activates immediately                                     |
| Cancel intent while in AwaitingSafeWindow                 | Agent returns to ReadyToActivate; intent marked Cancelled |
| Two cooperative bundles pending on same agent             | Each gates independently on its own bundle's flags        |

### 10.4 Restart Cases

| Test                                                         | Pass condition                                               |
| ------------------------------------------------------------ | ------------------------------------------------------------ |
| Agent restart while in AwaitingSafeWindow                    | After restart, Register reports state; master re-evaluates AND; if true, signal fires |
| Master restart while agent in AwaitingSafeWindow             | After master restart and agent reconnect, master needs app to re-signal (heartbeat); AND eventually resolves |
| Master restart with app heartbeating                         | App's heartbeat after restart restores _windowFlags; signal fires when AND becomes true |
| Signal lost (race: master sent, agent crashed before applying) | After agent restart, NotifyAwaiting fires again on Register, signal re-sent, activation proceeds |

### 10.5 Endpoints

| Test                                                    | Pass condition                                               |
| ------------------------------------------------------- | ------------------------------------------------------------ |
| GET /api/safe-window/pending for LN with no pending     | Empty pending array, 200                                     |
| GET /api/safe-window/pending for LN with one pending    | Returns the pending entry with current flags                 |
| GET /api/safe-window/status full snapshot               | Shows all agents in AwaitingSafeWindow                       |
| POST /api/safe-window stress: 100 concurrent flag flips | All requests succeed; no lost updates; final state consistent |

### 10.6 Stale Intent Behaviour

| Test                                                    | Pass condition                                               |
| ------------------------------------------------------- | ------------------------------------------------------------ |
| Intent in AwaitingSafeWindow for longer than staleAfter | Warning written to operator message queue; intent not cancelled |
| Operator dismisses warning, intent still pending        | Warning does not re-emit unless something material changes   |

------

## 11. Implementation Sequence

1. **`SafeWindowCoordinator` core** — in-memory flag and awaiting maps, AND computation, snapshot accessor. Unit tests for each method.
2. **`ISafeWindowCoordinator` DI wiring**; singleton on the master.
3. **`POST /api/safe-window` endpoint** wired to coordinator. HTTP-level tests.
4. **`GET /api/safe-window/pending` endpoint** — query existing intents joined with coordinator state.
5. **`GET /api/safe-window/status` endpoint** for operator UI.
6. **`SignalSafeWindowMessage` DTO** and SignalR send path on master. Test by mocking hub.
7. **Hub `Register` handler primes coordinator** from agent's reported state. Test with simulated agents.
8. **Hub `ReportStatus` handler updates coordinator** on AwaitingSafeWindow / non-AwaitingSafeWindow transitions.
9. **Agent: `PendingActivate` SQLite table** with DAL.
10. **Agent: `StateMachineRunner` cooperative-hot-swap branch** in HandleActivate. Tests for the three activation modes.
11. **Agent: `SignalSafeWindowAsync` handler** in runner; idempotent on no-pending-activate. Tests.
12. **Agent: SignalR client `On<SignalSafeWindowMessage>` registration**. End-to-end test against master.
13. **End-to-end single-logical-node cooperative deploy**. Acceptance test §10.1.
14. **Two-logical-node cases** (acceptance §10.2).
15. **Master and agent restart resilience tests** (§10.4).
16. **Operator UI panel** for Safe-Window Activity, polling `/api/safe-window/status`.
17. **Stale intent warning generation** — periodic task on master checks each AwaitingSafeWindow intent against its category's `staleAfter`; emits warning once per crossing.

After step 17, Phase 6 is complete. Phase 7 (chunked huge-file transfer) is independent and can start in parallel.

------

## 12. Open Questions for Implementation

A few small decisions:

- **`_windowFlags` persistence**: I chose ephemeral (cleared on master restart, re-populated by app heartbeats). Tell me if you'd prefer persisting in the snapshot so a master restart doesn't require app re-signaling. The trade-off is staleness risk vs. app burden. My default is ephemeral.
- **Periodic app heartbeats — what's the expected cadence?** Phase 6 doesn't specify this on the app side; it's the app's policy. For documentation, a reasonable default is "re-signal every 30 seconds for any flag currently `true`". Tell me if you'd want the master to write a recommendation into the operator runbook or even validate cadence (e.g. warn if a flag hasn't been refreshed in 5 minutes).
- **`SignalSafeWindow(false)` from master**: I've defined the master as never sending `false` — once `true` is sent, it's committed. The agent ignores any `false` it does receive. If you'd want `false` signals to be meaningful (e.g. for race-defended cancel paths), the agent's state machine would need to be able to transition back from `AwaitingSafeWindow` on signal. My default is one-way `true`; cancel uses the existing `Cancel` command path.
- **`agentSwapEligible` response field name**: the API spec already specified it. Phase 6 implements as documented. Tell me if you'd prefer a less verbose name (`canSwap`?). My default is keep what's in the spec.
- **Empty-logical-nodes vacuous AND**: I made the AND vacuously true for agents with no logical nodes. Alternative: refuse to deploy cooperative-hot-swap bundles to such agents. My default is "vacuous true" — robust to misconfiguration.
- **What happens when an intent is cancelled while AwaitingSafeWindow**: agent transitions to `ReadyToActivate`. Master coordinator clears `_agentAwaiting[(agentId, bundleId)]`. Future Activate commands for this bundle start fresh. Confirm this is the expected behaviour.

These are minor; flag preferences and the build can proceed.



# Phase 7 Detailed Design

## Chunked Huge-File Transfer

*Companion to Architecture Design Document Revision 2, API Specification, Phases 1–6 Designs* *Targets `Phase 7` from §21 of the architecture doc* *C# / .NET 8 · May 2026*

------

## 1. Phase Scope

### 1.1 What Phase 7 Adds On Top of Phase 6

Phase 4 introduced the `DirectHttp` engine for zip-container bundles, and noted that ChunkedHugeFile bundles could *function* with the same engine (single GET, byte-range resume) but would be slow and fragile for 0.5 TB single files. Phase 7 replaces that with the proper `ChunkedHugeFile` engine: download by chunk boundaries, verify each chunk against the manifest's recorded SHA-256, resume from the last verified chunk.

Deliverables:

1. **`ChunkedHugeFileEngine`** on the agent — selects automatically when `manifest.container == "none"`, downloads chunks in parallel, verifies each on the fly.
2. **Chunk state in agent SQLite** — `ChunkState` table tracks which chunks are verified per `(bundleId, version)`; survives restart.
3. **Resume from last verified chunk** — interruption (network drop, agent crash, master crash) leaves the partial file plus verified-chunk records; the next attempt downloads only the missing chunks.
4. **Parallel chunk downloads** — configurable concurrency (default 4), bounded to avoid saturating a slow inter-segment link.
5. **Final Merkle-root check** — after all chunks verify, the agent computes the bundle's `groupHash` from chunk hashes and compares against the manifest; a final integrity gate before moving to `Staged`.

### 1.2 Explicitly Deferred

| Concern                                             | Deferred to                               |
| --------------------------------------------------- | ----------------------------------------- |
| Recordings                                          | Phase 8                                   |
| Two-phase commit, coordinated activation            | Phase 9                                   |
| Fleet sync window scheduler                         | Phase 10                                  |
| Garbage collection                                  | Phase 11                                  |
| On-demand whole-file SHA-256 (Level 6 verification) | Operator tooling, not a phase deliverable |

Phase 7 does **not** change anything on the master or relay side. The master's pull cache fills a raw file from NAS as a single byte stream (Phase 4 behaviour) and serves byte-range requests from the cached file (Phase 4 behaviour). Per-chunk hashes are an agent-side construct entirely.

### 1.3 Definition of Done

After Phase 7 an operator can:

- Publish a 100 GB raw bundle (or larger — up to ~0.5 TB) and deploy to agents
- Watch each agent download chunks in parallel, with per-chunk progress visible
- Kill an agent's network in the middle of any chunk; verify the next attempt resumes from the last completed chunk (not the start)
- Kill the master mid-transfer; verify agents resume against the master after it comes back, with no chunk re-downloaded
- Corrupt a single byte in the master's cache after fill; verify that the affected chunk fails verification on the agent; agent retries (gets the same corrupted bytes); after retry limit, operator-message warning fires; operator purges the cache entry to force a fresh fill from NAS

------

## 2. Chunked Transfer Model

### 2.1 What the Manifest Says

Phase 1's raw-variant manifest already carries the chunk plan:

```json
{
  "bundleId":     "TerrainDatabase",
  "version":      "2026-05-17-001",
  "dataCategory": "ChunkedHugeFile",
  "container":    "none",
  "groupHash":    "sha256:e3d1...",        // Merkle root over chunk hashes
  "totalBytes":   549755813888,
  "fileCount":    1,
  "file": {
    "name": "dataset.zip",
    "size": 549755813888
  },
  "chunks": [
    { "index": 0,    "offset": 0,           "size": 67108864, "sha256": "sha256:1111..." },
    { "index": 1,    "offset": 67108864,    "size": 67108864, "sha256": "sha256:2222..." },
    /* ... 8191 entries total for a 0.5 TB file at 64 MB per chunk ... */
    { "index": 8191, "offset": 549688704512, "size": 67108864, "sha256": "sha256:zzzz..." }
  ],
  "activation": { "mode": "in-place", "targetPath": "Data/TerrainDatabase/dataset.zip" }
}
```

Properties guaranteed by the Phase 1 publish CLI and the manifest validator:

- Chunks are contiguous starting at offset 0
- Chunk indices are 0-based, sequential
- All chunks except possibly the last are equal-sized (the chunk size declared in the bundle definition)
- Sum of chunk sizes equals `file.size`

### 2.2 How the Agent Uses Chunks

The agent treats the chunk plan as its work list. For each chunk:

1. If `ChunkState` row exists with `Verified = 1` for `(bundleId, version, index)`, skip.
2. Otherwise:
   - HTTP GET with `Range: bytes={offset}-{offset+size-1}`
   - Stream bytes into the partial file at the correct offset
   - Compute SHA-256 of the stream
   - Compare against `manifest.chunks[i].sha256`
   - On match: write `ChunkState(... Verified=1)`, report progress
   - On mismatch: increment failure count, retry (up to a limit)

Multiple chunks proceed in parallel. Each runs in its own `Task`.

### 2.3 Why Per-Chunk Granularity

Resume from a per-byte offset (Phase 4's byte-range mechanism) works for zip-container bundles where validation happens after the whole file is in place. For a 0.5 TB raw file, post-hoc whole-file validation is impractical (it would take ~30 minutes of SHA-256 on commodity hardware) and offers no partial credit on failure.

Per-chunk granularity gives three properties no byte-only resume provides:

- **Partial credit**: 7,900 chunks already verified means you only redo the remaining 91
- **Bounded reprocessing on failure**: a single bad chunk doesn't poison the whole transfer
- **Streaming integrity**: corruption is detected within 64 MB of where it happened, not after 500 GB

------

## 3. Parallel Download

### 3.1 Concurrency Model

```
ChunkedHugeFileEngine.ExecuteAsync(...):
  1. Read manifest. Build work list = chunks where !Verified.
  2. Pre-allocate partial file at staging path with manifest.file.size.
     (NTFS: SetEndOfFile gives a sparse file; OS handles unwritten regions.)
  3. Open file with FileShare.ReadWrite (multiple writers).
  4. Drain the work list through a SemaphoreSlim of size MaxParallelChunks.
     For each chunk:
       a. Acquire semaphore slot.
       b. Spawn Task: DownloadAndVerifyChunkAsync(chunk).
  5. Wait for all tasks to complete or first failure to bubble.
  6. On all-chunks-verified: compute groupHash from manifest's chunk hashes
     (just concatenation + SHA-256, no file IO), compare to manifest.groupHash.
  7. Return TransferResult.
```

Each chunk task is self-contained:

- Opens the partial file with a per-task `FileStream` positioned at the chunk's offset
- Issues a `GET` with the chunk's `Range` header
- Streams bytes through both the file and a `SHA256` accumulator
- Closes the file stream
- On success, writes to `ChunkState`
- On failure, releases the semaphore and either retries or surfaces the error

### 3.2 Default Concurrency

| Network shape                      | Recommended `MaxParallelChunks`                              |
| ---------------------------------- | ------------------------------------------------------------ |
| Master segment, gigabit LAN        | 2–4 (single connection nearly saturates)                     |
| Cross-segment, 100 Mbps            | 2 (TCP fairness; multiple connections give modest improvement) |
| Cross-segment, slow / high-latency | 4–8 (helps fill the bandwidth-delay product)                 |

Default is 4 in `agent.json`. Operators tune per fleet.

### 3.3 Per-Connection Throttling

The agent's `HttpClient` is configured with `MaxConnectionsPerServer = MaxParallelChunks`. This prevents the agent from opening more TCP connections than chunks-in-flight allows.

If `dataSource.url` is the relay's URL and the relay has its own `MaxConcurrentReads` limit (Phase 5 didn't introduce one but could), the relay applies backpressure naturally via accepted connection limits. Phase 7 doesn't introduce a new limit.

### 3.4 File Handle Strategy

Two approaches considered:

**Approach A — one shared FileStream, locked writes:**

- Single FileStream opened, all chunk tasks share it
- Each task acquires a lock, seeks, writes, releases
- Drawback: serializes writes; loses parallelism benefit

**Approach B — per-task FileStream, multiple writers:**

- Each chunk task opens its own FileStream with `FileShare.ReadWrite`
- Each seeks to its chunk offset and writes
- OS handles concurrent writes at non-overlapping offsets
- This works on Windows for non-overlapping byte ranges

Phase 7 uses **Approach B**. Tested on NTFS in lab; behavior is correct for non-overlapping writes to a sparse file.

### 3.5 Pre-Allocation

```csharp
using var f = new FileStream(stagingPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
f.SetLength(manifest.File.Size);   // sparse on NTFS; physical blocks allocated on first write
```

`SetLength` is fast (metadata operation). NTFS allocates physical blocks only when each chunk is actually written. This avoids a noticeable startup pause for a fresh 0.5 TB file.

The pre-allocated file is the canonical partial. If the agent restarts, the file (with whatever chunks were written) is still there. The `ChunkState` table tells the agent which chunks survived.

------

## 4. Chunk Verification

### 4.1 Streaming Hash

While downloading a chunk:

```csharp
using var sha = SHA256.Create();
await using var src = await response.Content.ReadAsStreamAsync(ct);

// Open file at chunk offset
await using var dst = new FileStream(stagingPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
dst.Seek(chunk.Offset, SeekOrigin.Begin);

var buffer = new byte[1 << 20];
long bytesWritten = 0;
int read;
while ((read = await src.ReadAsync(buffer, ct)) > 0)
{
    if (bytesWritten + read > chunk.Size)
    {
        // Server returned more bytes than expected; treat as failure
        return ChunkResult.Failed("server returned excess bytes");
    }
    sha.TransformBlock(buffer, 0, read, null, 0);
    await dst.WriteAsync(buffer.AsMemory(0, read), ct);
    bytesWritten += read;
}
sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

if (bytesWritten != chunk.Size)
    return ChunkResult.Failed($"got {bytesWritten} bytes, expected {chunk.Size}");

var actual = HashFormat.Format(sha.Hash!);
if (actual != chunk.Sha256)
    return ChunkResult.Failed($"hash mismatch: got {actual}, expected {chunk.Sha256}");

return ChunkResult.Success;
```

No re-read pass. The hash is computed from the same buffer being written.

### 4.2 Chunk Retry Policy

| Attempt | Behavior                                              |
| ------- | ----------------------------------------------------- |
| 1       | Initial download                                      |
| 2       | After 5s delay                                        |
| 3       | After 30s delay                                       |
| 4+      | Stop. Mark transfer failed. Operator-message warning. |

Per-chunk failure count tracked in `ChunkState.FailureCount`. Resets to 0 when the chunk eventually verifies.

If three consecutive chunks fail (or any single chunk fails three times), the transfer aborts and reports `TransferFailed`. The state machine treats this normally (back to `Queued` for retry after backoff). On the next attempt, the verified-chunks list is preserved; only the failing chunks (with their failure counts retained) are re-attempted.

### 4.3 Final Merkle-Root Check

After all chunks report verified, the engine computes:

```csharp
private static string ComputeMerkleRoot(IReadOnlyList<ChunkEntry> chunks)
{
    using var sha = SHA256.Create();
    foreach (var c in chunks)
    {
        var bytes = HashFormat.HexToBytes(c.Sha256);   // 32 raw bytes
        sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
    }
    sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
    return HashFormat.Format(sha.Hash!);
}
```

This is the same calculation the publish CLI did in Phase 1. The result must match `manifest.groupHash`.

A mismatch here, given all per-chunk hashes already matched, would imply manifest corruption. Treat as a hard failure and surface to operator.

### 4.4 What If a Chunk's Hash Persistently Fails

Most likely cause: corrupted bytes in the master's pull cache. The agent's retry loop will keep getting the same wrong bytes.

Recovery sequence:

1. Agent retries 3 times, fails each.
2. Engine reports `TransferFailed` with detail "chunk N persistently failed: expected X, got Y".
3. State machine moves to `TransferFailed`, after retry attempts moves to terminal Failed.
4. Operator-message warning written: "Persistent chunk failure on agent SIM-03 for bundle B v42 chunk 47. Consider purging master cache and retrying."
5. Operator runs `POST /api/cache/evict?bundleId=B&version=v42` (Phase 11 endpoint; in Phase 7 the operator manually deletes `cache/bundles/B/v42/` on the master).
6. Operator clicks Retry on the failed intent.
7. Agent re-issues GET; master's coordinator sees `NotCached`, re-fills from NAS, serves fresh bytes.
8. Agent's chunk verification succeeds this time.

The `Phase 7 — Open Questions` section asks whether to add a `POST /api/cache/evict` endpoint now (small, useful) or wait for Phase 11.

------

## 5. State Machine Updates

### 5.1 Where the Chunked Engine Fits

The Phase 4 state machine's `Transferring → Transferred → Verifying → Staged` chain is unchanged. The chunked engine's per-chunk verification happens *during* `Transferring`. By the time the engine returns success, all chunks are already verified.

The agent's runner does only a residual check in `Verifying` for raw bundles: the Merkle-root comparison (cheap, in-memory). It doesn't re-read the file.

### 5.2 Engine Selection

```csharp
private ITransferEngine SelectEngine(BundleManifest manifest)
{
    return manifest.Container switch
    {
        ContainerKind.Zip  => _directHttpEngine,
        ContainerKind.None => _chunkedHugeFileEngine,
        _                  => throw new NotSupportedException($"Unknown container: {manifest.Container}")
    };
}
```

Selection at the start of each `Stage` command. No other state machine code changes.

### 5.3 No New States

`Transferring`, `Transferred`, `Verifying`, `Staged` semantics are unchanged. Internally the chunked engine does more work, but the visible state vocabulary is identical to Phase 4.

The state machine's `ReportStatus(Transferring, progressPct)` carries chunk-aggregated progress: `progressPct = verifiedChunks * 100 / totalChunks`.

------

## 6. Local Storage and SQLite

### 6.1 New Table

```sql
CREATE TABLE ChunkState (
    BundleId       TEXT    NOT NULL,
    Version        TEXT    NOT NULL,
    ChunkIndex     INTEGER NOT NULL,
    Verified       INTEGER NOT NULL DEFAULT 0,    -- 0 or 1
    LastAttemptAt  TEXT,
    FailureCount   INTEGER NOT NULL DEFAULT 0,
    BytesAttempted INTEGER NOT NULL DEFAULT 0,    -- diagnostics; size successfully written before failure
    PRIMARY KEY (BundleId, Version, ChunkIndex)
);

CREATE INDEX IX_ChunkState_Bundle ON ChunkState(BundleId, Version);
```

The engine's first action on starting a transfer is a single SELECT to load all rows for `(bundleId, version)` into memory. With 8,000 chunks per bundle, the result set is ~600 KB and loads in milliseconds.

Updates happen as each chunk completes — single-row UPDATE/INSERT, no batching needed at this scale.

### 6.2 Local File Layout

```
{DataRoot}/
  staging/
    {bundleId}/
      {version}/
        {filename}            <-- pre-allocated, partial during transfer
  versions/
    {bundleId}/
      {version}/
        manifest.json
        {filename}            <-- moved from staging after Staged
```

For raw bundles, there is no `extracted/` directory (no extraction step).

After `Verifying → Staged`, the agent moves the file from staging to `versions/`:

```csharp
File.Move(
    Path.Combine(stagingDir, manifest.File!.Name),
    Path.Combine(versionDir, manifest.File!.Name));
```

`ChunkState` rows are kept for the version (useful for future verification and forensics; small footprint per row). Cleanup is part of Phase 11 GC.

### 6.3 Restart Recovery

On agent restart with a transfer in flight:

1. Open `agent.db`.
2. `BundleState` shows `(bundleId, Transferring, targetVersion)`.
3. The engine is invoked on the next Stage replay (`Register` → `ReplayedCommands`).
4. Engine loads `ChunkState` rows for this `(bundleId, version)`.
5. Engine builds work list = chunks where `Verified = 0` OR no row exists.
6. Engine resumes.

Worst-case lost progress on hard crash: any chunks in flight at crash time. With 64 MB chunks, that's bounded.

### 6.4 Master / Relay Side: No Change

The master and relay caches operate exactly as in Phases 4–5. They serve the raw file via byte-range; they don't know or care about chunk boundaries. The cache is filled once per `(bundleId, version)` and serves arbitrary `Range` headers from the cached file.

This is a critical separation. Per-chunk integrity is the agent's responsibility. The transport layer (master/relay HTTP) is content-agnostic.

------

## 7. Code Sketches

### 7.1 The Engine

```csharp
public sealed class ChunkedHugeFileEngine : ITransferEngine
{
    public string MethodId => "ChunkedHugeFile";

    private readonly HttpClient _http;
    private readonly IChunkStateRepository _chunkState;
    private readonly ChunkedHugeFileOptions _options;
    private readonly ILogger<ChunkedHugeFileEngine> _log;

    public async Task<TransferResult> ExecuteAsync(
        TransferJob job,
        BundleManifest manifest,
        IProgress<TransferProgress> progress,
        CancellationToken ct)
    {
        if (manifest.Container != ContainerKind.None || manifest.Chunks is null)
            return TransferResult.Failure("Engine requires raw container with chunks");

        var stagingPath = Path.Combine(job.StagingPath, manifest.File!.Name);
        Directory.CreateDirectory(Path.GetDirectoryName(stagingPath)!);

        // Pre-allocate
        using (var f = new FileStream(stagingPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
        {
            if (f.Length != manifest.File.Size)
                f.SetLength(manifest.File.Size);
        }

        // Load chunk state
        var verified = await _chunkState.GetVerifiedSetAsync(job.BundleId, job.Version, ct);
        var workList = manifest.Chunks!.Where(c => !verified.Contains(c.Index)).ToList();

        _log.LogInformation("Resuming chunked transfer: {Verified}/{Total} chunks already verified, downloading {Pending}",
            verified.Count, manifest.Chunks.Count, workList.Count);

        // Drain work list with bounded concurrency
        using var sem = new SemaphoreSlim(_options.MaxParallelChunks);
        var totalChunks = manifest.Chunks.Count;
        var completedChunks = verified.Count;

        var tasks = workList.Select(async chunk =>
        {
            await sem.WaitAsync(ct);
            try
            {
                var result = await DownloadAndVerifyChunkAsync(job, manifest, stagingPath, chunk, ct);
                if (!result.Success)
                    throw new ChunkFailedException(chunk.Index, result.ErrorDetail!);

                await _chunkState.MarkVerifiedAsync(job.BundleId, job.Version, chunk.Index, ct);

                var done = Interlocked.Increment(ref completedChunks);
                progress.Report(new TransferProgress(
                    bytesDownloaded: (long)done * (manifest.Chunks!.Count == 1 ? manifest.File.Size : manifest.Chunks![0].Size),
                    bytesTotal: manifest.File.Size,
                    percentComplete: 100 * done / totalChunks));
            }
            finally { sem.Release(); }
        }).ToList();

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (ChunkFailedException ex)
        {
            return TransferResult.Failure($"Chunk {ex.ChunkIndex} failed: {ex.Reason}");
        }

        // Final Merkle root check
        var computedRoot = ComputeMerkleRoot(manifest.Chunks);
        if (computedRoot != manifest.GroupHash)
            return TransferResult.Failure($"groupHash mismatch: computed {computedRoot}, expected {manifest.GroupHash}");

        return TransferResult.Success(manifest.File.Size);
    }

    private async Task<ChunkResult> DownloadAndVerifyChunkAsync(
        TransferJob job, BundleManifest manifest, string stagingPath, ChunkEntry chunk, CancellationToken ct)
    {
        var maxAttempts = _options.MaxChunkAttempts;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var result = await TryDownloadAndVerifyAsync(job, chunk, stagingPath, ct);
                if (result.Success) return result;

                _log.LogWarning("Chunk {Idx} attempt {Attempt}/{Max} failed: {Reason}",
                    chunk.Index, attempt, maxAttempts, result.ErrorDetail);

                await _chunkState.IncrementFailureAsync(job.BundleId, job.Version, chunk.Index, ct);
                if (attempt < maxAttempts) await Task.Delay(BackoffFor(attempt), ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _log.LogWarning(ex, "Chunk {Idx} attempt {Attempt} exception", chunk.Index, attempt);
                if (attempt < maxAttempts) await Task.Delay(BackoffFor(attempt), ct);
            }
        }
        return ChunkResult.Failed($"exhausted {maxAttempts} attempts");
    }

    private async Task<ChunkResult> TryDownloadAndVerifyAsync(
        TransferJob job, ChunkEntry chunk, string stagingPath, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{job.SourceBaseUrl}/content/bundles/{job.BundleId}/{job.Version}/{job.FileName}");
        request.Headers.Range = new RangeHeaderValue(chunk.Offset, chunk.Offset + chunk.Size - 1);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode != HttpStatusCode.PartialContent)
            return ChunkResult.Failed($"expected 206, got {(int)response.StatusCode}");

        using var sha = SHA256.Create();
        await using var src = await response.Content.ReadAsStreamAsync(ct);
        await using var dst = new FileStream(stagingPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        dst.Seek(chunk.Offset, SeekOrigin.Begin);

        var buffer = new byte[_options.BufferSizeBytes];
        long bytesWritten = 0;
        int read;
        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            if (bytesWritten + read > chunk.Size)
                return ChunkResult.Failed("server returned excess bytes");
            sha.TransformBlock(buffer, 0, read, null, 0);
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            bytesWritten += read;
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

        if (bytesWritten != chunk.Size)
            return ChunkResult.Failed($"got {bytesWritten} bytes, expected {chunk.Size}");

        var actual = HashFormat.Format(sha.Hash!);
        if (actual != chunk.Sha256)
            return ChunkResult.Failed($"hash mismatch: got {actual}, expected {chunk.Sha256}");

        return ChunkResult.Success;
    }

    private static TimeSpan BackoffFor(int attempt) => attempt switch
    {
        1 => TimeSpan.FromSeconds(5),
        2 => TimeSpan.FromSeconds(30),
        _ => TimeSpan.FromMinutes(2)
    };

    private static string ComputeMerkleRoot(IReadOnlyList<ChunkEntry> chunks)
    {
        using var sha = SHA256.Create();
        foreach (var c in chunks)
        {
            var raw = HashFormat.HexToBytes(c.Sha256);
            sha.TransformBlock(raw, 0, raw.Length, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return HashFormat.Format(sha.Hash!);
    }
}

public sealed record ChunkResult(bool Success, string? ErrorDetail)
{
    public static ChunkResult Success { get; } = new(true, null);
    public static ChunkResult Failed(string reason) => new(false, reason);
}

public sealed class ChunkFailedException : Exception
{
    public int ChunkIndex { get; }
    public string Reason { get; }
    public ChunkFailedException(int idx, string reason) : base($"Chunk {idx}: {reason}")
    {
        ChunkIndex = idx;
        Reason = reason;
    }
}
```

### 7.2 Chunk State Repository

```csharp
public interface IChunkStateRepository
{
    Task<HashSet<int>> GetVerifiedSetAsync(string bundleId, string version, CancellationToken ct);
    Task MarkVerifiedAsync(string bundleId, string version, int chunkIndex, CancellationToken ct);
    Task IncrementFailureAsync(string bundleId, string version, int chunkIndex, CancellationToken ct);
    Task ClearAsync(string bundleId, string version, CancellationToken ct);    // called when transfer completes
}

public sealed class ChunkStateRepository : IChunkStateRepository
{
    private readonly IDbConnectionFactory _factory;

    public async Task<HashSet<int>> GetVerifiedSetAsync(string bundleId, string version, CancellationToken ct)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<int>(@"
            SELECT ChunkIndex FROM ChunkState
            WHERE BundleId = @b AND Version = @v AND Verified = 1",
            new { b = bundleId, v = version });
        return rows.ToHashSet();
    }

    public async Task MarkVerifiedAsync(string bundleId, string version, int chunkIndex, CancellationToken ct)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(@"
            INSERT INTO ChunkState (BundleId, Version, ChunkIndex, Verified, LastAttemptAt, FailureCount)
            VALUES (@b, @v, @i, 1, @now, 0)
            ON CONFLICT(BundleId, Version, ChunkIndex)
              DO UPDATE SET Verified = 1, LastAttemptAt = @now",
            new { b = bundleId, v = version, i = chunkIndex, now = DateTimeOffset.UtcNow.ToString("O") });
    }

    // ... IncrementFailureAsync, ClearAsync similarly
}
```

### 7.3 State Machine Wiring

```csharp
private async Task HandleStageAsync(Command cmd, CancellationToken ct)
{
    // ... shared transitions through Queued ...

    var manifest = await ReadManifestAsync(cmd, ct);
    var engine = SelectEngine(manifest);

    await _db.SetBundleStateAsync(cmd.BundleId!, BundleState.Transferring, cmd.Version, ct);

    var progress = new Progress<TransferProgress>(p =>
        _ = _hub.ReportStatusAsync(cmd.BundleId!, BundleState.Transferring, cmd.Version, p.PercentComplete));

    var stagingDir = _paths.StagingDir(cmd.BundleId!, cmd.Version!);
    var job = new TransferJob(
        JobId: Guid.NewGuid().ToString(),
        CommandId: cmd.CommandId,
        BundleId: cmd.BundleId!,
        Version: cmd.Version!,
        SourceBaseUrl: _slice.Current.DataSource.Url,
        FileName: manifest.Container == ContainerKind.Zip ? manifest.Archive!.Name : manifest.File!.Name,
        StagingPath: stagingDir);

    var result = await engine.ExecuteAsync(job, manifest, progress, ct);

    if (!result.Success)
    {
        await _db.SetBundleStateAsync(cmd.BundleId!, BundleState.TransferFailed, cmd.Version, ct);
        await _hub.AckAsync(new CommandAck(cmd.CommandId, AckResult.Failed, result.ErrorDetail));
        return;
    }

    // ... continue to Transferred / Verifying / Staged as before ...
}
```

`SelectEngine` returns the chunked engine for raw bundles, the DirectHttp engine for zip bundles. The state machine itself is engine-agnostic.

### 7.4 Verifying for Raw Bundles

For zip bundles, `Verifying` extracts and validates per-file hashes (Phase 4). For raw bundles, the work is much smaller — chunks are already verified during transfer. The runner does the final Merkle root check:

```csharp
private async Task HandleVerifyAsync(BundleManifest manifest, string stagingPath, CancellationToken ct)
{
    if (manifest.Container == ContainerKind.Zip)
        return await _extractVerifier.ExtractAndVerifyAsync(...);

    // Raw: chunks already verified; Merkle root checked inside engine.
    // Optionally: verify the manifest's chunks match the actual file size on disk.
    var info = new FileInfo(Path.Combine(stagingPath, manifest.File!.Name));
    if (info.Length != manifest.File.Size)
        return VerificationResult.Failed($"size mismatch: file {info.Length}, manifest {manifest.File.Size}");

    return VerificationResult.Success;
}
```

------

## 8. Configuration

### 8.1 Agent `agent.json`

```json
{
  "AgentId":    "SIM-03",
  "MasterUrl":  "http://master.local:8080",
  "DataRoot":   "C:/ProgramData/SyncAgent",
  "AppDataRoot": "C:/AppData",
  "Transfer": {
    "BufferSizeBytes":      1048576,
    "RequestTimeoutMinutes": 60,
    "MaxRetries":            3
  },
  "ChunkedHugeFile": {
    "MaxParallelChunks":    4,
    "MaxChunkAttempts":     3,
    "BufferSizeBytes":      1048576
  }
}
```

Defaults are sensible. `MaxParallelChunks: 4` matches `HttpClient.MaxConnectionsPerServer = 4` (Phase 4 config).

### 8.2 No New Master Configuration

The master's existing `Cache:` settings apply unchanged. Raw bundles flow through the cache exactly like zip bundles — the master never opens the file to inspect chunks.

------

## 9. Acceptance Tests

### 9.1 Happy Path

| Test                                                       | Pass condition                                               |
| ---------------------------------------------------------- | ------------------------------------------------------------ |
| 1 GB raw bundle, single agent, master in same segment      | All 16 chunks verified; file at versions/.../dataset.zip; state reaches Active |
| 100 GB raw bundle, 10 agents                               | All 10 reach Active; NAS sees 1 SMB read; relay sees 1 master pull (Phase 5 cascade still works) |
| 0.5 TB raw bundle, single agent                            | All ~8000 chunks verified over time; reaches Active          |
| Smallest case: 1 chunk file (file smaller than chunk size) | Works; single Range request; verified                        |

### 9.2 Resume Cases

| Test                                               | Pass condition                                               |
| -------------------------------------------------- | ------------------------------------------------------------ |
| Kill agent network mid-transfer, restore after 30s | Resume from last verified chunk; no chunk re-downloaded; transfer completes |
| Hard-kill agent process at 50% complete            | After restart, ChunkState shows ~50% verified; resume downloads only the rest |
| Master restart mid-transfer                        | Agent's GETs fail; engine retries; succeeds after master recovery; no chunk re-downloaded |
| Relay restart mid-transfer (Phase 5 failover)      | Agent's slice flips to master; GETs to master succeed; resume continues |
| Agent restart mid-transfer with relay also offline | Both unavailable; transfer fails; agent waits; on relay recovery, slice flips back, resume continues |

### 9.3 Verification Cases

| Test                                                         | Pass condition                                               |
| ------------------------------------------------------------ | ------------------------------------------------------------ |
| Corrupt single byte in master cache after fill, force agent re-download | Affected chunk fails; after 3 retries, transfer fails with operator warning |
| Operator deletes master cache entry; retries intent          | Master refills from NAS; agent's retry succeeds              |
| Manifest hash mismatch (publish CLI bug simulation)          | Caught at Merkle-root check; transfer reports specific error |
| ChunkEntry size mismatch with server response                | Chunk attempt reports excess/insufficient bytes; retries; fails after attempts |

### 9.4 Concurrency

| Test                                                   | Pass condition                                               |
| ------------------------------------------------------ | ------------------------------------------------------------ |
| MaxParallelChunks = 1 (sequential)                     | Works, slower; no race issues                                |
| MaxParallelChunks = 8                                  | Faster on slow links; no file corruption; all chunks at correct offsets |
| 10 agents in same segment each running parallel chunks | Relay handles aggregate connections; CPU and network use proportional |
| MaxParallelChunks = 32 (intentionally high)            | Works; HttpClient connection pool caps somewhere; performance plateau |

### 9.5 Edge Cases

| Test                                                       | Pass condition                                               |
| ---------------------------------------------------------- | ------------------------------------------------------------ |
| ChunkState rows pre-exist (e.g. from prior version)        | Filtered correctly by (bundleId, version); no false skip     |
| File size 0 (rejected at manifest validation in Phase 1)   | Never reaches engine                                         |
| Manifest chunks not contiguous (synthesized test)          | Engine validates; refuses; reports error                     |
| File deleted from staging between agent restart and resume | Engine detects file missing; re-creates with SetLength; restarts download |

------

## 10. Implementation Sequence

1. **`IChunkStateRepository` + SQLite DAL.** Unit tests for CRUD.
2. **Migration: add `ChunkState` table** to existing agent.db. Tests verify migration on existing DB.
3. **`ChunkResult`, `ChunkFailedException`, helper records.**
4. **Per-chunk download function** (single `TryDownloadAndVerifyAsync`). Unit test with a stub HTTP server serving a known file.
5. **Sequential variant of engine** (`MaxParallelChunks = 1`) — confirms semantics. End-to-end test with a real 100 MB file.
6. **Parallel variant** with `SemaphoreSlim`. Tests for race-free writes at non-overlapping offsets.
7. **Merkle root verification** after all chunks. Unit test with intentionally bad manifest.
8. **Engine selection in state machine** based on `manifest.container`. Tests verify zip vs raw routing.
9. **Resume on agent restart.** Integration test: kill process mid-transfer, restart, observe resume from `ChunkState`.
10. **Resume across network failure.** Integration test using a proxy that severs the connection.
11. **Master/relay-agnostic transport.** Verify chunked engine works correctly whether `dataSource.url` is master or relay (Phase 5 cascade).
12. **Verification failures** — corrupt master cache; observe chunk failure / retry / terminal failure flow.
13. **Whole bundle lifecycle test** — publish 0.5 TB bundle, deploy, verify Active state, observe via operator UI.
14. **Cleanup** — Phase 11 will GC `ChunkState` rows; in Phase 7, rows persist indefinitely. Acceptable for now.

After step 13, Phase 7 is complete. Phase 8 (recordings) is the next thread and is independent of this engine.

------

## 11. Open Questions for Implementation

A few decisions:

- **`POST /api/cache/evict?bundleId=X&version=Y` endpoint**: needed for operator recovery when master cache is corrupted (§4.4). It's tiny — a handful of lines on the master. I'd suggest adding now even though it's nominally a Phase 11 concern. Confirm.
- **`ChunkState` retention**: rows persist indefinitely after transfer completes. With 8000 rows per 0.5 TB bundle and tens of bundles over months, the table can grow to a few MB. Manageable. Phase 11 GC can prune rows for evicted versions. Confirm OK.
- **`MaxParallelChunks` default of 4**: chosen for the typical case. Tell me if you'd prefer a different default — 2 for the conservative case, 8 for aggressive saturation of bandwidth-delay product.
- **Per-chunk hash on master cache fill**: I deliberately omitted this (§4.4 rationale). Master pulls raw file as-is from NAS, trusts NAS integrity, lets agent catch corruption per-chunk. Alternative would be to compute per-chunk hashes during master fill and refuse to cache a chunk that doesn't match. More work on master side; catches corruption earlier; deferred for now. Confirm preferred direction.
- **Concurrent chunk write strategy** (Approach B from §3.4): multiple FileStreams writing non-overlapping ranges to the same file. Works on NTFS. If you're targeting other filesystems (ReFS, network shares mounted at staging dir), this assumption may need revisiting. My default: NTFS local disk; document the constraint.
- **`maxChunkAttempts` of 3**: matches `DirectHttp` retries. Same trade-off (give up vs. keep trying). Set higher (5–10) for transient-failure-heavy environments. Confirm.

These are minor; flag preferences and the build can proceed.



# Phase 8 Detailed Design

## Recordings Upload, Session Lifecycle, Replay

*Companion to Architecture Design Document Revision 2, API Specification, Phases 1–7 Designs* *Targets `Phase 8` from §21 of the architecture doc* *C# / .NET 8 · ASP.NET Core · May 2026*

------

## 1. Phase Scope

### 1.1 What Phase 8 Adds

Recordings are the only flow in the system that travels *upward* — from nodes through the master to the NAS. The shape mirrors the downward flow's principles (master is the only SMB client; everything else is HTTP) but inverted: agents PUT chunks to the master, master assembles and writes to NAS, NAS stores the zip canonical.

Deliverables:

1. **Per-node upload** — app declares files; agent stages a temporary local zip; agent chunk-uploads to the master.
2. **Master receive and persist** — master accepts chunked PUTs, stages on NAS, assembles, atomically moves to final location.
3. **Within-file chunked upload with resume** — 64 MB chunks, per-chunk SHA-256, resume from last successful chunk on either agent or master restart.
4. **Session lifecycle on master** — auto-created on first `POST /api/recordings`; tracks per-node upload status; finalized via `POST /api/sessions/{id}/finalize`, which writes the `_session.json` marker on NAS.
5. **Session listing and deletion** — `GET /api/sessions`, `GET /api/sessions/{id}`, `DELETE /api/sessions/{id}`. Deletion broadcasts `EvictSession` to all agents.
6. **Replay flow** — `POST /api/recordings/replay` instructs an agent to fetch a session's zip from the master and extract it locally.
7. **Node-local LRU GC** of extracted recordings — disk-watermark-driven eviction of older sessions on the agent.

### 1.2 Explicitly Deferred

| Concern                                        | Deferred to                                     |
| ---------------------------------------------- | ----------------------------------------------- |
| Two-phase commit, coordinated activation       | Phase 9                                         |
| Fleet sync window scheduler                    | Phase 10                                        |
| Master pull-cache GC, NAS GC, dry-run API      | Phase 11                                        |
| Relay-mediated uploads (node → relay → master) | Not planned — uploads go node → master directly |
| Auto-retry of stale `RecordingUpload` intents  | Operator-driven retry only                      |

Per the architecture: **uploads do not use relays.** Each node uploads directly to the master, accepting that an inter-segment link carries each contribution once per uploading node. The aggregation savings that relays provide for downloads (one inter-segment crossing per bundle) don't apply to recordings, because each node's contribution is unique.

If CAL pressure on the master from concurrent uploads becomes a concern, the operator throttles via the existing `Cache.MaxConcurrentFills` semantics (Phase 4) — same pattern, different direction.

### 1.3 Definition of Done

After Phase 8 an operator can:

- See the simulation app finalize a session on 10 nodes; all 10 agents upload their zipped recordings to the master; master writes all 10 zips to the NAS under `/NAS/Recordings/{sessionId}/`
- Watch the upload progress per node in the operator UI
- Kill the master mid-upload; verify agents pause, retry, resume from last-completed chunk after master recovery
- Kill an agent mid-zip-creation; verify the agent restarts the zip from scratch on next attempt (acceptable — zipping is fast)
- Kill an agent mid-upload; verify it resumes from the last successful chunk after restart
- Call `POST /api/sessions/{sessionId}/finalize` with all nodes complete → see `_session.json` appear on NAS with `status: "complete"`
- Call finalize with one node still pending → see 409 with the missing-node list
- Call finalize with `?force=true` and one node missing → see `status: "partial"` and the missing node listed
- Call `DELETE /api/sessions/{sessionId}` → see NAS folder removed, all agents evict their extracted copies
- Call `POST /api/recordings/replay` for a session on a node that doesn't have it extracted → see the agent download from master, extract, and become ready for the app to read
- Fill a node's local recordings disk past the watermark → see LRU eviction free space without touching the canonical NAS copies

------

## 2. End-to-End Flow

### 2.1 Upload Sequence

```
  App (LN 42)         Master                       Agent SIM-03                  NAS

  POST /api/recordings
    { sessionId: 5b2f, LN: 42, files: [...] }
  ─────────────────>
                      Create RecordingUpload intent
                      Get-or-create session 5b2f
                      Add 42 to expectedNodes
                      Persist snapshot
                      ReceiveCommand(UploadRecording, sid=5b2f, LN=42, files) ─>
  202 Accepted { intentId }
  <─────────────────                                Read manifest of declared files
                                                    Validate paths exist
                                                    Create temp local zip
                                                      C:/.../staging/upload-5b2f-42.zip
                                                    Compress all files + sidecars
                                                    State: Preparing → Uploading
                                                    Compute chunk count
                                                    ReportStatus(Uploading, pct=0)
                                                    <───────────────

                                                    For each chunk N:
                                                      PUT /content/recordings/5b2f/42/chunks/N
                                                        Header: X-Chunk-SHA256: ...
                                                        Body: 64 MB
                                                    ─────────────────────────────>
                                                                                  Validate chunk hash
                                                                                  Write to _staging/5b2f/42/chunks/N
                                                                                  204 No Content
                                                                                  <───────────────
                                                    ReportStatus(Uploading, pct=N/total)
                                                    <───────────────

                                                    After last chunk:
                                                    POST /content/recordings/5b2f/42/complete
                                                      { totalChunks, totalBytes, finalSHA256 }
                                                    ─────────────────────────────>
                                                                                  Assemble chunks → temp zip
                                                                                  Verify totalSize, finalSHA256
                                                                                  Move _staging/5b2f/42/chunks → /NAS/Recordings/5b2f/42.zip
                                                                                  (atomic Directory.Move or File.Move)
                                                                                  Delete _staging/5b2f/42/
                                                                                  Mark intent Complete
                                                                                  200 OK { nasPath }
                                                                                  <───────────────
                                                    Delete local temp zip
                                                    AckCommand(intentId, Complete)
                                                    <───────────────
                                                    Master updates session: completedNodes += [42]
                                                    Persist snapshot
```

### 2.2 Finalize Sequence

```
  App                Master                                      NAS

  POST /api/sessions/5b2f/finalize
  ─────────────────>
                     Check session 5b2f exists
                     Check all expected nodes are Complete
                       (or partial=true if ?force=true)
                     Write _session.json (status: complete, ...)
                     ─────────────────────────────────────────────>
                                                                  /NAS/Recordings/5b2f/_session.json
                                                                  <───────────────
                     Mark session Finalized in snapshot
  200 OK { status: complete, ... }
  <─────────────────
```

### 2.3 Replay Sequence

```
  App / Operator     Master                          Agent SIM-03               NAS

  POST /api/recordings/replay
    { sessionId: 5b2f, logicalNodeIds: [42] }
  ─────────────────>
                     For each LN, resolve to agent
                     Create RecordingDownload intent per (session, LN)
                     ReceiveCommand(DownloadRecording, sid=5b2f, LN=42) ─>
  202 Accepted
  <─────────────────                                Check local recordings/5b2f/42/ — not present
                                                    State: Downloading
                                                    GET http://master.local:8080/content/recordings/5b2f/42.zip
                                                    ─────────────────────────────>
                                                                                  master streams from NAS
                                                                                  <───────────────
                                                    Receive zip stream, write to staging
                                                    Extract zip to recordings/5b2f/42/
                                                    Delete staged zip
                                                    Update ExtractedRecording table
                                                    ReportStatus(Active for session)
                                                    AckCommand(intentId, Complete)
                                                    <───────────────
                                                                                  App reads from
                                                                                  C:/.../recordings/5b2f/42/...
```

------

## 3. Upload Protocol

### 3.1 Two Stages

Stage A — local zip creation on the agent (single-shot, restart-from-scratch on failure):

```
agent receives UploadRecording(sessionId, logicalNodeId, files: [{path, size}, ...])
agent validates each path exists and matches declared size; if any missing, ack NotApplicable
agent creates temp zip at {DataRoot}/staging/upload-{sessionId}-{logicalNodeId}.zip
for each file:
  add to zip with Deflate Optimal (10× compression on raw recording data is typical)
agent records temp zip size, computes its SHA-256 (for finalize verification)
agent writes RecordingUploadJob row with State = Uploading
```

Stage B — chunked upload with resume (multi-shot, per-chunk granularity):

```
agent computes chunk count = ceil(zipSize / 64 MB)
for chunk N from NextChunkIndex to totalChunks-1:
  read 64 MB (or remainder) from temp zip at offset N * 64 MB
  compute chunk SHA-256
  PUT /content/recordings/{sessionId}/{logicalNodeId}/chunks/{N}
    Header: X-Chunk-SHA256: sha256:...
    Body: chunk bytes
  on 204: NextChunkIndex = N+1; persist; report progress
  on 4xx/5xx: retry with backoff (3 attempts: 5s, 30s, 2min)
  on persistent failure: state = Failed, ack to master
after all chunks:
  POST /content/recordings/{sessionId}/{logicalNodeId}/complete
    Body: { totalChunks, totalBytes, finalSHA256 }
on 200: state = Complete, delete temp zip, AckCommand(Complete)
```

### 3.2 Chunk Size

Fixed at 64 MB (same as ChunkedHugeFile bundle chunks). For a 5 GB compressed contribution that's 80 chunks; for a 50 GB contribution it's 800 chunks. Last chunk is smaller as needed.

The chunk size is constant per upload — the upload doesn't carry a manifest the way download bundles do. The master infers chunk boundaries from total bytes received and the final `totalBytes` claim from the agent.

### 3.3 PUT Endpoint Contract

```
PUT /content/recordings/{sessionId}/{logicalNodeId}/chunks/{N}

Headers:
  Content-Type:    application/octet-stream
  Content-Length:  {chunk size in bytes}
  X-Chunk-SHA256:  sha256:{hex hash of body}    [REQUIRED]

Body: raw chunk bytes

Responses:
  204 No Content    — chunk accepted and persisted
  400 Bad Request   — missing/malformed X-Chunk-SHA256 header
  404 Not Found     — no RecordingUpload intent exists for (sessionId, logicalNodeId)
  409 Conflict      — chunk hash mismatch (body details expected vs received)
  416 Range Not Satisfiable — chunk index outside expected range
  500/503           — master IO failure; transient
```

Master receives chunk, validates header hash against body content, writes to `_staging/{sessionId}/{logicalNodeId}/chunks/{N}` (atomic-rename through `.tmp`). On success, responds 204.

Agent treats 204 as commit and increments `NextChunkIndex` in SQLite.

### 3.4 Complete Endpoint Contract

```
POST /content/recordings/{sessionId}/{logicalNodeId}/complete

Headers:
  Content-Type:  application/json

Body:
  {
    "totalChunks": 192,
    "totalBytes":  12884901888,
    "finalSHA256": "sha256:..."
  }

Responses:
  200 OK             — accepted; body has { nasPath, completedAt }
  404 Not Found      — no upload intent for (sessionId, logicalNodeId)
  409 Conflict       — chunk count mismatch (some chunks missing) OR finalSHA256 mismatch after assembly
  500                — NAS write failure; transient
```

On the master, complete handler:

1. Verify all chunks 0..totalChunks-1 exist in `_staging/{sessionId}/{logicalNodeId}/chunks/`
2. Verify total bytes match
3. Assemble: open output stream, copy each chunk file in order; running SHA-256
4. Verify final SHA-256 matches `finalSHA256`
5. Atomic move: `_staging/{sessionId}/{logicalNodeId}/chunks/_assembled.zip` → `/NAS/Recordings/{sessionId}/{logicalNodeId}.zip`
6. Delete `_staging/{sessionId}/{logicalNodeId}/` recursively
7. Update session state: `completedNodes += logicalNodeId`
8. Persist snapshot

On any verification failure: respond 409, do not move into final location. Agent re-attempts via complete (chunks are still present; agent may also re-upload specific chunks if it suspects which one is wrong).

### 3.5 Resume Semantics

**Agent restart mid-upload:**

```
agent restarts
agent.db: RecordingUploadJob has State=Uploading, NextChunkIndex=47, TempZipPath=...
agent's StateMachineRunner finds this job, continues from chunk 47
master's _staging has chunks 0..46 already
PUT chunk 47 → 204; continue 48, 49, ...
on completion, POST .../complete → 200
```

**Master restart mid-upload:**

```
master restarts
master snapshot has RecordingUpload intent in Executing state
agents reconnect via Register; receive replayed UploadRecording command
agents check their RecordingUploadJob — already Uploading with NextChunkIndex=47
agents resume chunk uploads from 47
master's _staging directory still has chunks 0..46 from before restart
PUT chunk 47 → 204; continue
```

The master's _staging directory is the source of truth for "which chunks have been received." If the agent and master state disagree (e.g. agent thinks chunk 50 was sent but master shows only chunk 49), the agent's next PUT for chunk 50 just succeeds again — idempotent.

**Master restart between chunk and complete:**

```
agent has sent all 100 chunks; about to POST .../complete
master restarts
agent's POST fails (connection refused)
agent retries; once master is up, agent's POST succeeds; master sees all chunks in _staging
master assembles and writes to NAS as normal
```

### 3.6 What If the Agent Detects Local Source Files Changed?

If a file's content or size differs from what was declared at `POST /api/recordings` time, the upload would produce a zip different from what the master thinks it's getting. Phase 8 takes a conservative approach:

- At zip-creation time, agent records each input file's SHA-256 (cheap, files are local).
- If a file's hash differs from a previous attempt's recorded hash (recovery case), agent restarts the zip from scratch (Stage A) — the temp zip from the previous attempt is invalid.
- The protocol does **not** transmit per-file hashes to the master. The master only sees the assembled zip and validates `finalSHA256`.

The simulation app is expected to declare a recording only when it's *done* writing. If the app continues writing after `POST /api/recordings`, that's a contract violation.

------

## 4. Session Lifecycle on Master

### 4.1 Session State

```csharp
public sealed record RecordingSession(
    Guid SessionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinalizedAt,
    SessionStatus Status,                       // Pending | Complete | Partial
    IReadOnlySet<int> ExpectedNodes,            // declared at finalize, or grown via POST /api/recordings
    IReadOnlySet<int> CompletedNodes,
    IReadOnlySet<int> MissingNodes,             // empty until finalize
    IReadOnlyList<UploadIntentRecord> UploadIntents);

public enum SessionStatus { Pending, Complete, Partial }

public sealed record UploadIntentRecord(
    Guid IntentId,
    int LogicalNodeId,
    string AgentId,
    UploadIntentState State,                    // Pending | Executing | Complete | Failed
    long BytesUploaded,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public enum UploadIntentState { Pending, Executing, Complete, Failed }
```

### 4.2 Auto-Creation on First POST

```
POST /api/recordings { sessionId: 5b2f, logicalNodeId: 42, files: [...] }
  master.SessionRepository.GetOrCreate(5b2f):
    if missing: create RecordingSession { Status=Pending, ExpectedNodes=[42] }
    else: ExpectedNodes += 42
  create RecordingUpload intent for agent SIM-03
  dispatch ReceiveCommand
  snapshot
  respond 202 { intentId }
```

Sessions exist in master state as soon as the first `POST /api/recordings` arrives for them. Subsequent `POST`s for the same `sessionId` join the same session.

### 4.3 Finalize

```
POST /api/sessions/5b2f/finalize [body: { expectedNodes: [...] }] [?force=true]

master.SessionService.FinalizeAsync(5b2f, body, force):
  session = repository.Get(5b2f)
  if not exists: 404

  if body.expectedNodes is supplied:
    session.ExpectedNodes = body.expectedNodes   // authoritative override

  missing = session.ExpectedNodes - session.CompletedNodes
  if missing is empty:
    status = Complete
  elif force:
    status = Partial
  else:
    return 409 { missingNodes, pendingIntents }

  write /NAS/Recordings/5b2f/_session.json:
    { sessionId, finalizedAt, participatingNodes: ExpectedNodes, status, missingNodes }
  atomic via .tmp + rename

  session.FinalizedAt = now
  session.Status = status
  session.MissingNodes = missing
  snapshot

  return 200 { status, finalizedAt, sessionMarker, missingNodes }
```

The `_session.json` is the authoritative on-NAS record. Without it, a session is "in progress" from the NAS's perspective even if the master state has finalized it. (This matters if master state is lost — the NAS itself is the recovery source.)

### 4.4 Intent Persistence

`RecordingUpload` and `RecordingDownload` intents are persisted in master snapshot alongside `Deploy`, `Activate`, etc. (Phase 2 intents). They survive master restart. On agent reconnect (Register), pending intents replay the same way bundle commands do.

### 4.5 Session State After Master Restart

```
master restarts
load snapshot → sessions[] hydrated
agents reconnect via Register
  agent's payload includes uploadProgress for in-flight uploads (added to RegisterRequest in Phase 8)
master rebuilds session.CompletedNodes by:
  1. Walking persisted UploadIntents — those marked Complete
  2. Querying NAS — for each /NAS/Recordings/{sessionId}/{LN}.zip that exists, mark LN as completed
master pushes ReceiveCommand replays for any Executing UploadIntents
```

`RegisterRequest` gets a new field:

```csharp
public sealed record RegisterRequest(
    string AgentId,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<InstalledVersion> CurrentVersions,
    IReadOnlyList<RecordingProgress> ActiveRecordingUploads);    // new in Phase 8

public sealed record RecordingProgress(
    Guid SessionId,
    int LogicalNodeId,
    int NextChunkIndex,
    long TotalBytesSoFar);
```

Master uses these to confirm/correct its view of upload state.

------

## 5. NAS Layout

### 5.1 Final Layout

```
/NAS/Recordings/
  {sessionId}/                            session folder, finalized when _session.json present
    _session.json
    {logicalNodeId}.zip                   one per per-node contribution
    {logicalNodeId}.zip
    ...
  _staging/                               work in progress, never read for serving
    {sessionId}/
      {logicalNodeId}/
        chunks/
          0
          1
          47.tmp                          .tmp suffix during write, atomic rename to filename
          47
        _assembled.zip                    intermediate during complete; never serves to anyone
  _trash/                                 evicted sessions retained briefly
    {timestamp}-{sessionId}/
```

### 5.2 Atomic Operations

| Operation                                       | Mechanism                                                    |
| ----------------------------------------------- | ------------------------------------------------------------ |
| Write chunk N to `_staging/{sid}/{ln}/chunks/N` | `File.WriteAllBytes(.tmp)` then `File.Move(.tmp, N)`         |
| Assemble chunks into `_assembled.zip`           | Stream-copy each chunk file in order into `_assembled.zip.tmp`; then `File.Move` to `_assembled.zip` |
| Move assembled into final location              | `File.Move(_assembled.zip, /NAS/Recordings/{sid}/{ln}.zip)`  |
| Write `_session.json`                           | `File.WriteAllBytes(.tmp)` then `File.Move(.tmp, _session.json)` |
| Delete session                                  | `Directory.Move(/NAS/Recordings/{sid}, /NAS/Recordings/_trash/{ts}-{sid})` |

All atomicity guarantees require `_staging/`, `_trash/`, and the session folders to live on the same volume — same constraint as the master pull cache (Phase 4 §2.7).

### 5.3 Startup Cleanup on Master

```
master startup:
  delete contents of /NAS/Recordings/_staging/{sid}/{ln}/ for any (sid, ln) where:
    - no corresponding UploadIntent in snapshot (intent was Cancelled or expired)
    - corresponding UploadIntent is in Complete state (data already moved to final location)
  for each (sid, ln) in _staging with Executing intent in snapshot:
    leave intact; agent will resume
  delete contents of /NAS/Recordings/_trash/{*} older than TrashRetentionHours (default 168 = 7 days)
```

Note: recording staging cleanup is more conservative than cache staging because recordings are user data, not regenerable. We never delete user data without an explicit operator action; the only cleanup is for staging that has no living intent.

------

## 6. Agent State and Behaviour

### 6.1 SQLite Schema Additions

```sql
CREATE TABLE RecordingUploadJob (
    SessionId        TEXT    NOT NULL,
    LogicalNodeId    INTEGER NOT NULL,
    IntentId         TEXT    NOT NULL,
    State            TEXT    NOT NULL,             -- Preparing | Uploading | Complete | Failed
    TempZipPath      TEXT,
    SourceFilesJson  TEXT    NOT NULL,             -- JSON: [{path,size,sha256?}]
    TotalChunks      INTEGER,
    TotalBytes       INTEGER,
    NextChunkIndex   INTEGER NOT NULL DEFAULT 0,
    FailureCount     INTEGER NOT NULL DEFAULT 0,
    StartedAt        TEXT,
    UpdatedAt        TEXT    NOT NULL,
    PRIMARY KEY (SessionId, LogicalNodeId)
);

CREATE TABLE ExtractedRecording (
    SessionId       TEXT    NOT NULL,
    LogicalNodeId   INTEGER NOT NULL,
    LocalPath       TEXT    NOT NULL,              -- absolute path to recordings/{sid}/{ln}/
    ExtractedAt     TEXT    NOT NULL,
    LastAccessedAt  TEXT    NOT NULL,
    SizeBytes       INTEGER NOT NULL,
    PRIMARY KEY (SessionId, LogicalNodeId)
);

CREATE INDEX IX_RecordingUploadJob_State ON RecordingUploadJob(State);
CREATE INDEX IX_ExtractedRecording_LastAccessed ON ExtractedRecording(LastAccessedAt);
```

### 6.2 New Local Directories

```
{DataRoot}/
  recordings/
    {sessionId}/
      {logicalNodeId}/
        {extracted files}
        {sidecars}
  staging/
    uploads/
      upload-{sessionId}-{logicalNodeId}.zip       transient temp zip during upload
    downloads/
      download-{sessionId}-{logicalNodeId}.zip     transient during replay download
```

### 6.3 Upload State Machine

```
        ┌──────────┐
        │ Preparing│ ← UploadRecording command received; zipping
        └────┬─────┘
             │ zip complete
             ▼
        ┌──────────┐
        │ Uploading│ ← chunks being PUT to master
        └────┬───┬─┘
   complete  │   │ persistent failure
             │   │
             ▼   ▼
        ┌─────────┐ ┌─────────┐
        │ Complete│ │ Failed  │
        └─────────┘ └─────────┘
```

Transitions:

| From        | To                                         | Trigger                                                      |
| ----------- | ------------------------------------------ | ------------------------------------------------------------ |
| (none)      | `Preparing`                                | `ReceiveCommand(UploadRecording)` arrived; row inserted      |
| `Preparing` | `Uploading`                                | Temp zip created successfully                                |
| `Preparing` | `Failed`                                   | Source files missing or inaccessible (NotApplicable to master) |
| `Uploading` | `Complete`                                 | All chunks PUT, complete endpoint returned 200               |
| `Uploading` | `Failed`                                   | Chunk failed beyond retry limit                              |
| `Failed`    | (manual operator retry creates new intent) | Operator-initiated                                           |

The `Failed` state is terminal in Phase 8. Phase 11 may introduce automatic retry policies; for now, the operator inspects the message queue and decides whether to retry (`POST /api/intents/{id}/retry`).

### 6.4 Download State Machine (Replay)

Simpler:

```
Preparing → Downloading → Extracting → Complete
                     |              |
                     v              v
                   Failed          Failed
```

Transitions for replay are short-lived and don't merit a SQLite state — they're transient and re-attempted as a unit if interrupted.

### 6.5 Restart Behaviour

```
agent restart with RecordingUploadJob rows:
  state = Preparing: temp zip might be partial → delete temp zip, restart preparing
  state = Uploading: temp zip exists → resume from NextChunkIndex
  state = Complete:  nothing to do
  state = Failed:    nothing to do (operator must retry)

ExtractedRecording rows:
  validate localPath still exists; if missing, delete row (will be re-downloaded on demand)
```

------

## 7. Replay Flow

### 7.1 Trigger

Either the app or an operator initiates replay via REST:

```
POST /api/recordings/replay

Body:
  {
    "sessionId":      "5b2f...",
    "logicalNodeIds": [42, 43, 44]
  }

Response:
  202 Accepted
  {
    "intents": [
      { "intentId": "...", "agentId": "SIM-03", "logicalNodeId": 42, "state": "Pending" },
      ...
    ]
  }
```

The master creates one `RecordingDownload` intent per (session, logical-node) pair. Each intent targets the agent that hosts the logical node.

### 7.2 Agent Behaviour

```
agent receives ReceiveCommand(DownloadRecording, sessionId, logicalNodeId)

  if recordings/{sessionId}/{logicalNodeId}/ exists (per ExtractedRecording row):
    update LastAccessedAt
    ack Complete immediately

  else:
    create staging/downloads/download-{sid}-{ln}.zip
    GET http://master.local:8080/content/recordings/{sid}/{ln}.zip
      stream to staging path
    extract zip to recordings/{sid}/{ln}/
    insert ExtractedRecording row
    delete staged zip
    ack Complete
```

### 7.3 Master Serves the Zip

```
GET /content/recordings/{sessionId}/{logicalNodeId}.zip

  nasPath = /NAS/Recordings/{sessionId}/{logicalNodeId}.zip
  if not exists: 404
  return Results.File(nasPath, "application/zip", enableRangeProcessing: true)
```

The master streams directly from the NAS — **no pull cache for recordings**. Rationale: recordings are usually accessed once for replay, the access pattern doesn't favour caching, and adding a cache layer for archive data costs disk without saving real work.

For the master-co-located-with-NAS common case, the read is local FS — fast. For the COTS NAS case, it's a single SMB stream per agent download — fine, one CAL slot at a time.

### 7.4 Concurrent Replays of Same Session

Multiple agents downloading the same session: each gets its own HTTP stream from the master. Master's underlying file read is shared (`FileShare.Read`); OS file cache helps. No coalescing needed at this scale.

------

## 8. Listing and Deletion

### 8.1 List Sessions

```
GET /api/sessions
  ?status=complete|pending|partial
  &since=2026-05-01
  &until=2026-05-31
  &cursor=...
  &limit=50
```

Returns sessions from master state with summary:

```json
{
  "items": [
    {
      "sessionId":          "5b2f...",
      "status":             "complete",
      "createdAt":          "2026-05-17T10:30:00Z",
      "finalizedAt":        "2026-05-17T10:45:00Z",
      "participatingNodes": [42, 43, 44],
      "missingNodes":       [],
      "totalBytes":         42949672960
    }
  ],
  "nextCursor": null
}
```

`totalBytes` is the sum of all `{logicalNodeId}.zip` sizes for the session. Master computes by querying NAS on demand (cheap — just file size, no read).

### 8.2 Session Detail

`GET /api/sessions/{sessionId}` — already defined in API spec §8.2. Returns full per-node upload state.

### 8.3 Delete Session

```
DELETE /api/sessions/{sessionId}

master.SessionService.DeleteAsync(sessionId):
  session = repository.Get(sessionId)
  if not exists: 404
  if any upload intent in Executing/Pending state: 409 "cancel uploads first"

  move /NAS/Recordings/{sessionId} → /NAS/Recordings/_trash/{ts}-{sessionId}
  remove session from master state, snapshot

  for each agent in fleet:
    create EvictSession intent (small, fast; broadcasts to all agents)
    dispatch ReceiveCommand(EvictSession, sessionId)

  return 204
```

### 8.4 Agent Handles EvictSession

```
agent receives ReceiveCommand(EvictSession, sessionId):
  if ExtractedRecording row exists for sessionId:
    delete recordings/{sessionId}/ recursively (all logical nodes for this session)
    delete ExtractedRecording rows for this session
  ack Complete
```

Idempotent: if the agent doesn't have the session, the command is a no-op.

### 8.5 Operator Recovery

The `_trash/` retention (default 7 days) gives an operator a window to recover an accidentally-deleted session. Manual `Move-Item` from `_trash/` back to `/NAS/Recordings/` restores the data. The master will pick it up on next session-listing read (master doesn't cache the listing — queries NAS on demand for the list view).

------

## 9. Node-Local GC of Extracted Recordings

### 9.1 Policy

Configurable per category in site config (added in Phase 3):

```json
"categoryDefaults": {
  "Recording": {
    "chunkSize":         "64MB",
    "compressionLevel":  "Optimal"
  }
},
"operational": {
  "diskWatermarkPercent": 10,
  "agentRetention": {
    "recordings": { "keepLastNSessions": 5 }
  }
}
```

Agent eviction logic:

```
on a periodic schedule (every 30 minutes) AND after every successful extraction:
  freePercent = computeDiskFreePercent({DataRoot})
  if freePercent < diskWatermarkPercent OR ExtractedRecording.Count > keepLastNSessions:
    evictLruUntilOk()
```

`evictLruUntilOk`:

```
candidates = ExtractedRecording rows
  ordered by LastAccessedAt ASC
  excluding any session currently being uploaded or actively read by app (best-effort)

for each candidate:
  if (free% >= watermark AND count <= keepLastN): break
  delete recordings/{sessionId}/{logicalNodeId}/ recursively
  delete ExtractedRecording row
  log eviction
```

### 9.2 "Actively Read by App" Detection

The agent has no reliable way to know whether the app is currently reading a session's files. Two pragmatic options:

**Option A (simpler):** ignore — evict regardless. The app must tolerate file disappearance during a session. Most replay use cases load files into memory upfront and don't keep them open continuously.

**Option B:** introduce a "pin" mechanism — app calls `POST /api/recordings/pin?logicalNodeId=42&sessionId=5b2f` to mark a session as in-use. Agent skips pinned sessions during eviction. Pin auto-expires after configurable timeout (e.g. 1 hour without refresh).

Phase 8 implements **Option A**. If a use case appears requiring B, it can be added later. The pin endpoint is small and additive.

### 9.3 No NAS-Side GC

`/NAS/Recordings/` is kept indefinitely. Operator-triggered deletion only. The `_trash/` retention sweeps trashed sessions older than 7 days.

------

## 10. SignalR Hub Additions

### 10.1 New Command Actions

```csharp
public enum CommandAction
{
    Stage,           // bundles, Phase 2+
    Activate,        // bundles, Phase 2+
    Cancel,          // intents, Phase 2+
    Verify,          // bundles, Phase 4+
    CacheWarm,       // relay warming, Phase 5+
    UploadRecording, // new in Phase 8
    DownloadRecording, // new in Phase 8 (replay)
    EvictSession     // new in Phase 8 (session delete broadcast)
}
```

### 10.2 Command Payloads

For `UploadRecording`:

```json
{
  "commandId":   "...",
  "action":      "UploadRecording",
  "sessionId":   "5b2f...",
  "logicalNodeId": 42,
  "files": [
    { "path": "C:/AppData/Recordings/5b2f/node42/main.dat",     "size": 12884901888 },
    { "path": "C:/AppData/Recordings/5b2f/node42/main.sidecar", "size":         2048 }
  ]
}
```

For `DownloadRecording`:

```json
{
  "commandId":     "...",
  "action":        "DownloadRecording",
  "sessionId":     "5b2f...",
  "logicalNodeId": 42
}
```

For `EvictSession`:

```json
{
  "commandId":   "...",
  "action":      "EvictSession",
  "sessionId":   "5b2f..."
}
```

`Command` DTO extended with optional `SessionId`, `LogicalNodeId`, and `Files[]` fields.

### 10.3 Register Payload Extension

Already noted in §4.5:

```csharp
public sealed record RegisterRequest(
    string AgentId,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<InstalledVersion> CurrentVersions,
    IReadOnlyList<RecordingProgress> ActiveRecordingUploads);
```

Master uses `ActiveRecordingUploads` to reconcile in-flight upload state after restart.

------

## 11. REST and Data Plane Endpoints

### 11.1 Phase 8 Endpoints

Already documented in API spec §7 and §8 and §14. Phase 8 implements them:

| Method | Path                                        | Phase 8 behaviour                                          |
| ------ | ------------------------------------------- | ---------------------------------------------------------- |
| POST   | `/api/recordings`                           | Create upload intent, dispatch to agent                    |
| POST   | `/api/sessions/{sessionId}/finalize`        | Verify completeness, write `_session.json`, return status  |
| POST   | `/api/recordings/replay`                    | Create download intents, dispatch to agents                |
| GET    | `/api/sessions`                             | List from master state, augmented by NAS file-size queries |
| GET    | `/api/sessions/{sessionId}`                 | Detail from master state                                   |
| DELETE | `/api/sessions/{sessionId}`                 | Move to trash, broadcast EvictSession                      |
| PUT    | `/content/recordings/{sid}/{ln}/chunks/{N}` | Validate, write to staging                                 |
| POST   | `/content/recordings/{sid}/{ln}/complete`   | Assemble, verify, move to final                            |
| GET    | `/content/recordings/{sid}/{ln}.zip`        | Stream from NAS (replay download)                          |

### 11.2 Endpoints Not Yet Implemented

| Method | Path                                                         | Phase                                    |
| ------ | ------------------------------------------------------------ | ---------------------------------------- |
| POST   | `/api/recordings/pin` (if Option B from §9.2 is adopted)     | Phase 8 optional                         |
| POST   | `/api/sessions/{sessionId}/recover` (rebuild master state from NAS) | Operator-driven, not a phase deliverable |

------

## 12. Code Sketches

### 12.1 Agent: Upload Pipeline

```csharp
public sealed class RecordingUploader
{
    private readonly IRecordingUploadRepository _repo;
    private readonly HttpClient _http;
    private readonly IAgentSliceManager _slice;
    private readonly IHubProxy _hub;
    private readonly ILogger<RecordingUploader> _log;
    private readonly UploaderOptions _opts;

    public async Task<UploadResult> UploadAsync(Command cmd, CancellationToken ct)
    {
        var sessionId = Guid.Parse(cmd.SessionId!);
        var ln = cmd.LogicalNodeId!.Value;
        var files = cmd.Files!;

        // Persist intent on agent side
        var job = await _repo.GetOrCreateAsync(sessionId, ln, cmd.CommandId, files, ct);

        if (job.State == UploadState.Complete) return UploadResult.AlreadyComplete;

        // Stage A: zip
        if (job.State == UploadState.Preparing)
        {
            try
            {
                var tempPath = await CreateTempZipAsync(sessionId, ln, files, ct);
                var (totalBytes, finalHash) = await ComputeZipMetricsAsync(tempPath, ct);
                var totalChunks = (int)((totalBytes + _opts.ChunkSize - 1) / _opts.ChunkSize);

                await _repo.MarkUploadingAsync(sessionId, ln, tempPath, totalChunks, totalBytes, ct);
                job = await _repo.GetAsync(sessionId, ln, ct);

                await _hub.ReportStatusAsync(/* state Uploading */);
            }
            catch (FileNotFoundException ex)
            {
                await _repo.MarkFailedAsync(sessionId, ln, ex.Message, ct);
                return UploadResult.SourceMissing(ex.Message);
            }
        }

        // Stage B: chunked PUT
        var baseUrl = _slice.Current.DataSource.Url;     // points to master
        var chunkUrlBase = $"{baseUrl}/content/recordings/{sessionId}/{ln}";

        await using var stream = File.OpenRead(job.TempZipPath!);
        for (int i = job.NextChunkIndex; i < job.TotalChunks; i++)
        {
            ct.ThrowIfCancellationRequested();
            stream.Seek((long)i * _opts.ChunkSize, SeekOrigin.Begin);

            var thisChunkSize = (int)Math.Min(_opts.ChunkSize, job.TotalBytes - stream.Position);
            var buffer = new byte[thisChunkSize];
            await stream.ReadExactlyAsync(buffer, ct);

            var hash = HashFormat.Format(SHA256.HashData(buffer));
            var success = await PutChunkAsync($"{chunkUrlBase}/chunks/{i}", buffer, hash, ct);

            if (!success)
            {
                await _repo.IncrementFailureAsync(sessionId, ln, ct);
                return UploadResult.ChunkFailed(i);
            }

            await _repo.SetNextChunkIndexAsync(sessionId, ln, i + 1, ct);
            await _hub.ReportStatusAsync(/* state Uploading, pct */);
        }

        // Complete
        var complete = new { totalChunks = job.TotalChunks, totalBytes = job.TotalBytes, finalSHA256 = job.FinalHash };
        var resp = await _http.PostAsJsonAsync($"{chunkUrlBase}/complete", complete, ct);
        if (!resp.IsSuccessStatusCode)
            return UploadResult.CompleteFailed((int)resp.StatusCode);

        await _repo.MarkCompleteAsync(sessionId, ln, ct);
        File.Delete(job.TempZipPath!);
        return UploadResult.Success;
    }

    private async Task<bool> PutChunkAsync(string url, byte[] data, string hash, CancellationToken ct)
    {
        for (int attempt = 1; attempt <= _opts.MaxChunkAttempts; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Put, url);
                req.Headers.Add("X-Chunk-SHA256", hash);
                req.Content = new ByteArrayContent(data);
                req.Content.Headers.ContentType = new("application/octet-stream");
                using var resp = await _http.SendAsync(req, ct);
                if (resp.IsSuccessStatusCode) return true;
                if (resp.StatusCode == HttpStatusCode.Conflict)
                {
                    _log.LogWarning("Chunk hash mismatch reported by master");
                    return false;   // master says our data is bad; don't retry blindly
                }
                _log.LogWarning("PUT chunk {Url} attempt {N}: {Code}", url, attempt, resp.StatusCode);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _log.LogWarning(ex, "PUT chunk {Url} attempt {N} exception", url, attempt);
            }
            if (attempt < _opts.MaxChunkAttempts) await Task.Delay(BackoffFor(attempt), ct);
        }
        return false;
    }

    // CreateTempZipAsync, ComputeZipMetricsAsync omitted for brevity.
}
```

### 12.2 Master: Chunk Receive Endpoint

```csharp
app.MapPut("/content/recordings/{sessionId:guid}/{logicalNodeId:int}/chunks/{n:int}",
    async (Guid sessionId, int logicalNodeId, int n, HttpContext ctx,
           ISessionRepository sessions, INasPaths nasPaths,
           IIntentRepository intents) =>
{
    var declaredHash = ctx.Request.Headers["X-Chunk-SHA256"].ToString();
    if (string.IsNullOrEmpty(declaredHash))
        return Results.BadRequest(Problem("Missing X-Chunk-SHA256 header"));

    var session = sessions.Get(sessionId);
    if (session is null) return Results.NotFound(Problem($"No session {sessionId}"));

    var intent = intents.GetActiveUploadIntent(sessionId, logicalNodeId);
    if (intent is null) return Results.NotFound(Problem("No active upload intent"));

    var stagingDir = nasPaths.RecordingStagingChunks(sessionId, logicalNodeId);
    Directory.CreateDirectory(stagingDir);

    // Stream to temp, compute hash, atomic rename
    var tmpPath = Path.Combine(stagingDir, $"{n}.tmp");
    var finalPath = Path.Combine(stagingDir, n.ToString());

    using var sha = SHA256.Create();
    await using (var ms = new MemoryStream())   // capture body to memory for both write and hash
    {
        await ctx.Request.Body.CopyToAsync(ms);
        var bytes = ms.ToArray();
        var actualHash = HashFormat.Format(sha.ComputeHash(bytes));
        if (actualHash != declaredHash)
            return Results.Conflict(Problem($"Hash mismatch: declared {declaredHash}, actual {actualHash}"));

        await File.WriteAllBytesAsync(tmpPath, bytes);
    }
    File.Move(tmpPath, finalPath, overwrite: true);

    intents.UpdateBytesUploaded(intent.IntentId, bytesAddedToThisChunk: /* count */);

    return Results.NoContent();
});
```

For very large chunks (64 MB), buffering in `MemoryStream` is fine but a streaming hash computation (read from body, simultaneously write to file and update hash) is more memory-efficient. Use whichever fits your master's memory budget.

### 12.3 Master: Complete Endpoint

```csharp
app.MapPost("/content/recordings/{sessionId:guid}/{logicalNodeId:int}/complete",
    async (Guid sessionId, int logicalNodeId, CompleteRequest body,
           ISessionRepository sessions, INasPaths nasPaths,
           IIntentRepository intents) =>
{
    var stagingDir = nasPaths.RecordingStagingChunks(sessionId, logicalNodeId);

    // Verify chunk count
    var chunkFiles = Directory.GetFiles(stagingDir).Where(f => int.TryParse(Path.GetFileName(f), out _))
        .OrderBy(f => int.Parse(Path.GetFileName(f)))
        .ToList();
    if (chunkFiles.Count != body.TotalChunks)
        return Results.Conflict(Problem($"Chunk count {chunkFiles.Count} != declared {body.TotalChunks}"));

    // Assemble
    var assemblePath = Path.Combine(stagingDir, "_assembled.zip.tmp");
    using var sha = SHA256.Create();
    long totalBytes = 0;
    await using (var dst = File.Create(assemblePath))
    {
        var buf = new byte[1 << 20];
        foreach (var chunkPath in chunkFiles)
        {
            await using var src = File.OpenRead(chunkPath);
            int read;
            while ((read = await src.ReadAsync(buf)) > 0)
            {
                sha.TransformBlock(buf, 0, read, null, 0);
                await dst.WriteAsync(buf.AsMemory(0, read));
                totalBytes += read;
            }
        }
    }
    sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
    var finalHash = HashFormat.Format(sha.Hash!);

    if (totalBytes != body.TotalBytes)
        return Results.Conflict(Problem($"Total bytes {totalBytes} != declared {body.TotalBytes}"));
    if (finalHash != body.FinalSHA256)
        return Results.Conflict(Problem($"Final SHA-256 mismatch"));

    // Rename to assembled, then move to final
    var assembledPath = Path.Combine(stagingDir, "_assembled.zip");
    File.Move(assemblePath, assembledPath);

    var finalPath = nasPaths.RecordingFinal(sessionId, logicalNodeId);
    Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
    File.Move(assembledPath, finalPath);

    // Cleanup
    Directory.Delete(stagingDir, recursive: true);

    // Update intent and session
    intents.MarkComplete(/* intentId */);
    sessions.AddCompletedNode(sessionId, logicalNodeId);

    return Results.Ok(new CompleteResponse(/* sessionId, logicalNodeId, intentId, nasPath, completedAt */));
});
```

### 12.4 Master: Session Finalize

```csharp
public async Task<FinalizeResult> FinalizeAsync(Guid sessionId, FinalizeRequest req, bool force, CancellationToken ct)
{
    using var _ = await _sessionLock.LockAsync(sessionId, ct);

    var session = await _repo.GetAsync(sessionId, ct)
        ?? throw new NotFoundException();

    var expected = req.ExpectedNodes ?? session.ExpectedNodes;
    var missing = expected.Except(session.CompletedNodes).ToList();

    if (missing.Count > 0 && !force)
        throw new ConflictException("Some nodes still pending", new { missingNodes = missing });

    var status = missing.Count == 0 ? SessionStatus.Complete : SessionStatus.Partial;

    var marker = new SessionMarker(
        sessionId, DateTimeOffset.UtcNow, expected.ToList(), status, missing);

    var markerPath = _nasPaths.SessionMarkerFile(sessionId);
    var bytes = JsonSerializer.SerializeToUtf8Bytes(marker, _options);
    var tmp = markerPath + ".tmp";
    await File.WriteAllBytesAsync(tmp, bytes, ct);
    File.Move(tmp, markerPath, overwrite: false);

    await _repo.UpdateAsync(sessionId, s => s with {
        Status = status,
        FinalizedAt = DateTimeOffset.UtcNow,
        ExpectedNodes = expected,
        MissingNodes = missing.ToHashSet()
    }, ct);

    _snapshot.MarkDirty();

    return new FinalizeResult(status, DateTimeOffset.UtcNow, markerPath, missing);
}
```

------

## 13. Acceptance Tests

### 13.1 Upload Happy Path

| Test                                                  | Pass condition                                               |
| ----------------------------------------------------- | ------------------------------------------------------------ |
| Single node, single 100 MB recording file             | All chunks PUT, complete returns 200, NAS has `/Recordings/{sid}/{ln}.zip` |
| Single node, multi-file (e.g. 5 files totalling 5 GB) | Temp zip created with all 5 files; uploaded as single zip; NAS has one zip |
| 10 nodes, same session, parallel uploads              | All 10 zips on NAS; session.completedNodes has all 10        |
| 50 GB recording on slow inter-segment link            | Completes (slowly); all chunks verified; integrity good      |
| Compression ratio                                     | Compressed zip is roughly 1/10 of raw recording size         |

### 13.2 Upload Resume

| Test                                                      | Pass condition                                               |
| --------------------------------------------------------- | ------------------------------------------------------------ |
| Agent killed mid-upload at chunk 40/100                   | After restart, agent resumes from chunk 40; master's staging has chunks 0..39 |
| Master killed mid-upload                                  | Agent retries; after master recovery, resume succeeds        |
| Network drop mid-chunk PUT                                | Retry policy kicks in; chunk eventually succeeds             |
| Persistent chunk hash mismatch (test rig sends bad bytes) | After max attempts, intent marked Failed; operator message   |

### 13.3 Finalize

| Test                                                         | Pass condition                                             |
| ------------------------------------------------------------ | ---------------------------------------------------------- |
| Finalize all expected nodes complete                         | 200, status: complete, `_session.json` on NAS              |
| Finalize with one node missing, no force                     | 409 with missingNodes list                                 |
| Finalize with one node missing, ?force=true                  | 200, status: partial, `_session.json` with status: partial |
| Finalize with explicit expectedNodes (incl. nodes that never posted) | Missing list includes those nodes; partial status if force |
| Finalize already-finalized session                           | Idempotent; returns same status                            |

### 13.4 Listing and Detail

| Test                                   | Pass condition                        |
| -------------------------------------- | ------------------------------------- |
| GET /api/sessions empty fleet          | Empty items                           |
| GET /api/sessions with pagination      | Correct cursors and counts            |
| GET /api/sessions filter by status     | Only matching status returned         |
| GET /api/sessions/{id} for unknown     | 404                                   |
| GET /api/sessions/{id} for in-progress | Shows pendingNodes and completedNodes |

### 13.5 Delete

| Test                                  | Pass condition                                               |
| ------------------------------------- | ------------------------------------------------------------ |
| Delete session not currently in use   | Trash folder has the moved session; all agents with extracted copies evict |
| Delete session with active upload     | 409 (cancel uploads first)                                   |
| Delete session, then attempt download | 404 (NAS no longer has it)                                   |
| Trash retention                       | Trash folder cleaned up after retention period               |

### 13.6 Replay

| Test                                             | Pass condition                                               |
| ------------------------------------------------ | ------------------------------------------------------------ |
| Replay on node with no local copy                | Agent downloads zip, extracts, ExtractedRecording row inserted |
| Replay on node with stale local copy (rare race) | Phase 8 assumes immutable session content once finalized; idempotent |
| Replay during concurrent EvictSession            | Race-safe: either eviction completes first (re-download), or extraction completes (then eviction removes it) |

### 13.7 Node-Local GC

| Test                                                 | Pass condition                                               |
| ---------------------------------------------------- | ------------------------------------------------------------ |
| Disk drops below watermark with N sessions extracted | LRU eviction reduces extracted sessions below watermark      |
| keepLastNSessions exceeded                           | Eviction down to N most recent                               |
| Eviction of sessions actively being uploaded         | Skipped (active uploads protected); old extracted sessions evicted preferentially |

------

## 14. Implementation Sequence

1. **DTOs**: `Command` extension with `SessionId`, `LogicalNodeId`, `Files[]`. `RecordingProgress`. Tests for serialization.
2. **Master `SessionRepository`** with in-memory state and snapshot integration.
3. **`POST /api/recordings`** endpoint. Creates session and upload intent. Tests at HTTP level.
4. **Agent SQLite migration**: add `RecordingUploadJob`, `ExtractedRecording` tables.
5. **Agent `RecordingUploader`** core, sequential single-chunk upload only. Tests with stub master.
6. **Multi-chunk upload** with progress reporting and retries. Tests with simulated transient failures.
7. **Master `PUT /content/recordings/.../chunks/{N}`** endpoint with hash validation. HTTP-level tests.
8. **Master `POST /content/recordings/.../complete`** endpoint with assembly and atomic move. Tests for hash verification.
9. **Agent resume on restart** from `RecordingUploadJob` rows. Integration test killing agent mid-upload.
10. **Master resume of in-flight uploads after restart**. Integration test.
11. **`POST /api/sessions/{id}/finalize`** with `?force` and `expectedNodes`. Tests for all three response outcomes.
12. **`GET /api/sessions`** and `GET /api/sessions/{id}`. Tests with pagination and filters.
13. **`DELETE /api/sessions/{id}`** with NAS move-to-trash and `EvictSession` broadcast.
14. **Agent `EvictSession` handler** and `ExtractedRecording` row deletion.
15. **`POST /api/recordings/replay`** dispatching `DownloadRecording` commands.
16. **Master `GET /content/recordings/{sid}/{ln}.zip`** serving directly from NAS.
17. **Agent replay download** + extract + `ExtractedRecording` insertion.
18. **Node-local recording GC** — LRU eviction with watermark and keepLastN policy.
19. **Operator UI additions**: sessions panel showing list, status, per-node upload progress.
20. **End-to-end acceptance tests** (§13).

After step 20, Phase 8 is complete.

------

## 15. Open Questions for Implementation

A few decisions:

- **Streaming hash on master chunk PUT** (§12.2): I sketched a memory-buffered version for clarity. For 64 MB chunks, that's 64 MB allocated per concurrent PUT. With 10 concurrent uploads, 640 MB. Streaming via a `CryptoStream` pipeline avoids the allocation. Tell me if you'd prefer the streaming variant up front, or accept the memory cost for clarity. My default: streaming for production, but the memory variant is fine for the first cut.
- **"Pin" mechanism for actively-used extracted sessions** (§9.2 Option B): I've chosen Option A (evict regardless). If your app holds files open for replay and would fail on file disappearance, Option B (pin endpoint) needs to land. Confirm preference.
- **Replay download bandwidth**: I noted replay downloads happen directly from master (no relay aggregation). For session deletion broadcasts, `EvictSession` fans out to every agent. With 200 agents, that's 200 commands per delete — trivially cheap. No throttling needed. Confirm.
- **`_trash` retention default of 7 days**: longer for cautious operators (30 days). Phase 11 GC can sweep with more sophistication. Tell me if you'd prefer a different default.
- **`expectedNodes` on finalize**: I've supported both implicit (union of POSTed nodes) and explicit (operator-supplied). If the explicit path turns out unused in practice, the implementation can simplify. Keep both, or implicit only?
- **Per-file SHA-256 of source recordings during zip creation**: I record per-file hashes to detect inter-attempt file changes (§3.6). If the app's contract ("don't modify recordings after declaring") is reliable, this guard is unnecessary. Optional — defaults on, adds a small pre-zip cost. Tell me if you'd want it off.
- **Recording stream-zip vs file-then-upload**: Phase 8 uses file-then-upload (two-stage). Stream-zipping (compress while uploading) eliminates the temp disk but loses resume granularity for the zip part. Keep two-stage. Confirm.

These are minor; flag preferences and the build can proceed.



# Phase 9 Detailed Design

## Coordinated Deployment (Two-Phase Commit) and Session-Ready Gating

*Companion to Architecture Design Document Revision 2, API Specification, Phases 1–8 Designs* *Targets `Phase 9` from §21 of the architecture doc* *C# / .NET 8 · ASP.NET Core · May 2026*

------

## 1. Phase Scope

### 1.1 What Phase 9 Adds

Phases 2–8 deploy bundles to agents independently — each agent transfers, verifies, stages, and activates on its own schedule. Phase 9 introduces the master's ability to coordinate activation *across* multiple agents so that a group of agents either all reach `Active` on a new version or none do.

Deliverables:

1. **`DeploymentTransaction`** — a new master-side aggregate that groups related per-agent intents into a single coordinated unit, with explicit `Prepare → Commit → Complete` (or `Failed → RolledBack`) lifecycle.
2. **Prepare-then-commit dispatch** — when a transaction enters `Preparing`, sub-intents Stage in parallel. When all required agents reach `ReadyToActivate`, the transaction moves to `ReadyToCommit`. Master then dispatches `Activate` commands to all agents simultaneously.
3. **Configurable failure policy** — `RollbackAll` or `OperatorIntervention` per transaction. On commit failure, RollbackAll automatically reverts all agents that had activated; OperatorIntervention halts the transaction and waits for an explicit operator decision.
4. **Rollback mechanism** — new `Rollback` command action. Agent repoints `active/{bundleId}` from the new version to the immediately previous version (kept on disk per Phase 4 §6.1's retention rule).
5. **Session-ready gating** — `GET /api/groups/{groupId}/readiness` exposes whether all agents in a group have the latest version of every `requiredForSession` bundle currently Active. The external session manager polls this before starting a session.
6. **Transaction-aware operator surface** — list/inspect/cancel/force-commit endpoints under `/api/transactions`, plus operator UI panel.

### 1.2 Explicitly Deferred

| Concern                                                      | Deferred to                                  |
| ------------------------------------------------------------ | -------------------------------------------- |
| Fleet sync window scheduler                                  | Phase 10                                     |
| Master pull-cache GC, NAS GC, dry-run API                    | Phase 11                                     |
| Cross-version dependency tracking ("v43 requires X.v18")     | Not planned                                  |
| Multi-bundle transactions (one transaction with multiple bundles) | Not planned in Phase 9; could be added later |

A Phase 9 transaction is **one bundle, one version, many agents**. If a session truly needs two bundles to be synchronized (rare in practice), the operator issues two separate deploys; the session-readiness gate (§7) ensures the session won't start until both arrive.

### 1.3 Definition of Done

After Phase 9 an operator can:

- Deploy a Group-scoped bundle to 20 agents in a group and watch all 20 reach `ReadyToActivate` before any of them activates
- See the master fire `Activate` commands to all 20 agents simultaneously when the prepare phase completes
- Have all 20 agents reach `Active` and the transaction marked `Complete`
- Force a single agent's activation to fail in test; verify that with `RollbackAll` policy the other 19 are automatically rolled back to the previous version
- Force a single agent's activation to fail with `OperatorIntervention`; verify the master halts the transaction and waits for operator action
- Call `GET /api/groups/{groupId}/readiness` and see `readyToStart: false` while a transaction is in flight; `true` once it completes
- Cancel a transaction in prepare phase; verify no agents activate, staged versions remain on disk
- Cancel a transaction mid-commit with `?rollback=true`; verify already-committed agents revert

------

## 2. The Two-Phase Model

### 2.1 Lifecycle States

```
                         ┌────────────┐
                         │  Pending   │  created, sub-intents not yet dispatched
                         └─────┬──────┘
                               │ dispatch Stage commands
                               ▼
                         ┌────────────┐
                         │ Preparing  │  some agents transferring/verifying/staging
                         └─────┬──────┘
                               │ all required agents reached ReadyToActivate
                               ▼
                         ┌───────────────┐
                         │ ReadyToCommit │  brief; master decides to commit
                         └─────┬─────────┘
                               │ dispatch Activate commands
                               ▼
                         ┌────────────┐
                         │ Committing │  Activate in flight to all agents
                         └─────┬──┬───┘
              all Active │      │ any ActivationFailed
                         │      │
                         ▼      ▼
              ┌────────────┐  ┌──────────────────────────┐
              │  Complete  │  │  Failed                  │
              └────────────┘  │  (FailurePolicy decides) │
                              └────┬─────────────────┬───┘
                                   │                 │
                       RollbackAll │                 │ OperatorIntervention
                                   ▼                 ▼
                            ┌─────────────┐    ┌──────────┐
                            │ RollingBack │    │  Failed  │  (terminal until operator acts)
                            └─────┬───────┘    └──────────┘
                                  │ all reverted
                                  ▼
                            ┌────────────┐
                            │ RolledBack │
                            └────────────┘
                            
              ┌─────────────────────┐
              │      Cancelled      │  operator-initiated cancel; reachable from
              └─────────────────────┘  Pending, Preparing, ReadyToCommit, or Failed
```

### 2.2 What's Coordinated, What's Not

| Activity                   | Coordinated by master?                                       |
| -------------------------- | ------------------------------------------------------------ |
| Stage / transfer / verify  | No — agents progress independently as fast as their network and storage allow |
| Reaching `ReadyToActivate` | Master *waits* until all required agents arrive              |
| Sending `Activate`         | Yes — all commands dispatched in parallel from a single point in time |
| Atomic junction repoint    | No — happens locally on each agent, takes milliseconds       |
| Reporting `Active`         | Master collects acknowledgments                              |
| Rollback on failure        | Yes — coordinated by the master                              |

The "two phases" are *prepare* (everybody stages) and *commit* (everybody activates). Both transitions are master-driven; the work in between is the agents' own.

### 2.3 What About Cooperative-Hot-Swap?

Cooperative-hot-swap (Phase 6) introduces `AwaitingSafeWindow`, which sits between `ReceiveCommand(Activate)` and the actual junction repoint. Two-phase commit and cooperative mode compose cleanly:

- During Prepare: all agents stage normally; reach `ReadyToActivate`.
- During Commit: master sends `Activate` to all. Each agent inspects the manifest and routes to `AwaitingSafeWindow` (cooperative) or `Activating` (atomic-directory-swap / in-place).
- Cooperative agents wait for app safe-window signals before actually swapping; non-cooperative agents swap immediately.
- The transaction's `Committing → Complete` boundary is reached when all agents report `Active`, regardless of which path they took.

A consequence: a cooperative-mode coordinated transaction can sit in `Committing` for a long time waiting for safe-window signals. This is by design — the transaction holds the coordinated activation invariant. Per-bundle staleness applies (operator-message warning after `staleAfter`).

**`RollbackAll` is incompatible with cooperative mode.** If some agents have already activated cooperatively (their apps are now actively using the new version) and one agent fails, rolling back the others while their apps are running is destructive. Phase 9 rule: **for cooperative-hot-swap bundles, the only legal failure policy is `OperatorIntervention`**. Validation enforces this when creating the transaction.

------

## 3. DeploymentTransaction

### 3.1 Aggregate Shape

```csharp
public sealed record DeploymentTransaction(
    string TransactionId,                        // UUID
    string BundleId,
    string Version,
    DeploymentScope Scope,
    FailurePolicy FailurePolicy,
    TransactionState State,
    IReadOnlySet<string> RequiredAgents,         // resolved at creation; fixed
    IReadOnlyDictionary<string, AgentTxState> AgentStates,
    IReadOnlyList<string> SubIntentIds,          // one Stage intent per agent
    IReadOnlyList<string> CommitIntentIds,       // one Activate intent per agent (created at commit)
    DateTimeOffset CreatedAt,
    DateTimeOffset? PreparedAt,
    DateTimeOffset? CommittedAt,
    DateTimeOffset? CompletedAt);

public enum TransactionState
{
    Pending, Preparing, ReadyToCommit, Committing,
    Complete, Failed, RollingBack, RolledBack, Cancelled
}

public enum FailurePolicy { RollbackAll, OperatorIntervention }

public sealed record AgentTxState(
    string AgentId,
    AgentTxStage Stage,         // Pending, Preparing, Ready, Activating, Active, Failed, RolledBack
    string? PreviousVersion,     // captured at the moment Activate command is sent
    DateTimeOffset UpdatedAt,
    string? Error);

public enum AgentTxStage { Pending, Preparing, Ready, Activating, Active, Failed, RolledBack }
```

### 3.2 Relationship to Intents

A transaction owns sub-intents. Each sub-intent is a per-agent unit visible through the existing intent API:

- During `Preparing`: one `Deploy` (kind=Stage internally) intent per required agent. These dispatch Stage commands.
- During `Committing`: one `Activate` intent per required agent. These dispatch Activate commands.
- During `RollingBack`: one `Rollback` intent per agent that needs to revert.

The intents are first-class. Operators can see them in `GET /api/intents`. Cancelling an individual sub-intent affects only that agent and updates the transaction's `AgentStates` accordingly.

The transaction itself is *also* first-class — it's the aggregate that operators interact with when they think in terms of "the group deploy" rather than "20 separate things".

### 3.3 Resolution at Creation

When a deploy is created with `target.type == "Group"`:

```
1. Resolve agents = { agents whose currentGroupId == target.groupId }
2. If empty:
   - return 200 with transaction in state=Complete, no work done
3. Determine failure policy:
   - From request body if supplied
   - Otherwise from bundle's defaultScope.failurePolicy
   - Otherwise default: RollbackAll for non-cooperative, OperatorIntervention for cooperative
4. Validate: cooperative + RollbackAll → 400
5. Create transaction
6. Create one Stage sub-intent per agent
7. Persist snapshot
8. Dispatch all Stage commands in parallel via SignalR
9. Return 202 with transactionId
```

The required-agents set is **frozen at creation time**. If a node joins or leaves the group later, the transaction is unaffected. New joiners are handled by the normal membership-change flow (Phase 3 §4.3 auto-deploy).

This deliberately keeps a single transaction's success criterion deterministic. Volatility comes through new transactions, not by mutating existing ones.

------

## 4. Triggering Coordination

### 4.1 Deploy Request Extensions

`POST /api/deploy` body grows two optional fields:

```json
{
  "intentId":     "11111111-...",
  "bundleId":     "ScenarioA-Config",
  "version":      "v18",
  "target":       { "type": "Group", "groupId": "session-A" },
  "priority":     "Normal",
  "deadline":     null,
  "requireCoordination": true,
  "failurePolicy":      "RollbackAll"
}
```

`requireCoordination`:

- For `target.type == "Group"`: default `true`. Explicit `false` opts out (rare).
- For all other target types: default `false`. Explicit `true` opts in.

`failurePolicy`:

- Default per `requireCoordination` and activation mode (see §3.3 step 3).
- Ignored when `requireCoordination` resolves to `false`.

If `requireCoordination` resolves to `true`, the master creates a `DeploymentTransaction`. Otherwise, the master creates independent sub-intents per agent (Phase 2+ behaviour, no transaction wrapping).

### 4.2 When the Master Auto-Triggers Coordination

Phase 3 §4.3 specified that membership change creates intents for Group-scoped bundles missing on a newly-joined agent. Phase 9 adds: if the bundle's `defaultScope.type == "Group"`, those intents are wrapped in a transaction.

But: when a single node joins, the transaction has only that one agent. There's nothing to coordinate. The transaction acts as if `requireCoordination == false` — it's just a single-agent deploy. The transaction wrapping is harmless overhead.

In Phase 9 we keep this consistent: every Group-scoped deploy creates a transaction, even of size 1. The transaction's outcome is meaningful for the readiness API (§7) regardless of size.

------

## 5. Prepare Phase

### 5.1 State Updates

```
Transaction Preparing:
  for each agent in RequiredAgents:
    if agent reports Stage state progression (Queued → Transferring → ... → ReadyToActivate):
      update AgentStates[agentId].Stage accordingly
      if Stage == Ready (= reported BundleState.ReadyToActivate):
        increment "ready count"
    if all RequiredAgents have AgentStates.Stage == Ready:
      transaction.State = ReadyToCommit
```

Each agent's progress is tracked via the existing `ReportStatus` SignalR call (Phase 2). The master correlates agent status updates to the transaction by matching `bundleId + version + agentId`.

### 5.2 Agent Failures During Prepare

If an agent reports `TransferFailed`, `VerificationFailed`, or `ActivationFailed` during prepare:

- `AgentStates[agentId].Stage = Failed`
- `AgentStates[agentId].Error = <reason>`
- Transaction stays in `Preparing`
- Operator-message warning emitted

The transaction does **not** auto-fail on per-agent prepare failures. Rationale: prepare failures are recoverable (e.g. retry after a transient SMB hiccup). The master waits for either (a) all agents to reach `Ready`, or (b) operator intervention.

Operator options in this state:

- `POST /api/intents/{subIntentId}/retry` — retry the failed agent's Stage
- `DELETE /api/transactions/{txId}` — cancel the whole transaction
- `POST /api/transactions/{txId}/commit?force=true` — commit despite missing agents (the failed agents stay at their old version; the rest commit)

If `staleAfter` elapses with the transaction stuck in `Preparing`, a warning fires. Still no auto-cancel.

### 5.3 Cancellation During Prepare

`DELETE /api/transactions/{txId}` during `Preparing`:

- Cancel each sub-intent that's still active (sends `Cancel` command to agents)
- Agents in mid-transfer abort; staged version (if any) is left in `staging/` and GC'd by Phase 11
- Transaction transitions to `Cancelled`

Already-prepared agents (those at `ReadyToActivate`) keep their staged version on disk. The version is `ReadyToActivate` but no longer associated with an active transaction. A future Activate command would still find it ready.

------

## 6. Commit Phase

### 6.1 Triggering Commit

The master enters `Committing` automatically when `Preparing` becomes `ReadyToCommit`. There's no manual gate by default.

If the operator wants to inspect a `ReadyToCommit` transaction before committing, they can configure `commitDelay` (default 0, but settable per-transaction):

```json
"requireCoordination": true,
"failurePolicy":       "RollbackAll",
"commitDelay":         "5m"        // wait 5 minutes in ReadyToCommit before auto-committing
```

During the delay, the operator may inspect, cancel, or force-commit immediately. After the delay expires, the master auto-commits.

### 6.2 Commit Dispatch

```
Transaction enters Committing:
  for each agent in RequiredAgents that has Stage == Ready:
    capture AgentStates[agentId].PreviousVersion from current Active state
    create CommitIntent (Activate) sub-intent for this agent
    dispatch ReceiveCommand(Activate, bundleId, version) via SignalR
    set AgentStates[agentId].Stage = Activating
  transaction.CommittedAt = now
```

Master records the previous version *at the moment of commit dispatch*. This is the rollback target if the failure policy fires.

The SignalR sends happen in parallel using a `Task.WhenAll` over `Clients.Client(connId).SendAsync(...)`. They are unordered — agents receive their commands within a small spread of time, but not at literally the same instant. The "simultaneity" is approximate (sub-second for a normal-sized group), and it's sufficient: the swap itself is local and atomic on each agent.

### 6.3 Per-Agent Activation

On each agent:

1. Receive `Activate` command.
2. Inspect manifest activation mode:
   - `atomic-directory-swap` or `in-place`: proceed to `Activating` → atomic operation → `Active`.
   - `cooperative-hot-swap`: proceed to `AwaitingSafeWindow`, wait for `SignalSafeWindow(true)` (Phase 6), then `Activating` → `Active`.
3. Report `Active` (or `ActivationFailed`).

Master updates `AgentStates[agentId].Stage` accordingly.

### 6.4 Per-Agent Failure During Commit

When `AgentStates[agentId].Stage == Failed` during `Committing`:

- Master logs an operator-message: "agent X failed to activate Y v43: <reason>".
- Master evaluates the transaction's `FailurePolicy`.

**RollbackAll**:

```
1. Transaction.State = RollingBack
2. For each agent where AgentStates[agentId].Stage == Active:
   - Create Rollback sub-intent for this agent
   - Dispatch ReceiveCommand(Rollback, bundleId, version=AgentStates[agentId].PreviousVersion)
   - Set AgentStates[agentId].Stage = "RollingBack"
3. For each agent where AgentStates[agentId].Stage == Activating or AwaitingSafeWindow:
   - Send Cancel command (best-effort)
   - The agent's runner aborts the activate-in-progress and stays at ReadyToActivate
4. When all RollingBack agents report Active for previous version OR ack rollback:
   - Transaction.State = RolledBack
```

**OperatorIntervention**:

```
1. Transaction.State = Failed
2. Operator-message escalation: "transaction X failed; operator action required"
3. Master takes no further automatic action
4. Already-committed agents stay at new version
5. Already-failed agents stay at their failed state (typically TransferFailed → re-stage on retry, or ActivationFailed → rollback or operator forces)
```

### 6.5 Commit Completion

```
Transaction in Committing:
  if all RequiredAgents have AgentStates[agentId].Stage == Active:
    transaction.State = Complete
    transaction.CompletedAt = now
    persist snapshot
    write operator-message: "transaction X committed successfully"
```

The transaction is now done. The associated sub-intents are also `Complete`. The bundle is `Active` on every required agent.

Subsequent rollback can still be initiated by the operator via `POST /api/transactions/{txId}/rollback`, even after `Complete` — useful for "we deployed but it broke the simulation; revert everyone".

------

## 7. Session-Ready Gating

### 7.1 The Concept

The session manager (an external system, not part of sync) wants to start a session for group `session-A`. It cannot start until every member agent has every `requiredForSession` bundle at the latest version.

Sync exposes this readiness as a query:

```
GET /api/groups/{groupId}/readiness

Response:
  {
    "groupId":       "session-A",
    "readyToStart":  false,
    "asOf":          "2026-05-17T12:00:00Z",
    "memberAgents":  ["SIM-03", "SIM-04"],
    "perAgent": [
      {
        "agentId":          "SIM-03",
        "ready":            true,
        "missing":          []
      },
      {
        "agentId":          "SIM-04",
        "ready":            false,
        "missing": [
          {
            "bundleId":          "ScenarioA-Config",
            "expectedVersion":   "v18",
            "currentVersion":    "v17",
            "status":            "Activating",
            "transactionId":     "..."
          }
        ]
      }
    ]
  }
```

### 7.2 The Readiness Computation

```
ready(group):
  members = agents with currentGroupId == group
  if members is empty: return true     // vacuous; or maybe false depending on use case
  
  requiredBundles = bundles where defaultScope.groupId == group AND defaultScope.requiredForSession == true
  
  for each agent in members:
    for each bundle in requiredBundles:
      latest = bundle.latestPublishedVersion
      if agent's Active version for bundle != latest:
        agent.missing.append({bundleId, expected: latest, current: agent's version, status: agent's per-bundle state})
    agent.ready = (agent.missing is empty)
  
  group.ready = all agents are ready
```

The `requiredForSession` flag was introduced in Phase 3 via `DeploymentScope.requiredForSession`. Phase 9 makes it active for the readiness gate.

Bundles where `requiredForSession == false` don't block session start, even if a deploy is in progress. Operators use this to distinguish "must have" (sim configs) from "nice to have" (telemetry settings, dashboards).

### 7.3 Vacuous Group

When a group has no members yet (e.g. session manager just created `session-A` but no nodes have joined), what does `readyToStart` return?

I default to **`true` with `memberAgents: []`**: an empty group is trivially ready (there's nothing to check). The session manager can then start the session and wait for agents to join.

Alternative: false ("nothing to start"). The session manager is the right place to interpret this — sync just reports facts.

### 7.4 Polling Pattern

The session manager polls `GET /api/groups/{groupId}/readiness` periodically (every few seconds) until `readyToStart: true`, then starts the session. There's no SignalR push for this — polling is simple, sufficient, and matches the architecture's "external system queries sync" model.

If readiness flaps (transaction starts, agents drop to non-ready, transaction completes, back to ready), the session manager sees the flip-flop. Implementation may add a "ready for at least N seconds" stabilisation if needed.

------

## 8. Rollback

### 8.1 The Rollback Command

New `CommandAction`:

```csharp
public enum CommandAction
{
    Stage, Activate, Cancel, Verify, CacheWarm,
    UploadRecording, DownloadRecording, EvictSession,
    Rollback                                            // new in Phase 9
}
```

Command payload:

```json
{
  "commandId":   "...",
  "action":      "Rollback",
  "bundleId":    "ScenarioA-Config",
  "version":     "v17"               // rollback target — the previous version
}
```

### 8.2 Agent Handles Rollback

```
agent receives Rollback(bundleId, targetVersion):
  1. Verify targetVersion exists in InstalledVersion table and on disk
     - If missing: ack Failed("rollback target v17 not on disk")
  2. Repoint active/{bundleId} junction to versions/{bundleId}/{targetVersion}/extracted
     (or the raw file path for in-place activation mode)
  3. Mark current Active version's row as RolledBack in InstalledVersion
  4. Update BundleState: Active version = targetVersion
  5. Report status
  6. Ack Complete
```

The "previous version" is on disk because Phase 4 §6.1's retention rule keeps it ("the Active version + the immediately previous version"). If for any reason the previous version is missing (corrupted disk, manual deletion), rollback fails and the operator must intervene.

### 8.3 Cooperative Rollback Is Restricted

As noted in §2.3: for cooperative-hot-swap bundles, the only legal failure policy is `OperatorIntervention`. Automatic rollback is not attempted because the app may be actively using the new version on already-committed agents.

If an operator wants to manually roll back a cooperative bundle, they can issue `POST /api/transactions/{txId}/rollback` which (a) creates Rollback intents for each agent currently at the new version, (b) those agents transition to a "cooperative rollback" state — same gating as cooperative activation: wait for `SignalSafeWindow(true)` from the app before reverting. This makes the rollback as safe as the original activation was, at the cost of taking similarly long.

------

## 9. Concurrency

### 9.1 One Active Transaction Per (BundleId, Group)

At most one active (non-terminal) transaction per `(bundleId, groupId)` pair. A second deploy targeting the same bundle on the same group while a transaction is in flight: **409 Conflict** with the existing `transactionId`. The caller must cancel or wait.

Rationale: two simultaneous transactions for the same bundle/group would race on the same set of agents and produce undefined outcomes.

A second deploy for a *different* bundle on the same group is fine — the readiness gate handles "is everything ready" by accumulating across bundles.

### 9.2 Single-Agent Transaction Membership

An agent participates in at most one *active* transaction per bundle. (Two transactions on different bundles are fine.) If the same agent is targeted by two simultaneous transactions for the same bundle, the first wins; the second is refused with 409.

### 9.3 Membership Churn During Transaction

If an agent leaves the group while a transaction is in `Preparing` or later:

- The agent is still in `RequiredAgents` (frozen at creation).
- If the agent is still online and reachable, it continues to participate. Master doesn't care about current group membership for transaction purposes.
- If the agent goes offline, the transaction stalls per §5.2 (no auto-fail; operator decides).

If an agent joins the group while a transaction is `Preparing` or later:

- The new agent is *not* added to the transaction. It's not in `RequiredAgents`.
- The new agent gets its own auto-deploy intent (Phase 3 §4.3) for the bundle, dispatched as a *non-transactional* deploy. The new agent reaches `Active` independently.
- The readiness gate (§7) checks per-agent state and naturally accounts for the new agent.

This keeps Phase 9 transactions simple. Dynamic membership doesn't break the all-or-nothing semantic; it just means the transaction's semantics apply to *the agents that were in the group when the deploy was issued*.

------

## 10. State Machine Updates

### 10.1 No New Agent-Side States

The agent's existing state vocabulary is sufficient:

- `Stage` command → `Queued → Transferring → ... → ReadyToActivate` (unchanged)
- `Activate` command → `ReadyToActivate → Activating → Active` (or via `AwaitingSafeWindow` for cooperative; unchanged)
- `Rollback` command → handled by agent; transitions the bundle's Active version pointer

The agent doesn't know about transactions. It just executes commands and reports state. The transaction is a master-side construct on top of the existing per-agent flow.

### 10.2 Agent Bundle State Transitions Added

```
Active (v43) → Activating → Active (v17, rolled back from v43)
```

Triggered by `Rollback(v17)` command. The agent's `BundleState.CurrentVersion` flips from `v43` back to `v17`. An entry is added to `ActivationHistory` with `Result: RolledBack`.

### 10.3 Master Tracks Per-Agent Stage Inside Transaction

The `AgentTxState.Stage` enum (§3.1) is a master-side view of the agent's progress through the transaction:

```
AgentTxStage.Pending     — Stage command not yet dispatched
AgentTxStage.Preparing   — Stage command dispatched, agent transferring/verifying
AgentTxStage.Ready       — Agent reported BundleState.ReadyToActivate
AgentTxStage.Activating  — Activate command dispatched, agent activating
AgentTxStage.Active      — Agent reported BundleState.Active for the target version
AgentTxStage.Failed      — Agent reported a failure state
AgentTxStage.RolledBack  — Agent acked Rollback command
```

The mapping from `BundleState` to `AgentTxStage` is:

| BundleState (per agent, per bundle)                    | AgentTxStage |
| ------------------------------------------------------ | ------------ |
| `Queued, Transferring, Transferred, Verifying, Staged` | `Preparing`  |
| `ReadyToActivate`                                      | `Ready`      |
| `Activating, AwaitingSafeWindow`                       | `Activating` |
| `Active` (with target version)                         | `Active`     |
| `TransferFailed, VerificationFailed, ActivationFailed` | `Failed`     |

------

## 11. SignalR and Persistence

### 11.1 New SignalR Activity

Only one new outbound message type from master to agent: the `Rollback` command (via existing `ReceiveCommand` with new `CommandAction.Rollback`). No new agent-to-master messages — the existing `ReportStatus` and `AckCommand` carry rollback progress.

For the parallel dispatch of `Activate` to N agents during commit: master uses `Task.WhenAll` over per-agent `Clients.Client(connId).SendAsync(...)` calls. Phase 9 does **not** use SignalR groups for this — the master needs to know which specific commands went to which agents (each command has a different `commandId`), so it sends individually. SignalR's group broadcast (`Clients.Group(groupId).SendAsync(...)`) doesn't fit.

### 11.2 Snapshot Additions

`master-state.json` gains:

```json
{
  "schemaVersion": 4,                          // bumped from Phase 5's 3
  ...
  "transactions": [
    {
      "transactionId":  "...",
      "bundleId":       "ScenarioA-Config",
      "version":        "v18",
      "scope":          {...},
      "failurePolicy":  "RollbackAll",
      "state":          "Committing",
      "requiredAgents": ["SIM-03", "SIM-04"],
      "agentStates": {
        "SIM-03": { "stage": "Active",  "previousVersion": "v17", "updatedAt": "..." },
        "SIM-04": { "stage": "Activating", "previousVersion": "v17", "updatedAt": "..." }
      },
      "subIntentIds":    ["...", "..."],
      "commitIntentIds": ["...", "..."],
      "createdAt":       "...",
      "preparedAt":      "...",
      "committedAt":     "...",
      "completedAt":     null
    }
  ]
}
```

Schema migration from Phase 8's 4 (oh — Phase 8 didn't bump; Phase 5 bumped to 3, so we go 3 → 4 here). Master detects, hydrates missing `transactions[]` as empty on first load.

### 11.3 Master Restart Recovery

```
master starts
  load snapshot → transactions[] hydrated, intents[] hydrated
  
  for each transaction in non-terminal state (Preparing, ReadyToCommit, Committing, RollingBack):
    re-evaluate AgentStates against the current FleetState reports as agents reconnect:
      for each agent in RequiredAgents:
        match against BundleState as agents Register
        update AgentStates[agentId].Stage accordingly
    if transaction.State == Preparing and all agents now Ready:
      transition to ReadyToCommit (will auto-commit when delay elapses)
    if transaction.State == Committing and all agents now Active:
      transition to Complete
  
  agent reconnect replays pending sub-intents via existing Register mechanism
  rollback intents in-flight before crash are also replayed
```

The transaction's state is reconstructible from snapshot + agent reports. No data is lost.

------

## 12. REST Endpoints

### 12.1 New Endpoints

| Method | Path                                                  | Purpose                                                      |
| ------ | ----------------------------------------------------- | ------------------------------------------------------------ |
| GET    | `/api/transactions`                                   | List transactions (filterable by state, bundle, group)       |
| GET    | `/api/transactions/{transactionId}`                   | Detail                                                       |
| DELETE | `/api/transactions/{transactionId}?rollback=true`     | Cancel; with `?rollback=true`, also revert any committed agents |
| POST   | `/api/transactions/{transactionId}/commit?force=true` | Force commit in `Preparing` state, leaving any non-Ready agents behind |
| POST   | `/api/transactions/{transactionId}/rollback`          | Operator-initiated rollback (works even from `Complete` state) |
| GET    | `/api/groups/{groupId}/readiness`                     | Session-ready status                                         |

### 12.2 `POST /api/deploy` Extended Response

Response now includes `transactionId` (or null if no coordination):

```json
{
  "intentId":       "11111111-...",
  "transactionId":  "tx-...",         // null if requireCoordination resolved to false
  "state":          "Pending",
  "resolvedAgents": ["SIM-03", "SIM-04"],
  "createdAt":      "..."
}
```

For non-coordinated deploys, `transactionId: null`. Caller can use `intentId` for status as before.

### 12.3 `GET /api/transactions/{id}` Detail

```json
{
  "transactionId":  "...",
  "bundleId":       "ScenarioA-Config",
  "version":        "v18",
  "scope":          { "type": "Group", "groupId": "session-A" },
  "failurePolicy":  "RollbackAll",
  "state":          "Committing",
  "requiredAgents": ["SIM-03", "SIM-04"],
  "agentStates": [
    { "agentId": "SIM-03", "stage": "Active",     "previousVersion": "v17", "error": null, "updatedAt": "..." },
    { "agentId": "SIM-04", "stage": "Activating", "previousVersion": "v17", "error": null, "updatedAt": "..." }
  ],
  "subIntents": [
    { "intentId": "...", "kind": "Deploy",   "agentId": "SIM-03", "state": "Complete" },
    { "intentId": "...", "kind": "Deploy",   "agentId": "SIM-04", "state": "Complete" },
    { "intentId": "...", "kind": "Activate", "agentId": "SIM-03", "state": "Complete" },
    { "intentId": "...", "kind": "Activate", "agentId": "SIM-04", "state": "Executing" }
  ],
  "createdAt":   "...",
  "preparedAt":  "...",
  "committedAt": "...",
  "completedAt": null
}
```

------

## 13. Code Sketches

### 13.1 Transaction Service

```csharp
public interface IDeploymentTransactionService
{
    Task<DeploymentTransaction> CreateAsync(DeployRequest req, IReadOnlyList<string> agents, CancellationToken ct);
    Task ObserveAgentStateAsync(string agentId, string bundleId, BundleState state, string? version, CancellationToken ct);
    Task<DeploymentTransaction?> GetAsync(string transactionId, CancellationToken ct);
    Task<IReadOnlyList<DeploymentTransaction>> ListAsync(TransactionFilter filter, CancellationToken ct);
    Task CancelAsync(string transactionId, bool rollback, CancellationToken ct);
    Task ForceCommitAsync(string transactionId, CancellationToken ct);
    Task RollbackAsync(string transactionId, CancellationToken ct);
}

public sealed class DeploymentTransactionService : IDeploymentTransactionService
{
    private readonly IIntentRepository _intents;
    private readonly IIntentDispatcher _dispatcher;
    private readonly IFleetState _fleet;
    private readonly IBundleRegistry _bundles;
    private readonly ISnapshotWriter _snapshot;
    private readonly IOperatorMessageQueue _messages;
    private readonly ConcurrentDictionary<string, DeploymentTransaction> _transactions = new();

    public async Task<DeploymentTransaction> CreateAsync(DeployRequest req, IReadOnlyList<string> agents, CancellationToken ct)
    {
        var bundle = await _bundles.GetAsync(req.BundleId, ct) ?? throw new NotFoundException();

        // Validate: cooperative + RollbackAll
        if (bundle.ActivationMode == ActivationMode.CooperativeHotSwap &&
            req.FailurePolicy == FailurePolicy.RollbackAll)
            throw new ValidationException("RollbackAll is incompatible with cooperative-hot-swap mode");

        // Validate: no overlapping active transaction for this bundle/group
        if (_transactions.Values.Any(t =>
            t.BundleId == req.BundleId &&
            t.Scope.GroupId == req.Target.GroupId &&
            !IsTerminal(t.State)))
        {
            throw new ConflictException($"Active transaction exists for {req.BundleId} on group {req.Target.GroupId}");
        }

        var txId = "tx-" + Guid.NewGuid();
        var tx = new DeploymentTransaction(
            TransactionId: txId,
            BundleId: req.BundleId,
            Version: req.Version,
            Scope: req.Target,
            FailurePolicy: req.FailurePolicy,
            State: TransactionState.Pending,
            RequiredAgents: agents.ToHashSet(),
            AgentStates: agents.ToDictionary(a => a, a => new AgentTxState(a, AgentTxStage.Pending, null, DateTimeOffset.UtcNow, null)),
            SubIntentIds: new List<string>(),
            CommitIntentIds: new List<string>(),
            CreatedAt: DateTimeOffset.UtcNow,
            PreparedAt: null, CommittedAt: null, CompletedAt: null);

        _transactions[txId] = tx;

        // Create one Stage sub-intent per agent
        foreach (var agentId in agents)
        {
            var subIntent = await _intents.CreateAsync(IntentRequest.Stage(agentId, req.BundleId, req.Version, txId), ct);
            tx = tx with { SubIntentIds = tx.SubIntentIds.Append(subIntent.IntentId).ToList() };
            await _dispatcher.DispatchAsync(subIntent, ct);
        }

        _transactions[txId] = tx with { State = TransactionState.Preparing };
        _snapshot.MarkDirty();

        return _transactions[txId];
    }

    public async Task ObserveAgentStateAsync(string agentId, string bundleId, BundleState state, string? version, CancellationToken ct)
    {
        // Find transaction(s) involving this (agent, bundle)
        foreach (var tx in _transactions.Values.Where(t =>
            t.BundleId == bundleId && t.RequiredAgents.Contains(agentId) && !IsTerminal(t.State)))
        {
            var newStage = MapBundleStateToTxStage(state, version, tx.Version);
            var prevStage = tx.AgentStates[agentId].Stage;
            if (newStage == prevStage) continue;

            // Update agent state
            var agentStates = new Dictionary<string, AgentTxState>(tx.AgentStates);
            agentStates[agentId] = tx.AgentStates[agentId] with { Stage = newStage, UpdatedAt = DateTimeOffset.UtcNow };
            var newTx = tx with { AgentStates = agentStates };

            // Possibly transition transaction state
            newTx = await EvaluateTransitionAsync(newTx, ct);

            _transactions[tx.TransactionId] = newTx;
            _snapshot.MarkDirty();
        }
    }

    private async Task<DeploymentTransaction> EvaluateTransitionAsync(DeploymentTransaction tx, CancellationToken ct)
    {
        switch (tx.State)
        {
            case TransactionState.Preparing:
                if (tx.AgentStates.Values.All(s => s.Stage == AgentTxStage.Ready))
                {
                    tx = tx with { State = TransactionState.ReadyToCommit, PreparedAt = DateTimeOffset.UtcNow };
                    // Auto-commit immediately (commitDelay support omitted for brevity)
                    tx = await BeginCommitAsync(tx, ct);
                }
                break;

            case TransactionState.Committing:
                if (tx.AgentStates.Values.All(s => s.Stage == AgentTxStage.Active))
                {
                    tx = tx with { State = TransactionState.Complete, CompletedAt = DateTimeOffset.UtcNow };
                    _messages.Enqueue(OperatorMessage.Info("Transaction complete",
                        $"Transaction {tx.TransactionId} ({tx.BundleId} {tx.Version}) committed on {tx.RequiredAgents.Count} agents."));
                }
                else if (tx.AgentStates.Values.Any(s => s.Stage == AgentTxStage.Failed))
                {
                    tx = await HandleCommitFailureAsync(tx, ct);
                }
                break;

            case TransactionState.RollingBack:
                if (tx.AgentStates.Values.All(s => s.Stage is AgentTxStage.RolledBack or AgentTxStage.Failed))
                {
                    tx = tx with { State = TransactionState.RolledBack, CompletedAt = DateTimeOffset.UtcNow };
                    _messages.Enqueue(OperatorMessage.Warning("Transaction rolled back",
                        $"Transaction {tx.TransactionId} fully rolled back."));
                }
                break;
        }
        return tx;
    }

    private async Task<DeploymentTransaction> HandleCommitFailureAsync(DeploymentTransaction tx, CancellationToken ct)
    {
        if (tx.FailurePolicy == FailurePolicy.OperatorIntervention)
        {
            _messages.Enqueue(OperatorMessage.Error("Transaction failed",
                $"Transaction {tx.TransactionId} failed; operator action required."));
            return tx with { State = TransactionState.Failed };
        }

        // RollbackAll
        var rollbackIntents = new List<string>();
        var updatedAgentStates = new Dictionary<string, AgentTxState>(tx.AgentStates);
        foreach (var (agentId, state) in tx.AgentStates)
        {
            if (state.Stage == AgentTxStage.Active && state.PreviousVersion is not null)
            {
                var intent = await _intents.CreateAsync(
                    IntentRequest.Rollback(agentId, tx.BundleId, state.PreviousVersion, tx.TransactionId), ct);
                await _dispatcher.DispatchAsync(intent, ct);
                rollbackIntents.Add(intent.IntentId);
            }
            else if (state.Stage is AgentTxStage.Activating)
            {
                // Best-effort cancel (Activate may already have arrived; agent will ignore Cancel if already Active)
                await _dispatcher.DispatchCancelAsync(agentId, tx.BundleId, ct);
                updatedAgentStates[agentId] = state with { Stage = AgentTxStage.Failed };
            }
        }
        return tx with {
            State = TransactionState.RollingBack,
            CommitIntentIds = tx.CommitIntentIds.Concat(rollbackIntents).ToList(),
            AgentStates = updatedAgentStates
        };
    }

    // BeginCommitAsync, MapBundleStateToTxStage, IsTerminal omitted.
}
```

### 13.2 Readiness Service

```csharp
public interface ISessionReadinessService
{
    Task<ReadinessResult> ComputeAsync(string groupId, CancellationToken ct);
}

public sealed class SessionReadinessService : ISessionReadinessService
{
    private readonly IFleetState _fleet;
    private readonly IBundleRegistry _bundles;

    public async Task<ReadinessResult> ComputeAsync(string groupId, CancellationToken ct)
    {
        var members = _fleet.GetAgentsInGroup(groupId);
        var requiredBundles = (await _bundles.ListAsync(ct))
            .Where(b => b.DefaultScope.Type == DeploymentScopeType.Group
                     && b.DefaultScope.GroupId == groupId
                     && b.DefaultScope.RequiredForSession)
            .ToList();

        var perAgent = new List<AgentReadinessReport>();
        foreach (var agentId in members)
        {
            var agent = _fleet.GetAgent(agentId);
            var missing = new List<MissingBundle>();

            foreach (var bundle in requiredBundles)
            {
                var latest = await _bundles.GetLatestVersionAsync(bundle.BundleId, ct);
                if (latest is null) continue;          // no published version yet; can't check
                var active = agent.GetActiveVersion(bundle.BundleId);
                if (active != latest.Version)
                {
                    var current = agent.BundleStates.TryGetValue(bundle.BundleId, out var s) ? s.State : BundleState.Unknown;
                    missing.Add(new MissingBundle(bundle.BundleId, latest.Version, active, current));
                }
            }

            perAgent.Add(new AgentReadinessReport(agentId, missing.Count == 0, missing));
        }

        return new ReadinessResult(
            GroupId: groupId,
            ReadyToStart: perAgent.All(a => a.Ready),
            AsOf: DateTimeOffset.UtcNow,
            MemberAgents: members,
            PerAgent: perAgent);
    }
}
```

### 13.3 Agent Rollback Handler

```csharp
// In StateMachineRunner

private async Task HandleRollbackAsync(Command cmd, CancellationToken ct)
{
    var bundleId = cmd.BundleId!;
    var targetVersion = cmd.Version!;       // the previous-version to revert to

    // Verify target version exists locally
    var installedVersions = await _db.GetInstalledVersionsAsync(bundleId, ct);
    if (!installedVersions.Any(iv => iv.Version == targetVersion))
    {
        await _hub.AckAsync(new CommandAck(cmd.CommandId, AckResult.Failed,
            $"Rollback target {targetVersion} not on disk"));
        return;
    }

    // For cooperative bundles, must wait for safe-window (matches activation semantics)
    var manifest = await ReadManifestAsync(bundleId, targetVersion, ct);
    if (manifest.Activation.Mode == ActivationMode.CooperativeHotSwap)
    {
        await _db.SetBundleStateAsync(bundleId, BundleState.AwaitingSafeWindow, targetVersion, ct);
        await _hub.ReportStatusAsync(bundleId, BundleState.AwaitingSafeWindow, targetVersion);
        await _db.RecordPendingRollbackAsync(cmd.CommandId, bundleId, targetVersion, ct);
        return;
    }

    // Atomic rollback
    await PerformRollbackAsync(cmd, manifest, ct);
}

private async Task PerformRollbackAsync(Command cmd, BundleManifest manifest, CancellationToken ct)
{
    await _db.SetBundleStateAsync(cmd.BundleId!, BundleState.Activating, cmd.Version, ct);
    await _hub.ReportStatusAsync(cmd.BundleId!, BundleState.Activating, cmd.Version);

    var newTarget = manifest.Container == ContainerKind.Zip
        ? _paths.ExtractedPath(cmd.BundleId!, cmd.Version!)
        : _paths.RawFilePath(cmd.BundleId!, cmd.Version!, manifest.File!.Name);

    try
    {
        await JunctionWriter.RepointAsync(_paths.ActiveJunction(cmd.BundleId!), newTarget);
        await _db.SetBundleStateAsync(cmd.BundleId!, BundleState.Active, cmd.Version, ct);
        await _db.RecordActivationHistoryAsync(cmd.BundleId!, fromVersion: /* current version */, toVersion: cmd.Version!, result: "RolledBack", ct);
        await _hub.ReportStatusAsync(cmd.BundleId!, BundleState.Active, cmd.Version);
        await _hub.AckAsync(new CommandAck(cmd.CommandId, AckResult.Complete, null));
    }
    catch (Exception ex)
    {
        await _hub.AckAsync(new CommandAck(cmd.CommandId, AckResult.Failed, ex.Message));
    }
}
```

------

## 14. Acceptance Tests

### 14.1 Happy Path

| Test                                                | Pass condition                                               |
| --------------------------------------------------- | ------------------------------------------------------------ |
| Deploy bundle to Group of 2 agents                  | Transaction created; both stage; both reach Ready; both Activate simultaneously; transaction Complete |
| Deploy with `requireCoordination: false` to a Group | No transaction created; standard per-agent deploys           |
| Group with 0 members                                | Transaction Complete immediately with empty agent set        |
| Group with 20 members                               | All 20 reach Active within timing budget                     |

### 14.2 Prepare Phase

| Test                                                         | Pass condition                                               |
| ------------------------------------------------------------ | ------------------------------------------------------------ |
| One agent's transfer fails                                   | Transaction stays Preparing; operator-message warning; other agents wait at Ready |
| Operator retries failed agent                                | Agent retries; reaches Ready; transaction proceeds to ReadyToCommit |
| Operator cancels in Preparing                                | All sub-intents Cancelled; agents revert state machines; transaction Cancelled |
| Force-commit with `?force=true` while some agents are not Ready | Ready agents commit; non-Ready agents stay at current version; transaction Complete with note of partial coverage |

### 14.3 Commit Phase — Happy

| Test                                         | Pass condition                                               |
| -------------------------------------------- | ------------------------------------------------------------ |
| All agents commit successfully               | Transaction Complete; CompletedAt set; operator-message info |
| Commit with cooperative-hot-swap             | Activate dispatched; agents enter AwaitingSafeWindow; safe-window arrives; Activate; Complete |
| Commit with cooperative + RollbackAll policy | Refused at create time (400)                                 |

### 14.4 Commit Phase — Failure

| Test                                                         | Pass condition                                               |
| ------------------------------------------------------------ | ------------------------------------------------------------ |
| RollbackAll: one agent ActivationFails                       | Other Active agents rolled back; transaction RolledBack; previous version Active everywhere |
| OperatorIntervention: one agent ActivationFails              | Transaction Failed; other agents stay at new version; operator-message error |
| Force RollbackAll with `POST /api/transactions/{id}/rollback` from Complete state | All agents rolled back to previous version                   |

### 14.5 Concurrency

| Test                                           | Pass condition                                               |
| ---------------------------------------------- | ------------------------------------------------------------ |
| Two simultaneous deploys for same bundle/group | Second returns 409 with existing transactionId               |
| Deploy for different bundles on same group     | Both transactions run independently                          |
| Agent leaves group mid-transaction             | Transaction continues; agent stays in RequiredAgents         |
| Agent joins group mid-transaction              | New agent gets independent auto-deploy intent (not part of transaction) |

### 14.6 Session Readiness

| Test                                                    | Pass condition                                               |
| ------------------------------------------------------- | ------------------------------------------------------------ |
| Group with all required bundles Active at latest        | `readyToStart: true`                                         |
| Group with one bundle mid-transaction                   | `readyToStart: false`; agent.missing lists in-progress bundle |
| Group with no members                                   | `readyToStart: true` with `memberAgents: []`                 |
| Group with no required bundles (only non-required ones) | `readyToStart: true`                                         |
| Transaction completes during a poll                     | Next poll shows `readyToStart: true`                         |

### 14.7 Restart Resilience

| Test                          | Pass condition                                               |
| ----------------------------- | ------------------------------------------------------------ |
| Master restart in Preparing   | After restart, transaction visible; agents reconnect; state reconstructed |
| Master restart in Committing  | Activate intents replayed; agents resume; transaction completes |
| Master restart in RollingBack | Rollback intents replayed; agents revert; transaction reaches RolledBack |
| Agent restart in Activating   | Agent comes back; Activate intent replayed; activates; reports Active |

------

## 15. Implementation Sequence

1. **DTOs**: `DeploymentTransaction`, `AgentTxState`, `TransactionState`, `FailurePolicy`. Round-trip serialization tests.
2. **`IDeploymentTransactionService`** in-memory store with `_transactions` ConcurrentDictionary. Tests for create, get, list.
3. **`IIntentRepository.CreateAsync`** extended to accept `transactionId` for sub-intents.
4. **Transaction creation in `POST /api/deploy`** when `target.type == "Group"` or `requireCoordination: true`. HTTP-level tests.
5. **Agent state observation**: hook `ReportStatus` and `AckCommand` to update `AgentStates`. Unit tests with simulated state reports.
6. **Prepare → ReadyToCommit transition** when all agents reach Ready. Unit tests.
7. **Commit dispatch** with parallel Activate sends. Test against multiple stub agents.
8. **Committing → Complete transition** when all reach Active.
9. **Failure detection in Committing** triggering policy evaluation.
10. **RollbackAll policy**: create Rollback intents; track RollingBack progression. Tests.
11. **OperatorIntervention policy**: stop and emit message. Tests.
12. **`Rollback` command on agent side** in `StateMachineRunner`. Tests with cooperative and non-cooperative modes.
13. **`POST /api/transactions/{id}/commit?force=true`** for partial commit. Tests.
14. **`POST /api/transactions/{id}/rollback`** for operator-initiated rollback from Complete. Tests.
15. **`DELETE /api/transactions/{id}?rollback=true`** for cancel with optional rollback.
16. **Concurrency guards**: refuse overlapping transactions for same bundle/group. Tests.
17. **Master snapshot**: persist `transactions[]`. Migration tests.
18. **Restart recovery**: reconstruct transaction state from snapshot + Register state. Integration tests.
19. **`SessionReadinessService`** and `GET /api/groups/{groupId}/readiness`. Tests.
20. **Operator UI panel** showing transactions with their state, per-agent stage, and links to sub-intents.
21. **End-to-end acceptance tests** (§14).

After step 21, Phase 9 is complete.

------

## 16. Open Questions for Implementation

A few decisions:

- **`commitDelay` default**: I noted this in §6.1 with default 0 (immediate auto-commit after Prepare). Some operators might want a brief delay to inspect before commit. Reasonable default? 0 keeps the happy path fast; 30s gives a small inspection window. My default: 0.
- **Cancellation semantics during Committing**: `DELETE /api/transactions/{id}?rollback=true` while in Committing currently treats it as a normal failure and triggers the RollbackAll path. `?rollback=false` halts the transaction in Failed state. Tell me if this should differ.
- **Force-commit from Preparing**: I've included `POST /api/transactions/{id}/commit?force=true` to commit despite some agents not being Ready. The non-Ready agents are essentially "dropped" — their Stage intents are left to complete (or fail) independently. Is this the right semantic, or should non-Ready agents be cancelled at force-commit time?
- **`failurePolicy` defaults**: I default RollbackAll for non-cooperative Group deploys, OperatorIntervention for cooperative. Tell me if you'd prefer OperatorIntervention as the safer universal default — explicit RollbackAll would then be opt-in.
- **Required-agents frozen at creation**: a transaction's `RequiredAgents` set doesn't change if membership shifts. Alternative would be to track group membership dynamically and grow/shrink the set. The frozen model is simpler and more predictable. Confirm.
- **Vacuous group readiness**: I return `readyToStart: true` for an empty group. Alternative is `false` ("nothing to start"). The session manager interprets; sync reports facts. Confirm choice.
- **Operator UI for transactions**: a new panel listing transactions and their per-agent breakdown. Read-only in Phase 9; control actions (cancel, force-commit, rollback) deferred to a Phase 11 operator-tool project. Confirm scope.

These are minor; flag preferences and the build can proceed.





# Phase 10 Detailed Design

## Fleet Sync Window Scheduler

*Companion to Architecture Design Document Revision 2, API Specification, Phases 1–9 Designs* *Targets `Phase 10` from §21 of the architecture doc* *C# / .NET 8 · ASP.NET Core · May 2026*

------

## 1. Phase Scope

### 1.1 What Phase 10 Adds

Phases 2–9 handled deployments that are reactive: operator clicks `POST /api/deploy`, or an agent joins a group, or an app calls finalize. Phase 10 adds the **scheduled, mode-gated mass sync** — overnight bulk distribution of any bundle versions agents are missing, exclusive of any active session.

Deliverables:

1. **`FleetSyncMode` global gate** — boolean state on the master. When `true`, fleet-scoped and capability-scoped auto-deploys are eligible; group memberships are refused. When `false`, the system operates in normal session-window mode.
2. **Scheduled trigger** from site config — `operational.fleetSyncWindow` (e.g. `"01:00-05:00"`); a hosted service flips the gate on at window start and off at window end.
3. **Operator-triggered toggle** — `POST /api/fleet-sync-mode` to set the gate manually (the API endpoint already documented; Phase 10 wires the behaviour).
4. **Auto-deploy on enable** — when the gate flips to `true`, master scans every fleet/capability-scoped bundle, finds agents missing the latest published version, and creates Deploy intents.
5. **Throttled cross-segment dispatch** — intents fan out with per-segment concurrency limits; relay caches pre-warm before agents start transferring.
6. **Mutual exclusion with sessions** — gate enable refused when any agent has a `currentGroupId`; `POST /api/membership` returning 409 while gate is on.
7. **Window-end behaviour** — gate flips off; in-flight transfers complete; unstarted intents stay Pending until next window (no auto-cancel).

### 1.2 Explicitly Deferred

| Concern                                                      | Deferred to |
| ------------------------------------------------------------ | ----------- |
| Garbage collection at fleet-sync edges                       | Phase 11    |
| Multiple overlapping windows per day                         | Not planned |
| Per-bundle scheduling (different bundles in different windows) | Not planned |
| Day-of-week or calendar-aware scheduling                     | Not planned |

Phase 10 supports one daily window per master. Operators who need finer scheduling either run the operator-triggered toggle from external automation or accept the simple model.

### 1.3 Definition of Done

After Phase 10 an operator can:

- Configure `fleetSyncWindow: "01:00-05:00"` in site config; reload; observe the scheduler service pick it up
- At 01:00 wall clock, see `FleetSyncMode` flip on automatically (provided no groups are active)
- Watch the master enumerate fleet/capability-scoped bundles, find agents missing latest versions, create intents, pre-warm relay caches, and dispatch transfers across all segments with bounded concurrency
- Call `POST /api/membership` while the window is open and see 409 with detail "FleetSyncMode is active"
- At 05:00, see `FleetSyncMode` flip off; in-flight transfers complete naturally; remaining Pending intents stay Pending
- Call `POST /api/fleet-sync-mode {enabled: true}` outside the scheduled window (for ad-hoc maintenance); same behaviour
- Watch the operator UI show a "Fleet Sync Active" banner with progress across segments

------

## 2. The `FleetSyncMode` Gate

### 2.1 What the Gate Controls

A single boolean on the master:

```csharp
public sealed class FleetSyncModeState
{
    public bool Enabled { get; private set; }
    public DateTimeOffset? EnabledAt { get; private set; }
    public DateTimeOffset? ScheduledOffAt { get; private set; }    // for operator visibility
    public FleetSyncEnableSource Source { get; private set; }      // Scheduled, Operator, None

    public void SetEnabled(bool enabled, DateTimeOffset? scheduledOff, FleetSyncEnableSource source);
}

public enum FleetSyncEnableSource { None, Scheduled, Operator }
```

Persisted in master snapshot. Survives restart — if the gate was on at master crash time, it stays on after restart (the scheduler reconciles at window-end time).

### 2.2 Effects of `Enabled == true`

1. **`POST /api/membership`** with non-null `groupId` returns **409 Conflict** with detail `"FleetSyncMode is active; group memberships disabled"`.
2. **Fleet-scoped and capability-scoped auto-deploys are dispatched** (§5).
3. **Operator UI** shows a banner: "Fleet Sync Active until 05:00:00".
4. **`SetFleetSyncMode(true)` SignalR push** is sent to all connected agents (advisory; lets agents log the state, optionally reduce aggressive bandwidth limits if they had any).

### 2.3 Effects of `Enabled == false`

1. **Group memberships permitted** normally.
2. **No auto-deploy** from fleet/capability scope changes (operator can still issue manual `POST /api/deploy` regardless of mode).
3. **In-flight intents from a previous window continue** — they don't pause when the gate flips off.
4. **`SetFleetSyncMode(false)` SignalR push** sent.

### 2.4 Why the Gate Survives Restart

If master restarts mid-window, `Enabled = true` persists. On reboot:

- The scheduler hosted service starts.
- It checks "is current time within today's window AND `Enabled == true`?" — if so, nothing to do, stay on.
- If current time is past window end and `Enabled` is still on, scheduler flips it off (catch-up).
- If current time is within today's window but `Enabled` was off, scheduler enables it (catch-up).

This makes the master self-correcting after restart.

### 2.5 Persistent State

In `master-state.json`:

```json
{
  "schemaVersion": 5,
  ...
  "fleetSyncMode": {
    "enabled":         true,
    "enabledAt":       "2026-05-18T01:00:00Z",
    "scheduledOffAt":  "2026-05-18T05:00:00Z",
    "source":          "Scheduled"
  }
}
```

Schema bump from Phase 9's 4 to 5. Migration: missing field defaults to `{ enabled: false, source: None }`.

------

## 3. Scheduled Trigger

### 3.1 Window Format

`operational.fleetSyncWindow` is a string `"HH:MM-HH:MM"` in 24-hour wall-clock time. Examples:

- `"01:00-05:00"` — 4-hour overnight window
- `"23:00-05:00"` — 6-hour window crossing midnight
- `""` or absent — no scheduled window (operator-only)

Time zone: the **master's local time zone** (not UTC). This matches operator intuition ("fleet sync at 1 AM" means 1 AM where the master lives). The master's TZ is fixed at process start.

### 3.2 The Scheduler

A hosted service runs continuously on the master:

```
every 60 seconds:
  cfg = SiteConfig.Operational.FleetSyncWindow
  if cfg is empty: skip
  parse cfg → startTime, endTime (TimeOnly)
  now = master local time
  inWindow = isWithinWindow(now, startTime, endTime)        // handles cross-midnight

  if inWindow and not FleetSyncMode.Enabled:
    attemptEnable("Scheduled")
  elif not inWindow and FleetSyncMode.Enabled and FleetSyncMode.Source == Scheduled:
    disable()                          # auto-off only if scheduler enabled it
```

The 60-second polling cadence is good enough — operators don't notice 60 seconds either way for a multi-hour overnight job.

The scheduler **only auto-disables** if it was the one that enabled. Operator-enabled mode persists until the operator turns it off, even if the scheduled window ends. This prevents the scheduler from accidentally killing an operator-initiated ad-hoc fleet sync.

### 3.3 When Enable Fails Due to Active Groups

```
attemptEnable(source):
  activeGroups = fleet.GetAgentsWithGroup()
  if activeGroups is non-empty:
    enqueue OperatorMessage.Warning(
      "Fleet sync window blocked",
      $"Cannot enable FleetSyncMode at window start: {activeGroups.Count} agents still in groups.")
    return false
  setEnabled(true, scheduledOffAt: endOfWindow, source)
  return true
```

The scheduler does **not** retry on failure. If the window is blocked, an operator message is written, and the scheduler waits for the next window. (Operator may manually retry via the API once groups clear.)

### 3.4 Cross-Midnight Window

```csharp
bool IsWithinWindow(DateTimeOffset now, TimeOnly start, TimeOnly end)
{
    var nowTime = TimeOnly.FromDateTime(now.LocalDateTime);
    return start <= end
        ? nowTime >= start && nowTime < end                       // normal window: 01:00-05:00
        : nowTime >= start || nowTime < end;                      // cross-midnight: 23:00-05:00
}
```

A 6-hour window 23:00-05:00 yields:

- `inWindow` from 23:00 to midnight on day N
- `inWindow` from midnight to 05:00 on day N+1

The transition across midnight is seamless because the 60-second poll keeps detecting `inWindow == true` on both sides of midnight.

### 3.5 DST and Clock Changes

For DST "spring forward" — the 02:00 → 03:00 jump that happens in many zones — a window of `01:00-05:00` loses one hour (effectively 01:00-02:00 then 03:00-05:00). The 60-second polling handles this transparently: the scheduler just sees `inWindow == true` for a shorter duration. No special handling needed.

For DST "fall back" — 02:00 → 01:00 — the window is technically 5 hours. The gate just stays on for the extra hour. Fine.

For wall-clock changes (operator runs `w32tm` mid-window): same — scheduler adapts on next 60-second tick.

------

## 4. Operator-Triggered Toggle

`POST /api/fleet-sync-mode` per the API spec §6.2:

```json
Request: { "enabled": true }

Response (200):
{
  "enabled":              true,
  "enabledAt":            "2026-05-17T22:30:00Z",
  "scheduledOffAt":       null,
  "source":               "Operator",
  "pendingFleetIntents":  0
}
```

When `enabled: true`:

- Same activeGroups check as scheduler. If groups are active, 409 with member list.
- Source set to `Operator`.
- `scheduledOffAt` set to `null` — operator must turn off manually.

When `enabled: false`:

- Always succeeds. Source set to `None`.
- In-flight intents continue.

Setting `enabled` to its current value is a no-op (idempotent, returns 200).

### 4.1 Interaction Between Operator and Scheduler

Scenario: operator manually enables at 22:00 ("emergency patch tonight"). The configured window is 01:00-05:00.

```
22:00 — operator POST /api/fleet-sync-mode {enabled:true}
         → Source = Operator, no scheduledOffAt
01:00 — scheduler sees inWindow = true, mode already Enabled
         → no-op; Source stays Operator
05:00 — scheduler sees inWindow = false, but Source == Operator
         → does NOT disable; mode stays on
08:00 — operator manually POST {enabled:false}
         → mode disabled
```

The operator's choice overrides the scheduler in both directions: operator-enabled mode is sticky until operator turns it off.

If operator wants the scheduler to take over after a manual enable, they can set source via a `?makeScheduled=true` query param — Phase 10 doesn't include this; operators who want the behaviour can disable then let the scheduler enable on the next poll.

------

## 5. Auto-Deploy on Enable

### 5.1 What Triggers the Scan

The moment `FleetSyncMode` flips to `true` (whether scheduler or operator), the master invokes the `FleetSyncDispatcher` service:

```
FleetSyncDispatcher.DispatchAsync():
  for each bundle in bundleRegistry:
    if bundle.DefaultScope.Type not in (Fleet, Capability): continue
    latest = bundle.LatestPublishedVersion
    if latest is null: continue

    scopeAgents = resolveScope(bundle.DefaultScope)
    for each agent in scopeAgents:
      activeVersion = fleet.GetActiveVersion(agent.AgentId, bundle.BundleId)
      if activeVersion == latest.Version: continue
      if existingIntent(agent, bundle, latest) is Pending or Executing: continue

      intent = createDeployIntent(agent, bundle, latest, priority: Background)
      enqueueForDispatch(intent)

  dispatchQueueAsync()
```

The dispatcher runs once on each enable. It does **not** re-run if new versions get published mid-window — those are picked up at the next enable.

(If you'd want mid-window pickup, Phase 10 can add a periodic re-scan; default is one-shot.)

### 5.2 Scope Resolution

Reusing Phase 3's `IdentityResolver`:

- `Fleet` target: every agent in `topology.agents[]` excluding `isMaster == true` and `isRelay == true`.
- `Capability` target: every agent whose `capabilities[]` contains the filter string.

The exclusion of master and relay machines matches Phase 3 §8 — these aren't application-running agents.

### 5.3 Priority and Deadlines

Fleet-sync intents are dispatched with `priority: Background`. This is informational metadata on the intent — useful in operator UI filtering — but doesn't currently change agent behaviour (the agent's runner processes incoming commands FIFO regardless of priority).

If priority-aware execution becomes useful later, the agent's runner can promote the existing per-bundle work queue to a priority queue. Phase 10 doesn't require it.

No deadline is set on fleet-sync intents. If the window closes before the transfer completes, the intent continues; no rush.

### 5.4 Idempotency on Re-Enable

If the master is restarted mid-window and `FleetSyncMode` was Enabled (so the gate stays on after restart), the dispatcher must not duplicate intents.

The duplicate-check uses `existingIntent(agent, bundle, latest) is Pending or Executing`:

- A previous-window intent that's still Pending: not duplicated.
- A previous-window intent that's `Complete` for a now-outdated version: triggers a new intent for the new latest version.
- A previous-window intent that's `Failed`: the dispatcher does **not** auto-retry; the operator must `POST /api/intents/{id}/retry`.

The dispatcher is idempotent in the "don't make duplicates" sense, not the "auto-retry failures" sense.

------

## 6. Throttled Dispatch and Relay Pre-Warming

### 6.1 The Dispatch Queue

Once the dispatcher has created intents, it pushes them into a per-segment-bounded queue:

```csharp
public sealed class SegmentDispatchQueue
{
    private readonly Dictionary<string, SemaphoreSlim> _perSegment;        // segmentId → semaphore
    private readonly Channel<Intent> _channel;

    public SegmentDispatchQueue(int maxConcurrentPerSegment)
    {
        _maxConcurrentPerSegment = maxConcurrentPerSegment;
        _channel = Channel.CreateUnbounded<Intent>();
    }

    public ValueTask EnqueueAsync(Intent intent) => _channel.Writer.WriteAsync(intent);

    public async Task RunAsync(CancellationToken ct)
    {
        await foreach (var intent in _channel.Reader.ReadAllAsync(ct))
        {
            var segment = _fleet.GetSegment(intent.AgentId) ?? "master";
            var sem = _perSegment.GetOrAdd(segment, _ => new SemaphoreSlim(_maxConcurrentPerSegment));

            _ = Task.Run(async () =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    await _dispatcher.DispatchAsync(intent, ct);
                    await WaitForIntentCompletionAsync(intent.IntentId, ct);
                }
                finally { sem.Release(); }
            }, ct);
        }
    }
}
```

`WaitForIntentCompletionAsync` polls or subscribes to the intent's terminal state. Once the agent reports `Active` or `Failed`, the semaphore is released, freeing a slot for the next intent in that segment.

Default `maxConcurrentPerSegment = 5`. Tunable via `operational.fleetSync.maxConcurrentPerSegment`.

### 6.2 Relay Pre-Warming

Phase 5 introduced `CacheWarm`. Phase 10 uses it heavily:

```
before enqueueing per-agent Stage intents for a (bundle, segment) pair:
  if segment is not master-segment AND relay is online AND relay does not already have version cached:
    create CacheWarm intent for the relay
    dispatch
```

The flow:

1. Dispatcher groups by `(bundleId, version, segmentId)`.
2. For each unique grouping where the segment is not the master's:
   - Send `CacheWarm` to the segment's relay (if online).
   - The CacheWarm runs in parallel with the per-agent Stage commands; the cache fills while agents start their transfers.
3. By the time an agent's `GET /content/...` arrives at the relay, the cache is warm (or filling, and the coordinator coalesces).

This is exactly the §5.3 behaviour from Phase 5, just at higher volume (potentially dozens of CacheWarm intents at window open).

### 6.3 Cross-Segment Parallelism

Each segment's queue runs independently with its own semaphore. 10 segments × 5 concurrent = up to 50 active transfers in parallel across the fleet.

The master is in the data path (per Phase 5's master-as-data-plane-gateway model), so master CPU/bandwidth needs to handle the aggregate. For 10 segments pulling at 100 Mbps each = 1 Gbps inbound to the master. The master's NIC and its NAS link need to handle this. In typical configurations (master co-located with NAS, 10 Gbps internal) it's fine.

If master saturation appears, lowering `maxConcurrentPerSegment` is the lever.

### 6.4 Intent Completion Reporting

The dispatcher tracks per-intent state via `IIntentRepository` events. When an intent reaches `Complete`, `Failed`, or `Cancelled`, the wrapping task in `SegmentDispatchQueue` completes and releases the semaphore.

For Phase 10, "completion" means the agent ack'd `Active` for the bundle version. Cooperative-hot-swap bundles do **not** appear in fleet-sync deploys (Group scope only) — fleet sync targets bundles in `Fleet` or `Capability` scope, which use `atomic-directory-swap` or `in-place`.

------

## 7. Mutual Exclusion With Sessions

### 7.1 Enable Refused When Groups Active

```
POST /api/fleet-sync-mode { enabled: true }
  if any agent has currentGroupId != null:
    return 409 { activeGroups: [...], agents: [...] }
  setEnabled(true, scheduledOffAt, source)
  return 200
```

### 7.2 Membership Refused When Mode Active

```
POST /api/membership { agentId/logicalNodeId, groupId: non-null }
  if FleetSyncMode.Enabled:
    return 409 { reason: "FleetSyncMode is active", scheduledOffAt: ... }
  ... existing logic
```

Clear-membership (groupId: null) is always allowed — operators may want to clear stragglers before enabling the window.

### 7.3 No Conflict During Disable

`POST /api/fleet-sync-mode { enabled: false }` always succeeds. Even if there are 100 fleet-sync intents in flight, they continue running. The gate only governs *new* admission, not existing work.

### 7.4 In-Flight Intents Across the Window Boundary

Scenario: window opens at 01:00. By 04:55, dispatcher has 1000 intents created, 50 in-flight, 600 already complete, 350 still Pending. Window closes at 05:00.

```
05:00 — FleetSyncMode.Enabled = false
         in-flight 50 intents: continue to completion
         Pending 350 intents: stay Pending
         no new intents created
06:30 — last in-flight intent completes
         (the 350 Pending intents are still there)
```

Next day 01:00 — window reopens. The dispatcher re-runs (§5.4 idempotency). The 350 still-Pending intents are not duplicated; they remain visible in the operator's intent list and get dispatched.

If any of them have an outdated target version (a new version was published in the meantime), they remain pointing at the old version. The dispatcher creates *additional* intents for the new version on those agents.

This is acceptable behaviour for fleet sync: outdated Pending intents will simply fail to do useful work (the agent is still at an older state); the operator can cancel them after the window if they want a clean intent list.

### 7.5 No Hard Cancel of Pending at Window End

I considered auto-cancelling Pending fleet-sync intents at window end. Decided against it because:

1. Some intents may have been *just* about to dispatch; the operator would lose work.
2. The next window picks them up; no real loss.
3. Auto-cancel is a destructive action; the architecture's principle is "no auto-cancel; operator decides" (§22 of architecture doc).

If `operational.fleetSync.stopAtWindowEnd: true` is set in site config, the dispatcher *does* cancel un-dispatched intents at window close. Default false. §16 lists this for confirmation.

------

## 8. Window Closure

At window close:

1. `FleetSyncMode.Enabled = false`, `Source = None`.
2. `SetFleetSyncMode(false)` SignalR push to all online agents.
3. `SegmentDispatchQueue` stops draining new intents from its channel (well, the channel keeps draining, but no new intents are pushed by the dispatcher).
4. The dispatcher's main scan task (if still running) sees mode=disabled and stops creating new intents.
5. In-flight semaphore slots stay held until their intents complete.
6. Operator-message info: `"Fleet sync window closed; X intents completed, Y intents in flight, Z Pending"`.

The semaphores live in memory. If master restarts, they reset to empty; queued tasks die. In-flight transfers on agents continue regardless (transfers are agent-driven once dispatched; master is just the data-plane source).

On restart with `FleetSyncMode.Enabled` still true (operator-initiated, didn't auto-off):

- Master rebuilds dispatcher queue from existing Pending intents
- Resumes throttled dispatch
- In-flight transfers continue naturally

------

## 9. Configuration

### 9.1 Site Config Additions

```json
"operational": {
  "fleetSyncWindow": "01:00-05:00",
  "fleetSync": {
    "maxConcurrentPerSegment": 5,
    "warmRelaysBeforeDispatch": true,
    "stopAtWindowEnd":          false
  }
}
```

| Field                      | Default            | Notes                                                      |
| -------------------------- | ------------------ | ---------------------------------------------------------- |
| `fleetSyncWindow`          | `""` (no schedule) | Wall-clock window in master TZ. Empty → operator-only.     |
| `maxConcurrentPerSegment`  | 5                  | Number of concurrent transfers per segment during dispatch |
| `warmRelaysBeforeDispatch` | true               | Send CacheWarm to relays before per-agent Stage commands   |
| `stopAtWindowEnd`          | false              | Cancel un-dispatched Pending intents at window close       |

The scheduler reads these from the canonical site config; values take effect on the next 60-second poll after `POST /api/config/reload`.

### 9.2 No New Agent-Side Configuration

Agents are passive recipients of the `SetFleetSyncMode` SignalR push. No new agent config.

------

## 10. SignalR Hub

### 10.1 `SetFleetSyncMode` (Master → Agent)

Per architecture doc §8.2. Phase 10 wires it.

```csharp
public sealed record SetFleetSyncModeMessage(
    bool Enabled,
    DateTimeOffset? ScheduledOffAt);

// Agent registers handler:
_conn.On<SetFleetSyncModeMessage>("SetFleetSyncMode", msg =>
{
    _agent.SetFleetSyncMode(msg.Enabled, msg.ScheduledOffAt);
    // For Phase 10, this is informational only — agents log the state change.
    // Future phases could use it (e.g. relax bandwidth limits during fleet sync).
});
```

The master broadcasts on every state change via `Clients.All`.

### 10.2 New Agents Connecting Mid-Window

When an agent connects during an active fleet-sync window, the `RegisterResponse` carries the current state. Per Phase 3, the `AgentSlice` already includes the master's view; we add the `fleetSyncMode` flag to it:

```csharp
public sealed record AgentSlice(
    ...,
    bool FleetSyncMode,            // new in Phase 10
    DateTimeOffset? FleetSyncOffAt);
```

The agent learns the mode at connect time without needing a separate `SetFleetSyncMode` send.

------

## 11. REST Endpoints

### 11.1 `POST /api/fleet-sync-mode`

Already documented in API spec §6.2. Phase 10 implements per §4.

### 11.2 `GET /api/fleet-sync-mode`

New endpoint, helpful for operator UI and external automation:

```
GET /api/fleet-sync-mode

Response (200):
{
  "enabled":             true,
  "enabledAt":           "2026-05-18T01:00:00Z",
  "scheduledOffAt":      "2026-05-18T05:00:00Z",
  "source":              "Scheduled",
  "configuredWindow":    "01:00-05:00",
  "agentsWithGroup":     0,
  "pendingFleetIntents": 350,
  "executingFleetIntents": 50,
  "completedFleetIntents": 600
}
```

The "fleet intents" counts are scoped to intents created during the current/most-recent fleet sync window. They reset when a new window opens.

### 11.3 `GET /api/fleet-sync-mode/preview`

Useful for the operator to see what *would* happen if the gate were enabled now:

```
GET /api/fleet-sync-mode/preview

Response (200):
{
  "wouldEnable":           true,
  "blockingGroups":        [],
  "wouldDispatchIntents":  423,
  "perSegment": [
    { "segmentId": "seg-A", "intentCount": 100, "bundleCount": 5 },
    { "segmentId": "seg-B", "intentCount": 80,  "bundleCount": 5 },
    ...
  ]
}
```

This is a dry-run of §5.1's logic. The operator sees the size of the planned work before committing.

------

## 12. Code Sketches

### 12.1 Scheduler

```csharp
public sealed class FleetSyncScheduler : BackgroundService
{
    private readonly ISiteConfigStore _config;
    private readonly IFleetSyncModeService _modeService;
    private readonly TimeProvider _time;
    private readonly ILogger<FleetSyncScheduler> _log;
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Catch-up on startup
        await ReconcileOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(Tick);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await ReconcileOnceAsync(stoppingToken);
    }

    private async Task ReconcileOnceAsync(CancellationToken ct)
    {
        var cfg = _config.Current.Operational?.FleetSyncWindow;
        if (string.IsNullOrEmpty(cfg)) return;

        if (!TryParseWindow(cfg, out var start, out var end))
        {
            _log.LogWarning("Invalid fleetSyncWindow format: {Window}", cfg);
            return;
        }

        var now = _time.GetLocalNow();
        var inWindow = IsWithinWindow(now, start, end);
        var state = _modeService.Current;

        if (inWindow && !state.Enabled)
        {
            var endTimestamp = ComputeNextEndInstant(now, end);
            await _modeService.TryEnableAsync(scheduledOffAt: endTimestamp, source: FleetSyncEnableSource.Scheduled, ct);
        }
        else if (!inWindow && state.Enabled && state.Source == FleetSyncEnableSource.Scheduled)
        {
            await _modeService.DisableAsync(ct);
        }
    }

    private static bool IsWithinWindow(DateTimeOffset now, TimeOnly start, TimeOnly end)
    {
        var t = TimeOnly.FromDateTime(now.LocalDateTime);
        return start <= end ? t >= start && t < end : t >= start || t < end;
    }

    private static DateTimeOffset ComputeNextEndInstant(DateTimeOffset now, TimeOnly end)
    {
        var nowTime = TimeOnly.FromDateTime(now.LocalDateTime);
        var date = now.LocalDateTime.Date;
        var endDate = end >= nowTime ? date : date.AddDays(1);
        return new DateTimeOffset(endDate + end.ToTimeSpan(), now.Offset);
    }

    // TryParseWindow omitted
}
```

### 12.2 Mode Service

```csharp
public interface IFleetSyncModeService
{
    FleetSyncModeState Current { get; }
    Task<TryEnableResult> TryEnableAsync(DateTimeOffset? scheduledOffAt, FleetSyncEnableSource source, CancellationToken ct);
    Task DisableAsync(CancellationToken ct);
    event EventHandler<FleetSyncModeChangedEventArgs>? Changed;
}

public sealed class FleetSyncModeService : IFleetSyncModeService
{
    private readonly IFleetState _fleet;
    private readonly IHubContext<SyncHub> _hub;
    private readonly ISnapshotWriter _snapshot;
    private readonly IFleetSyncDispatcher _dispatcher;
    private readonly IOperatorMessageQueue _messages;
    private readonly object _lock = new();

    public FleetSyncModeState Current { get; private set; } = new();

    public async Task<TryEnableResult> TryEnableAsync(DateTimeOffset? scheduledOffAt, FleetSyncEnableSource source, CancellationToken ct)
    {
        lock (_lock)
        {
            if (Current.Enabled) return TryEnableResult.AlreadyEnabled(Current);

            var blocking = _fleet.GetAgentsWithGroup().ToList();
            if (blocking.Count > 0)
            {
                _messages.Enqueue(OperatorMessage.Warning(
                    "Fleet sync window blocked",
                    $"Cannot enable FleetSyncMode: {blocking.Count} agents still in groups."));
                return TryEnableResult.Blocked(blocking);
            }

            Current = new FleetSyncModeState
            {
                Enabled = true,
                EnabledAt = DateTimeOffset.UtcNow,
                ScheduledOffAt = scheduledOffAt,
                Source = source
            };
        }

        _snapshot.MarkDirty();
        await _hub.Clients.All.SendAsync("SetFleetSyncMode",
            new SetFleetSyncModeMessage(true, scheduledOffAt), ct);
        Changed?.Invoke(this, new FleetSyncModeChangedEventArgs(Current));

        // Kick off the dispatcher
        _ = Task.Run(() => _dispatcher.DispatchAsync(ct));

        return TryEnableResult.Enabled(Current);
    }

    public async Task DisableAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            if (!Current.Enabled) return;
            Current = new FleetSyncModeState { Enabled = false, Source = FleetSyncEnableSource.None };
        }

        _snapshot.MarkDirty();
        await _hub.Clients.All.SendAsync("SetFleetSyncMode",
            new SetFleetSyncModeMessage(false, null), ct);
        Changed?.Invoke(this, new FleetSyncModeChangedEventArgs(Current));

        _messages.Enqueue(OperatorMessage.Info(
            "Fleet sync window closed",
            $"Mode disabled. {_dispatcher.GetSummary()}"));
    }
}
```

### 12.3 Dispatcher

```csharp
public interface IFleetSyncDispatcher
{
    Task DispatchAsync(CancellationToken ct);
    FleetSyncSummary GetSummary();
}

public sealed class FleetSyncDispatcher : IFleetSyncDispatcher
{
    private readonly IBundleRegistry _bundles;
    private readonly IFleetState _fleet;
    private readonly IIntentRepository _intents;
    private readonly IIntentDispatcher _intentDispatcher;
    private readonly ISiteConfigStore _config;
    private readonly ILogger<FleetSyncDispatcher> _log;
    private readonly SegmentDispatchQueue _queue;

    public async Task DispatchAsync(CancellationToken ct)
    {
        var opts = _config.Current.Operational.FleetSync;
        _log.LogInformation("Fleet sync dispatch starting");

        // Phase 1: gather all (agent, bundle, version) tuples needing an intent
        var work = new List<DispatchUnit>();
        foreach (var bundle in await _bundles.ListAsync(ct))
        {
            if (bundle.DefaultScope.Type is not (DeploymentScopeType.Fleet or DeploymentScopeType.Capability))
                continue;
            var latest = await _bundles.GetLatestVersionAsync(bundle.BundleId, ct);
            if (latest is null) continue;

            var scopeAgents = _fleet.ResolveScope(bundle.DefaultScope);
            foreach (var agentId in scopeAgents)
            {
                var active = _fleet.GetActiveVersion(agentId, bundle.BundleId);
                if (active == latest.Version) continue;

                if (await _intents.HasPendingOrExecutingAsync(agentId, bundle.BundleId, latest.Version, ct))
                    continue;

                work.Add(new DispatchUnit(agentId, bundle.BundleId, latest.Version, _fleet.GetSegment(agentId)));
            }
        }

        _log.LogInformation("Fleet sync: {Count} intents to dispatch", work.Count);

        // Phase 2: pre-warm relays
        if (opts.WarmRelaysBeforeDispatch)
        {
            var warmKeys = work
                .Where(w => w.SegmentId is not null && _fleet.GetSegment(_config.Current.Topology.Master.AgentId) != w.SegmentId)
                .Select(w => (w.BundleId, w.Version, w.SegmentId!))
                .Distinct()
                .ToList();

            foreach (var (bid, ver, seg) in warmKeys)
            {
                var relayId = _config.Current.Topology.GetRelayForSegment(seg);
                if (relayId is null) continue;
                if (_fleet.GetPresence(relayId) != Presence.Online) continue;

                var warm = await _intents.CreateAsync(IntentRequest.CacheWarm(relayId, bid, ver), ct);
                await _intentDispatcher.DispatchAsync(warm, ct);
            }
        }

        // Phase 3: enqueue per-agent intents with throttling
        foreach (var unit in work)
        {
            var intent = await _intents.CreateAsync(
                IntentRequest.Deploy(unit.AgentId, unit.BundleId, unit.Version, IntentPriority.Background),
                ct);
            await _queue.EnqueueAsync(intent, ct);
        }

        await _queue.RunAsync(ct);
    }

    public FleetSyncSummary GetSummary()
    {
        // Aggregate intent stats for the current/most-recent window
        // (impl detail: query IIntentRepository with a time filter)
        return new FleetSyncSummary(/* ... */);
    }
}

public sealed record DispatchUnit(string AgentId, string BundleId, string Version, string? SegmentId);
```

### 12.4 Membership Guard

In `MembershipService.SetMembershipAsync` (Phase 3 §10.3), add a guard at the top:

```csharp
public async Task<MembershipResult> SetMembershipAsync(MembershipRequest req, CancellationToken ct)
{
    if (req.GroupId is not null && _fleetSyncMode.Current.Enabled)
    {
        throw new ConflictException("FleetSyncMode is active; group memberships disabled",
            new { scheduledOffAt = _fleetSyncMode.Current.ScheduledOffAt });
    }

    // ... existing logic
}
```

------

## 13. Acceptance Tests

### 13.1 Scheduler

| Test                                                   | Pass condition                             |
| ------------------------------------------------------ | ------------------------------------------ |
| Window `01:00-05:00`, master starts at 02:00           | Mode enables (catch-up)                    |
| Window `01:00-05:00`, current time 06:00               | Mode does not enable                       |
| Window crosses midnight (`23:00-05:00`), test at 00:30 | Mode enables/stays on                      |
| Window changes via config reload                       | Scheduler picks up new window on next tick |
| Empty window                                           | Scheduler does nothing                     |

### 13.2 Mutual Exclusion

| Test                                                  | Pass condition                               |
| ----------------------------------------------------- | -------------------------------------------- |
| Enable while groups active                            | 409, list of blocking groups, mode stays off |
| Enable when no groups                                 | 200, mode enables                            |
| POST /api/membership while mode enabled               | 409                                          |
| POST /api/membership with groupId: null while enabled | Succeeds (clear allowed)                     |
| Disable while mode enabled                            | 200, mode disables                           |

### 13.3 Dispatcher

| Test                                                         | Pass condition                                           |
| ------------------------------------------------------------ | -------------------------------------------------------- |
| 1 fleet-scoped bundle, 10 agents missing                     | 10 intents created, dispatched within per-segment limits |
| Bundle already at latest on some agents                      | Those agents skipped; no duplicate intents               |
| Pre-existing Pending intent for same (agent, bundle, version) | Not duplicated                                           |
| Capability scope                                             | Only agents with matching capability targeted            |
| Bundle with no published version                             | Skipped silently                                         |

### 13.4 Cascade Integration

| Test                                     | Pass condition                                               |
| ---------------------------------------- | ------------------------------------------------------------ |
| 5 segments, fleet-scope bundle           | 5 CacheWarm intents created (one per non-master segment); agents pull from warm relays |
| Relay offline                            | Relay's segment falls back to direct master pull (Phase 5 behaviour) |
| Master saturation under high concurrency | `maxConcurrentPerSegment` lowered; rest queue behind         |

### 13.5 Window Close

| Test                                              | Pass condition                                         |
| ------------------------------------------------- | ------------------------------------------------------ |
| Window closes mid-transfer                        | In-flight transfers complete; new Pending intents stay |
| `stopAtWindowEnd: true`                           | Un-dispatched Pending intents cancelled at close       |
| Operator-enabled mode, scheduler tries to disable | Scheduler does not disable (source is Operator)        |

### 13.6 Restart

| Test                                       | Pass condition                                    |
| ------------------------------------------ | ------------------------------------------------- |
| Master restart mid-window                  | Mode persists; scheduler reconciles on startup    |
| Master restart, window has just ended      | Scheduler disables mode (catch-up)                |
| Pending fleet-sync intents survive restart | They're in master snapshot; visible after restart |

------

## 14. Implementation Sequence

1. **`FleetSyncModeState` record** with persistence in master snapshot. Schema migration test.
2. **`IFleetSyncModeService`** core: lock-guarded state, enable/disable, blocking-groups check. Unit tests.
3. **`POST /api/fleet-sync-mode`** endpoint wired to service. HTTP tests.
4. **`GET /api/fleet-sync-mode`** endpoint with summary. Smoke test.
5. **Membership guard** in `MembershipService`. Test attempting membership during mode.
6. **`SetFleetSyncMode` SignalR push** on state change. Test that all agents receive it.
7. **AgentSlice carries `FleetSyncMode`** flag and `FleetSyncOffAt`. Agent caches it. Test the slice flow.
8. **`FleetSyncScheduler` hosted service** with 60s polling. Tests with mocked clock.
9. **Cross-midnight window handling**. Edge-case tests.
10. **`FleetSyncDispatcher`** scan logic (§5.1). Unit tests with mock bundle registry and fleet state.
11. **`SegmentDispatchQueue`** with per-segment semaphores. Integration tests with many concurrent intents.
12. **Relay pre-warming** via CacheWarm. Tests verify CacheWarm intents created for non-master segments.
13. **`GET /api/fleet-sync-mode/preview`** dry-run endpoint. Tests.
14. **Window-close behaviour**: in-flight continuation, optional `stopAtWindowEnd`. Tests.
15. **Restart reconciliation**: scheduler catch-up on startup. Integration test.
16. **Operator UI**: banner showing "Fleet Sync Active" with progress.
17. **End-to-end acceptance tests** (§13).

After step 17, Phase 10 is complete.

------

## 15. Operator UI

The Phase 5 UI gains a top banner when `FleetSyncMode.Enabled == true`:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ ⚙ FLEET SYNC ACTIVE — until 05:00:00  (Scheduled)                          │
│   Pending: 350    Executing: 50    Complete: 600    Failed: 2              │
└─────────────────────────────────────────────────────────────────────────────┘
```

The fleet table gets a column showing intent priority (`Background` shows muted, `Normal`/`High` show colored). A new pane "Fleet Sync Progress" lists per-segment dispatch state:

```
Segment    Pending  Executing  Complete  Failed
seg-A      40       5           50        0
seg-B      30       5           45        0
seg-C      80       5           55        2
master     0        0           0         0
```

Polled at the same 5s interval as the rest of the UI.

------

## 16. Open Questions for Implementation

A few decisions:

- **`maxConcurrentPerSegment` default of 5**: tuned for typical 100 Mbps inter-segment links. For 1 Gbps, raise to 10+. For 10 Mbps, lower to 2. My default: 5; tell me if you'd prefer different.
- **`stopAtWindowEnd` default of false**: I've kept Pending intents alive across windows so they get picked up next night. Some operators prefer a clean slate each night. Confirm.
- **One-shot scan per enable**: dispatcher scans bundles once when mode enables. If a new version is published mid-window, it's not picked up. Alternative: periodic re-scan every N minutes during a window. Implementation cost is small. My default: one-shot for simplicity. Confirm.
- **Auto-clearing group membership at window start**: the architecture refuses to enable if groups are active. An alternative is to auto-clear memberships (force-disconnect sessions) on enable. I've gone with refuse — operator must clear first. The current behaviour is safer (no surprise session termination). Confirm.
- **Operator-enabled mode persisting past window end**: if operator enables at 22:00 and scheduled window is 01:00-05:00, operator-enabled mode stays on past 05:00. Until operator manually turns it off. Confirm this is the right behaviour, vs. respecting the scheduled window boundary regardless of source.
- **`Background` priority effect**: in Phase 10 it's informational only (intent metadata). Tell me if you'd want runtime effect (e.g. agents process Background intents after Normal-priority intents on the same agent). Implementation requires agent's runner to use a priority queue.
- **DST handling**: I've described that the 60s polling adapts gracefully. If you have specific test cases for DST transitions (spring forward, fall back), include them in the test plan. Confirm OK.

These are minor; flag preferences and the build can proceed.





# Phase 11 Detailed Design

## Garbage Collection

*Companion to Architecture Design Document Revision 2, API Specification, Phases 1–10 Designs* *Targets `Phase 11` from §21 of the architecture doc* *C# / .NET 8 · ASP.NET Core · May 2026*

------

## 1. Phase Scope

### 1.1 What Phase 11 Adds

Earlier phases established narrow GC capabilities — Phase 5 introduced LRU eviction for relay caches under disk pressure; Phase 8 added node-local LRU for extracted recordings. Phase 11 unifies and completes the GC story: explicit operator-driven cleanup across all three scopes (node-local, master cache, NAS), with dry-run preview and atomic run semantics.

Deliverables:

1. **Node-local bundle-version GC** — agents prune installed versions per retention policy (currently Active + previous + N most recent). Triggered by master command, not just disk watermark.
2. **Master cache GC enabled by default** — Phase 5's gated code (`Cache.GcEnabled`) becomes default-on for the master. Continuous LRU eviction runs alongside explicit GC.
3. **NAS GC** — new and substantial. Per-bundle retention (`retentionCount`); safety floors (latest pointer, any Active version, in-flight intents); trash with retention period.
4. **Dry-run preview API** — `POST /api/gc/preview` produces a comprehensive report without making changes.
5. **Run API** — `POST /api/gc/run` executes the same plan, tracked as a `Gc` intent.
6. **Scheduled trigger** — optional daily GC run via site config; defaults to manual-only.
7. **Manual eviction endpoint** — `POST /api/cache/evict` for forcing a specific master-cache entry out (operator recovery for corrupted cache, suggested in Phase 7).
8. **ChunkState row cleanup** — orphaned chunk-state rows for evicted versions get pruned.
9. **`_staging` orphan cleanup** — for both NAS recording uploads and master cache fills, stale staging directories without active intents are removed.

### 1.2 Explicitly Deferred / Not Planned

| Concern                                                      | Status                                                       |
| ------------------------------------------------------------ | ------------------------------------------------------------ |
| Recordings NAS deletion via GC                               | Out of scope — recordings are user data, operator-deletion only |
| Cross-volume archive (move evicted versions to long-term storage) | Not planned                                                  |
| Compression of old versions in place                         | Not planned                                                  |
| Time-based retention (keep anything newer than N days)       | Use `retentionCount` for now; calendar retention can be added later |

### 1.3 Definition of Done

After Phase 11 an operator can:

- Call `POST /api/gc/preview` and see exactly what would be deleted across all agents, the master cache, and the NAS, with byte counts per scope
- Compare the preview against expectations, then `POST /api/gc/run` to execute the same plan
- Watch the run progress as a `Gc` intent, with per-scope and per-agent status visible in the operator UI
- Cancel a running GC intent and verify nothing partial breaks the system
- Configure a daily scheduled GC at 06:00 and observe it runs automatically
- See a master cache file persistently corrupted, call `POST /api/cache/evict`, and trigger a fresh fill from NAS

------

## 2. The Three GC Scopes

| Scope                                                        | Owner                    | Trigger options                                      | What's protected                                             | What gets evicted                                            |
| ------------------------------------------------------------ | ------------------------ | ---------------------------------------------------- | ------------------------------------------------------------ | ------------------------------------------------------------ |
| **Agent-local** (bundle versions)                            | Each agent               | Continuous LRU on watermark + master `RunGc` command | Active version, immediately previous, anything in non-terminal state ({Staged, ReadyToActivate, AwaitingSafeWindow, ActivationPending}) | Older unreferenced versions; keep up to `keepLastN` total    |
| **Agent-local** (recordings)                                 | Each agent               | Continuous LRU on watermark + master `RunGc` command | Active uploads, in-progress downloads                        | Older extracted sessions; keep last N sessions               |
| **Master cache** (bundles)                                   | Master                   | Continuous LRU on watermark + explicit eviction      | Currently-published versions of every bundle, versions in any in-flight transfer, recently-warmed entries (within `MinCacheAgeMinutes`) | LRU evictees when over watermark or per explicit eviction    |
| **Relay cache**                                              | Each relay               | Continuous LRU on watermark (Phase 5)                | Same as master cache                                         | Same as master cache                                         |
| **NAS bundles**                                              | Master (only SMB client) | Master `RunGc` only                                  | `latest.json` target, any version Active on any agent, versions referenced by Pending/Executing intents, last N published versions per bundle (`retentionCount`) | Older versions outside retention                             |
| **NAS recordings**                                           | Operator only            | None (no automatic GC)                               | Everything                                                   | Nothing — only `DELETE /api/sessions/{id}` evicts            |
| **`_staging` orphans** (master cache, NAS recording uploads) | Master                   | Run-time sweep on master start + on each GC run      | Staging belonging to active intents                          | Staging directories older than configured threshold with no corresponding intent |
| **`_trash` retention** (relay, master, NAS)                  | Each owner               | Continuous time-based sweep                          | Trash newer than threshold                                   | Trash older than `TrashRetentionHours` (default 168 hours = 7 days for NAS bundles; 24 hours for relay cache) |

### 2.1 Continuous vs. Explicit GC

Two GC modalities coexist:

- **Continuous** — disk-watermark or time-driven, runs in the background, evicts to keep the system healthy. No operator interaction. Already in place for relay cache and node-local recordings; Phase 11 enables it for master cache and adds it for node-local bundle versions.
- **Explicit** — operator runs `POST /api/gc/run`. Walks the full retention policy across all scopes. May evict more aggressively than continuous (e.g. enforce `keepLastN` even when there's no disk pressure).

The continuous modality keeps the system from filling disks. The explicit modality cleans up "for housekeeping" — removing old versions that haven't been touched in months.

### 2.2 Idempotency

GC is idempotent. Running it twice in a row evicts on the first run; the second run finds nothing left to evict (or only items newly aged into eligibility). This means `preview` followed by `run` is safe even if other operations happened in between — the second pass just re-evaluates from current state.

------

## 3. Agent-Local Bundle GC

### 3.1 Retention Rules

For each `bundleId`, the agent keeps:

1. The version with `BundleState.State == Active`.
2. The version that was `Active` immediately before — the rollback target. Determined from `ActivationHistory` (Phase 4 §6.1 retention rule).
3. Any version in `{Queued, Transferring, Transferred, Verifying, Staged, ReadyToActivate, AwaitingSafeWindow, ActivationPending, Activating}` — these are non-terminal states.
4. Up to `keepLastN - 2` additional unreferenced versions, ordered by `InstalledVersion.InstalledAt` descending.

`keepLastN` comes from the agent's slice (`operational.agentRetention.bundles.keepLastN`), defaulting to 2 (meaning rules 1+2 cover it; no extras kept).

Anything else is evictable. The agent computes the eviction set and removes:

- `versions/{bundleId}/{version}/` directory recursively
- `InstalledVersion` row for `(bundleId, version)`
- `ChunkState` rows for `(bundleId, version)`

### 3.2 Continuous Eviction

A periodic hosted service on the agent (every 30 minutes by default):

```
on tick OR after successful Stage/Activate:
  freePercent = computeDiskFreePercent(DataRoot)
  if freePercent >= diskWatermarkPercent: skip
  walk InstalledVersion ordered by InstalledAt ascending
  for each (bundleId, version):
    if version is protected by retention rules 1-3: skip
    delete versions/{bundleId}/{version}/, related rows
    recompute freePercent
    if freePercent >= diskWatermarkPercent: stop
```

This protects against disk exhaustion. It does **not** enforce `keepLastN` — that's the explicit run's job.

### 3.3 Explicit Eviction (Triggered by Master)

The agent receives `ReceiveCommand(RunGarbageCollection)`:

```csharp
public sealed record GarbageCollectionResult(
    int BundleVersionsEvicted,
    long BundleBytesReclaimed,
    int RecordingsEvicted,
    long RecordingBytesReclaimed,
    int ChunkStateRowsPruned,
    string? FailureDetail);
```

Agent:

1. Computes the bundle eviction set per all four retention rules.
2. Computes the recording eviction set per `keepLastNSessions`.
3. Evicts each (filesystem + SQLite).
4. Returns the result in `AckCommand(commandId, Complete, JSON.Serialize(result))`.

The master collects results from all agents into the GC run summary.

### 3.4 Preview on the Agent Side?

For the preview API, the master does **not** ask each agent to compute its preview. Instead, the master uses its own view of each agent's state (from Register reports) to compute what *would* be evicted:

- Master knows each agent's `InstalledVersion` list (delivered via Register's `currentVersions[]`).
- Master knows the slice's retention policy.
- Master computes the projected eviction set per agent without a round trip.

This means preview is fast: no per-agent SignalR commands, just a few computations. Trade-off: if an agent's local state has changed since the last Register report, the preview may be slightly stale. This is acceptable — preview is an estimate, not a contract.

------

## 4. Master Cache GC

### 4.1 Enabling It

Phase 5 introduced the code; Phase 11 sets `Cache.GcEnabled: true` by default for the master. The behaviour is identical to relay cache GC:

- Continuous LRU under `WatermarkPercent`
- Skip rules: `State != Cached`, `ActiveReads > 0`, age < `MinCacheAgeMinutes`
- Move evictees to `_trash/{timestamp}-{bundleId}-{version}/`
- Trash retention: `TrashRetentionHours` (default 24)

### 4.2 Explicit Eviction Endpoint

`POST /api/cache/evict`:

```
POST /api/cache/evict
{
  "bundleId": "TerrainTextures",
  "version":  "v42"
}

Response (200):
{
  "evicted":      true,
  "wasState":     "Cached",
  "bytesReclaimed": 1073741824,
  "trashPath":    "C:/...../cache/_trash/2026-05-18-014523-TerrainTextures-v42"
}
```

When called:

- If state is `Cached`: move to `_trash/`, mark `NotCached` in coordinator.
- If state is `Filling`: refuse with 409 ("wait for fill to complete or cancel its triggering intent").
- If state is `NotCached`: return 200 with `evicted: false`.

This is the recovery path mentioned in Phase 7 §11 for persistent chunk-hash failures: operator evicts the cache entry, the next agent request triggers a fresh fill from NAS.

### 4.3 In Explicit GC

When `POST /api/gc/run` executes the master cache step, it applies the full retention rule (not just watermark):

- Keep: every (bundleId, version) for which a `PublishedVersion` exists in the registry.
- Keep: every entry being actively served (`ActiveReads > 0`).
- Keep: every entry referenced by an in-flight Stage or CacheWarm intent.
- Evict: everything else.

This is more aggressive than the continuous watermark eviction — it'll clean up cache entries for unpublished versions that have been sitting around taking space (e.g. if a publish was aborted).

------

## 5. NAS GC

The most consequential of the three scopes — these deletions destroy data that no agent currently has and that won't be re-fetchable.

### 5.1 Retention Rules per Bundle

For each `bundleId`:

```
protected_versions = {
  latest_pointer_target,
  ∪ (any version Active on any agent, per FleetState),
  ∪ (any version referenced by a Pending or Executing intent),
  ∪ (top N versions ordered by publishedAt desc, where N = bundle.retentionCount)
}

evictable = (all_versions_on_nas) - protected_versions
```

`bundle.retentionCount` defaults to 3 (Phase 1 §2.7's bundle definition default). Operators can set higher for critical bundles.

### 5.2 Eviction Mechanics

For each evictable version:

- Move `bundles/{bundleId}/versions/{version}/` to `bundles/_trash/{timestamp}-{bundleId}-{version}/`
- The version's `published.json` is part of the directory; it moves with it
- `manifest.json` and zip/raw file also move
- After move, no readers can see the version

Trash retention: configurable, default 168 hours (7 days). Operator may manually recover within the window by moving directories back from `_trash/` to `versions/`.

### 5.3 Recordings on NAS — Not Affected

NAS GC does **not** touch `/NAS/Recordings/`. Recordings are operator-deletion only via `DELETE /api/sessions/{id}` (Phase 8). The `_trash/` cleanup for recordings is its own scheduled task.

### 5.4 `_staging` Cleanup (Recording Uploads)

Phase 8 §5.3 noted "delete contents of `/NAS/Recordings/_staging/{sid}/{ln}/` for any (sid, ln) where no corresponding UploadIntent in snapshot OR corresponding UploadIntent is in Complete state". Phase 11 adds this as part of the GC run plus a periodic sweep:

```
every 6 hours OR during GC run:
  for each directory in /NAS/Recordings/_staging/{sid}/{ln}/:
    matchingIntent = intentRepository.Find(kind: UploadRecording, sessionId: sid, logicalNodeId: ln)
    if matchingIntent is null OR matchingIntent.State in {Complete, Failed, Cancelled}:
      if directory mtime > stagingOrphanHours ago:
        delete directory recursively
```

`stagingOrphanHours` defaults to 48 (configurable). Active uploads are protected.

### 5.5 Master Cache `_fill/` Cleanup

Phase 5 §2.7 specified: on master startup, `_fill/` directories are cleared. Phase 11 extends this to run during GC too — any `_fill/` directory older than 48 hours with no active fill task is removed.

### 5.6 Concurrency With Other Operations

Two specific concerns:

**Concern 1**: An agent is currently transferring a version, and GC tries to evict it from NAS.

Protected by rule 3: any version referenced by a Pending or Executing intent is protected. The intent tracking captures this. As long as the intent exists, the NAS version is safe.

**Concern 2**: A new publish lands during GC's run.

The new publish creates a new `PublishedVersion` record before any version is moved to `_trash/`. GC's retention computation reads from a snapshot of the registry taken at GC start; the new publish's version is in the registry by the time GC examines that bundle.

To avoid race conditions, the master takes a single-bundle lock when computing and applying retention for that bundle. New publishes for that bundle wait briefly (locks are sub-second). The publish CLI sees a small additional latency under heavy GC activity; acceptable.

------

## 6. Dry-Run Preview

### 6.1 Endpoint

```
POST /api/gc/preview

Body (optional, all default to "all"):
{
  "scope":    "all" | "agents" | "masterCache" | "nas",
  "agentIds": ["SIM-03", ...]      // restrict agents scope
}

Response (200):
{
  "asOf":           "2026-05-18T03:00:00Z",
  "scope":          "all",
  "nas": {
    "bundlesToDelete": [
      { "bundleId": "TerrainTextures", "version": "v38", "bytes": 12345678,
        "reason": "older than retentionCount=3 and not Active on any agent" }
    ],
    "stagingOrphans": [
      { "path": "/NAS/Recordings/_staging/5b2f.../42/", "bytes": 268435456,
        "reason": "no active upload intent; age 50h" }
    ],
    "bytesReclaimable": 280781134
  },
  "masterCache": {
    "bundlesToEvict": [
      { "bundleId": "OldDataset", "version": "v3", "bytes": 5000000,
        "reason": "no longer published; not in any in-flight intent" }
    ],
    "bytesReclaimable": 5000000
  },
  "agents": [
    {
      "agentId": "SIM-03",
      "bundlesToEvict": [
        { "bundleId": "TerrainTextures", "version": "v40", "bytes": 9876543,
          "reason": "older than keepLastN=2; not Active or rollback target" }
      ],
      "recordingsToEvict": [
        { "sessionId": "5b2f...", "bytes": 12000000000,
          "reason": "older than keepLastNSessions=5" }
      ],
      "chunkStatePruneCount": 16,
      "bytesReclaimable": 12009876543
    }
  ],
  "totalBytesReclaimable": 12294657677
}
```

### 6.2 Implementation

Master computes preview from in-memory state:

1. NAS scope: walk `BundleRegistry`, for each bundle compute retention, walk NAS directory listing to find files.
2. Master cache scope: walk cache coordinator's entries.
3. Agents scope: walk `FleetState.AgentBundleStates`, apply retention per agent's slice.

The preview report is generated synchronously. For typical fleet sizes (200 agents × tens of bundles), it completes in under a second.

The result is **not persisted** — preview is a snapshot of current state, repeatable but ephemeral.

### 6.3 What the Preview Doesn't Show

- Continuous LRU evictions that may happen between preview and run (negligible at typical operation).
- Cache `_fill/` cleanups (small).
- Time-based `_trash/` retention sweeps (also continuous).

These are bookkeeping. Preview shows the operator-significant changes only.

------

## 7. Triggered Run

### 7.1 Endpoint

```
POST /api/gc/run

Body (same shape as preview):
{
  "scope":    "all",
  "agentIds": null
}

Response (202):
{
  "intentId":       "gc-2026-05-18-030000",
  "state":          "Executing",
  "previewMatched": true,
  "startedAt":      "2026-05-18T03:00:00Z"
}
```

The response is `202 Accepted`. The full operation may take minutes (NAS scan + deletes + per-agent commands). The operator polls `GET /api/intents/{intentId}` for progress and final summary.

### 7.2 Intent Body

A `Gc` intent's body carries the plan:

```json
{
  "intentId":  "gc-2026-05-18-030000",
  "kind":      "Gc",
  "state":     "Executing",
  "createdAt": "...",
  "scope":     "all",
  "plan": {
    "nas":         { "bundleVersionCount": 12, "stagingOrphanCount": 3, "bytesReclaimable": 280781134 },
    "masterCache": { "entryCount": 1, "bytesReclaimable": 5000000 },
    "agents":      { "agentCount": 200, "totalBytesReclaimable": 1200000000000 }
  },
  "progress": {
    "nas":              "Complete",
    "masterCache":      "Complete",
    "agentsTotal":      200,
    "agentsCompleted":  187,
    "agentsFailed":     1
  },
  "completedAt":   null
}
```

`progress` updates in real-time as phases complete.

### 7.3 Execution Order

```
1. Compute plan (same as preview)
2. Snapshot the plan into the intent body
3. NAS phase:
   - For each bundle: take lock, move evictable versions to _trash/, release lock
   - Clean staging orphans
   - Clean master cache _fill/ orphans (already on master, but counted in this phase)
   - Update intent.progress.nas = "Complete"
4. Master cache phase:
   - Walk coordinator, evict per plan
   - Update intent.progress.masterCache = "Complete"
5. Agents phase:
   - For each agent:
     - Send ReceiveCommand(RunGarbageCollection)
     - Wait for AckCommand with result
     - Aggregate into intent
   - Update progress as each agent completes
6. Mark intent.state = "Complete"
7. Final summary written to operator message queue
```

Phases run sequentially. NAS first (safest order — agents are unaffected; master cache may need to drop entries that were just evicted from NAS; agents are last so they see the cleaned state).

### 7.4 Cancellation

`DELETE /api/intents/{gcIntentId}`:

- During NAS or master cache phase: best-effort stop. Already-moved evictees stay in `_trash/`.
- During agents phase: the master stops sending new RunGarbageCollection commands. Already-dispatched ones complete on their respective agents.

No rollback. Evictees in `_trash/` can be manually recovered if needed.

### 7.5 Concurrent GC Runs

Only one GC intent in `Pending` or `Executing` state at a time. A second `POST /api/gc/run` while one is in flight returns **409 Conflict** with the existing `gcIntentId`.

### 7.6 Master Restart Mid-Run

If the master crashes during a GC run, the intent is in `Executing` state in the snapshot. On restart:

- NAS evictions in progress when the crash happened may have moved some directories to `_trash/` but not yet updated the intent. The system is still consistent (the NAS truth is the file system).
- Master cache: same — coordinator state in `_trash/` reflects what was evicted; the coordinator rebuilds from on-disk presence.
- Agents phase: per-agent commands that were in flight may have completed without master receiving the ack.

On restart, master examines the in-flight GC intent. Three options:

A. **Resume**: re-execute from where it left off (re-running already-completed phases is idempotent — they'll find less to do). B. **Mark Failed**: stop, leave the intent in Failed state, operator inspects and retries if needed. C. **Auto-cancel**: mark the intent Cancelled.

Default in Phase 11: **A (resume)**. The whole flow is idempotent; re-running phases is safe.

------

## 8. Scheduled Trigger

### 8.1 Site Config

```json
"operational": {
  ...,
  "gc": {
    "scheduledTime":         "06:00",
    "trashRetentionHours":   168,
    "stagingOrphanHours":    48,
    "scope":                 "all"
  }
}
```

`scheduledTime` is wall-clock time in the master's local TZ. Format `"HH:MM"`. Empty or absent → no schedule.

`scope` is what the scheduled run targets — typically `"all"`, but operators may choose `"masterCache"` or `"agents"` for narrower automation.

### 8.2 Scheduler

A hosted service identical in shape to Phase 10's `FleetSyncScheduler`:

```
every 60 seconds:
  cfg = SiteConfig.Operational.Gc
  if cfg.ScheduledTime is empty: skip
  parse cfg.ScheduledTime → TimeOnly
  now = master local time
  if not within 60 seconds of scheduledTime AND not already run today: skip
  if a GC intent is currently Pending or Executing: skip (operator-driven takes precedence)
  trigger POST /api/gc/run internally
  mark "last run today" to prevent duplicate firing on the same minute
```

Last-run-today is tracked in master state (single timestamp `gc.lastScheduledRun`). Persisted in snapshot.

### 8.3 Interaction With Fleet Sync Window

If the scheduled GC time falls inside an active fleet sync window, GC still runs — the two are independent. GC's per-agent commands queue alongside fleet sync's deploy commands. Per-agent SignalR is FIFO; commands execute in order.

In practice, operators usually configure GC just outside the fleet sync window (e.g. fleet sync `01:00-05:00`, GC at `06:00`). This is policy, not enforced.

------

## 9. ChunkState Cleanup

Phase 7 introduced the `ChunkState` SQLite table on agents. Rows persist after transfers complete. Phase 11 prunes:

- When a bundle version is evicted from `versions/`, all `ChunkState` rows for that `(bundleId, version)` are deleted at the same time. Done within the same SQLite transaction as the InstalledVersion row deletion.

This keeps the table bounded. For a fleet operating for years, the table contains rows only for currently-installed versions.

No separate operator command — the cleanup is implicit in agent GC.

------

## 10. SignalR Hub

### 10.1 New Command Action

```csharp
public enum CommandAction
{
    Stage, Activate, Cancel, Verify, CacheWarm,
    UploadRecording, DownloadRecording, EvictSession,
    Rollback,
    RunGarbageCollection                  // new in Phase 11
}
```

Command payload:

```json
{
  "commandId":   "gc-agent-SIM-03-...",
  "action":      "RunGarbageCollection"
}
```

No additional parameters — the agent runs GC per its slice's retention policy. The result is reported via `AckCommand`:

```json
{
  "commandId": "gc-agent-SIM-03-...",
  "result":    "Complete",
  "errorDetail": null,
  "payload": {
    "bundleVersionsEvicted":  3,
    "bundleBytesReclaimed":   12345678,
    "recordingsEvicted":      2,
    "recordingBytesReclaimed": 12000000000,
    "chunkStateRowsPruned":   16,
    "failureDetail":          null
  }
}
```

`CommandAck` gains an optional `payload` field for command-specific result data:

```csharp
public sealed record CommandAck(
    string CommandId,
    AckResult Result,
    string? ErrorDetail,
    object? Payload);          // new in Phase 11
```

For non-GC commands, `Payload` is null.

------

## 11. REST Endpoints

### 11.1 Endpoints Added or Promoted in Phase 11

| Method | Path                | Status                | Purpose                          |
| ------ | ------------------- | --------------------- | -------------------------------- |
| POST   | `/api/gc/preview`   | New                   | Dry-run report                   |
| POST   | `/api/gc/run`       | New                   | Execute GC; returns intent       |
| GET    | `/api/gc/status`    | New                   | Last/current GC intent summary   |
| POST   | `/api/cache/evict`  | Promoted from Phase 7 | Force-evict a master cache entry |
| GET    | `/api/cache/status` | Existing (Phase 4)    | Now includes GC-related fields   |

### 11.2 `GET /api/gc/status`

```json
{
  "currentRun": {
    "intentId":       "gc-2026-05-18-030000",
    "state":          "Executing",
    "startedAt":      "2026-05-18T03:00:00Z",
    "progress":       { "nas": "Complete", "masterCache": "Executing", ... }
  },
  "lastRun": {
    "intentId":       "gc-2026-05-17-030000",
    "state":          "Complete",
    "startedAt":      "2026-05-17T03:00:00Z",
    "completedAt":    "2026-05-17T03:12:00Z",
    "bytesReclaimed": 1234567890
  },
  "scheduledNext": "2026-05-19T06:00:00Z"
}
```

`currentRun` is null when no GC is in flight. `lastRun` is the most recent completed run.

------

## 12. Code Sketches

### 12.1 GC Plan Builder

```csharp
public interface IGcPlanBuilder
{
    Task<GcPlan> BuildAsync(GcScope scope, IReadOnlyList<string>? agentIds, CancellationToken ct);
}

public sealed record GcPlan(
    DateTimeOffset AsOf,
    GcScope Scope,
    NasGcPlan Nas,
    MasterCacheGcPlan MasterCache,
    IReadOnlyList<AgentGcPlan> Agents);

public sealed class GcPlanBuilder : IGcPlanBuilder
{
    private readonly IBundleRegistry _bundles;
    private readonly IFleetState _fleet;
    private readonly IIntentRepository _intents;
    private readonly ICacheCoordinator _masterCache;
    private readonly INasReader _nas;
    private readonly ISiteConfigStore _config;

    public async Task<GcPlan> BuildAsync(GcScope scope, IReadOnlyList<string>? agentIds, CancellationToken ct)
    {
        var asOf = DateTimeOffset.UtcNow;
        var nas = scope.Includes(GcScope.Nas) ? await BuildNasPlanAsync(ct) : NasGcPlan.Empty;
        var cache = scope.Includes(GcScope.MasterCache) ? BuildMasterCachePlan() : MasterCacheGcPlan.Empty;
        var agents = scope.Includes(GcScope.Agents) ? await BuildAgentPlansAsync(agentIds, ct) : Array.Empty<AgentGcPlan>();
        return new GcPlan(asOf, scope, nas, cache, agents);
    }

    private async Task<NasGcPlan> BuildNasPlanAsync(CancellationToken ct)
    {
        var evictable = new List<NasVersionEvict>();
        var bytesReclaimable = 0L;

        foreach (var bundle in await _bundles.ListAsync(ct))
        {
            // Compute protected set
            var protectedVersions = new HashSet<string>(StringComparer.Ordinal);

            var latest = await _bundles.GetLatestVersionAsync(bundle.BundleId, ct);
            if (latest is not null) protectedVersions.Add(latest.Version);

            foreach (var agent in _fleet.GetAllAgents())
                if (agent.GetActiveVersion(bundle.BundleId) is { } v)
                    protectedVersions.Add(v);

            foreach (var intent in await _intents.GetInFlightForBundleAsync(bundle.BundleId, ct))
                protectedVersions.Add(intent.Version);

            var recent = (await _bundles.GetPublishedVersionsAsync(bundle.BundleId, ct))
                .OrderByDescending(v => v.PublishedAt)
                .Take(bundle.RetentionCount)
                .Select(v => v.Version);
            protectedVersions.UnionWith(recent);

            // Scan NAS for actual on-disk versions; subtract protected
            var onDisk = _nas.ListVersionsOnDisk(bundle.BundleId);
            foreach (var version in onDisk)
            {
                if (protectedVersions.Contains(version)) continue;
                var size = _nas.ComputeVersionSize(bundle.BundleId, version);
                evictable.Add(new NasVersionEvict(bundle.BundleId, version, size,
                    "older than retentionCount and not Active or pending"));
                bytesReclaimable += size;
            }
        }

        var stagingOrphans = await ComputeStagingOrphansAsync(ct);
        bytesReclaimable += stagingOrphans.Sum(o => o.Bytes);

        return new NasGcPlan(evictable, stagingOrphans, bytesReclaimable);
    }

    private MasterCacheGcPlan BuildMasterCachePlan()
    {
        var protectedKeys = new HashSet<(string, string)>();

        // Currently published
        foreach (var bundleId in _bundles.GetBundleIdsSync())
        foreach (var v in _bundles.GetPublishedVersionsSync(bundleId))
            protectedKeys.Add((bundleId, v.Version));

        // In-flight intents
        foreach (var intent in _intents.GetInFlightSync())
            if (intent.BundleId is not null && intent.Version is not null)
                protectedKeys.Add((intent.BundleId, intent.Version));

        var evictable = new List<MasterCacheEvict>();
        foreach (var entry in _masterCache.ListEntries())
        {
            if (entry.State != CacheFillState.Cached) continue;
            if (entry.ActiveReads > 0) continue;
            if (protectedKeys.Contains((entry.BundleId, entry.Version))) continue;
            evictable.Add(new MasterCacheEvict(entry.BundleId, entry.Version, entry.TotalBytes,
                "not published and not in-flight"));
        }
        return new MasterCacheGcPlan(evictable, evictable.Sum(e => e.Bytes));
    }

    private async Task<IReadOnlyList<AgentGcPlan>> BuildAgentPlansAsync(IReadOnlyList<string>? agentIds, CancellationToken ct)
    {
        var targets = agentIds ?? _fleet.GetAllAgents().Select(a => a.AgentId).ToList();
        var plans = new List<AgentGcPlan>();

        foreach (var agentId in targets)
        {
            var agent = _fleet.GetAgent(agentId);
            if (agent is null) continue;
            var slice = _config.ComputeSlice(agentId);

            var bundleEvictions = ComputeAgentBundleEvictions(agent, slice);
            var recordingEvictions = ComputeAgentRecordingEvictions(agent, slice);
            var chunkStateRows = CountChunkStateRowsForEvicted(agent, bundleEvictions);

            plans.Add(new AgentGcPlan(agentId, bundleEvictions, recordingEvictions, chunkStateRows));
        }
        return plans;
    }

    // ComputeAgentBundleEvictions, ComputeAgentRecordingEvictions, etc. omitted
}
```

### 12.2 GC Runner

```csharp
public interface IGcRunner
{
    Task<string> RunAsync(GcPlan plan, CancellationToken ct);    // returns intentId
}

public sealed class GcRunner : IGcRunner
{
    private readonly IIntentRepository _intents;
    private readonly INasGcExecutor _nasExec;
    private readonly IMasterCacheGcExecutor _cacheExec;
    private readonly IIntentDispatcher _dispatcher;
    private readonly IOperatorMessageQueue _messages;
    private readonly ILogger<GcRunner> _log;

    public async Task<string> RunAsync(GcPlan plan, CancellationToken ct)
    {
        // Concurrency guard
        var inFlight = await _intents.GetInFlightGcAsync(ct);
        if (inFlight is not null)
            throw new ConflictException($"GC already in flight: {inFlight.IntentId}");

        var intent = await _intents.CreateGcIntentAsync(plan, ct);
        _ = Task.Run(() => ExecuteAsync(intent.IntentId, plan, CancellationToken.None));
        return intent.IntentId;
    }

    private async Task ExecuteAsync(string intentId, GcPlan plan, CancellationToken ct)
    {
        try
        {
            // NAS phase
            if (plan.Scope.Includes(GcScope.Nas))
            {
                await _nasExec.ExecuteAsync(plan.Nas, ct);
                await _intents.UpdateGcProgressAsync(intentId, "nas", "Complete", ct);
            }

            // Master cache phase
            if (plan.Scope.Includes(GcScope.MasterCache))
            {
                await _cacheExec.ExecuteAsync(plan.MasterCache, ct);
                await _intents.UpdateGcProgressAsync(intentId, "masterCache", "Complete", ct);
            }

            // Agents phase
            if (plan.Scope.Includes(GcScope.Agents))
            {
                var totalAgents = plan.Agents.Count;
                var completed = 0;
                var failed = 0;

                foreach (var agentPlan in plan.Agents)
                {
                    var cmdResult = await SendAgentGcCommandAsync(agentPlan.AgentId, ct);
                    if (cmdResult.Success) completed++; else failed++;
                    await _intents.UpdateGcAgentProgressAsync(intentId, completed, failed, totalAgents, ct);
                }
            }

            await _intents.MarkCompleteAsync(intentId, ct);
            _messages.Enqueue(OperatorMessage.Info("GC complete",
                $"GC intent {intentId} reclaimed approximately {plan.TotalBytesReclaimable / (1L<<30):N0} GB"));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "GC intent {Id} failed", intentId);
            await _intents.MarkFailedAsync(intentId, ex.Message, ct);
            _messages.Enqueue(OperatorMessage.Error("GC failed",
                $"GC intent {intentId} failed: {ex.Message}"));
        }
    }

    private async Task<(bool Success, GarbageCollectionResult? Result)> SendAgentGcCommandAsync(string agentId, CancellationToken ct)
    {
        var intent = await _intents.CreateAsync(IntentRequest.RunGc(agentId), ct);
        await _dispatcher.DispatchAsync(intent, ct);
        return await _intents.WaitForCompletionAsync(intent.IntentId, TimeSpan.FromMinutes(30), ct);
    }
}
```

### 12.3 NAS GC Executor

```csharp
public sealed class NasGcExecutor : INasGcExecutor
{
    private readonly INasWriter _nas;
    private readonly IBundleRegistry _bundles;
    private readonly ILogger<NasGcExecutor> _log;

    public async Task ExecuteAsync(NasGcPlan plan, CancellationToken ct)
    {
        var grouped = plan.VersionEvictions.GroupBy(e => e.BundleId);

        foreach (var bundleGroup in grouped)
        {
            using var _ = await _bundles.AcquireBundleLockAsync(bundleGroup.Key, ct);

            foreach (var eviction in bundleGroup)
            {
                var versionDir = _nas.VersionDir(eviction.BundleId, eviction.Version);
                var trashDir = _nas.TrashDir(eviction.BundleId, eviction.Version, DateTimeOffset.UtcNow);

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(trashDir)!);
                    Directory.Move(versionDir, trashDir);
                    _log.LogInformation("Evicted {Bundle}/{Version} → {Trash} ({Bytes} bytes)",
                        eviction.BundleId, eviction.Version, trashDir, eviction.Bytes);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to evict {Bundle}/{Version}", eviction.BundleId, eviction.Version);
                }
            }
        }

        foreach (var orphan in plan.StagingOrphans)
        {
            try { Directory.Delete(orphan.Path, recursive: true); }
            catch (Exception ex) { _log.LogWarning(ex, "Failed to delete staging orphan {Path}", orphan.Path); }
        }
    }
}
```

### 12.4 Agent GC Handler

```csharp
private async Task HandleRunGarbageCollectionAsync(Command cmd, CancellationToken ct)
{
    try
    {
        var slice = _slice.Current;
        var retention = slice.Operational?.AgentRetention ?? AgentRetention.Default;

        var bundleEvictions = await ComputeBundleEvictionsAsync(retention, ct);
        var recordingEvictions = await ComputeRecordingEvictionsAsync(retention, ct);

        long bytesReclaimed = 0;
        var chunkRowsPruned = 0;

        // Bundle evictions
        foreach (var (bundleId, version) in bundleEvictions)
        {
            var size = ComputeVersionDirSize(bundleId, version);
            Directory.Delete(_paths.VersionDir(bundleId, version), recursive: true);
            await _db.DeleteInstalledVersionAsync(bundleId, version, ct);
            var pruned = await _db.DeleteChunkStateRowsAsync(bundleId, version, ct);
            chunkRowsPruned += pruned;
            bytesReclaimed += size;
        }

        // Recording evictions (Phase 8 logic; now also explicit-driven)
        foreach (var (sessionId, logicalNodeId) in recordingEvictions)
        {
            var size = ComputeRecordingSize(sessionId, logicalNodeId);
            Directory.Delete(_paths.RecordingDir(sessionId, logicalNodeId), recursive: true);
            await _db.DeleteExtractedRecordingAsync(sessionId, logicalNodeId, ct);
            bytesReclaimed += size;
        }

        var result = new GarbageCollectionResult(
            BundleVersionsEvicted: bundleEvictions.Count,
            BundleBytesReclaimed: bytesReclaimed,
            RecordingsEvicted: recordingEvictions.Count,
            RecordingBytesReclaimed: 0,    // tracked separately if desired
            ChunkStateRowsPruned: chunkRowsPruned,
            FailureDetail: null);

        await _hub.AckAsync(new CommandAck(cmd.CommandId, AckResult.Complete, null, result));
        await _db.MarkCommandDoneAsync(cmd.CommandId, ct);
    }
    catch (Exception ex)
    {
        await _hub.AckAsync(new CommandAck(cmd.CommandId, AckResult.Failed, ex.Message, null));
    }
}
```

------

## 13. Acceptance Tests

### 13.1 Preview

| Test                                              | Pass condition                                               |
| ------------------------------------------------- | ------------------------------------------------------------ |
| Empty fleet, no bundles                           | Preview returns empty plan, totalBytesReclaimable=0          |
| Fleet with old versions on agents, none Active    | Versions older than keepLastN reported as evictable per agent |
| NAS has old versions, not Active anywhere         | Versions outside retentionCount reported as evictable        |
| Master cache has entries for unpublished versions | Entries reported as evictable                                |
| Scope=agents                                      | Only agent plans populated; nas and masterCache empty        |
| Scope=nas                                         | Only nas plan; agents and masterCache empty                  |
| Specific agentIds                                 | Only those agents in the plan                                |

### 13.2 Run

| Test                             | Pass condition                                               |
| -------------------------------- | ------------------------------------------------------------ |
| Run after preview                | Same bytes reclaimed (modulo small concurrent activity)      |
| Two concurrent /api/gc/run calls | Second returns 409                                           |
| Run includes all scopes          | NAS phase completes, then master cache, then agents in order |
| Agent fails to run GC            | Other agents complete; intent marked partially-failed with the failed agent listed |
| Cancel mid-run                   | NAS / cache already done stay done; remaining agents not commanded |
| Master restart during run        | Resume from where left off; idempotent re-execution          |

### 13.3 NAS GC

| Test                                        | Pass condition                                              |
| ------------------------------------------- | ----------------------------------------------------------- |
| Bundle with retentionCount=3 and 6 versions | 3 oldest moved to _trash/ (assuming none Active or pending) |
| Bundle's `latest.json` target               | Never evicted, even if older than retentionCount            |
| Version Active on any agent                 | Never evicted                                               |
| Concurrent publish during GC                | New publish lands; GC's snapshot doesn't see it; race-safe  |
| Staging orphan older than 48h               | Deleted                                                     |
| `_trash/` retention                         | Items older than 168h removed in periodic sweep             |

### 13.4 Master Cache

| Test                                                   | Pass condition               |
| ------------------------------------------------------ | ---------------------------- |
| Cache entry with ActiveReads > 0                       | Not evicted (busy)           |
| Cache entry recently warmed (under MinCacheAgeMinutes) | Not evicted                  |
| `POST /api/cache/evict` for Cached entry               | Evicted; trash path returned |
| `POST /api/cache/evict` for Filling entry              | 409                          |
| `POST /api/cache/evict` for non-existent entry         | 200 with evicted=false       |

### 13.5 Agent GC

| Test                                | Pass condition                             |
| ----------------------------------- | ------------------------------------------ |
| Agent has 4 versions, keepLastN=2   | Active + previous kept; rest evicted       |
| Active version                      | Never evicted                              |
| Rollback target (previous Active)   | Never evicted                              |
| Version in Staged state             | Never evicted                              |
| ChunkState rows for evicted version | Pruned                                     |
| Continuous LRU under watermark      | Triggers eviction without explicit command |

### 13.6 Scheduling

| Test                                        | Pass condition                             |
| ------------------------------------------- | ------------------------------------------ |
| Scheduled at 06:00                          | Runs at 06:00                              |
| Master starts at 07:00                      | Already-ran today flag set; doesn't re-run |
| Scheduled run while operator GC in progress | Skipped (operator takes precedence)        |
| Empty scheduledTime                         | Never auto-runs                            |

------

## 14. Implementation Sequence

1. **DTOs**: `GcPlan`, `NasGcPlan`, `MasterCacheGcPlan`, `AgentGcPlan`, `GcScope`. Round-trip serialization tests.
2. **`IGcPlanBuilder`** core logic: NAS plan, master cache plan, per-agent plans. Unit tests with fake registry/fleet/intents.
3. **`POST /api/gc/preview`** endpoint wired to plan builder. HTTP tests.
4. **`INasGcExecutor`** with bundle lock + atomic-move to trash. Tests with simulated NAS layout.
5. **`IMasterCacheGcExecutor`** reusing the existing Phase 5 trash machinery.
6. **`Gc` intent kind** in `IIntentRepository`. Migration tests.
7. **`IGcRunner`** orchestrating phases. Unit tests for ordering and progress updates.
8. **`POST /api/gc/run`** endpoint wired to runner. Concurrency guard test (409 on duplicate).
9. **`GET /api/gc/status`** endpoint. Smoke tests.
10. **Agent `RunGarbageCollection` handler** in `StateMachineRunner`. Tests on real agent.db.
11. **ChunkState row pruning** integrated into agent GC. Tests verify rows match installed versions.
12. **Agent recording GC integration** with explicit command path (Phase 8 had continuous; Phase 11 adds explicit). Tests.
13. **`POST /api/cache/evict`** endpoint. Tests for all three states.
14. **`master cache GcEnabled = true` flip** with continuous LRU on master. Tests verify eviction at watermark.
15. **`GcScheduler` hosted service** with 60s polling. Tests with mocked clock.
16. **Master restart resumption** for in-flight GC intent. Integration test.
17. **Operator UI panel**: "Garbage Collection" with preview / run / cancel controls and per-scope progress.
18. **End-to-end acceptance tests** (§13).

After step 18, Phase 11 is complete — and so is the full system.

------

## 15. Operator UI

Phase 11 adds a "Maintenance" tab to the operator UI:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ Garbage Collection                                                          │
│                                                                             │
│ Last run:    2026-05-17 03:00 UTC  · Complete · reclaimed 12.3 GB           │
│ Next scheduled: 2026-05-18 06:00                                            │
│                                                                             │
│ [Preview] [Run Now]    Scope: [All ▼]    Agents: [All ▼]                    │
│                                                                             │
│ ── Preview Results (2026-05-18 02:45) ──                                    │
│                                                                             │
│ NAS                       12 versions, 3 staging orphans  →   85.4 GB       │
│ Master cache              1 entry                          →     5 MB       │
│ Agents (200)              avg 4 versions / 2 recordings    →   2.4 TB       │
│ ─────────────────────────────────────────────────────────                   │
│ Total reclaimable:                                            2.48 TB       │
│                                                                             │
│ [Execute This Plan]                                                         │
└─────────────────────────────────────────────────────────────────────────────┘
```

When a run is in progress, the UI shows live progress for each phase. Per-agent breakdown is available via drill-down.

------

## 16. Open Questions for Implementation

A few decisions:

- **Master restart mid-GC behaviour**: I default to **resume** (re-execute idempotently). Alternative: mark Failed and require operator retry. Resume is more forgiving; Failed is more conservative (operator sees explicit signal that GC was interrupted). My default: resume. Confirm.
- **`POST /api/gc/run` returning 202 vs synchronous**: I've made it asynchronous (202 with intent). For very small GC plans (just master cache, a few entries), the operator might prefer synchronous. Alternative: query param `?async=false` for synchronous up to a timeout. My default: always async, simpler model. Confirm.
- **Agent recording GC: continuous (Phase 8) vs explicit (Phase 11) overlap**: continuous keeps disk healthy; explicit applies the full policy. Both coexist. Tell me if you'd want one to disable the other (e.g. only continuous, never explicit, since they apply the same rules). My default: keep both — continuous is "always-on watchdog", explicit is "do it now even without disk pressure".
- **Scheduled GC and fleet sync overlap**: in §8.3 I noted they coexist. If a deployment is ongoing during the GC run, the GC's per-agent commands queue with the deploy's commands. No special handling. Confirm OK.
- **`trashRetentionHours` defaults**: 168 (7 days) for NAS bundles, 24 for relay/master cache, 168 for NAS recordings. Recordings get 7 days because they're user data; cache trash is 24 hours because the contents can be regenerated. Confirm or adjust.
- **NAS `_trash` recovery procedure**: I've noted operators can manually `Move-Item` from `_trash/` back to `versions/`. Should there be a `POST /api/gc/restore` endpoint to do this with master state coordination? Small but useful. Defer or add now? My default: defer; manual recovery is rare and the manual file-move is straightforward when needed.
- **GC of partially-uploaded recording staging on the master cache side**: Phase 11 sweeps `_staging/` orphans on NAS for recording uploads. The master cache's `_fill/` has its own startup-cleanup (Phase 5) and now also GC-time cleanup. Both run on the master. Single sweep that handles all staging-like directories, or keep them separate? My default: keep them separate — each has its own context.

These are minor; flag preferences and the build can proceed.