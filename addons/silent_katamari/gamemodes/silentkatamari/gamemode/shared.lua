-- Silent Katamari: James Sunderland rolls up Once Upon A KATAMARI's objects in Silent Hill's fog.
-- Everything from either game is read on the player's PC by the converter (docs/CONTRACT.md); this
-- gamemode only keeps Garry's Mod out of sight and runs Katamari's rules. Systems: sheets/systems.json.
GM.Name = "Silent Katamari"
GM.Author = "Silent Katamari contributors (remix of Katamari Sandbox by Ian Crocenzi)"
GM.TeamBased = false

SK = SK or {}

local shared_files = { "sk/sh_sheets.lua", "sk/sh_strings.lua", "sk/sh_content.lua", "sk/sh_katamath.lua",
	"sk/sh_growth.lua", "sk/sh_sounds.lua", "sk/sh_control.lua", "sk/sh_hide.lua" }
local client_files = { "sk/cl_meshes.lua", "sk/cl_james.lua", "sk/cl_camera.lua", "sk/cl_atmosphere.lua",
	"sk/cl_hud.lua", "sk/cl_pause.lua" }
local server_files = { "sk/sv_attach.lua", "sk/sv_pickup.lua", "sk/sv_player.lua", "sk/sv_stage.lua" }

for _, f in ipairs(shared_files) do
	if SERVER then AddCSLuaFile(f) end
	include(f)
end
for _, f in ipairs(client_files) do
	if SERVER then AddCSLuaFile(f) else include(f) end
end
if SERVER then
	for _, f in ipairs(server_files) do include(f) end
end
