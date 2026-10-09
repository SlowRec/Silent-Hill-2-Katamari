-- Control: the movement keys push the katamari relative to the camera, Shift dashes, Space restarts on the
-- results screen. Adapted from Katamari Sandbox (MIT, Ian Crocenzi); speeds are in katamari diameters.
local BLOCKED = bit.bor(IN_ATTACK, IN_ATTACK2, IN_JUMP, IN_DUCK, IN_USE, IN_RELOAD, IN_SPEED, IN_WALK, IN_ZOOM)

local function rollingBall(ply)
	local ball = ply:GetNWEntity("SKBall")
	if IsValid(ball) and ball.GetDiameter then return ball end
end

hook.Add("StartCommand", "sk_control_startcommand", function(ply, cmd)
	-- The server reads the keys and clears them; the client must send them untouched.
	if CLIENT then return end
	local ball = rollingBall(ply)
	if not ball then return end
	local state = SK.Stage.State()
	if state == "results" and cmd:KeyDown(IN_JUMP) and SK.Stage.CanRestart() then SK.Stage.Restart() end

	local fwd, side = cmd:GetForwardMove(), cmd:GetSideMove()
	local dash = cmd:KeyDown(IN_SPEED)
	cmd:ClearMovement()
	cmd:RemoveKey(BLOCKED)
	if not ball:GetRolling() then
		ball:SetInput(0)
		return
	end

	local phys = ball:GetPhysicsObject()
	if not IsValid(phys) then return end
	local yaw = cmd:GetViewAngles().y
	local auto = ply.SKAuto -- sk_selftest drives straight ahead
	if auto then
		if CurTime() < auto.untilT then fwd, side, yaw = 10000, 0, auto.yaw else ply.SKAuto = nil end
	end

	ball:SetInput((fwd > 0 and 1 or 0) + (fwd < 0 and 2 or 0) + (side < 0 and 4 or 0) + (side > 0 and 8 or 0) + (dash and 16 or 0))
	local ang = Angle(0, yaw, 0)
	local wish = ang:Forward() * fwd + ang:Right() * side
	wish.z = 0
	local dt = engine.TickInterval()
	local vel = phys:GetVelocity()
	local flat = Vector(vel.x, vel.y, 0)
	local d = ball:GetDiameter()
	local top = math.max(SK.Rule("min_speed"), d * SK.Rule("speed_diameters")) * (dash and SK.Rule("dash_mult") or 1)
	local accel = SK.Rule("push_accel") * d
	local target
	if wish:LengthSqr() > 1 then
		wish:Normalize()
		if dash then accel = accel * SK.Rule("dash_mult") end
		target = wish * top
		if dash and not ply.SKDashing then SK.Sounds.Emit(ball, "ouak.dash") end
		ply.SKDashing = dash
	else
		ply.SKDashing = false
		target = flat * math.max(0, 1 - SK.Rule("brake") * dt)
		accel = accel * 2
	end
	-- Steer the velocity straight toward the target, at most accel per second.
	local change = target - flat
	local maxStep = accel * dt
	if change:Length() > maxStep then change = change:GetNormalized() * maxStep end
	local newFlat = flat + change
	phys:SetVelocity(Vector(newFlat.x, newFlat.y, vel.z))
	-- Spin to match rolling, so friction never steers it off course.
	local r = ball:Radius()
	if r > 0 then
		local omega = Vector(0, 0, 1):Cross(newFlat) * (57.2958 / r)
		phys:SetAngleVelocity(phys:WorldToLocalVector(omega))
	end
end)

-- The hidden player rides along at the katamari's feet.
hook.Add("Move", "sk_control_move", function(ply, mv)
	local ball = rollingBall(ply)
	if not ball then return end
	mv:SetVelocity(vector_origin)
	mv:SetOrigin(ball:GetPos() - Vector(0, 0, ball:Radius()))
	return true
end)
