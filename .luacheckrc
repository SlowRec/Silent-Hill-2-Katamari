-- luacheck config for the Garry's Mod gamemode (GLua globals this project uses).
std = "lua51"
max_line_length = 160
globals = { "SK", "GM", "ENT" }
read_globals = {
	"AddCSLuaFile", "include", "CLIENT", "SERVER", "DeriveGamemode", "hook", "net", "util", "file", "timer", "ents",
	"player", "game", "engine", "physenv", "sound", "surface", "draw", "render", "cam", "vgui", "gui", "input",
	"bit", "Vector", "Angle", "Matrix", "Color", "Mesh", "Material", "CreateMaterial", "ClientsideModel", "IsValid",
	"SafeRemoveEntity", "LocalPlayer", "CurTime", "FrameTime", "ScrW", "ScrH", "Lerp", "CreateConVar",
	"CreateClientConVar", "GetConVarString", "RunConsoleCommand", "concommand", "DrawColorModify",
	"SetGlobal2String", "GetGlobal2String", "SetGlobal2Float", "GetGlobal2Float", "SetGlobal2Int", "GetGlobal2Int",
	"SetGlobal2Bool", "GetGlobal2Bool", "SetGlobal2Vector", "GetGlobal2Vector", "vector_origin",
	math = { fields = { "Clamp", "Round", "ApproachAngle" } },
	string = { fields = { "StartWith" } },
	"FCVAR_REPLICATED", "FCVAR_ARCHIVE", "CHAN_STATIC", "MATERIAL_FOG_LINEAR", "MASK_SOLID", "MASK_SOLID_BRUSHONLY",
	"SOLID_NONE", "SOLID_VPHYSICS", "MOVETYPE_NONE", "MOVETYPE_VPHYSICS", "COLLISION_GROUP_IN_VEHICLE",
	"RENDERGROUP_OPAQUE", "BOX_TOP", "BOX_FRONT", "TEXT_ALIGN_LEFT", "TEXT_ALIGN_CENTER", "TEXT_ALIGN_RIGHT",
	"TEXT_ALIGN_TOP", "TEXT_ALIGN_BOTTOM", "IN_ATTACK", "IN_ATTACK2", "IN_JUMP", "IN_DUCK", "IN_USE", "IN_RELOAD",
	"IN_SPEED", "IN_WALK", "IN_ZOOM",
}
files["tools/bench"] = { globals = { "SK" } }
-- GM:Hook(ply, ...) signatures keep unused arguments for readability.
unused_args = false
files["addons/silent_katamari/gamemodes/silentkatamari/gamemode/sk/sh_sheets.lua"] = { max_line_length = false }
