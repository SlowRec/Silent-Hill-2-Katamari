-- Attaching: a caught object stops being a physics object and becomes part of the katamari.
-- Adapted from Katamari Sandbox (MIT, Ian Crocenzi).
local A = {}
SK.Attach = A

local function caught(ent) return IsValid(ent) and ent:GetNWBool("SKCaught") end
A.IsCaught = caught

local function longestSide(ent)
	local d = ent:OBBMaxs() - ent:OBBMins()
	return math.max(d.x, d.y, d.z)
end

function A.Attach(ball, ent)
	local r = ball:Radius()
	local center = ball:GetPos()
	local side = longestSide(ent)
	local dir = ent:WorldSpaceCenter() - center
	if dir:LengthSqr() < 0.0001 then dir = Vector(0, 0, 1) end
	dir:Normalize()

	ent:PhysicsDestroy()
	ent:SetSolid(SOLID_NONE)
	ent:SetMoveType(MOVETYPE_NONE)
	ent:SetCollisionGroup(COLLISION_GROUP_IN_VEHICLE)

	-- Sink it into the surface where it touched, keeping how it was turned.
	local dist = r + side * (0.5 - SK.Rule("attach_depth"))
	local offset = ent:GetPos() - ent:WorldSpaceCenter()
	ent:SetPos(center + dir * dist + offset)
	ent:SetParent(ball)
	ent:SetNWBool("SKCaught", true)

	ball.Collected = ball.Collected or {}
	table.insert(ball.Collected, { ent = ent, outer = dist + side * 0.5 })
	ball:SetCaught(#ball.Collected)
end

-- After growing: hide what is buried deep inside, and keep the count bounded.
function A.Cull(ball)
	local list = ball.Collected
	if not list then return end
	local hideBelow = ball:Radius() * SK.Rule("bury_hide_ratio")
	for i = #list, 1, -1 do
		local c = list[i]
		if not IsValid(c.ent) then
			table.remove(list, i)
		elseif c.outer < hideBelow then
			c.ent:SetNoDraw(true)
		end
	end
	local max = SK.Rule("max_attached")
	if #list > max then
		table.sort(list, function(a, b) return a.outer > b.outer end)
		for i = #list, max + 1, -1 do
			SafeRemoveEntity(list[i].ent)
			list[i] = nil
		end
	end
end

function A.Clear(ball)
	for _, c in ipairs(ball.Collected or {}) do SafeRemoveEntity(c.ent) end
	ball.Collected = {}
end
