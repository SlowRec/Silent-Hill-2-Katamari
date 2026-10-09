-- Keeps Garry's Mod out of sight (hide sheet): no weapons, menus, chat, scoreboard, sprays or default HUD.
-- What can't be hidden (window title, Steam status, the listing) is said on the listing instead.

function GM:PlayerNoClip(ply, desired) return false end

if SERVER then
	function GM:PlayerLoadout(ply)
		ply:StripWeapons()
		ply:StripAmmo()
		return true
	end
	function GM:PlayerSwitchFlashlight(ply, enabled) return not enabled end
	function GM:PlayerSpray(ply) return true end
	function GM:CanPlayerSuicide(ply) return false end
	function GM:PlayerUse(ply, ent) return false end
	function GM:ShowHelp(ply) end
	function GM:ShowTeam(ply) end
	function GM:ShowSpare1(ply) end
	function GM:ShowSpare2(ply) end
	return
end

local hidden = {}
for _, name in ipairs(SK.Rule("hidden_hud_elements")) do hidden[name] = true end

function GM:HUDShouldDraw(name) return not hidden[name] end
function GM:HUDDrawTargetID() end
function GM:HUDDrawPickupHistory() end
function GM:DrawDeathNotice(x, y) end
function GM:ScoreboardShow() end
function GM:ScoreboardHide() end
function GM:ChatText(index, name, text, kind) return true end
function GM:PreDrawViewModel(vm, ply, weapon) return true end

-- Binds that would open Garry's Mod things. Movement, mouse and Esc stay untouched.
local BLOCKED = { "messagemode", "messagemode2", "+zoom", "+menu", "+menu_context", "impulse 100", "impulse 201",
	"noclip", "kill", "+voicerecord", "gm_showhelp", "gm_showteam", "gm_showspare1", "gm_showspare2", "+showscores",
	"undo", "gmod_undo", "+use", "+attack", "+attack2", "+reload", "lastinv", "invnext", "invprev", "slot" }

function GM:PlayerBindPress(ply, bind, pressed)
	bind = string.lower(bind)
	for _, b in ipairs(BLOCKED) do
		if string.find(bind, b, 1, true) == 1 then return true end
	end
end
