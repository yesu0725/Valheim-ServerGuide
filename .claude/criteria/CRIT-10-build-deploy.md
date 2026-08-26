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
| Version | `0.14.0` |
| CopyLocalLockFileAssemblies | `true` |
| NoWarn | `CS0436;NU1701;CS0618` (CS0618: `TMP_Text.enableWordWrapping` is obsolete in this TMP build but its replacement may not exist in Valheim's shipped runtime) |

---

## Four version strings, one number

A release has to bump **all four**, and the assembly version — the one that is easiest to check —
is the only one no player ever sees:

| # | Where | What it sets | Now enforced by |
|---|---|---|---|
| 1 | `<Version>` in `src/ValheimServerGuide.csproj` | the assembly version (`FileVersionInfo` / `AssemblyName`) | — (the source of the other checks) |
| 2 | `Plugin.PluginVersion` in `src/Plugin.cs` | the `[BepInPlugin]` version — **what BepInEx registers and the startup log prints** | `VerifyPluginVersion` |
| 3 | `version_number` in `Thunderstore files/ValheimServerGuide/manifest.json` | the Thunderstore package version | `VerifyManifestVersion` |
| 4 | `manifest.json` **inside each deploy target's plugin folder** | **what Gale and r2modman display in their mod list** | `DeployManifest` |

Each of (2), (3) and (4) has already shipped wrong at least once:

- **0.12.0** bumped (1) and (3) but left (2) at `0.11.1`, so the package would have installed as
  0.12.0 and then announced itself as 0.11.1 in-game. Verifying the assembly version does not
  catch this — it is a different string. → `VerifyPluginVersion` (`BeforeTargets="Build"`) now
  fails the build when (1) and (2) disagree, naming both values.
- **0.13.1** shipped with every deploy target reporting **0.13.0** in its mod manager. The deploy
  targets copied only `$(TargetPath)`, so (4) stayed frozen at whatever the mod manager last
  installed while the DLL advanced. → `DeployManifest` now copies the canonical manifest to any
  deploy target that already has one, and `VerifyManifestVersion` fails the build when (1) and
  (3) disagree.

To read (2) out of a built DLL, search its bytes for the plugin GUID followed by a version —
`com\.valheimserverguide.{0,40}?(\d+\.\d+\.\d+)` — since file metadata only carries (1).

### When a version looks wrong, check the manifest before the DLL

Three of the four strings live outside the assembly. A mod manager showing an old number almost
always means a stale `manifest.json` (4) next to a perfectly current DLL, not a stale DLL.

---

## Assembly References

All `<Private>false</Private>` (not copied to output — loaded from game at runtime).

| Assembly | Path | Notes |
|---|---|---|
| `assembly_valheim` | `$(ManagedDir)\assembly_valheim.dll` | `Publicize=true` via BepInEx.AssemblyPublicizer |
| `assembly_utils` | `$(ManagedDir)\assembly_utils.dll` | ZPackage vector types |
| `assembly_guiutils` | `$(ManagedDir)\assembly_guiutils.dll` | GUI utilities |
| `Splatform` | `$(ManagedDir)\Splatform.dll` | Needed for `Minimap.AddPin`'s optional `PlatformUserID` parameter even though it is never passed explicitly |
| `UnityEngine` | `$(ManagedDir)\UnityEngine.dll` | Core |
| `UnityEngine.CoreModule` | `$(ManagedDir)\UnityEngine.CoreModule.dll` | |
| `UnityEngine.PhysicsModule` | `$(ManagedDir)\UnityEngine.PhysicsModule.dll` | |
| `UnityEngine.AnimationModule` | `$(ManagedDir)\UnityEngine.AnimationModule.dll` | |
| `UnityEngine.UI` | `$(ManagedDir)\UnityEngine.UI.dll` | |
| `UnityEngine.IMGUIModule` | `$(ManagedDir)\UnityEngine.IMGUIModule.dll` | |
| `UnityEngine.InputLegacyModule` | `$(ManagedDir)\UnityEngine.InputLegacyModule.dll` | |
| `UnityEngine.UnityWebRequestModule` | `$(ManagedDir)\UnityEngine.UnityWebRequestModule.dll` | `UnityWebRequest` (Discord) |
| `UnityEngine.UIModule` | `$(ManagedDir)\UnityEngine.UIModule.dll` | `CanvasGroup` (intro overlay) |
| `UnityEngine.AudioModule` | `$(ManagedDir)\UnityEngine.AudioModule.dll` | `AudioSource` / `AudioClip` (there is no `AudioMan.GetAudioClip`) |
| `UnityEngine.TextRenderingModule` | `$(ManagedDir)\UnityEngine.TextRenderingModule.dll` | |
| `Unity.TextMeshPro` | `$(ManagedDir)\Unity.TextMeshPro.dll` | Every VSG panel's text |
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

Each of the three:
1. `MakeDir` on the install directory.
2. `Copy` only `$(TargetPath)` (the DLL) — no dependencies (they're already in the game).
3. Logs the deploy path at `Importance="high"`.

### 4. DeployManifest — keep the displayed version honest
```
Condition: the canonical Thunderstore manifest exists, AND the destination already has a manifest.json
Copies:    Thunderstore files/ValheimServerGuide/manifest.json -> each deploy target that has one
```
`manifest.json` is what Gale and r2modman **display**; the assembly version is not. The DLL-only
copy above left it frozen at whatever the mod manager last installed, so 0.13.1 shipped with every
target reporting 0.13.0 over a 0.13.1 DLL. The plain `plugins/ValheimServerGuide` client install
has no manifest and is skipped, so its shape is unchanged.

### 5. DeployToThunderstorePackage — Release only
```
Condition: '$(Configuration)' == 'Release' And Exists('$(ThunderstorePackageDir)')
Copies:    $(TargetPath) -> Thunderstore files/ValheimServerGuide/
```
Makes the package folder a complete, self-consistent package the moment the build finishes.
**Debug is excluded** so a Debug binary can never reach the published package. See the packaging
hazard below for why the timing matters.

Targets are **conditional** — if the directory doesn't exist, the target silently skips. This means builds on CI or machines without Valheim installed don't fail.

---

## Packaging hazard: the post-build zip hook

`.claude/settings.json` carries a `PostToolUse` hook that re-zips
`Thunderstore files/ValheimServerGuide_<today>.zip` from the package folder after **every**
`dotnet build`. It fires the instant MSBuild finishes, capturing whatever the folder holds at that
moment.

The old deploy flow copied the Release DLL into the package folder **by hand, after the build**, so
the hook fired inside that window and wrote a snapshot with the NEW `manifest.json` wrapped around
the PREVIOUS release's DLL. 0.14.0's date-stamped zip shipped a 0.13.1 binary that way, and it was
committed before anyone opened it. `DeployToThunderstorePackage` closes the window — the folder is
already correct before the hook can run.

**Verify every zip in the tree, not only the one the deploy created**: the root
`ValheimServerGuide_<version>.zip`, the date-stamped snapshots, and any version-named copy sitting
in `Thunderstore files/`. Check the DLL inside each one, not just its `manifest.json`.

---

## Criteria

- [ ] A release bumps all four version strings: csproj `<Version>`, `Plugin.PluginVersion`, the Thunderstore `manifest.json`, and (via `DeployManifest`) the manifest at each deploy target.
- [ ] The build fails if `Plugin.PluginVersion` does not match `<Version>` (`VerifyPluginVersion`).
- [ ] The build fails if the Thunderstore `manifest.json` does not match `<Version>` (`VerifyManifestVersion`).
- [ ] Release verification reads the **plugin** version out of every deployed DLL, not only the assembly version.
- [ ] Release verification opens **every** zip in the tree and checks the DLL inside it, not only the release zip.
- [ ] `DeployToThunderstorePackage` never runs for a Debug build.
- [ ] `YamlDotNet.dll` is NOT copied to any deploy target.
- [ ] YamlDotNet `PackageReference` version must match the version bundled with the installed Jötunn.
- [ ] All three deploy targets use `Condition="Exists(...)"` so they skip gracefully on machines where the target path doesn't exist.
- [ ] Only `$(TargetPath)` (the plugin DLL) and `manifest.json` are deployed — not assemblies from `CopyLocalLockFileAssemblies`.
- [ ] The publicized DLL path (`src/obj/Debug/publicized/assembly_valheim.dll`) is used for Mono.Cecil reflection and Harmony patch verification.
- [ ] Adding a new Unity module reference requires adding it to `<ItemGroup>` as `<Private>false</Private>`.
- [ ] `BepInEx.AssemblyPublicizer.MSBuild` must stay `PrivateAssets="all"` (build-time tool, not a runtime dep).
