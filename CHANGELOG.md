# Changelog

All notable changes to **Drawing** are listed here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-09-26

First public release.

### Added

- Stamp a translucent, weightless **drawing** of the selected blueprint anywhere in build
  mode: middle mouse button.
- Drawings cost no resources, ignore module limits, occupy no grid cells and never block a
  real placement.
- Two-tone feedback: cyan when the real blueprint would fit, orange when it would not.
- Delete the drawing under the cursor, clear the whole zone, and show/hide all drawings.
- **Automatic conveyor tiling.** A belt tile is one of three mesh states (`centre`, `end`,
  `single`) and the build cursor always shows `centre`, so a plan never revealed where a belt
  line began or ended. The tiler derives the tile from the neighbouring drawings and re-tiles
  the whole run whenever the plan changes. `C` overrides a cell by hand.
- Per-zone persistence in `gk2_projections.json`, a separate file next to the saves. The
  game save is never read or written.
- Stamping the same cell again replaces the drawing instead of failing.
- Configurable model variants (`VariantMode`) with per-blueprint pinning
  (`VariantOverrides`), and `F8` to list the available variation ids of a blueprint.
- A drawing is retired automatically when the real mechanism is built on that spot.
- An on-screen hint in the top-right, styled after the game's own build legend.

### Technical notes

- BepInEx 5 (Mono). Verified on Graveyard Keeper 2, Unity 6000.3.9f1, .NET Framework 4.x.
- Drawings are `Wgo` instances with `isTempObject`, spawned outside the chunk manager, so
  they are purely visual and cannot corrupt a save.
- Conveyor elements are created with `ConveyorWgoData`, mirroring what the game does. A plain
  `WgoData` omits the `ConveyorComponent` and the belt never renders — which is what made the
  first attempts look like a bare frame instead of a belt.
- Colliders are disabled in place rather than by deactivating their GameObject, which would
  hide the mesh as well on objects that share a node.
- This build ships no URP shaders, so drawings use a known-present built-in transparent unlit
  shader with the object's own texture, cached per (texture, tint). Game material assets are
  never modified.
- 80 unit tests. They cover the JSON serialiser and the conveyor tiler by compiling the real
  sources against Unity stubs. Three bugs were caught this way before ever reaching the game:
  an off-by-one in the JSON number parser that shifted stored coordinates by a fraction of a
  cell, a time-based key gate that ate every second click at 60 fps, and a neighbour test that
  compared object ids where it should have compared positions, so no belt neighbour was ever
  found.
