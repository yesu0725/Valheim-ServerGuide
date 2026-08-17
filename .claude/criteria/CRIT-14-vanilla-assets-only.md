# CRIT-14 — Vanilla Assets Only

**Constraint:** This mod must use ONLY vanilla Valheim/Unity assets and programmatic Unity primitives. No custom textures, models, prefabs, animations, sounds, or AssetBundles are permitted.

---

## What This Means

| Category | Allowed | Not Allowed |
|---|---|---|
| UI elements | Programmatic `GameObject` + vanilla Unity components (`Canvas`, `Image`, `CanvasGroup`, `RectTransform`) | Custom UI prefabs, custom sprites/textures |
| Text display | Vanilla `TextViewer` (Rune/Intro styles), `MessageHud`, `Chat`, `Raven`/`Tutorial` popup | Custom text meshes, custom fonts |
| Music | `MusicMan.StartMusic(name)` with vanilla track names (e.g., `"intro"`) | Custom audio clips, AssetBundle-loaded sounds |
| Visual FX | Vanilla ghost mode, TextViewer screen darken, programmatic `Image` color overlay | Particle systems from custom bundles, custom shaders |
| Icons / Images | Plain color fills (`Image.color = Color.black`), and vanilla `Sprite`s **already loaded by the running game** (see `VanillaUi`) | Loaded textures, `Sprite` assets from files, sprites from a mod's own bundle (including Jötunn's `GUIManager` woodpanel prefab) |

---

## Current Implementations by Feature

### Intro Black Overlay
Built entirely from programmatic Unity components:
- `GameObject` → `Canvas` (ScreenSpaceOverlay) → child `GameObject` → `RectTransform` + `Image` (black) + `CanvasGroup`
- No textures loaded; the solid black color is set via `Image.color = Color.black`.

### Raven (Hugin) Popup
Uses `Tutorial.instance.m_texts` and `Player.m_localPlayer.ShowTutorial` — 100% vanilla systems.

### Message Toast
Uses `MessageHud.instance.ShowMessage` — 100% vanilla.

### Chat Line
Uses `Chat.instance.AddString` with Unity rich-text color tags — 100% vanilla.

### Rune / Intro Text
Uses `TextViewer.instance.ShowText(Style.Rune / Style.Intro)` — vanilla TextViewer.

### Intro Music
Uses `MusicMan.instance.StartMusic("intro")` with the vanilla track name — vanilla music system.

### Ghost Mode
Uses `Player.SetGhostMode(true/false)` — vanilla player system.

### Codex Panel Styling — `src/Display/VanillaUi.cs`

The Guide Codex is drawn in the vanilla **player inventory** window's own art. Nothing is loaded:
`VanillaUi.TryResolve()` walks the live `InventoryGui` hierarchy and takes the `Sprite` objects the
game is *already* drawing with, together with the `Image` settings vanilla renders each one at
(`type`, material, `pixelsPerUnitMultiplier`). Sprites that are loaded but not currently drawn are
found with `Resources.FindObjectsOfTypeAll<Sprite>()`. Assets in use:

| Slot | Vanilla sprite | Used for |
|---|---|---|
| `Panel` | `woodpanel_playerinventory` | the codex window frame |
| `Inset` | `panel_interior_bkg_128` | the guide list / description / upcoming boxes |
| `Button` | `button` (+ `button_highlight` / `button_pressed` / `button_disabled`) | Close, the two toggle pills, the footer toggle |
| `Font` | whatever the inventory's own labels use (majority vote, outline variants skipped) | every text in the panel |

Rules this follows, and any future styling must too:

- **Every slot degrades.** A sprite that cannot be found leaves the caller on its flat-colour fill,
  so a vanilla rename cannot break the panel — it only makes it plainer.
- **Fixed-aspect art is never stretched.** Valheim has one bespoke panel sprite per window rather
  than a 9-sliced tile, so the codex sizes *itself* to `woodpanel_playerinventory`'s aspect ratio
  (`ResolvePanelSize`). Art that does carry 9-slice borders (`SpriteStyle.Scalable`) may be drawn at
  any size; art that does not is only used where its own proportions are honoured.
- **The cache is per GUI scene.** Sprites and fonts belong to the scene they were read from, so
  `TryResolve` re-reads them when `InventoryGui.instance` changes (a new session). A sprite from a
  destroyed scene would otherwise sit in the cache reading as "found but null" forever.

---

## Rules for Future Development

1. **No `AssetBundle.LoadFromFile/Memory`** — if you find yourself loading a bundle, you're violating this constraint. This includes borrowing another mod's bundled art: Jötunn's `GUIManager.CreateWoodpanel` looks like the vanilla panel but ships from Jötunn's own bundle. Read the sprite off the live game instead (`VanillaUi`).
2. **No custom PNG/JPG/WAV/OGG files in the plugin folder** — the plugin directory should contain only the DLL.
3. **No `Instantiate(prefab)` on non-vanilla prefabs** — only instantiate from ZNetScene/PrefabManager, or build from scratch using primitive components.
4. **Music tracks** must exist in Valheim's own MusicMan track list. You can discover valid track names by inspecting `MusicMan.m_music` at runtime or reading `MusicMan.GetCurrentMusic()`.
5. **UI color and style** must be achievable with Unity component properties (`Image.color`, `CanvasGroup.alpha`, RectTransform anchoring) without custom art.
6. **Text styling** must use Unity rich-text tags (`<color>`, `<b>`, `<i>`, `<size>`) or vanilla TextViewer styles. No TextMeshPro custom assets.

---

## Why This Constraint Exists

- Keeps the mod lightweight (no large asset bundle to distribute or version-manage).
- Avoids licensing questions around custom art.
- Ensures the mod's visuals are always consistent with the game's art style.
- Simplifies distribution — only one DLL file needs to be deployed.

---

## Criteria

- [ ] The plugin deploy folder contains ONLY `ValheimServerGuide.dll` — no additional files.
- [ ] No `AssetBundle` API is called anywhere in the codebase.
- [ ] No files are read from disk to produce textures, sprites, or audio clips.
- [ ] Sprites are only ever taken from objects the running game already has loaded, never from a bundle (ours or another mod's).
- [ ] Every `VanillaUi` slot has a flat-colour fallback, so a missing/renamed vanilla sprite makes a panel plainer and never broken.
- [ ] Fixed-aspect vanilla art is drawn at its own aspect ratio; only `Scalable` (9-sliced) art is stretched to arbitrary sizes.
- [ ] All UI GameObjects are built programmatically using `new GameObject()` + `AddComponent<T>()`.
- [ ] Music playback uses only `MusicMan.StartMusic(name)` with vanilla track names.
- [ ] `Player.SetGhostMode` is used for invulnerability/invisibility — not custom collision/layer changes.
- [ ] Screen-space overlays use `Image.color` (solid or transparent) — no loaded textures.
- [ ] Adding any new visual effect must be reviewed against this constraint before implementation.
