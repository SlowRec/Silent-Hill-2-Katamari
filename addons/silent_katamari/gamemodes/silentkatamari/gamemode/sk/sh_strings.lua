-- Text the player reads, in English or Spanish (strings sheet). {name} marks a value filled in at runtime.
local function lang()
	local l = CLIENT and GetConVarString("gmod_language") or "en"
	return string.sub(l or "", 1, 2) == "es" and "es" or "en"
end

function SK.L(id, vars)
	local row = SK.Sheets.strings[id]
	if not row then return id end
	local s = row[lang()] or row.en
	if vars then
		s = string.gsub(s, "{(%w+)}", function(k)
			local v = vars[k]
			if v == nil then return "{" .. k .. "}" end
			return tostring(v)
		end)
	end
	return s
end

function SK.Lang() return lang() end
