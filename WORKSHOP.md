# Steam Workshop — item content

Everything the Workshop item should carry, and the text to paste into the item page.

---

## Item title

```
Drawing - plan your Graveyard Keeper 2 builds
```

## Short description (Steam, ~300 chars)

```
Stamp translucent, weightless drawings of any blueprint in build mode. Drawings cost nothing,
ignore module limits and never block a real placement, so you can plan a whole basement
layout before spending a single resource. Works in every zone with build mode. Your save is
never modified. Requires BepInEx 5.
```

## Description (Workshop body)

````markdown
## Drawing

A planning aid for **Graveyard Keeper 2**.

Pick a blueprint, point at a cell, press **middle mouse** — and a translucent drawing of that
machine stays on the floor. It costs nothing, does not count against the module limit, does
not occupy the grid and does not stop you from building the real thing on top of it.

Sketch the whole basement first, then commit resources.

### Controls (build mode only)

| Key | Action |
|---|---|
| **Middle mouse** | stamp a drawing at the build cursor |
| **Delete** | delete the drawing under the cursor |
| **F9** | clear the zone |
| **H** | show / hide |
| **F8** | list the model variations of the selected blueprint |

### Colours mean something

* **cyan** — the real blueprint would fit there
* **orange** — it would not (resources, space or the module limit)

### It shows the finished machine

Workbenches have two models: the scaffold and the finished machine. The build cursor always
shows the scaffold; Drawing shows the machine you are actually planning for. Both the global
default and individual buildings are configurable.

### Keeps itself tidy

Build the real mechanism and the drawing in that spot removes itself.

### Your save is safe

Drawings live in their own file, `gk2_projections.json`, next to the saves. The mod never
reads or writes `Steam_1.dat`.

---

## ⚠️ Installation — BepInEx required

Graveyard Keeper 2's built-in mod loader only handles translations and voice-overs; it cannot
load code. This mod is a real code mod and needs **BepInEx 5** (the Mono build — **not** the
IL2CPP BepInEx 6).

1. Start the game once so it creates its folders, then close it.
2. Download [`BepInEx_win_x64_5.4.23.5.zip`](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5).
3. From the archive copy `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` **and** the
   whole `BepInEx` folder into your Graveyard Keeper 2 folder
   (`...\steamapps\common\Graveyard Keeper 2\`).
4. Copy `Drawing.dll` from this Workshop item into `BepInEx\plugins\`.
5. Start the game. `BepInEx\LogOutput.log` should contain `Loading [Drawing 1.0.0]`.

A one-click `install.ps1` is included in this item — right-click it → *Run with PowerShell*.

Developed and verified on Graveyard Keeper 2 (Unity 6000.3.9f1, Mono) with BepInEx 5.4.23.5.
````

## Tags

```
Graveyard Keeper 2
Mod
Utility
Building
Quality of life
```

## Suggested Workshop tags (must exist in Steamworks App Admin → Workshop → tags)

```
Mod
```

## Preview image

1920 × 1080 PNG, no text over the gameplay area. Suggested shot: the basement build grid with
a full row of cyan drawings laid out and the real build cursor on one of them — that single
frame shows what the mod does better than any text.

## External links

* Source: `https://github.com/Yaffinlan/DrawingGK2`
* BepInEx 5.4.23.5: `https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5`
