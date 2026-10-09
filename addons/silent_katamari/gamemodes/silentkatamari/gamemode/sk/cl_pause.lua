-- Our pause menu in place of Garry's Mod's (Esc): resume, restart, quit. Painted by hand so no
-- Garry's Mod skin shows. Pausing stops the clock and the katamari on the server (sv_stage.lua).
local L = SK.L
local menu

local function setPaused(on)
	net.Start("sk_pause")
	net.WriteBool(on)
	net.SendToServer()
end

local function close()
	if IsValid(menu) then menu:Remove() end
	menu = nil
	gui.EnableScreenClicker(false)
	setPaused(false)
end

local function button(parent, label, y, onClick)
	local b = vgui.Create("DButton", parent)
	b:SetText("")
	b:SetSize(ScrW() * 0.3, ScrH() * 0.065)
	b:SetPos((ScrW() - b:GetWide()) / 2, y)
	b.Paint = function(self, w, h)
		local hot = self:IsHovered()
		surface.SetDrawColor(hot and Color(235, 235, 228, 40) or Color(0, 0, 0, 0))
		surface.DrawRect(0, 0, w, h)
		draw.SimpleText(label, "SKMid", w / 2, h / 2, hot and Color(255, 255, 250) or Color(200, 200, 190), TEXT_ALIGN_CENTER, TEXT_ALIGN_CENTER)
	end
	b.DoClick = onClick
	return b
end

local function open()
	if SK.HudFonts then SK.HudFonts(math.Round(ScrH() / SK.Rule("hud_ref_height"), 3)) end
	menu = vgui.Create("EditablePanel")
	menu:SetSize(ScrW(), ScrH())
	menu:SetPos(0, 0)
	menu:MakePopup()
	menu:SetKeyboardInputEnabled(false)
	menu.Paint = function(_, w, h)
		local c = SK.Rule("fog_color")
		surface.SetDrawColor(c[1] * 0.25, c[2] * 0.25, c[3] * 0.25, 215)
		surface.DrawRect(0, 0, w, h)
		draw.SimpleText(L("pause_title"), "SKTitle", w / 2, h * 0.28, Color(235, 235, 228), TEXT_ALIGN_CENTER, TEXT_ALIGN_CENTER)
	end
	local y = ScrH() * 0.40
	local step = ScrH() * 0.08
	button(menu, L("pause_resume"), y, close)
	button(menu, L("pause_restart"), y + step, function()
		RunConsoleCommand("sk_restart")
		close()
	end)
	button(menu, L("pause_quit"), y + step * 2, function()
		RunConsoleCommand("quit")
	end)
	gui.EnableScreenClicker(true)
	setPaused(true)
end

function GM:OnPauseMenuShow()
	if IsValid(menu) then close() else open() end
	return false
end
