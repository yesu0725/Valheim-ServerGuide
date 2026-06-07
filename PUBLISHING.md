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

1. Edit the `.md` files in `wiki\` in this project.
2. Copy the changed files into the cloned wiki repo folder.
3. In GitHub Desktop, switch to the wiki repo, commit, and push.

> The `wiki\` folder in this project is the source of truth. Always edit there first, then copy over.

---

## Thunderstore.io

### What goes in the ZIP

The Thunderstore package ZIP must contain these files **at the root** (not inside a subfolder):

```
ValheimServerGuide_v0.1.0.zip
├── manifest.json
├── README.md
├── CHANGELOG.md
├── icon.png          ← 256×256 PNG, you supply this
└── ValheimServerGuide.dll
```

All of these (except `icon.png`) are already in `Thunderstore files\ValheimServerGuide\`.

> Do **not** include the `wiki\` subfolder in the ZIP. The wiki files are for GitHub only.

---

### Building the ZIP

1. Add your `icon.png` (256×256 PNG) to `Thunderstore files\ValheimServerGuide\`.
2. Open `Thunderstore files\ValheimServerGuide\` in Explorer.
3. Select `manifest.json`, `README.md`, `CHANGELOG.md`, `icon.png`, and `ValheimServerGuide.dll`.
4. Right-click → **Compress to ZIP file** (Windows 11) or **Send to → Compressed folder**.
5. Name it `ValheimServerGuide_v0.1.0.zip` (match the version in `manifest.json`).

---

### Uploading to Thunderstore

1. Go to [thunderstore.io](https://thunderstore.io) and log in with your GitHub account.
2. Navigate to **Upload** (top-right menu).
3. Select game: **Valheim**.
4. Upload your ZIP.
5. Thunderstore validates the manifest and icon automatically. Fix any errors it reports, re-zip, and re-upload.

---

### Releasing a new version

1. Build the project in Release mode: `dotnet build src\ValheimServerGuide.csproj -c Release`.
2. Copy the new `src\bin\Release\ValheimServerGuide.dll` to `Thunderstore files\ValheimServerGuide\`.
3. Update `version_number` in `Thunderstore files\ValheimServerGuide\manifest.json`.
4. Add a section to `Thunderstore files\ValheimServerGuide\CHANGELOG.md` for the new version.
5. Update the **Version** line at the bottom of `Thunderstore files\ValheimServerGuide\README.md`.
6. Build the ZIP as above (name it with the new version number).
7. Upload to Thunderstore.
8. Commit and push the updated files to GitHub.
