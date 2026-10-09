# MODLOG: Silent Hill 2 × Once Upon A KATAMARI

Journal for this project: paths, IDs, formats, decisions, what failed and why, next step.
Whatever isn't written here is lost at the next context compaction.

## Idea (agreed with the user)
**James pushes the katamari.** James Sunderland (SILENT HILL 2) takes the Prince's place and rolls up the
real objects of Once Upon A KATAMARI by Katamari's real rules, in Silent Hill 2's fog, sound and music.
Single player. First version: one foggy stage with a size goal and a time limit. Later, a real
Silent Hill 2 location (the opening Rest Area) loaded from the player's copy.

User's request: Garry's Mod must not show inside the game. Only Katamari and SH2 should be visible.

## Melty
- Remix of `katamari-sandbox` v0.1.0 by Ian Crocenzi (MIT). Draft: modId `ce4844b8-5f06-4fc1-a13c-df84d2fcdeca`,
  slug `silent-katamari` (page https://melty.gg/m/silent-katamari once published),
  Studio https://melty.gg/studio/ce4844b8-5f06-4fc1-a13c-df84d2fcdeca
- 2026-10-09 Listing chosen by the user: title "Silent Katamari", tagline "James Sunderland rolls up Katamari in
  Silent Hill's fog." Saved with update_mod, along with the games (garrys-mod, custom-once-upon-a-katamari,
  custom-silent-hill-2). The user then chose license MIT and allowed remixes (saved with update_mod). Still to
  write: the description (after the real run).
- Games (search_games): `garrys-mod` (primary, engine source, **no loader**: plain addon files),
  `custom-once-upon-a-katamari` (secondary, Steam 1880620), `custom-silent-hill-2` (secondary, Steam 2124490).
  Neither guest is in Melty's catalog: the mod finds them itself through Steam and says so in game when one is missing.
- Original recipe pattern: `setup` runs `converter convert --gmod {game}` once and waits for a done-file;
  `launch` runs `converter play --gmod {game} <gmod args>` (re-converts when a game's buildid changed, then starts gmod).
- Garry's Mod failures seen by Melty (game_info): file conflicts between mashups that ship the same path,
  download timeouts, disk full, game missing. → Ship only paths unique to this mashup.

## Route (toolkit vocabulary)
**route: reimplementation** = mashup Pattern 1 (converter turns the player's own installs into a private local
cache) + Pattern 5 (Katamari's rules as a sim inside the host). Not passthrough, not geometry transfer:
neither guest game ever runs, is launched, patched or hooked. They are only read from disk.

## Hosts and games
| Game | Engine | Install | Content | Notes |
|---|---|---|---|---|
| Garry's Mod | Source (Lua) | `{game}` from Melty; `gmod.exe` or `bin/win64/gmod.exe` | addons, data_static | ships `bin/studiomdl.exe` (unverified on this PC) |
| Once Upon A KATAMARI | Unity 6, IL2CPP, URP | `steamapps/common/OnceUponaKATAMARI` | Addressables bundles (hashed names), CRIWARE audio (.acb/.awb, HCA?) | namespace `App.KatamariSin`, objects = `MonoInfo` in `CollectionAssetTable`; online mode (PlayFab) never touched |
| SILENT HILL 2 (2024) | UE 5.1(.1) | `steamapps/common/SILENT HILL 2`, project `SHProto` | IoStore `SHProto/Content/Paks/*.utoc/.ucas` + `.pak` | James: `/Game/Game/Characters/Humans/JamesSunderland/`; Wwise audio; Steam DRM wrapper on the exe (never touched) |

## Open unknowns that only the player's PC can answer (probe first)
1. SH2: is `SHProto-Windows.utoc` encrypted? (EIoContainerFlags at byte 80, Encrypted = 0x02.)
   **If encrypted: stop the SH2 asset path.** No leaked keys, no key extraction. Tell the user.
2. SH2: compression methods in the .utoc (Oodle?) and whether any `oo2core*.dll` ships in the install.
3. SH2: a `.usmap` mappings file is needed for unversioned UE5 properties; none is public on GitHub.
4. SH2: where Wwise media lives (loose .bnk/.wem in the .pak, or AkMediaAsset packages).
5. OUAK: Unity version, Addressables catalog (json/bin), bundle type trees, audio container (CRI .acb/.awb).
6. OUAK: how `MonoInfo` links an object to its prefab/mesh, and its size fields.

## Decisions
- 2026-10-08 Host = Garry's Mod via remix of Katamari Sandbox (user chose option 1 and asked to hide GMod).
- 2026-10-08 Own gamemode `silentkatamari` derived from `base` (no Sandbox UI), started with
  `+gamemode silentkatamari +map gm_flatgrass`. Sky cleared to fog colour, fog hides the map.
- 2026-10-08 Converter in C# (.NET, self-contained win-x64 single file, buildable from Linux).
  SH2 via CUE4Parse (Apache-2.0); OUAK via AssetsTools.NET (MIT); audio via bundled vgmstream-cli (ISC + LGPL DLLs).
  Rendering in GMod via IMesh from converted JSON (the original's proven path); James skinned on the CPU from a low LOD.
- The sheets in `sheets/` are the source of truth. `tools/gen.py` writes `sk/sh_sheets.lua`;
  `tools/preflight.py` must be clean before every build.

## Evidence log
- (none yet from a real run; everything below this line must say how it was verified)
- 2026-10-09 Built (source + synthetic tests only; NOTHING has run against the real games yet):
  - Gamemode `silentkatamari`: luacheck clean (24 files), headless bench 17/17 (REROLL pickup anchors: ant 1.5 cm,
    thumbtack 3.1, caramel 4.4, eraser 7.4; stage reaches 15 cm after 57% of objects, best band reachable).
  - Converter: 27/27 xunit (Steam/GOG/Epic discovery on a fake tree, safe swap, PNG/VTF/SMD/QC/Euler round trip,
    utoc/pak trailer parsing, UE->Source axes and facing, Unity vertex streams/half floats/alignment, winding,
    texture flip, TTF names). win-x64 single-file exe builds (~50 MB).
  - Package `dist/silent-katamari-0.1.0.zip` (55 files): Melty validate_recipe = valid, one_click_check = yes.
  - Bundled vgmstream r2117 win64 zip sha256 6c4a8a38…dc6c (identical vgmstream-cli.exe to Katamari Sandbox's).
- 2026-10-09 Garry's Mod API check against the wiki text (luttje/glua-api-snippets; wiki.facepunch.com is blocked
  here) plus Facepunch/garrysmod `scripts/surfaceproperties.txt`. Every function the gamemode calls exists; the only
  names not in the API are our NetworkVar accessors. Found and fixed from the docs (still to see in the real game):
  - PreDrawSkyBox is never called on maps without a 3D skybox (gm_flatgrass): the 2D sky is now painted over in
    PostDraw2DSkyBox.
  - `default_silent` copies the impact sounds of `default`: entity sounds that aren't ours are now dropped in
    EntityEmitSound.
  - Esc: holding SHIFT still opens Garry's Mod's menu, and four blocked Esc presses in a short time make it show a
    note on that (menu realm, out of our reach). hide[pause_menu] is now "partly".
  - `quit` may be on Garry's Mod's blocked list for RunConsoleCommand: the quit button only shows when
    IsConCommandBlocked("quit") is false.
- Not yet verified in a real run (needs the user's PC): SH2 encryption/compression/Oodle/usmap, James mesh path and
  clip names, OUAK bundle layout/object table/prefab linkage/CRI audio, studiomdl on the player's Garry's Mod,
  every GMod API path (fog, sky clear, pause menu, loading screen via asset://, quit command).
