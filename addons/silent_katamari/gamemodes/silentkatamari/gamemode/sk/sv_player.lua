-- The player is always the one rolling: hidden, invulnerable, riding along with one katamari.
local PL = {}
SK.Player = PL

function PL.Ball(ply)
	local ball = ply:GetNWEntity("SKBall")
	if IsValid(ball) then return ball end
	ball = ents.Create("sk_ball")
	ball:SetPos(SK.Stage.StartPos() + Vector(0, 0, SK.Growth.StartDiameterUnits() * 0.5 + 1))
	ball:SetRoller(ply)
	ball:SetOwner(ply) -- no collisions between you and your own katamari
	ball:Spawn()
	ply:SetNWEntity("SKBall", ball)
	return ball
end

function GM:PlayerSpawn(ply)
	self.BaseClass.PlayerSpawn(self, ply)
	ply:SetNoDraw(true)
	ply:DrawShadow(false)
	ply:SetNotSolid(true)
	ply:DrawWorldModel(false)
	ply:GodEnable()
	ply:SetAvoidPlayers(false)
	local ball = PL.Ball(ply)
	ply:SetPos(ball:GetPos() - Vector(0, 0, ball:Radius()))
	ply:SetEyeAngles(Angle(20, 0, 0))
end

function GM:EntityTakeDamage(ent, dmg) return true end

function GM:PlayerDeathThink(ply)
	ply:Spawn()
	return true
end
