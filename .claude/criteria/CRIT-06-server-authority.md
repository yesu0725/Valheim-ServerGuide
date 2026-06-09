# CRIT-06 — Server Authority & RPC Sync

**File:** `src/Net/GuidanceSync.cs`

---

## Process Roles

| Process type | `Application.isBatchMode` | `ZNet.IsServer()` | YAML loader | Config source |
|---|---|---|---|---|
| Dedicated server | `true` | `true` | Started at `Plugin.Awake` | Own `guidance.yaml` |
| Host (local multiplayer) | `false` | `true` | Started at `ZNet.Awake` | Own `guidance.yaml` |
| Single-player | `false` | `true` | Started at `ZNet.Awake` | Own `guidance.yaml` |
| Pure client | `false` | `false` | Never started | Pushed by server via RPC |

A pure client never generates or reads a local `guidance.yaml`. Its `Plugin.CurrentConfig` is set entirely by the server's push.

---

## RPC Names

All registered on `ZRoutedRpc.instance`:

| RPC name | Direction | Payload | Purpose |
|---|---|---|---|
| `VSG_SyncConfig` | Server → Client | `ZPackage` (YAML string) | Push full config on join / hot-reload |
| `VSG_TriggerGlobal` | Client → Server | `string id, string playerName` | Client requests a global-scope fire |
| `VSG_PlayGlobal` | Server → All | `string id, string playerName` | Server broadcasts "show this now" |
| `VSG_AnnounceRequest` | Client → Server | `string id, string playerName` | Player-scope discord announcement |
| `VSG_AdminResetGlobal` | Client → Server | `string id` | Admin client requests global key removal |

---

## Registration Guard

`ZRoutedRpc.Register` throws `ArgumentException` if the same name is registered twice.
`_rpcsBound` (static bool) prevents double-registration:

```csharp
private static void EnsureRegistered()
{
    if (_rpcsBound) return;
    if (ZRoutedRpc.instance == null) return;
    // ... Register all RPCs ...
    _rpcsBound = true;
}
```

`_rpcsBound` is reset to `false` in the `ZNet.OnDestroy` postfix so the next world session re-registers on a fresh `ZRoutedRpc` instance.

---

## YAML Loader Lifecycle

```
Plugin.Awake()
  ├─ isBatchMode == true  → EnsureLoaderStarted() immediately
  └─ isBatchMode == false → defer

ZNet.Awake postfix
  ├─ EnsureRegistered()   (always)
  ├─ IsServer() == true   → EnsureLoaderStarted()
  └─ IsServer() == false  → log "waiting for server config push"

ZNet.OnDestroy postfix
  ├─ _rpcsBound = false
  └─ isBatchMode == false → ShutdownLoader(); CurrentConfig = Empty
     (dedicated server keeps loader alive across ZNet sessions)
```

---

## Config Push on Peer Join

`ZNet.RPC_PeerInfo` postfix (server-side only):
1. Gets the newly joined peer via `__instance.GetPeer(rpc)`.
2. Calls `EnsureRegistered()` (idempotent).
3. Calls `SendToPeer(peer.m_uid, Plugin.CurrentConfig)`.

This ensures every client always has the current config, even if they join after a hot-reload.

---

## Server Authority Guard (Local Edits)

In `Plugin.OnConfigChanged` (fired by the local `FileSystemWatcher`):

```csharp
var authoritative = ZNet.instance == null || ZNet.instance.IsServer();
if (!authoritative) return; // silently ignore — server's config takes priority
```

A pure client with a local `guidance.yaml` (e.g., from a previous hosting session) will never apply that local file while connected as a client.

---

## Serialization

- **Serialize:** `SerializerBuilder` with `UnderscoredNamingConvention` → YAML string.
- **Deserialize:** `DeserializerBuilder` with `UnderscoredNamingConvention` + `IgnoreUnmatchedProperties` → `GuidanceConfig`.
- The YAML is sent as a plain string inside a `ZPackage`.
- YamlDotNet.dll is NOT deployed with the plugin (Jötunn's transitive dep provides it).

---

## Criteria

- [ ] Dedicated server starts the YAML loader in `Plugin.Awake` (before any ZNet context).
- [ ] Host/single-player starts the YAML loader in `ZNet.Awake` postfix when `IsServer() == true`.
- [ ] Pure client never starts the YAML loader; its config comes entirely from the server push.
- [ ] `_rpcsBound` guard prevents `ArgumentException` on double-registration.
- [ ] `_rpcsBound` is reset to `false` in `ZNet.OnDestroy` so re-joining a world re-registers RPCs.
- [ ] Every newly joining peer receives the current config via `VSG_SyncConfig` in the `RPC_PeerInfo` postfix.
- [ ] A hot-reloaded config is broadcast to ALL currently connected clients via `BroadcastToClients`.
- [ ] Discord webhook URL is never included in `VSG_SyncConfig` or any other RPC payload.
- [ ] On dedicated server, `ShutdownLoader` is NOT called in `ZNet.OnDestroy` — the loader persists for the server's lifetime.
