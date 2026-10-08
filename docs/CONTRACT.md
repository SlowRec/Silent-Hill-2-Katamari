# Contract: converter → Garry's Mod gamemode

The converter (`converter/`, runs on the player's PC) reads the player's own Once Upon A KATAMARI and
SILENT HILL 2 and writes a private content addon. The gamemode (`addons/silent_katamari/`) only reads that
addon. Nothing in it is ever shipped or committed. This file fixes every format that crosses between them.
If a format changes, change it here first, then both sides.

## Location and lifecycle
- Content root: `<GarrysMod>/garrysmod/addons/silent_katamari_content/`
  (`{game}` from Melty is `<GarrysMod>`). Garry's Mod mounts it like any addon, so its `materials/`, `sound/`,
  `models/`, `resource/` and `data_static/` folders are visible to the game.
- The converter builds everything in `<content root>.staging/`, verifies it, then swaps it in place of the old
  folder. `data_static/silent_katamari/status.json` is written **last**; its presence means "conversion finished".
- Melty's `setup.done` file: `{game}/garrysmod/addons/silent_katamari_content/data_static/silent_katamari/status.json`.
- Converter log: `%LOCALAPPDATA%/SilentKatamari/converter.log` (Melty `runtimeData`).
- Re-conversion: `play` re-runs `convert` when a game's Steam `buildid`, its install folder, or the converter
  version differs from `status.json`, or when either game was missing last time and is now installed.

## Units and axes
- Every length the converter writes is in **centimetres** in **Source axes**: +X forward, +Y left, +Z up
  (right-handed). The gamemode converts cm to Source units with `rules.cm_per_unit`.
- Unity (left-handed, +Y up, +Z forward, metres): `source = (z, -x, y) * 100`, triangle winding reversed,
  `v = 1 - v`.
- Unreal (left-handed, +Z up, +X forward, +Y right, centimetres): `source = (x, -y, z)`, winding reversed.
- Volumes in cm³. Times in seconds.

## data_static/silent_katamari/status.json
```json
{
  "converter": "0.1.0",
  "finished_utc": "2026-10-09T12:00:00Z",
  "games": {
    "ouak": { "state": "ok|missing|unreadable", "folder": "D:/SteamLibrary/steamapps/common/OnceUponaKATAMARI",
              "build": "20412345", "reason": null },
    "sh2":  { "state": "ok|missing|unreadable", "folder": "...", "build": "...",
              "reason": "short human reason when not ok, e.g. 'containers are encrypted'" }
  },
  "rows": { "<extract id>": { "ok": true, "seconds": 1.4, "note": "short note or null" } },
  "probe": { "free-form facts the probe found, for the log and the bug report" : "..." }
}
```
- `state` per game: `ok` = every `required` extract row of that game succeeded; `missing` = not installed;
  `unreadable` = installed but a required row failed (`reason` says why, in plain words, no paths).
- The gamemode shows `strings.missing_<game>` or `strings.<game>_unreadable` with `{reason}`.

## data_static/silent_katamari/rules.json (extract `ouak_rules`)
```json
{ "catch_ratio": 0.464, "volume_rate": 0.8, "objects_read": 3741, "source": "MonoInfo medians" }
```
Only keys the converter really read from the player's game are present. The gamemode uses them over the
`rules` sheet values whose `source` is `game` or `research`.

## data_static/silent_katamari/objects.json (extract `ouak_objects`)
```json
{
  "objects": [
    {
      "id": "o1234",              // stable within one conversion: "o" + the game's own object id
      "name": "Daruma Head",      // the game's own display name when found, else its asset name
      "tier": "t2_tiny",          // tiers sheet id the converter chose it for
      "pickup_cm": 6.4,           // katamari diameter needed to roll it up (game's own when found)
      "volume_cm3": 31.0,         // volume added before rate (game's own when found)
      "rate": 0.8,                // share of volume added (game's own when found)
      "size": [7.1, 6.0, 6.3],    // bounding box of the mesh in cm (x, y, z)
      "mesh": "meshes/o1234.json",// relative to data_static/silent_katamari/
      "hull": [x, y, z, x, y, z, ...] // <= 64 points (cm, mesh space) for PhysicsInitConvex; >= 4, not flat
    }
  ]
}
```
- Mesh space: the object's pivot at the centre of its bottom face (it sits on the floor at z = 0).
- Per tier the converter writes exactly `tiers.kinds` objects (fewer only when the game has fewer that fit;
  then `status.rows.ouak_objects.note` says so).

## Mesh JSON (objects: `data_static/silent_katamari/meshes/<id>.json`; katamari core: `core.json`)
Same layout as Katamari Sandbox's meshes, in cm:
```json
{
  "materials": { "m0": { "tex": "silent_katamari/ouak/<name>", "color": [1, 1, 1, 1] } },
  "parts": [ { "mat": "m0", "v": [px, py, pz, nx, ny, nz, u, v, ...], "i": [0, 1, 2, ...] } ]
}
```
- `tex` is a material path without extension; the file is `materials/<tex>.png` (RGBA, power-of-two sides
  not required, at most 1024 px on the longest side). `color` multiplies it. `tex` may be null (colour only).
- 8 floats per vertex; `i` indexes vertices (0-based), 3 per triangle, counter-clockwise seen from outside
  in Source axes.
- `core.json` (extract `ouak_core`): the katamari core, normalised to radius 1.

## James (extract `sh2_james_mesh` + `sh2_james_anims`)
A real Source model compiled on the player's PC with Garry's Mod's own `bin/studiomdl.exe`:
- `models/silent_katamari/james.mdl` (+ `.vvd`, `.dx90.vtx`, `.phy` if any)
- `materials/models/silent_katamari/james/<mat>.vmt` + `.vtf`
- Model space in cm, Source axes, facing +X, feet at z = 0.
- Sequences (names = `anims` sheet ids): `idle`, `walk`, `run`, `walk_back`, `strafe_left`, `strafe_right`,
  each looping, 30 fps. A clip the converter could not match is compiled as a copy of `idle` and listed in
  `data_static/silent_katamari/james.json` → `"missing_clips"`.
- `data_static/silent_katamari/james.json`:
  ```json
  { "height_cm": 182.0, "bones": 87, "triangles": 15800, "lod": 1, "source_mesh": "/Game/.../SK_James...",
    "clips": { "walk": "/Game/.../James_Walk_F" }, "missing_clips": [],
    "arm_bones": { "upperarm_l": "upperarm_l", "upperarm_r": "upperarm_r", "lowerarm_l": "lowerarm_l", "lowerarm_r": "lowerarm_r" } }
  ```
  `arm_bones` maps the roles the renderer bends (characters.arm_pose) to the bone names in the compiled model.

## Size display (extract `ouak_hud`, optional)
`data_static/silent_katamari/hud.json`: `{ "ref": [1920, 1080], "elements": { "<hud row id>": { "sprite":
"silent_katamari/hud/<name>", "rect": [x, y, w, h], "color": [r, g, b, a] } } }` in reference pixels, origin
top-left. Sprites at `materials/silent_katamari/hud/<name>.png`.

## Font (extract `ouak_font`, optional)
`resource/fonts/silent_katamari.ttf` and `data_static/silent_katamari/font.json`: `{ "family": "<family name
inside the TTF>" }` (surface.CreateFont needs the family name, not the file name).

## Sounds
`sound/silent_katamari/<game>/<name>.wav`, 16-bit PCM, 44.1 or 48 kHz, as named in the `extract` sheet.
Loops (music, ambience, radio) are whole files the gamemode loops itself.

## Ground (extract `sh2_ground_tex`, optional)
`materials/silent_katamari/sh2/ground.png`, tiled by the gamemode every `ground_tile_cm` (32 cm).
