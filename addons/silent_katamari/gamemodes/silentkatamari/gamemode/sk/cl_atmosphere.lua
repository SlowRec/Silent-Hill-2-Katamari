-- Silent Hill over the stage: fog that closes in around the katamari, the sky cleared to fog, a washed-out
-- grade, the ground, and SH2's own music, ambience and radio static (which rises as time runs out).
local K = SK.Katamath
local musicVolume = CreateClientConVar("sk_music_volume", SK.Sheets.commands.sk_music_volume.default, true, false,
	SK.Sheets.commands.sk_music_volume.help, 0, 1)

local function fogColor() return SK.Rule("fog_color") end

-- Fog distances follow the katamari's size, never past rules.fog_end_max_m.
local function fogRange()
	local ball = SK.MyBall and SK.MyBall()
	local d = IsValid(ball) and ball:GetDiameter() or K.ToUnits(SK.Growth.Stage().start_diameter_cm)
	local maxEnd = K.ToUnits(SK.Rule("fog_end_max_m") * 100)
	return math.min(d * SK.Rule("fog_start_diameters"), maxEnd * 0.5), math.min(d * SK.Rule("fog_end_diameters"), maxEnd)
end

local function applyFog(scale)
	local c = fogColor()
	local s, e = fogRange()
	render.FogMode(MATERIAL_FOG_LINEAR)
	render.FogStart(s * (scale or 1))
	render.FogEnd(e * (scale or 1))
	render.FogMaxDensity(1)
	render.FogColor(c[1], c[2], c[3])
	return true
end

function GM:SetupWorldFog() return applyFog(1) end
function GM:SetupSkyboxFog(scale) return applyFog(scale) end

-- Maps with a 3D skybox call PreDrawSkyBox (returning true skips both skies); maps with only a 2D sky
-- (gm_flatgrass) never call it, so the 2D sky is also painted over right after it is drawn.
function GM:PreDrawSkyBox()
	local c = fogColor()
	render.Clear(c[1], c[2], c[3], 255)
	return true
end

function GM:PostDraw2DSkyBox()
	local c = fogColor()
	render.Clear(c[1], c[2], c[3], 255)
end

function GM:PreDrawOpaqueRenderables(depth, sky)
	if sky or depth then return end
	SK.Meshes.DrawGround(SK.StagePos and SK.StagePos() or vector_origin, K.ToUnits(SK.Growth.Stage().ground_size_m * 100))
end

local grade = { ["$pp_colour_addr"] = 0, ["$pp_colour_addg"] = 0, ["$pp_colour_addb"] = 0, ["$pp_colour_mulr"] = 0,
	["$pp_colour_mulg"] = 0, ["$pp_colour_mulb"] = 0 }
function GM:RenderScreenspaceEffects()
	grade["$pp_colour_brightness"] = SK.Rule("color_brightness")
	grade["$pp_colour_contrast"] = SK.Rule("color_contrast")
	grade["$pp_colour_colour"] = SK.Rule("color_saturation")
	DrawColorModify(grade)
end

-- The stage centre as the server traced it (sv_stage.lua ST.StartPos).
function SK.StagePos() return GetGlobal2Vector("sk_floor", vector_origin) end

-- Music, ambience and radio static: started once, mixed every frame.
function GM:Think()
	local st = SK.Growth.Stage()
	local S = SK.Sounds
	S.Loop(st.music)
	S.Loop(st.ambience)
	S.Loop(st.radio)
	local master = musicVolume:GetFloat()
	if gui.IsGameUIVisible() or GetGlobal2Bool("sk_paused", false) then master = master * 0.3 end
	local state = GetGlobal2String("sk_state", "intro")
	local left = GetGlobal2Float("sk_end", 0) - CurTime()
	if GetGlobal2Bool("sk_paused", false) then left = GetGlobal2Float("sk_left", 0) end
	local radioStart = SK.Rule("radio_start_left_s")
	local radio = 0
	if state == "rolling" then radio = math.Clamp(1 - left / radioStart, 0, 1) ^ 1.5 end
	if state == "results" then radio = 0.6 end
	S.SetLoopVolume(st.music, master * (state == "results" and 0.4 or 1) * (1 - radio * 0.6))
	S.SetLoopVolume(st.ambience, master)
	S.SetLoopVolume(st.radio, master * radio)
end
