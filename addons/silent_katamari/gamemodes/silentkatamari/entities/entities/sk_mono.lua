-- One Once Upon A KATAMARI object ("mono"): its own mesh from the player's copy, at its true size,
-- with convex physics from its hull. It stays put until the katamari rolls it up.
AddCSLuaFile()

ENT.Type = "anim"
ENT.Base = "base_anim"
ENT.PrintName = "Katamari object"
ENT.Spawnable = false
ENT.DisableDuplicator = true
ENT.RenderGroup = RENDERGROUP_OPAQUE

function ENT:SetupDataTables()
	self:NetworkVar("String", 0, "ObjId")
	self:NetworkVar("Bool", 0, "Catchable")
end

local function hullPoints(o)
	local K = SK.Katamath
	local pts = {}
	for i = 1, #o.hull - 2, 3 do
		pts[#pts + 1] = Vector(K.ToUnits(o.hull[i]), K.ToUnits(o.hull[i + 1]), K.ToUnits(o.hull[i + 2]))
	end
	return pts
end

local function bounds(o)
	local K = SK.Katamath
	local sx, sy, sz = K.ToUnits(o.size[1]) * 0.5, K.ToUnits(o.size[2]) * 0.5, K.ToUnits(o.size[3])
	return Vector(-sx, -sy, 0), Vector(sx, sy, sz)
end

if SERVER then
	function ENT:Initialize()
		self:SetModel("models/hunter/blocks/cube025x025x025.mdl") -- never drawn; physics comes from the hull
		self:DrawShadow(false)
		local o = SK.Content.Object(self:GetObjId())
		if not o then
			self:Remove()
			return
		end
		local mins, maxs = bounds(o)
		local pts = hullPoints(o)
		if #pts >= 4 then
			self:PhysicsInitConvex(pts, "default_silent")
		else
			self:PhysicsInitBox(mins, maxs, "default_silent")
		end
		self:SetMoveType(MOVETYPE_VPHYSICS)
		self:SetSolid(SOLID_VPHYSICS)
		self:EnableCustomCollisions(true)
		self:SetCollisionBounds(mins, maxs)
		local phys = self:GetPhysicsObject()
		if IsValid(phys) then
			phys:SetMaterial("default_silent")
			phys:EnableMotion(false)
		end
	end
else
	-- The object id arrives over the network after the entity exists, so bounds are set once it's here.
	function ENT:Think()
		if self.SKBounds then return end
		local o = SK.Content.Object(self:GetObjId())
		if o then
			local mins, maxs = bounds(o)
			self:SetRenderBounds(mins, maxs)
			self.SKBounds = true
		end
	end

	function ENT:Draw()
		if SK.Meshes then SK.Meshes.DrawObject(self) end
	end
end
