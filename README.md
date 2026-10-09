# Silent Katamari

**James Sunderland pushes the katamari.** Roll up the real objects of *Once Upon A KATAMARI* by Katamari's own
rules, in *SILENT HILL 2*'s fog, with its radio static and music closing in as time runs out. A single-player
mashup for [Melty](https://melty.gg): press Play once.

## What you get
- **James Sunderland himself** behind the katamari: his model, textures and animations, read from your own
  SILENT HILL 2 and compiled on your PC.
- **Once Upon A KATAMARI's objects** at their true size, placed in rings by how big your katamari must be to roll
  them up. They stick by Katamari's rule (the katamari needs about ten times an object's volume) and the katamari
  grows by their volume, read from your own copy.
- **A first stage:** start at 5 cm, reach 15 cm in 4 minutes. Results show how big you got.
- **Silent Hill's fog** that closes in around you, a washed-out picture, SILENT HILL 2's music and ambience, and the
  pocket radio's static rising in the last minute.
- **Garry's Mod stays out of sight:** its own gamemode, HUD, pause menu and loading screen. (Garry's Mod still runs
  underneath, so the window title and Steam say "Garry's Mod".)

## You need
- **Garry's Mod** (Melty starts it)
- **Once Upon A KATAMARI** (Steam)
- **SILENT HILL 2** (2024, Steam; GOG and Epic copies are found too)

Nothing from either game is in this download. On the first Play, the converter reads what the mashup needs from
your copies into `garrysmod/addons/silent_katamari_content` on your PC (a few minutes, only once, and again after
either game updates). If a game is missing, the game says so plainly; nothing stands in for it.

## Controls
**WASD** roll · **Mouse** look · **Shift** dash · **Esc** pause · **Space** roll again after the results

## How it works
- `addons/silent_katamari/` – the Garry's Mod gamemode `silentkatamari` (Lua): Katamari's rules, the stage,
  James, the fog, HUD, pause menu, loading screen.
- `converter/silent-katamari-convert.exe` – finds both games through Steam (GOG/Epic for SILENT HILL 2), reads
  Once Upon A KATAMARI's Unity bundles (AssetsTools.NET) and SILENT HILL 2's Unreal containers (CUE4Parse), and
  writes the content addon. It never launches, patches or decrypts either game. `play` re-converts when a game
  changed, then starts Garry's Mod.
- The design lives in `sheets/*.json`; `tools/gen.py` turns them into code and `tools/preflight.py` checks them.
  File formats between the converter and the game: `docs/CONTRACT.md`.

## Troubleshooting
- Log: `%LOCALAPPDATA%\SilentKatamari\converter.log`. A read-only report of both games:
  `silent-katamari-convert probe --gmod "<Garry's Mod folder>"` (saved to `probe.json` next to the log).
- To remove the converted content, delete `garrysmod/addons/silent_katamari_content`.

Built with Claude (an AI assistant by Anthropic). Remix of [Katamari Sandbox](https://melty.gg/m/katamari-sandbox)
by Ian Crocenzi. See CREDITS.md.
