-- Katamari camera: behind and above the ball, pulling back as it grows (Katamari Sandbox, MIT).
local curDist, curHeight

local function myBall()
	local ball = LocalPlayer():GetNWEntity("SKBall")
	if IsValid(ball) and ball.GetDiameter and ball:GetDiameter() > 0 then return ball end
end
SK.MyBall = myBall

function GM:CalcView(ply, pos, angles, fov)
	local ball = myBall()
	if not ball then curDist = nil return self.BaseClass.CalcView(self, ply, pos, angles, fov) end
	local d = ball:GetDiameter()
	local wantDist = math.max(SK.Rule("cam_min_dist"), d * SK.Rule("cam_dist_diameters"))
	local wantHeight = d * SK.Rule("cam_height_diameters")
	local k = math.min(1, FrameTime() * SK.Rule("cam_smooth"))
	curDist = curDist and Lerp(k, curDist, wantDist) or wantDist
	curHeight = curHeight and Lerp(k, curHeight, wantHeight) or wantHeight

	local center = ball:GetPos()
	local look = Angle(math.Clamp(angles.p, -30, 60), angles.y, 0)
	local want = center - look:Forward() * curDist + Vector(0, 0, curHeight)
	-- Keep out of solid things, but never inside the katamari: if they're closer than that, go up instead.
	local tr = util.TraceHull({ start = center + Vector(0, 0, d * 0.25), endpos = want, mins = Vector(-1, -1, -1),
		maxs = Vector(1, 1, 1), mask = MASK_SOLID_BRUSHONLY })
	local origin = tr.HitPos
	local minDist = ball:Radius() * 1.6 + 2
	if origin:Distance(center) < minDist then
		local up = util.TraceHull({ start = center, endpos = center + Vector(0, 0, minDist + curHeight), mins = Vector(-1, -1, -1),
			maxs = Vector(1, 1, 1), mask = MASK_SOLID_BRUSHONLY })
		origin = up.HitPos
	end
	local target = center + Vector(0, 0, d * 0.35)
	return { origin = origin, angles = (target - origin):Angle(), fov = fov, drawviewer = false, znear = 0.5 }
end

function GM:ShouldDrawLocalPlayer(ply) return false end
