# Drawing

A planning aid for **Graveyard Keeper 2**.

Stamp translucent, weightless **drawings** of any blueprint in build mode. Drawings cost
nothing, ignore module limits, never block a real placement and are stored in their own file,
so your save is never touched. Sketch the whole basement layout first, then spend resources
on the real thing.

Works in **every** zone that has build mode: the basement, the graveyard, the church, the
garden plots.

---

## Controls

Active only while build mode is open. The hint sits in the top-right corner.

| Key | Action |
|---|---|
| **Middle mouse** | stamp a drawing where the build cursor is |
| **Delete** | delete the drawing under the build cursor |
| **C** | cycle the conveyor tile under the cursor: auto → centre → end → single |
| **F9** | clear every drawing in the current zone |
| **H** | show / hide all drawings |
| **F8** | write the selected blueprint's model variations to the log |

All keys are rebindable in `BepInEx\config\drawing.gk2.cfg`.

### Colours mean something

* **cyan** — the drawing sits where the real blueprint *would* fit (resources, space and the
  module limit all allow it);
* **orange** — the drawing sits where the real thing would *not* fit.

You can plan anywhere, but the colour tells you up front what will actually go up in one go.

### Conveyor tiles are worked out for you

A belt tile in Graveyard Keeper 2 is one of three mesh states: `centre` (belt runs through),
`end` (belt stops here) and `single` (an isolated tile). The build cursor always shows
`centre`, which is why a plan made with it never showed where a belt line began or ended.

Drawing derives the right tile from the drawings already placed next to it:

* no belt neighbours → `single`
* one neighbour → `end`, pointing at it
* two or more → `centre`, along the correct axis

The whole belt run is re-tiled whenever the plan changes, so extending a line turns the old
end piece into a through piece automatically. Press **C** to override any cell by hand; the
choice is stored per cell and survives a restart.

### Models

Workbenches ship one model in several rotations, so there is no separate "finished" mesh to
switch to - a drawing already looks like what you get. Where an object *does* have distinct
variants, `VariantMode` decides which one is used:

```ini
[Appearance]
# finished - the variation the build cursor does NOT show
# default  - whatever the object considers its default state
# cursor   - copy the build cursor's variation
VariantMode = finished
```

Need a specific model for a specific building? Pin it:

```ini
VariantOverrides = conveyor_furnace_t1_place=conveyor_furnace_t1
```

Press **F8** in build mode and the exact ids are written to `BepInEx\LogOutput.log`, with
renderer counts and sizes so you can tell them apart.

### Keeps itself tidy

When you actually build a mechanism, the drawing that was standing in the same spot
disappears on its own — the plan stays truthful without manual cleanup.

Stamping the same cell again **replaces** the drawing (handy for changing the blueprint or
spinning a machine). One drawing per cell: two in the same place would be a contradictory
plan. Use **Delete** to remove one.

---

## Install

Requires **BepInEx 5** (the plain Mono build — BepInEx 6 for IL2CPP will not work).

### Easiest

1. Open the game once so it creates its folders, then close it.
2. Run `install.ps1` (right-click → *Run with PowerShell*).

The script downloads BepInEx if needed, installs it and copies the plugin.

### Manual

1. Download [`BepInEx_win_x64_5.4.23.5.zip`](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5).
2. Copy `winhttp.dll`, `doorstop_config.ini` and `.doorstop_version` into
   `...\steamapps\common\Graveyard Keeper 2\`.
3. Copy the `BepInEx` folder from the archive into the same location.
4. Copy `BepInEx\core\Drawing.dll` into `...\Graveyard Keeper 2\BepInEx\plugins\`.

To verify, start the game and open `BepInEx\LogOutput.log` — you should see
`Loading [Drawing 1.0.0]`.

### Uninstall

Run `install.ps1 -Uninstall`, or delete `BepInEx\plugins\Drawing.dll` on its own. To remove
everything, delete `BepInEx` and `winhttp.dll` from the game folder plus
`gk2_projections.json` from `%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\`.
Your save is never modified by this mod, so nothing else is needed.

---

## Files

**Your saves** (never touched by this mod):

```
%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\
├── Steam_1.dat              ← the save, Odin Inspector binary serialisation
├── Steam_1.info             ← metadata: day, quality, reputation, gameSaveVersion
├── Steam_1_backup_1..3.dat  ← automatic backups
└── ...
```

`Steam_1.dat` layout: `GameSave, Assembly-CSharp` → `worldData` → `GameSceneData[]`
(the world, its zones, WGO objects, inventories).

**Drawings** live in their own file, which the game does not read:

```
%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\gk2_projections.json
```

Hand-editable JSON:

```json
{
  "version": 1,
  "zones": {
    "conveyor": [
      { "wgo": "conveyor_cell", "pos": [-110.08, 0.01, -419.40],
        "variation": "centre", "rotation": 0, "tile": 0 }
    ]
  }
}
```

`tile` is `0` for auto, or `1` centre / `2` end / `3` single. The zone key is the
`WorldZone.Id`; drawings from different zones never interfere.

**Log**: `BepInEx\LogOutput.log` in the game folder.
**Config**: `BepInEx\config\drawing.gk2.cfg`.

---

## How it works

Graveyard Keeper 2 is **Unity 6000.3.9f1 on Mono**, so real code modding through
BepInEx/Harmony works. The game's own mod loader (Shift+F10) only handles `Languages/` and
`VoiceOvers/` content and cannot load code, hence BepInEx.

The build pipeline:

```
BuildManager.TryEnable(builder)
  └─ BuildController.EnableBuildMode(BuildData, WorldZone, needs, inventory)
       ├─ BuildPointer.Enable(...)      → spawns the cursor preview
       │    └─ Wgo.Spawn(WgoData { isTempObject = true }, ...)
       └─ BuildLayout.EnableBuildingMode(...)
            ├─ BuildGridData.FormGridData(...)  → BuildCellData[,] 200×200
            └─ BuildGrid3D.Draw(...)            → 512×512 texture grid
```

Each drawing is `Wgo.Spawn(..., isTempObject: true, ignoreChunkRegistration: true)`, so it
never enters the scene's WGO list, the save or physics. On top of that the mod disables every
collider (`collider.enabled = false` — *not* `SetActive`, which would take the mesh with it),
turns off navmesh carving, shadows, light probes, reflection probes and motion vectors, and
swaps in a shared unlit translucent material built from the object's own texture.

Game material assets are never modified: a mechanism you actually build looks completely
normal.

Conveyor elements are the case worth knowing about. The game places them with
`ConveyorWgoData`, whose constructor creates a `ConveyorComponent`; a plain `WgoData` leaves
that out and the belt never renders. Drawing mirrors the game's own choice
(`ConveyorPlace` → `ConveyorWgoData`, everything else → `WgoData`), reading the mode from
`GameBalance.buildableWgos`, so drawings loaded from disk are classified correctly without a
save-format change.

A WGO's mesh is a list of mutually exclusive `WgoPartState` variations
(`WgoPart.Variations`), toggled by `variationId` + `rotationIndex`. For belt cells those
three states *are* the tile types, which is what `ConveyorTiler` picks between.

Hooks used:

| Hook | Why |
|---|---|
| `BuildController.EnableBuildMode` (postfix) | restore the zone's drawings |
| `BuildController.DisableBuildMode` (postfix) | save and destroy the ghosts |
| `WgoBuildPointer.TryDoBuildAction` (postfix) | retire the drawing a real build just replaced |
| `ConveyorBuildPointer.TryDoBuildAction` (postfix) | same for conveyor cells |
| `MainGame.Update` (postfix) | safety net for exits that bypass the above |

> Note: this build has no URP shaders compiled in, so drawings use a known-present built-in
> transparent shader (`Unlit/Transparent`, falling back to `Sprites/Default`). Poking
> `_Surface`/`_BaseColor` into the game's own hand-written shaders produced solid black
> silhouettes.

---

## Build from source

```powershell
.\build.ps1                 # build + run the unit tests
.\build.ps1 -SkipTests      # build only
.\install.ps1               # deploy to the game folder
```

80 unit tests cover the two pieces of pure logic that are easy to get subtly wrong:

* the hand-rolled JSON serialiser — round-trip, escaping, float precision, corrupt-file
  rejection, no-op writes, atomic replace;
* the conveyor tiler — neighbour detection, the auto rules, manual overrides, whole-plan
  topologies (a straight run and an elbow), and fallback when an object lacks a state.

The suite compiles the **real** `ProjectionStore.cs` and `ConveyorTiler.cs` with
`UnityEngine.Vector3` and `Application` stubbed, so the code is tested exactly as it ships.

Game and BepInEx paths live in `src/Drawing/Drawing.csproj` (`GameManagedDir`, `BepInExDir`).
Requires the .NET SDK 8.

```
src/Drawing/          the plugin
├── DrawingPlugin.cs       entry point, BepInEx wiring
├── HarmonyPatches.cs       the five hooks
├── ProjectionController.cs the zone's drawings, spawning, re-tiling
├── ProjectionStore.cs      gk2_projections.json (hand-rolled JSON)
├── ConveyorTiler.cs        belt tile resolution (pure logic, tested)
├── VariantSelector.cs      mesh variant choice
├── Ghostifier.cs           physics strip + translucent material
├── ProjectionHotkeys.cs    key handling
├── ProjectionHud.cs        the top-right hint
└── Settings.cs             config
src/Drawing.Tests/     the unit tests
```
