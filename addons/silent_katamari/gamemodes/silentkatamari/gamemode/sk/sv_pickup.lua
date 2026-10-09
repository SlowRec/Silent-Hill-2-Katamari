-- Pickup: an object touching the rolling katamari is caught if the katamari is big enough for it
-- (its own pickup size from the player's Katamari), otherwise it just blocks.
local P = {}
SK.Pickup = P
local G = SK.Growth
local K = SK.Katamath

P.Log = false -- sk_selftest turns this on so the test can read results from the console log

function P.Check(ball)
	local center, r = ball:GetPos(), ball:Radius()
	local reach = r + SK.Rule("touch_margin")
	local grew = false
	for _, ent in ipairs(ents.FindInSphere(center, reach + r)) do
		if ent:GetClass() == "sk_mono" and not SK.Attach.IsCaught(ent) and not IsValid(ent:GetParent())
			and ent:NearestPoint(center):Distance(center) <= reach and G.CanCatch(ball, ent) then
			local o = G.ObjectData(ent)
			local volume = G.Grow(ball, ent)
			SK.Attach.Attach(ball, ent)
			ball:SetVolumeCm3(volume)
			SK.Sounds.Emit(ball, SK.Sounds.RollupFor(o.pickup_cm), math.random(95, 105))
			grew = true
			if P.Log then
				print(string.format("[SilentKatamari] caught %s (%s, needs %.1f cm) -> %.2f cm", o.name, o.id,
					o.pickup_cm, K.ToCm(ball:GetDiameter())))
			end
			center, r = ball:GetPos(), ball:Radius()
			reach = r + SK.Rule("touch_margin")
		end
	end
	if grew then SK.Attach.Cull(ball) end
end

hook.Add("Think", "sk_pickup_think", function()
	if not SK.Stage or SK.Stage.State() ~= "rolling" then return end
	for _, ball in ipairs(ents.FindByClass("sk_ball")) do
		if ball:GetRolling() then P.Check(ball) end
	end
end)
