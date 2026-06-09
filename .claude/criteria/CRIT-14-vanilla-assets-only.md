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
| Icons / Images | Plain color fills (`Image.color = Color.black`) | Loaded textures, `Sprite` assets from files |

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

---

## Rules for Future Development

1. **No `AssetBundle.LoadFromFile/Memory`** — if you find yourself loading a bundle, you're violating this constraint.
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
- [ ] All UI GameObjects are built programmatically using `new GameObject()` + `AddComponent<T>()`.
- [ ] Music playback uses only `MusicMan.StartMusic(name)` with vanilla track names.
- [ ] `Player.SetGhostMode` is used for invulnerability/invisibility — not custom collision/layer changes.
- [ ] Screen-space overlays use `Image.color` (solid or transparent) — no loaded textures.
- [ ] Adding any new visual effect must be reviewed against this constraint before implementation.
