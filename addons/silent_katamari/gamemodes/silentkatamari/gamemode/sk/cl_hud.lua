-- On screen (hud sheet): size display, goal, timer, count, intro line, results, controls hint and the
-- missing-game notices. Katamari's own font when the converter found it; otherwise a system font.
local K = SK.Katamath
local L = SK.L
local scaleCvar = CreateClientConVar("sk_hud_scale", SK.Sheets.commands.sk_hud_scale.default, true, false,
	SK.Sheets.commands.sk_hud_scale.help, 0.4, 2.5)

local fontK
local function fonts(k)
	if fontK == k then return end
	fontK = k
	local fam = (SK.Content.font and SK.Content.font.family) or "Arial"
	surface.CreateFont("SKBig", { font = fam, size = math.floor(64 * k), weight = 800, antialias = true, extended = true })
	surface.CreateFont("SKMid", { font = fam, size = math.floor(34 * k), weight = 700, antialias = true, extended = true })
	surface.CreateFont("SKSmall", { font = fam, size = math.floor(24 * k), weight = 600, antialias = true, extended = true })
	surface.CreateFont("SKTitle", { font = fam, size = math.floor(54 * k), weight = 600, antialias = true, extended = true })
end
SK.HudFonts = fonts

local WHITE, DIM, RED, SHADOW = Color(235, 235, 228), Color(200, 200, 190, 200), Color(196, 48, 40), Color(0, 0, 0, 170)

local function text(s, font, x, y, col, ax, ay)
	draw.SimpleTextOutlined(s, font, x, y, col, ax or TEXT_ALIGN_LEFT, ay or TEXT_ALIGN_TOP, 2, SHADOW)
end

-- Katamari's size text: numbers big, units small.
local function sizeText(cm, x, y, k, col)
	for _, p in ipairs(K.FormatSize(cm)) do
		local font = p[2] and "SKBig" or "SKMid"
		surface.SetFont(font)
		local w, h = surface.GetTextSize(p[1])
		text(p[1], font, x, y + (p[2] and 0 or 64 * k - h - 6 * k), col)
		x = x + w + (p[2] and 2 or 10) * k
	end
end

local function ring(cx, cy, r, thick, frac, col)
	draw.NoTexture()
	surface.SetDrawColor(col)
	local segs = 48
	for i = 0, math.floor(segs * frac) - 1 do
		local a0, a1 = (i / segs) * 2 * math.pi - math.pi / 2, ((i + 1) / segs) * 2 * math.pi - math.pi / 2
		surface.DrawPoly({
			{ x = cx + math.cos(a0) * r, y = cy + math.sin(a0) * r }, { x = cx + math.cos(a1) * r, y = cy + math.sin(a1) * r },
			{ x = cx + math.cos(a1) * (r - thick), y = cy + math.sin(a1) * (r - thick) },
			{ x = cx + math.cos(a0) * (r - thick), y = cy + math.sin(a0) * (r - thick) } })
	end
end

local lastNext, shownAt
local NOTICE_S = 15 -- notices fade after this, except while the stage is blocked by a missing game

function GM:HUDPaint()
	local k = ScrH() / SK.Rule("hud_ref_height") * scaleCvar:GetFloat()
	fonts(math.Round(k, 3))
	shownAt = shownAt or CurTime()
	local st = SK.Growth.Stage()
	local state = GetGlobal2String("sk_state", "intro")
	local ball = SK.MyBall and SK.MyBall()

	-- Missing or unreadable games: said plainly, in the middle.
	local notices = SK.Content.Notices()
	local y = ScrH() * 0.30
	local showNotices = state == "blocked" or CurTime() - shownAt < NOTICE_S
	for _, n in ipairs(notices) do
		if showNotices then
			local w = ScrW() * 0.62
			local lines = {}
			local msg = L(n.id, n.vars)
			surface.SetFont("SKSmall")
			local cur = ""
			for word in string.gmatch(msg, "%S+") do
				local test = cur == "" and word or (cur .. " " .. word)
				if surface.GetTextSize(test) > w - 40 * k then lines[#lines + 1] = cur cur = word else cur = test end
			end
			lines[#lines + 1] = cur
			local h = #lines * 30 * k + 30 * k
			draw.RoundedBox(6, (ScrW() - w) / 2, y, w, h, Color(10, 10, 10, 200))
			for j, line in ipairs(lines) do
				text(line, "SKSmall", ScrW() / 2, y + 15 * k + (j - 1) * 30 * k, WHITE, TEXT_ALIGN_CENTER)
			end
			y = y + h + 12 * k
		end
	end

	if not IsValid(ball) then return end
	local cm = K.ToCm(ball:GetDiameter())
	local shown = K.DisplayCm(cm)

	-- Size display (top left): ring filling toward the next size mark.
	local prev, nxt = K.Milestones(cm, st.start_diameter_cm)
	if lastNext and nxt > lastNext then SK.Sounds.PlayLocal("ouak.scaleup") end
	lastNext = nxt
	local frac = math.Clamp((cm - prev) / math.max(0.001, nxt - prev), 0, 1)
	local cx, cy, r = 150 * k, 150 * k, 110 * k
	draw.RoundedBox(110 * k, cx - r, cy - r, 2 * r, 2 * r, Color(20, 20, 20, 150))
	ring(cx, cy, r, 10 * k, 1, Color(255, 255, 255, 40))
	ring(cx, cy, r, 10 * k, frac, Color(210, 205, 190, 230))
	sizeText(shown, cx - 85 * k, cy - 50 * k, k, WHITE)
	text(K.FormatSizeText(nxt), "SKSmall", cx, cy + 40 * k, DIM, TEXT_ALIGN_CENTER)

	if state == "blocked" then return end
	-- Goal and count under it.
	text(K.FormatSizeText(st.goal_cm), "SKMid", cx, cy + r + 18 * k, cm >= st.goal_cm and Color(220, 220, 140) or WHITE, TEXT_ALIGN_CENTER)
	text(L("objects_left", { count = ball:GetCaught() }), "SKSmall", 40 * k, cy + r + 64 * k, DIM)

	-- Timer (top right).
	local left = GetGlobal2Float("sk_end", 0) - CurTime()
	if GetGlobal2Bool("sk_paused", false) then left = GetGlobal2Float("sk_left", 0) end
	if state == "intro" then left = st.time_s end
	if state == "results" then left = 0 end
	local danger = left <= SK.Rule("radio_start_left_s") and state == "rolling"
	text(K.FormatTime(left), "SKBig", ScrW() - 50 * k, 40 * k, danger and RED or WHITE, TEXT_ALIGN_RIGHT)

	-- Intro: the goal line.
	if state == "intro" or (state == "rolling" and CurTime() - GetGlobal2Float("sk_intro_end", 0) < 1) then
		text(L("goal_intro", { goal = K.FormatSizeText(st.goal_cm), time = K.FormatTime(st.time_s) }), "SKTitle",
			ScrW() / 2, ScrH() * 0.42, WHITE, TEXT_ALIGN_CENTER)
	end
	-- Controls, first seconds only.
	if CurTime() - shownAt < 12 and state ~= "results" then
		text(L("controls_hint"), "SKSmall", ScrW() / 2, ScrH() - 60 * k, DIM, TEXT_ALIGN_CENTER)
	end

	-- Results.
	if state == "results" then
		local final = GetGlobal2Float("sk_final_cm", cm)
		local rating = K.Rating(final, st.rating_cm)
		surface.SetDrawColor(0, 0, 0, 150)
		surface.DrawRect(0, ScrH() * 0.3, ScrW(), ScrH() * 0.36)
		text(L("time_up"), "SKMid", ScrW() / 2, ScrH() * 0.34, DIM, TEXT_ALIGN_CENTER)
		local line = rating > 0 and L("result_success", { size = K.FormatSizeText(K.DisplayCm(final)) })
			or L("result_fail", { size = K.FormatSizeText(K.DisplayCm(final)) })
		text(line, "SKTitle", ScrW() / 2, ScrH() * 0.42, rating > 0 and WHITE or RED, TEXT_ALIGN_CENTER)
		if rating > 0 then
			text(string.rep("*", rating), "SKBig", ScrW() / 2, ScrH() * 0.50, Color(220, 220, 140), TEXT_ALIGN_CENTER)
		end
		text(L("result_again", { key = input.LookupBinding("+jump") or "SPACE" }), "SKSmall", ScrW() / 2, ScrH() * 0.60, DIM, TEXT_ALIGN_CENTER)
	end
end
