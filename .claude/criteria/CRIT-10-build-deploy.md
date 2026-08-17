# CRIT-10 — Build & Deploy Targets

**File:** `src/ValheimServerGuide.csproj`

---

## Project Properties

| Property | Value |
|---|---|
| TargetFramework | `net48` |
| AssemblyName | `ValheimServerGuide` |
| LangVersion | `latest` |
| Nullable | `disable` |
| Version | `0.1.0` |
| CopyLocalLockFileAssemblies | `true` |
| NoWarn | `CS0436;NU1701` |

---

## Assembly References

All `<Private>false</Private>` (not copied to output — loaded from game at runtime).

| Assembly | Path | Notes |
|---|---|---|
| `assembly_valheim` | `$(ManagedDir)\assembly_valheim.dll` | `Publicize=true` via BepInEx.AssemblyPublicizer |
| `assembly_utils` | `$(ManagedDir)\assembly_utils.dll` | ZPackage vector types |
| `assembly_guiutils` | `$(ManagedDir)\assembly_guiutils.dll` | GUI utilities |
| `UnityEngine` | `$(ManagedDir)\UnityEngine.dll` | Core |
| `UnityEngine.CoreModule` | `$(ManagedDir)\UnityEngine.CoreModule.dll` | |
| `UnityEngine.PhysicsModule` | `$(ManagedDir)\UnityEngine.PhysicsModule.dll` | |
| `UnityEngine.AnimationModule` | `$(ManagedDir)\UnityEngine.AnimationModule.dll` | |
| `UnityEngine.UI` | `$(ManagedDir)\UnityEngine.UI.dll` | |
| `UnityEngine.IMGUIModule` | `$(ManagedDir)\UnityEngine.IMGUIModule.dll` | |
| `UnityEngine.InputLegacyModule` | `$(ManagedDir)\UnityEngine.InputLegacyModule.dll` | |
| `UnityEngine.UnityWebRequestModule` | `$(ManagedDir)\UnityEngine.UnityWebRequestModule.dll` | `UnityWebRequest` (Discord) |
| `UnityEngine.UIModule` | `$(ManagedDir)\UnityEngine.UIModule.dll` | `CanvasGroup` (intro overlay) |
| `BepInEx` | `$(BepInExDir)\core\BepInEx.dll` | |
| `0Harmony` | `$(BepInExDir)\core\0Harmony.dll` | |
| `Jotunn` | `$(BepInExDir)\plugins\ValheimModding-Jotunn\Jotunn.dll` | |

---

## Package References

| Package | Version | Notes |
|---|---|---|
| `BepInEx.AssemblyPublicizer.MSBuild` | `0.4.2` | `PrivateAssets="all"` — build tool only |
| `YamlDotNet` | `16.3.0` | NOT copied to output (see below) |

### Why YamlDotNet.dll is NOT deployed

Jötunn pulls in `ValheimModding-YamlDotNet` as a transitive dependency, which installs `YamlDotNet.dll` into both mod-manager profiles and dedicated server plugin folders. Deploying our own copy would cause version conflicts or duplicate-load issues.

The version **must** match Jötunn's bundled version (currently `16.3.0`). If Jötunn updates its bundled version, update `PackageReference` to match.

---

## Publicized Assembly

`Publicize=true` on `assembly_valheim` causes the publicizer MSBuild task to generate a publicized DLL at:
```
src/obj/Debug/publicized/assembly_valheim.dll
```
This is the DLL used by the compiler (and Mono.Cecil reflection) to verify method signatures, field names, and parameter names for Harmony patches.

---

## Deploy Targets (all `AfterTargets="Build"`)

### 1. DeployToBepInEx
```
Condition: Exists('$(BepInExDir)')
Default: C:\Program Files (x86)\Steam\steamapps\common\Valheim\BepInEx\plugins\ValheimServerGuide
Override: dotnet build -p:VALHEIM_INSTALL="D:\Games\Valheim"
```

### 2. DeployToTestProfile — the test client
```
Condition: Exists('$(TEST_PROFILE_DIR)')
Default: C:\Users\<user>\AppData\Roaming\com.kesomannen.gale\valheim\profiles\HB Test
Installs to: $(TEST_PROFILE_DIR)\BepInEx\plugins\TaegukGaming-ValheimServerGuide
Override: dotnet build -p:TEST_PROFILE_DIR="..."
```
The test client is the **Gale** profile "HB Test" (it replaced the old r2modman
`Hearthbound Valheim - Test` profile; that path is no longer written to). The DLL lands in the
mod's own `TaegukGaming-ValheimServerGuide` subfolder, overwriting the Thunderstore-installed
copy — not loose in `plugins/`, where BepInEx's recursive scan would see two copies of the same
GUID and skip one.

### 3. DeployToDedicatedServer
```
Condition: Exists('$(VALHEIM_DEDICATED_SERVER_DIR)')
Default: C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server\BepInEx\plugins\TaegukGaming-ValheimServerGuide
Override: dotnet build -p:VALHEIM_DEDICATED_SERVER_DIR="..."
```

Each target:
1. `MakeDir` on the install directory.
2. `Copy` only `$(TargetPath)` (the DLL) — no dependencies (they're already in the game).
3. Logs the deploy path at `Importance="high"`.

Targets are **conditional** — if the directory doesn't exist, the target silently skips. This means builds on CI or machines without Valheim installed don't fail.

---

## Criteria

- [ ] `YamlDotNet.dll` is NOT copied to any deploy target.
- [ ] YamlDotNet `PackageReference` version must match the version bundled with the installed Jötunn.
- [ ] All three deploy targets use `Condition="Exists(...)"` so they skip gracefully on machines where the target path doesn't exist.
- [ ] Only `$(TargetPath)` (the plugin DLL) is deployed — not assemblies from `CopyLocalLockFileAssemblies`.
- [ ] The publicized DLL path (`src/obj/Debug/publicized/assembly_valheim.dll`) is used for Mono.Cecil reflection and Harmony patch verification.
- [ ] Adding a new Unity module reference requires adding it to `<ItemGroup>` as `<Private>false</Private>`.
- [ ] `BepInEx.AssemblyPublicizer.MSBuild` must stay `PrivateAssets="all"` (build-time tool, not a runtime dep).
