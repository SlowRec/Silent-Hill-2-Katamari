-- James Sunderland pushing the katamari: the Source model the converter compiled on this PC from his
-- SILENT HILL 2 mesh, playing his own animations, arms bent forward onto the ball (characters sheet).
local J = {}
SK.James = J
local MODEL = "models/silent_katamari/james.mdl"
local ent, state = nil, { anim = nil, t = 0, yaw = 0 }

local function available()
	return SK.Content.Ok("sh2") and SK.Content.james ~= nil and file.Exists(MODEL, "GAME")
end
J.Available = available

local function model()
	if IsValid(ent) then return ent end
	if not available() then return nil end
	ent = ClientsideModel(MODEL, RENDERGROUP_OPAQUE)
	if not IsValid(ent) then return nil end
	ent:SetNoDraw(false)
	ent:DrawShadow(true)
	return ent
end

-- Which of his clips fits what the katamari is doing (anims sheet ids = sequence names).
local function chooseAnim(ball, speed)
	local input = ball:GetInput()
	if bit.band(input, 15) == 0 then return speed > ball:GetDiameter() * 0.5 and "walk" or "idle" end
	if bit.band(input, 16) ~= 0 and bit.band(input, 1) ~= 0 then return "run" end
	if bit.band(input, 1) ~= 0 then return speed > ball:GetDiameter() * 2.5 and "run" or "walk" end
	if bit.band(input, 2) ~= 0 then return "walk_back" end
	if bit.band(input, 8) ~= 0 then return "strafe_right" end
	if bit.band(input, 4) ~= 0 then return "strafe_left" end
	return "idle"
end

-- Bend the arms forward so his hands rest on the ball (characters.arm_pose; rules.james_arm_*).
local function armPose(m)
	local bones = SK.Content.james.arm_bones or {}
	local up, low = SK.Rule("james_arm_upper"), SK.Rule("james_arm_lower")
	for role, name in pairs(bones) do
		local id = m:LookupBone(name)
		if id then
			local a = string.find(role, "upper", 1, true) and up or low
			local mirror = string.sub(role, -1) == "r" and -1 or 1
			m:ManipulateBoneAngles(id, Angle(a[1], a[2] * mirror, a[3] * mirror))
		end
	end
end

hook.Add("PreRender", "sk_james_pose", function()
	local ball = SK.MyBall and SK.MyBall()
	local m = ball and model()
	if not m then
		if IsValid(ent) then ent:SetNoDraw(true) end
		return
	end
	m:SetNoDraw(false)
	local wantYaw = LocalPlayer():EyeAngles().y
	state.yaw = math.ApproachAngle(state.yaw, wantYaw, FrameTime() * 540)
	local fwd = Angle(0, state.yaw, 0):Forward()
	local vel = ball:GetVelocity()
	local speed = Vector(vel.x, vel.y, 0):Length()

	local name = chooseAnim(ball, speed)
	local row = SK.Sheets.anims[name]
	if name ~= state.anim then
		local seq = m:LookupSequence(name)
		if seq and seq >= 0 then m:ResetSequence(seq) end
		state.anim, state.t = name, 0
	end
	local dur = math.max(0.05, m:SequenceDuration())
	state.t = state.t + FrameTime() * (row and row.speed or 1)
	m:SetCycle((state.t % dur) / dur)

	local d = ball:GetDiameter()
	local heightUnits = SK.Rule("james_height_ratio") * d
	local scale = heightUnits / math.max(1, SK.Content.james.height_cm or 180)
	local feet = ball:GetPos() - fwd * (ball:Radius() + d * SK.Rule("james_back_ratio"))
	local bottom = ball:GetPos().z - ball:Radius()
	local tr = util.TraceLine({ start = Vector(feet.x, feet.y, bottom + d * 0.5), endpos = Vector(feet.x, feet.y, bottom - d * 0.6),
		mask = MASK_SOLID, filter = function(e) return e ~= ball and not e:IsPlayer() and e:GetParent() ~= ball end })
	feet = Vector(feet.x, feet.y, tr.Hit and tr.HitPos.z or bottom)
	m:SetModelScale(scale, 0)
	m:SetPos(feet)
	m:SetAngles(Angle(0, state.yaw, 0))
	armPose(m)
	m:SetupBones()
end)
