-- Meshes the converter read from the player's games (docs/CONTRACT.md "Mesh JSON", cm, Source axes),
-- built once into IMesh objects. Objects are drawn at their true size: cm -> Source units via katamath.
local M = {}
SK.Meshes = M
local K = SK.Katamath
local cache = {}

local function material(key, info)
	local mat = CreateMaterial("silent_katamari_" .. key, "VertexLitGeneric", {
		["$basetexture"] = "color/white", ["$model"] = 1, ["$halflambert"] = 1, ["$nocull"] = 1 })
	if info.tex then
		local png = Material(info.tex .. ".png", "smooth mips")
		if png and not png:IsError() then mat:SetTexture("$basetexture", png:GetTexture("$basetexture")) end
	end
	local c = info.color or { 1, 1, 1, 1 }
	mat:SetVector("$color2", Vector(c[1], c[2], c[3]))
	return mat
end

local function buildPart(part)
	local v, tris = part.v, {}
	for _, i in ipairs(part.i) do
		local b = i * 8
		tris[#tris + 1] = { pos = Vector(v[b + 1], v[b + 2], v[b + 3]), normal = Vector(v[b + 4], v[b + 5], v[b + 6]),
			u = v[b + 7], v = v[b + 8] }
	end
	local m = Mesh()
	m:BuildFromTriangles(tris)
	return m
end

-- Loads a mesh JSON relative to data_static/silent_katamari/; false when missing.
function M.Load(rel)
	if cache[rel] ~= nil then return cache[rel] end
	local d = SK.Content.ReadJSON(rel)
	if not d then cache[rel] = false return false end
	local out = { parts = {} }
	local mats = {}
	for name, info in pairs(d.materials or {}) do mats[name] = material(string.gsub(rel, "[^%w]", "_") .. "_" .. name, info) end
	for _, p in ipairs(d.parts or {}) do
		if #p.i >= 3 then out.parts[#out.parts + 1] = { mesh = buildPart(p), mat = mats[p.mat] } end
	end
	cache[rel] = out
	return out
end

-- Dim, flat, overcast light: Silent Hill's fog has no sun.
function M.Light(pos)
	local c = render.GetLightColor(pos)
	local a = 0.32 + (c.x + c.y + c.z) / 3 * 0.15
	render.SuppressEngineLighting(true)
	render.ResetModelLighting(a, a, a)
	render.SetModelLighting(BOX_TOP, a + 0.25, a + 0.25, a + 0.25)
	render.SetModelLighting(BOX_FRONT, a + 0.08, a + 0.08, a + 0.08)
end

local function drawParts(m, matrix)
	cam.PushModelMatrix(matrix)
	for _, p in ipairs(m.parts) do
		render.SetMaterial(p.mat)
		p.mesh:Draw()
	end
	cam.PopModelMatrix()
end

function M.DrawObject(ent)
	local o = SK.Content.Object(ent:GetObjId())
	if not o then return end
	local m = M.Load(o.mesh)
	if not m then return end
	local s = 1 / K.R("cm_per_unit")
	local mat = Matrix()
	mat:Translate(ent:GetPos())
	mat:Rotate(ent:GetAngles())
	mat:Scale(Vector(s, s, s))
	M.Light(ent:WorldSpaceCenter())
	drawParts(m, mat)
	render.SuppressEngineLighting(false)
end

-- A plain sphere for when Katamari's own core wasn't found (it imitates nothing).
local sphere
local function plainSphere()
	if sphere then return sphere end
	local tris, rings, segs = {}, 12, 18
	local function p(i, j)
		local th, ph = math.pi * i / rings, 2 * math.pi * j / segs
		local n = Vector(math.sin(th) * math.cos(ph), math.sin(th) * math.sin(ph), math.cos(th))
		return { pos = n, normal = n, u = j / segs, v = i / rings }
	end
	for i = 0, rings - 1 do
		for j = 0, segs - 1 do
			local a, b, c, d = p(i, j), p(i + 1, j), p(i + 1, j + 1), p(i, j + 1)
			tris[#tris + 1] = a; tris[#tris + 1] = b; tris[#tris + 1] = c
			tris[#tris + 1] = a; tris[#tris + 1] = c; tris[#tris + 1] = d
		end
	end
	local m = Mesh()
	m:BuildFromTriangles(tris)
	sphere = { parts = { { mesh = m, mat = material("plain_sphere", { color = { 0.45, 0.43, 0.4, 1 } }) } } }
	return sphere
end

function M.DrawCore(ball)
	local m = M.Load("core.json") or plainSphere()
	local r = ball:Radius() * 0.92
	local mat = Matrix()
	mat:Translate(ball:GetPos())
	mat:Rotate(ball:GetAngles())
	mat:Scale(Vector(r, r, r))
	M.Light(ball:GetPos())
	drawParts(m, mat)
	render.SuppressEngineLighting(false)
end

-- The ground plane: Silent Hill 2's own ground texture when converted, else plain dark ground.
local ground
function M.DrawGround(center, sizeUnits)
	if not ground then
		local tile = K.ToUnits(K.R("ground_tile_cm"))
		local h = sizeUnits / 2
		local reps = sizeUnits / tile
		local function v(x, y, u, w) return { pos = Vector(x, y, 0), normal = Vector(0, 0, 1), u = u, v = w } end
		local tris = { v(-h, -h, 0, 0), v(h, -h, reps, 0), v(h, h, reps, reps), v(-h, -h, 0, 0), v(h, h, reps, reps), v(-h, h, 0, reps) }
		local m = Mesh()
		m:BuildFromTriangles(tris)
		local tex = SK.Content.Has("sh2_ground_tex") and "silent_katamari/sh2/ground" or nil
		ground = { mesh = m, mat = material("ground", { tex = tex, color = tex and { 0.8, 0.8, 0.8, 1 } or { 0.22, 0.22, 0.21, 1 } }) }
	end
	local mat = Matrix()
	mat:Translate(center + Vector(0, 0, 0.05))
	M.Light(center + Vector(0, 0, 8))
	cam.PushModelMatrix(mat)
	render.SetMaterial(ground.mat)
	ground.mesh:Draw()
	cam.PopModelMatrix()
	render.SuppressEngineLighting(false)
end
