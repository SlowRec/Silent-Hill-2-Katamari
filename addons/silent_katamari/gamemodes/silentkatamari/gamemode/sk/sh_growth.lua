-- Katamari's rules applied to entities: each object carries its own pickup size, volume and rate
-- from the player's Once Upon A KATAMARI (objects.json); katamath does the maths.
local G = {}
SK.Growth = G
local K = SK.Katamath

local growthCvar = CreateConVar("sk_growth", SK.Sheets.commands.sk_growth.default, { FCVAR_REPLICATED },
	"Growth multiplier (1 = Katamari's own volume rate)", 0.1, 20)

function G.Stage() return SK.Sheets.stage[SK.Sheets.stage_order[1]] end

function G.StartDiameterUnits() return K.ToUnits(G.Stage().start_diameter_cm) end

-- An object entity's data row (objects.json), or nil.
function G.ObjectData(ent)
	if not IsValid(ent) or not ent.GetObjId then return nil end
	return SK.Content.Object(ent:GetObjId())
end

function G.CanCatch(ball, ent)
	local o = G.ObjectData(ent)
	if not o or not ent:GetCatchable() then return false end
	return K.CanCatch(K.ToCm(ball:GetDiameter()), o.pickup_cm)
end

-- Grows the ball by one caught object; returns the new volume in cm^3.
function G.Grow(ball, ent)
	local o = G.ObjectData(ent)
	local st = G.Stage()
	local volCm3 = K.SphereVolume(K.ToCm(ball:GetDiameter()))
	return K.Grow(volCm3, o.volume_cm3, o.rate or SK.Rule("volume_rate"), st.goal_cm, st.max_cm,
		SK.Rule("growth_mult") * growthCvar:GetFloat())
end
