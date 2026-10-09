# Credits

**Silent Katamari**, a remix of **Katamari Sandbox** by Ian Crocenzi (iancro22), MIT. Its Garry's Mod Lua (the
katamari entity, attaching, control, camera and growth code) is the base of this gamemode. Built with Claude
(an AI assistant by Anthropic).

## Games
- **Garry's Mod** by Facepunch Studios: the engine underneath, kept out of sight.
- **Once Upon A KATAMARI** by Bandai Namco Entertainment / RENGAME: the objects you roll up, their sizes and
  growth rates, the katamari's core, the font and the sounds. Read from the player's own copy on their PC.
- **SILENT HILL 2** (2024) by Konami / Bloober Team: James Sunderland's model, textures and animations, a street
  texture, the radio static, ambience and music. Read from the player's own copy on their PC.

None of either game's content is included in this download.

## Katamari's rules
The pickup rule (about 10x the object's volume), growth rates and first-stage numbers come from the community
datamine of Katamari Damacy REROLL in shookietea/reroll-notes, used as reference figures only; values the
converter reads from the player's Once Upon A KATAMARI take precedence.

## Software bundled in the converter
| Software | Author | License |
|---|---|---|
| CUE4Parse, CUE4Parse-Conversion | FabianFG and contributors | Apache-2.0 (NOTICE in `converter/THIRD_PARTY_LICENSES/CUE4Parse-NOTICE.txt`) |
| AssetsTools.NET | nesrak1 | MIT |
| AssetRipper.TextureDecoder | ds5678 | MIT |
| vgmstream r2117 and its decoders | vgmstream contributors | ISC-style; FFmpeg and mpg123 parts LGPL (`converter/vgmstream/COPYING`) |
| .NET 10 runtime (self-contained) | .NET Foundation | MIT |
| SixLabors.ImageSharp 3.1.12 | Six Labors | Six Labors Split License (Apache-2.0 terms for open-source use) |
| SkiaSharp | Microsoft | MIT |
| Newtonsoft.Json, Serilog, BouncyCastle, Blake3, K4os LZ4, ZstdSharp, LZMA-SDK, Oodle.NET (wrapper only, no Oodle codec), VGAudio, SharpGLTF, MemoryPack, NAudio.Core and the rest | their authors | MIT / Apache-2.0 / BSD-2-Clause, listed in `converter/THIRD_PARTY_LICENSES/NOTICES.md` |

Garry's Mod's own `studiomdl` compiles James on the player's PC; it is not shipped.
