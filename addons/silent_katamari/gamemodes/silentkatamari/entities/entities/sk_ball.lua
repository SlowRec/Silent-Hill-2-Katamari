-- The katamari: one physics sphere that grows. Caught objects are parented to it (sv_attach.lua).
-- Adapted from Katamari Sandbox's katamari_ball (MIT, Ian Crocenzi).
AddCSLuaFile()

ENT.Type = "anim"
ENT.Base = "base_anim"
ENT.PrintName = "Katamari"
ENT.Spawnable = false
ENT.DisableDuplicator = true
ENT.RenderGroup = RENDERGROUP_OPAQUE

function ENT:SetupDataTables()
	self:NetworkVar("Float", 0, "Diameter")      -- Source units
	self:NetworkVar("Bool", 0, "Rolling")
	self:NetworkVar("Entity", 0, "Roller")
	self:NetworkVar("Int", 0, "Caught")
	self:NetworkVar("Int", 1, "Input") -- held keys for James's animation: 1 fwd, 2 back, 4 left, 8 right, 16 dash
end

function ENT:Radius() return self:GetDiameter() * 0.5 end

if SERVER then
	function ENT:Initialize()
		-- A stock model only so the engine has one; it is never drawn (ENT:Draw draws Katamari's core).
		self:SetModel("models/hunter/misc/sphere025x025.mdl")
		self:DrawShadow(false)
		self.Collected = {}
		self:ResetKatamari()
	end

	-- Rebuilds the physics sphere at a new size, keeping its motion.
	function ENT:SetSize(diameter)
		local old = self:GetDiameter()
		local phys = self:GetPhysicsObject()
		local vel, angvel = Vector(), Vector()
		local moving = self:GetRolling()
		if IsValid(phys) then vel, angvel = phys:GetVelocity(), phys:GetAngleVelocity() end
		local r = diameter * 0.5
		if old > 0 and diameter > old then self:SetPos(self:GetPos() + Vector(0, 0, (diameter - old) * 0.5)) end
		self:PhysicsInitSphere(r, "default_silent")
		self:SetCollisionBounds(Vector(-r, -r, -r), Vector(r, r, r))
		self:SetDiameter(diameter)
		phys = self:GetPhysicsObject()
		if IsValid(phys) then
			local rel = diameter / SK.Growth.StartDiameterUnits()
			phys:SetMass(math.Clamp(10 * rel * rel, 5, 50000))
			phys:SetMaterial("default_silent")
			phys:SetDamping(0.05, 0.5)
			phys:EnableMotion(moving)
			if moving then
				phys:Wake()
				phys:SetVelocity(vel)
				phys:AddAngleVelocity(angvel)
			end
		end
	end

	function ENT:SetActive(on)
		self:SetRolling(on)
		local phys = self:GetPhysicsObject()
		if IsValid(phys) then
			phys:EnableMotion(on)
			if on then phys:Wake() end
		end
	end

	function ENT:SetVolumeCm3(v)
		self.VolumeCm3 = v
		self:SetSize(SK.Katamath.ToUnits(SK.Katamath.DiameterFromVolume(v)))
	end

	-- Back to the starting size with nothing attached.
	function ENT:ResetKatamari()
		SK.Attach.Clear(self)
		self:SetDiameter(0)
		self:SetVolumeCm3(SK.Katamath.SphereVolume(SK.Growth.Stage().start_diameter_cm))
		self:SetCaught(0)
	end

	function ENT:PhysicsCollide(data)
		local other = data.HitEntity
		if not self:GetRolling() or data.Speed < self:GetDiameter() * 3 then return end
		if IsValid(other) and other:GetClass() == "sk_mono" and SK.Growth.CanCatch(self, other) then return end
		if (self.NextHit or 0) < CurTime() then
			self.NextHit = CurTime() + 0.4
			SK.Sounds.Emit(self, "ouak.hit")
		end
	end

	function ENT:OnRemove()
		SK.Attach.Clear(self)
	end
else
	function ENT:Draw()
		if SK.Meshes then SK.Meshes.DrawCore(self) end
	end

	function ENT:Think()
		local r = self:Radius() * 1.3
		self:SetRenderBounds(Vector(-r, -r, -r), Vector(r, r, r))
	end
end
