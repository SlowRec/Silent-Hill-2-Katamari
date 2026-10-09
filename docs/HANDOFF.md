# Handoff: continue on the player's Windows PC

You are a Claude session on the user's PC, where Garry's Mod, Once Upon A KATAMARI and SILENT HILL 2 are
installed. A cloud session designed and built everything in this repo but could not touch the games. Read
`MODLOG.md` (decisions, unknowns, evidence), `docs/CONTRACT.md` (converter ↔ gamemode formats), `sheets/README.md`
(the sheets are the source of truth: change a sheet before the code) and `docs/RESEARCH.md` as needed.
The user speaks Spanish; talk to them in Spanish, briefly, one question at a time.

## Rules that still apply
- Never decrypt, patch, inject into or launch Once Upon A KATAMARI or SILENT HILL 2. Only read their files.
- No AES keys from anywhere. If SH2's containers are encrypted, stop the SH2 asset path and tell the user.
- Never ship or commit anything from either game. The converter output lives only in
  `garrysmod/addons/silent_katamari_content` on this PC.
- The Melty token lives only in the user's Melty prompt: never write it to a file, log, commit or screenshot.
- Before touching Garry's Mod's folder or launching it, tell the user what you'll do. Screenshots: the game window only.

## 0. Setup (tell the user what you install)
- .NET 10 SDK: `winget install Microsoft.DotNet.SDK.10`
- Python 3 (for `tools/gen.py`, `tools/preflight.py`, `tools/package.py`): `winget install Python.Python.3.12`
- Optional: Lua 5.1 + luacheck for `tools/bench`; universal-modder (`git clone https://github.com/rehan-remade/universal-modder`)
  for `um win shot/record` screenshots and clips.
- vgmstream for local runs: `python tools/package.py --skip-checks` downloads the pinned r2117 into
  `build/stage/converter/vgmstream`; or set `SK_VGMSTREAM` to a `vgmstream-cli.exe`.

## 1. Probe (read-only, about a minute)
```
dotnet run --project converter/SilentKatamari.Converter -- probe --gmod "C:\Program Files (x86)\Steam\steamapps\common\GarrysMod"
```
Saved to `%LOCALAPPDATA%\SilentKatamari\probe.json`. Answer these first (MODLOG "Open unknowns"):
1. `games.sh2.probe.any_encrypted` – true → SH2 assets are off-limits; tell the user and redesign with them.
2. `games.sh2.probe.compression` and `oodle_dlls_in_game` – Oodle with no DLL in the game → ask the user how to
   proceed (no silent downloads; a vetted decoder needs its license checked).
3. Mappings (.usmap): none is bundled. A public SH2 usmap exists (github.com/many-bees/SilentHillFileGuide,
   `fmodel_mappings_files/Silent Hill 2.usmap`) but has no license: it may be used locally for research
   (`SK_SH2_USMAP`), never shipped. How players get one is a decision for the user (e.g. we dump our own with
   UE4SS from the user's copy and ask whether to ship it).
4. `games.sh2.probe.james_files` – real names of James's meshes and animations; fix `sheets/anims.json` matches.
5. `games.ouak.probe`: unity_version, type_trees, bundle_groups, script_classes, table_fields, audio_files –
   adapt `converter/SilentKatamari.Converter/Ouak/*` (object table fields, prop bundle names, CRI banks).
6. `studiomdl` – Garry's Mod's model compiler path (James needs it).

## 2. Convert, then play
```
dotnet run --project converter/SilentKatamari.Converter -- convert --gmod "<GarrysMod>"
```
Read `%LOCALAPPDATA%\SilentKatamari\converter.log` and `garrysmod/addons/silent_katamari_content/data_static/silent_katamari/status.json`.
Then (with the user's OK) start the game the way Melty does:
```
dotnet run --project converter/SilentKatamari.Converter -- play --gmod "<GarrysMod>" -steam +gamemode silentkatamari +map gm_flatgrass +sv_loadingurl asset://garrysmod/html/silentkatamari/loading.html
```
The gamemode must be in `garrysmod/addons/silent_katamari` for a local run: copy `addons/silent_katamari` there
(Melty does this on install). In game: console `sk_dump`, `sk_selftest` (prints catches); check
`garrysmod/console.log` if launched with `-condebug`. Things to look at: the fog hides the map, no Garry's Mod UI
anywhere (Esc, Tab, Q, C, chat), James stands behind the ball facing the camera and animates, objects are the
right size, sounds play (and no Half-Life 2 impact sounds), the loading screen shows, the sky is fog-coloured when
looking up, the pause menu has a quit button and it works (if not, `IsConCommandBlocked("quit")` was wrong).

## 3. Fix, verify, record
- Each fix: sheet first, `python tools/gen.py`, `python tools/preflight.py`, code, tests.
- When a cell is confirmed in the real game, remove it from that row's `unverified`; set `systems[*].status`
  to `verified`. `python tools/preflight.py --release` must be clean before publishing.
- Append evidence to MODLOG.md (what ran, what was seen, how).

## 4. Melty (with the user)
- Draft listing (a remix of Katamari Sandbox by Ian Crocenzi): modId `ce4844b8-5f06-4fc1-a13c-df84d2fcdeca`,
  Studio https://melty.gg/studio/ce4844b8-5f06-4fc1-a13c-df84d2fcdeca. Title "Silent Katamari", tagline and games
  are saved (MODLOG "Melty").
- Still to settle with the user (see MODLOG for any answers already given): description (from the real build),
  license (repo uses MIT, same as the original), whether others may remix it. Then:
  `python tools/package.py` → `inspect_package`, `validate_recipe`, `one_click_check` (last cloud run: valid, one
  click yes) → `start_upload`/PUT/`finish_upload` → `submit_release` with `dist/melty.json` → screenshot of the real
  game (`add_screenshot`/PUT/`finish_screenshot`) → summary → publish only when the user says so.
- Saving `melty.json` at the repo root needs the user's OK first.
