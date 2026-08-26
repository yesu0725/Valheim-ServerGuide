# Publishing Guide

How to publish this mod to GitHub and Thunderstore.io.

---

## GitHub

### First-time publish (GitHub Desktop)

1. Open **GitHub Desktop**.
2. **File → Add Local Repository** → browse to `E:\Valheim Modding\Valheim ServerGuide` → **Add Repository**.
3. Click **Publish repository** in the top bar.
4. Set name to `Valheim-ServerGuide`. Choose public or private. Click **Publish Repository**.

Done. GitHub Desktop creates the remote repo and pushes all commits.

---

### Future updates

1. Make your changes (code, YAML, docs).
2. In GitHub Desktop, write a summary in the **Summary** box (bottom-left) and click **Commit to master**.
3. Click **Push origin** (top bar).

---

### Updating the Wiki

GitHub wiki is a separate git repository. Set it up once, then treat it like any other repo.

**First-time setup:**

1. Go to your GitHub repo → click the **Wiki** tab → **Create the first page** → save it. This initializes the wiki repo.
2. In GitHub Desktop → **File → Clone Repository** → search for `Valheim-ServerGuide.wiki` → clone it to a local folder (e.g. `E:\Valheim Modding\Valheim-ServerGuide.wiki`).
3. Copy all `.md` files from `wiki\` in this project into that cloned folder.
4. In GitHub Desktop, switch to the wiki repo (top-left repo switcher).
5. Commit all files, then click **Push origin**.

**Updating the wiki later:**

1. Edit the `.md` files **directly in `Github\Valheim-ServerGuide.wiki\`**, using their own
   content as the base.
2. Commit and push from inside that folder — it is its own git repository with its own remote,
   pushed separately from the main repo.

> ### WARNING: `wiki\` in this project is STALE — do not copy it over the wiki repo
>
> The single canonical location for wiki content is `Github\Valheim-ServerGuide.wiki\`.
>
> The project's own `wiki\` folder was abandoned and never kept up. Copying it over the wiki repo
> (as earlier versions of this guide instructed) would delete around 677 lines of live content:
> the whole `dvergr_*` trigger family, the `timed` interval docs, and the Shift+E conversation
> docs — all of which match the current code, while `wiki\` still describes hold-E and a required
> `id`. Confirmed 2026-08-17.
>
> To work out which pages a change affects, match the changed source file to its wiki counterpart
> — `GuidanceDisplay.cs` -> `Display-Modes.md`, `AdminCommands.cs` -> `Admin-Commands.md`,
> `*Trigger.cs` -> `Trigger-Types.md`, `NpcConversationPanel.cs` -> `NPC-Conversations.md`.

---

## Thunderstore.io

### What goes in the ZIP

The Thunderstore package ZIP must contain these files **at the root** (not inside a subfolder):

```
ValheimServerGuide_0.14.0.zip
├── manifest.json
├── README.md
├── CHANGELOG.md
├── icon.png          ← 256×256 PNG, you supply this
└── ValheimServerGuide.dll
```

All of these are already in `Thunderstore files\ValheimServerGuide\` — a Release build refreshes the DLL there itself (`DeployToThunderstorePackage`).

> Do **not** include the `wiki\` subfolder in the ZIP. The wiki files are for GitHub only.

---

### Building the ZIP

1. Add your `icon.png` (256×256 PNG) to `Thunderstore files\ValheimServerGuide\`.
2. Open `Thunderstore files\ValheimServerGuide\` in Explorer.
3. Select `manifest.json`, `README.md`, `CHANGELOG.md`, `icon.png`, and `ValheimServerGuide.dll`.
4. Right-click → **Compress to ZIP file** (Windows 11) or **Send to → Compressed folder**.
5. Name it `ValheimServerGuide_<version>.zip`, matching `version_number` in `manifest.json`, and put it at the repo root alongside the previous releases.

---

### Uploading to Thunderstore

1. Go to [thunderstore.io](https://thunderstore.io) and log in with your GitHub account.
2. Navigate to **Upload** (top-right menu).
3. Select game: **Valheim**.
4. Upload your ZIP.
5. Thunderstore validates the manifest and icon automatically. Fix any errors it reports, re-zip, and re-upload.

---

### Releasing a new version

Bump the version **everywhere first** — the build refuses to run if the numbers disagree:

1. `<Version>` in `src\ValheimServerGuide.csproj`.
2. `Plugin.PluginVersion` in `src\Plugin.cs` — `VerifyPluginVersion` fails the build if it does
   not match (1).
3. `version_number` in `Thunderstore files\ValheimServerGuide\manifest.json` —
   `VerifyManifestVersion` fails the build if it does not match (1).
4. `| Version |` in `CLAUDE.md`, and the **Version** line at the bottom of
   `Thunderstore files\ValheimServerGuide\README.md`.

Then:

5. Add a section to `Thunderstore files\ValheimServerGuide\CHANGELOG.md` for the new version.
6. Build in Release: `dotnet build src\ValheimServerGuide.csproj -c Release`. This **also** copies
   the fresh DLL into `Thunderstore files\ValheimServerGuide\` and pushes `manifest.json` out to
   every install target — there is no manual copy step any more.
7. Build the ZIP as above, named with the new version number.
8. **Verify before uploading** (see below).
9. Upload to Thunderstore, then commit and push the main repo and the wiki repo separately.

---

### Verify before uploading

The "Deployed to..." build messages report intent, not outcome, and more than one release has gone
out with a mismatched number. For the packaged DLL and for every install target, check all three:

- **Assembly version** — `[System.Reflection.AssemblyName]::GetAssemblyName($path).Version`.
- **The `[BepInPlugin]` version inside the DLL bytes** — search for
  `com\.valheimserverguide.{0,40}?(\d+\.\d+\.\d+)`. File metadata does not carry it, and this is
  the number BepInEx registers and the startup log prints.
- **The `manifest.json` beside the DLL** — this is what Gale and r2modman *display*, and it is the
  field that has been wrong most often. A manager showing an old version almost always means a
  stale manifest next to a perfectly current DLL.

Open **every** zip in the tree and check the DLL inside it, not only the release zip. A
`PostToolUse` hook re-zips `Thunderstore files\ValheimServerGuide_<today>.zip` after every
`dotnet build`, and that snapshot has shipped a stale DLL beside a current manifest before.

> **Thunderstore version numbers cannot be reused.** Once a version is uploaded it cannot be
> replaced, even if the original upload was fine. A correction means a new patch version — so do
> the verification before the upload, not after.
