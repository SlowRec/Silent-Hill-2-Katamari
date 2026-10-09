-- Sounds converted from the player's own games (sounds sheet). A row whose files aren't on this PC
-- stays silent: no other sound ever stands in for it.
local S = {}
SK.Sounds = S
local files = {}   -- sound id -> { "silent_katamari/ouak/x.wav", ... } (paths under sound/)

for id, row in pairs(SK.Sheets.sounds) do
	local list = {}
	for _, ex in ipairs(row.extract_ids) do
		local out = SK.Sheets.extract[ex].output -- "sound/silent_katamari/ouak/x.wav"
		if file.Exists(out, "GAME") then list[#list + 1] = string.sub(out, 7) end
	end
	files[id] = list
	if #list > 0 and not row.loop then
		sound.Add({ name = id, channel = CHAN_STATIC, volume = row.volume,
			level = row.level > 0 and row.level or 75, pitch = 100, sound = list })
	end
end

function S.Has(id) return files[id] ~= nil and #files[id] > 0 end

-- Positional sound from an entity.
function S.Emit(ent, id, pitch)
	if S.Has(id) and IsValid(ent) then ent:EmitSound(id, nil, pitch or 100) end
end

-- Roll-up sound for an object of this pickup size (rules.rollup_tiers_cm).
function S.RollupFor(pickupCm)
	local t = SK.Rule("rollup_tiers_cm")
	if pickupCm < t[1] then return "ouak.rollup_s" end
	if pickupCm < t[2] then return "ouak.rollup_m" end
	return "ouak.rollup_l"
end

if CLIENT then
	-- Heard only by the local player (level 0 rows).
	function S.PlayLocal(id)
		if S.Has(id) then surface.PlaySound(files[id][math.random(#files[id])]) end
	end

	-- Looping streams (music, ambience, radio): started once, volume driven every frame by atmosphere.
	local loops = {}
	function S.Loop(id)
		if loops[id] ~= nil or not S.Has(id) then return loops[id] end
		loops[id] = false -- loading
		sound.PlayFile("sound/" .. files[id][1], "noplay noblock", function(ch, err)
			if not IsValid(ch) then
				loops[id] = nil
				return
			end
			ch:EnableLooping(true)
			ch:SetVolume(0)
			ch:Play()
			loops[id] = ch
		end)
		return nil
	end

	function S.SetLoopVolume(id, v)
		local ch = loops[id]
		if ch and IsValid(ch) then ch:SetVolume(math.Clamp(v, 0, 1) * SK.Sheets.sounds[id].volume) end
	end

	function S.StopLoops()
		for id, ch in pairs(loops) do
			if ch and IsValid(ch) then ch:Stop() end
			loops[id] = nil
		end
	end
end
