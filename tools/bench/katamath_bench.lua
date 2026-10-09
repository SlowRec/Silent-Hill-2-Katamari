-- Headless bench for the Katamari maths (sh_katamath.lua) with the real sheet values. Plain Lua 5.1:
--   lua5.1 tools/bench/katamath_bench.lua        (from the repo root; exit code 1 on any failure)
-- Oracles: REROLL datamine anchors (shookietea/reroll-notes) and a greedy roll-through of our own stage.
local GM_DIR = "addons/silent_katamari/gamemodes/silentkatamari/gamemode/sk/"
SK = {}
dofile(GM_DIR .. "sh_sheets.lua")
SK.Rule = function(id) return SK.Rules[id] end
dofile(GM_DIR .. "sh_katamath.lua")
local K = SK.Katamath

local failed, passed = 0, 0
local function check(name, ok, detail)
	if ok then passed = passed + 1 else failed = failed + 1 end
	print(string.format("%s %s%s", ok and "ok  " or "FAIL", name, detail and ("  (" .. detail .. ")") or ""))
end
local function near(a, b, eps) return math.abs(a - b) <= (eps or 1e-6) end

-- 1. Pickup rule against REROLL objects: sphere-equivalent diameter -> required katamari size.
local anchors = { { "Ant", 0.73, 1.5 }, { "Thumbtack", 1.46, 3.1 }, { "Caramel", 2.07, 4.4 }, { "Hard Eraser", 3.44, 7.4 } }
for _, a in ipairs(anchors) do
	local vol = K.SphereVolume(a[2])
	local got = K.PickupFromVolume(vol)
	check("pickup " .. a[1], near(got, a[3], 0.05), string.format("deq %.2f cm -> %.1f cm, REROLL %.1f cm", a[2], got, a[3]))
end
check("can catch at exactly the pickup size", K.CanCatch(4.4, 4.4))
check("can't catch 1 mm under", not K.CanCatch(4.3, 4.4))

-- 2. Growth after the goal follows the measured Make a Star 1 curve, divided by the 0.8 object rate.
check("after-goal x1.0 at goal", near(K.AfterGoal(15, 15), 1.0))
check("after-goal x0.5 at 1.5 goal", near(K.AfterGoal(22.5, 15), 0.5))
check("after-goal x0.2 at 2 goal", near(K.AfterGoal(30, 15), 0.2))
check("after-goal held past the curve", near(K.AfterGoal(60, 15), 0.2))
local v = K.SphereVolume(44)
check("growth capped at max size", near(K.DiameterFromVolume(K.Grow(v, 1e6, 0.8, 15, 45, 1)), 45, 1e-6))

-- 3. Text.
check("size text cm", K.FormatSizeText(5.37) == "5cm 3mm", K.FormatSizeText(5.37))
check("size text m", K.FormatSizeText(123.4) == "1m 23cm", K.FormatSizeText(123.4))
check("time text", K.FormatTime(239.2) == "4:00" and K.FormatTime(61) == "1:01", K.FormatTime(239.2))

-- 4. Roll through our own stage greedily (smallest first, every catchable object), as Katamari's
--    designers check that a stage holds about 2x the volume the goal needs (REROLL MAS1: 20.9 cm max).
local stage = SK.Sheets.stage.clearing
local placed = {}
for _, tid in ipairs(stage.tiers) do
	local t = SK.Sheets.tiers[tid]
	if t.catchable then
		for i = 1, t.count do
			-- spread evenly over the tier's pickup range; volume from the pickup rule's inverse
			local p = t.pickup_min_cm + (t.pickup_max_cm - t.pickup_min_cm) * (i - 0.5) / t.count
			local deq = p * K.R("catch_ratio")
			placed[#placed + 1] = { pickup = p, vol = K.SphereVolume(deq) }
		end
	end
end
table.sort(placed, function(a, b) return a.pickup < b.pickup end)
local vol, caught, goalAt = K.SphereVolume(stage.start_diameter_cm), 0, nil
local pass = 0
repeat
	local before = caught
	pass = pass + 1
	for _, o in ipairs(placed) do
		if not o.got and K.CanCatch(K.DiameterFromVolume(vol), o.pickup) then
			o.got = true
			caught = caught + 1
			vol = K.Grow(vol, o.vol, K.R("volume_rate"), stage.goal_cm, stage.max_cm)
			if not goalAt and K.DiameterFromVolume(vol) >= stage.goal_cm then goalAt = caught end
		end
	end
until caught == before or pass > 20
local final = K.DiameterFromVolume(vol)
check("stage reaches the goal", goalAt ~= nil, string.format("goal %.0f cm after %s of %d objects, ends at %.1f cm",
	stage.goal_cm, tostring(goalAt), #placed, final))
check("stage leaves room past the goal (top rating reachable)", final >= stage.rating_cm[#stage.rating_cm],
	string.format("final %.1f cm, best band %.0f cm", final, stage.rating_cm[#stage.rating_cm]))
check("goal needs a fair share of the stage (30-65% of objects; Make a Star 1: 53%)",
	goalAt ~= nil and goalAt >= #placed * 0.3 and goalAt <= #placed * 0.65,
	string.format("goal after %s of %d objects = %.0f%%", tostring(goalAt), #placed, 100 * (goalAt or 0) / #placed))

print(string.format("\n%d passed, %d failed", passed, failed))
os.exit(failed == 0 and 0 or 1)
