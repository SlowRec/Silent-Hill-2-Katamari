-- What the converter wrote on this PC from the player's own games (docs/CONTRACT.md), and per game
-- whether its content is here, with the message to show when it isn't. Never substitutes anything.
SK.Content = SK.Content or {}
local C = SK.Content
local DIR = "data_static/silent_katamari/"

local function readJSON(name)
	local txt = file.Read(DIR .. name, "GAME")
	if not txt then return nil end
	return util.JSONToTable(txt)
end
C.ReadJSON = readJSON

function C.Load()
	C.status = readJSON("status.json")
	C.rules = readJSON("rules.json") or {}
	C.objects, C.byId = {}, {}
	local objs = readJSON("objects.json")
	for _, o in ipairs(objs and objs.objects or {}) do
		C.objects[#C.objects + 1] = o
		C.byId[o.id] = o
	end
	C.james = readJSON("james.json")
	C.font = readJSON("font.json")
	C.hud = readJSON("hud.json")
end

-- "ok", "missing", "unreadable" or "not_converted", and the reason the converter gave.
function C.GameState(gameId)
	if not C.status then return "not_converted" end
	local g = C.status.games and C.status.games[gameId]
	if not g then return "not_converted" end
	return g.state or "unreadable", g.reason
end

function C.Ok(gameId) return C.GameState(gameId) == "ok" end

-- Did the converter finish this extract row? (optional rows can fail while their game is still ok)
function C.Has(extractId)
	local r = C.status and C.status.rows and C.status.rows[extractId]
	return r ~= nil and r.ok == true
end

function C.Object(id) return C.byId[id] end

-- Messages to show, as { id = strings id, vars = {...} }, most important first.
function C.Notices()
	local out = {}
	if not C.status then
		out[1] = { id = "not_converted" }
		return out
	end
	for _, gameId in ipairs({ "ouak", "sh2" }) do
		local state, reason = C.GameState(gameId)
		if state == "missing" then
			out[#out + 1] = { id = SK.Sheets.games[gameId].missing_text }
		elseif state ~= "ok" then
			out[#out + 1] = { id = gameId .. "_unreadable", vars = { reason = reason or "?" } }
		end
	end
	return out
end

-- A rule: a value the converter read from the player's Katamari wins over the sheet's research value.
function SK.Rule(id)
	local row = SK.Sheets.rules[id]
	if row and (row.source == "game" or row.source == "research") and C.rules and C.rules[id] ~= nil then
		return C.rules[id]
	end
	return SK.Rules[id]
end

C.Load()
