-- Katamari's maths, with no Garry's Mod calls so tools/bench can run it headless (plain Lua 5.1).
-- Sizes are katamari diameters in cm, volumes in cm^3. Rules come from the rules sheet through K.R.
SK = SK or {}
local K = {}
SK.Katamath = K

K.R = function(id) return SK.Rule(id) end

local function clamp(x, a, b) if x < a then return a elseif x > b then return b end return x end
local function lerp(t, a, b) return a + (b - a) * t end

function K.ToUnits(cm) return cm / K.R("cm_per_unit") end
function K.ToCm(units) return units * K.R("cm_per_unit") end

function K.SphereVolume(d) return math.pi / 6 * d ^ 3 end
function K.DiameterFromVolume(v) return (6 * v / math.pi) ^ (1 / 3) end

-- Katamari's pickup rule: the katamari needs about 10x the object's volume, i.e. the object's
-- sphere-equivalent diameter is at most catch_ratio (0.464) of the katamari's. Rounded down to the mm.
function K.PickupFromVolume(volumeCm3)
	local deq = K.DiameterFromVolume(volumeCm3)
	return math.floor(deq / K.R("catch_ratio") * 10) / 10
end

function K.CanCatch(diameterCm, pickupCm) return diameterCm + 1e-6 >= pickupCm end

-- Growth falls off once the goal is passed (rules.after_goal_curve: {size/goal, multiplier} points).
function K.AfterGoal(diameterCm, goalCm)
	local curve = K.R("after_goal_curve")
	local x = diameterCm / goalCm
	if x <= curve[1][1] then return curve[1][2] end
	for i = 2, #curve do
		local a, b = curve[i - 1], curve[i]
		if x <= b[1] then return lerp((x - a[1]) / (b[1] - a[1]), a[2], b[2]) end
	end
	return curve[#curve][2]
end

-- New katamari volume after catching an object (volume and rate from the player's game).
function K.Grow(volumeCm3, objVolumeCm3, rate, goalCm, maxCm, mult)
	local d = K.DiameterFromVolume(volumeCm3)
	local add = objVolumeCm3 * rate * (mult or K.R("growth_mult")) * K.AfterGoal(d, goalCm)
	local v = volumeCm3 + add
	return math.min(v, K.SphereVolume(maxCm))
end

function K.DisplayCm(diameterCm) return diameterCm + K.R("size_display_offset_cm") end

-- Katamari's size text: big numbers, small units. Returns { {text, isNumber}, ... }
function K.FormatSize(cm)
	local parts = {}
	local function add(n, u)
		parts[#parts + 1] = { tostring(n), true }
		parts[#parts + 1] = { u, false }
	end
	if cm < 100 then
		local whole = math.floor(cm)
		add(whole, "cm")
		add(math.floor((cm - whole) * 10), "mm")
	elseif cm < 100000 then
		local m = math.floor(cm / 100)
		add(m, "m")
		add(math.floor(cm - m * 100), "cm")
	else
		local km = math.floor(cm / 100000)
		add(km, "km")
		add(math.floor((cm - km * 100000) / 100), "m")
	end
	return parts
end

function K.FormatSizeText(cm)
	local out = {}
	for _, p in ipairs(K.FormatSize(cm)) do out[#out + 1] = p[1] .. (p[2] and "" or " ") end
	return (string.gsub(table.concat(out), "%s+$", ""))
end

function K.FormatTime(seconds)
	seconds = math.max(0, math.ceil(seconds))
	return string.format("%d:%02d", math.floor(seconds / 60), seconds % 60)
end

-- The size mark below and above the current size (rules.milestones_cm).
function K.Milestones(cm, startCm)
	local list = K.R("milestones_cm")
	local prev, nxt = startCm, nil
	for _, m in ipairs(list) do
		if m <= cm then prev = m elseif not nxt then nxt = m end
	end
	return prev, nxt or prev * 2
end

-- 0 = below the goal, 1..#bands = which result band was reached.
function K.Rating(cm, bands)
	local r = 0
	for i, b in ipairs(bands) do if cm >= b then r = i end end
	return r
end

K.Clamp, K.Lerp = clamp, lerp
