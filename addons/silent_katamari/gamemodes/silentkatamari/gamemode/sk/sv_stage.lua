-- The stage (stage + tiers sheets): Katamari objects from the player's copy placed in rings by the size
-- you need to roll them up, a time limit, a size goal and the results. States: intro -> rolling -> results;
-- "blocked" when Once Upon A KATAMARI's objects aren't available (the HUD says why).
local ST = {}
SK.Stage = ST
local K = SK.Katamath
util.AddNetworkString("sk_pause")

local INTRO_S = 3
local floorZ

function ST.Row() return SK.Growth.Stage() end
function ST.State() return GetGlobal2String("sk_state", "intro") end
local function setState(s) SetGlobal2String("sk_state", s) end

-- Floor height at the stage centre, found once by tracing down (the sheet's z is only a fallback).
function ST.StartPos()
	local c = ST.Row().center
	if not floorZ then
		local tr = util.TraceLine({ start = Vector(c[1], c[2], 16000), endpos = Vector(c[1], c[2], -16000), mask = MASK_SOLID_BRUSHONLY })
		floorZ = tr.Hit and tr.HitPos.z or c[3]
	end
	return Vector(c[1], c[2], floorZ)
end

local function clearObjects()
	for _, e in ipairs(ents.FindByClass("sk_mono")) do
		if not SK.Attach.IsCaught(e) then e:Remove() end
	end
end

-- Places every tier's objects in its ring. Fixed seed: like a Katamari level, the layout is the same each time.
function ST.Build()
	clearObjects()
	if not SK.Content.Ok("ouak") then return 0 end
	local byTier = {}
	for _, o in ipairs(SK.Content.objects) do
		byTier[o.tier] = byTier[o.tier] or {}
		table.insert(byTier[o.tier], o)
	end
	math.randomseed(20261008)
	local center = ST.StartPos()
	local placed, count = {}, 0
	local keepClear = K.ToUnits(ST.Row().start_diameter_cm) * 3
	for _, tid in ipairs(ST.Row().tiers) do
		local tier, kinds = SK.Sheets.tiers[tid], byTier[tid]
		if kinds and #kinds > 0 then
			for i = 1, tier.count do
				local o = kinds[(i - 1) % #kinds + 1]
				local rad = K.ToUnits(math.max(o.size[1], o.size[2]) * 0.5)
				for _ = 1, 30 do
					local a = math.random() * 2 * math.pi
					-- uniform over the ring's area
					local r0, r1 = K.ToUnits(tier.ring_min_m * 100), K.ToUnits(tier.ring_max_m * 100)
					local d = math.sqrt(r0 * r0 + math.random() * (r1 * r1 - r0 * r0))
					local pos = Vector(math.cos(a) * d, math.sin(a) * d, 0)
					local ok = pos:Length() > keepClear + rad
					for _, p in ipairs(placed) do
						if ok and pos:Distance(p.pos) < rad + p.rad then ok = false end
					end
					if ok then
						local e = ents.Create("sk_mono")
						e:SetObjId(o.id)
						e:SetCatchable(tier.catchable)
						e:SetPos(center + pos)
						e:SetAngles(Angle(0, math.random() * 360, 0))
						e:Spawn()
						placed[#placed + 1] = { pos = pos, rad = rad }
						count = count + 1
						break
					end
				end
			end
		end
	end
	math.randomseed(os.time())
	return count
end

function ST.Start()
	for _, ply in ipairs(player.GetAll()) do
		local ball = SK.Player.Ball(ply)
		ball:SetActive(false)
		ball:ResetKatamari()
		ball:SetPos(ST.StartPos() + Vector(0, 0, ball:Radius() + 1))
		ball:SetAngles(Angle(0, 0, 0))
		ply:SetEyeAngles(Angle(20, 0, 0))
	end
	SetGlobal2Int("sk_placed", ST.Build())
	SetGlobal2Float("sk_final_cm", 0)
	SetGlobal2Bool("sk_paused", false)
	if not SK.Content.Ok("ouak") then
		setState("blocked")
		for _, ply in ipairs(player.GetAll()) do SK.Player.Ball(ply):SetActive(true) end
		return
	end
	setState("intro")
	SetGlobal2Float("sk_intro_end", CurTime() + INTRO_S)
	SetGlobal2Float("sk_end", CurTime() + INTRO_S + ST.Row().time_s)
end

function ST.CanRestart() return ST.State() == "results" and CurTime() > GetGlobal2Float("sk_results_at", 0) + 1.5 end

function ST.Restart() ST.Start() end

local function finish()
	setState("results")
	SetGlobal2Float("sk_results_at", CurTime())
	local best = 0
	for _, ply in ipairs(player.GetAll()) do
		local ball = SK.Player.Ball(ply)
		ball:SetActive(false)
		best = math.max(best, K.ToCm(ball:GetDiameter()))
	end
	SetGlobal2Float("sk_final_cm", best)
end

hook.Add("Think", "sk_stage_think", function()
	if GetGlobal2Bool("sk_paused", false) then return end
	local s = ST.State()
	if s == "intro" and CurTime() >= GetGlobal2Float("sk_intro_end", 0) then
		setState("rolling")
		for _, ply in ipairs(player.GetAll()) do SK.Player.Ball(ply):SetActive(true) end
	elseif s == "rolling" and CurTime() >= GetGlobal2Float("sk_end", 0) then
		finish()
	end
end)

-- Our pause menu (cl_pause.lua) stops the clock and the katamari while it is open.
net.Receive("sk_pause", function(_, ply)
	if not ply:IsListenServerHost() and not game.SinglePlayer() then return end
	local paused = net.ReadBool()
	if paused == GetGlobal2Bool("sk_paused", false) then return end
	SetGlobal2Bool("sk_paused", paused)
	if paused then
		SetGlobal2Float("sk_left", GetGlobal2Float("sk_end", 0) - CurTime())
		SetGlobal2Float("sk_intro_left", GetGlobal2Float("sk_intro_end", 0) - CurTime())
	else
		SetGlobal2Float("sk_end", CurTime() + GetGlobal2Float("sk_left", 0))
		SetGlobal2Float("sk_intro_end", CurTime() + GetGlobal2Float("sk_intro_left", 0))
	end
	local active = not paused and (ST.State() == "rolling" or ST.State() == "blocked")
	for _, p in ipairs(player.GetAll()) do SK.Player.Ball(p):SetActive(active) end
end)

function GM:InitPostEntity()
	-- Things fall at true speed for their size at this world scale (rules.gravity_cm_s2 / cm_per_unit).
	RunConsoleCommand("sv_gravity", tostring(math.Round(SK.Rule("gravity_cm_s2") / SK.Rule("cm_per_unit"))))
	physenv.SetPerformanceSettings({ MaxVelocity = 20000, MaxAngularVelocity = 36000 })
end

function GM:PlayerInitialSpawn(ply)
	timer.Simple(0.5, function()
		if IsValid(ply) and #player.GetAll() == 1 then ST.Start() end
	end)
end

local function hostOnly(ply) return not IsValid(ply) or ply:IsListenServerHost() or game.SinglePlayer() end

concommand.Add("sk_restart", function(ply)
	if hostOnly(ply) then ST.Restart() end
end, nil, SK.Sheets.commands.sk_restart.help)

concommand.Add("sk_dump", function(ply)
	if not hostOnly(ply) then return end
	for _, id in ipairs({ "ouak", "sh2" }) do
		local state, reason = SK.Content.GameState(id)
		print(string.format("[SilentKatamari] %s: %s %s", id, state, reason or ""))
	end
	print(string.format("[SilentKatamari] objects known %d, placed %d, state %s, gravity %s", #SK.Content.objects,
		GetGlobal2Int("sk_placed", 0), ST.State(), GetConVarString("sv_gravity")))
	print("[SilentKatamari] james model: " .. tostring(file.Exists("models/silent_katamari/james.mdl", "GAME")))
end, nil, SK.Sheets.commands.sk_dump.help)

-- Test run: straight ahead for 15 s, logging every catch and the final size.
concommand.Add("sk_selftest", function(ply)
	if not hostOnly(ply) or not IsValid(ply) then return end
	ST.Start()
	SetGlobal2Float("sk_intro_end", CurTime())
	SK.Pickup.Log = true
	ply.SKAuto = { untilT = CurTime() + 15, yaw = ply:EyeAngles().y }
	print(string.format("[SilentKatamari] selftest: start %.2f cm, %d objects placed", ST.Row().start_diameter_cm, GetGlobal2Int("sk_placed", 0)))
	timer.Simple(16, function()
		local ball = IsValid(ply) and SK.Player.Ball(ply)
		if IsValid(ball) then
			print(string.format("[SilentKatamari] selftest: end %.2f cm, caught %d", K.ToCm(ball:GetDiameter()), ball:GetCaught()))
		end
		SK.Pickup.Log = false
	end)
end, nil, SK.Sheets.commands.sk_selftest.help)
