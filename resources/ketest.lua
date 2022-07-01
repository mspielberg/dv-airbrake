local defaultMult = 0.1
local cylScale = 2.5
local volumes = {
    bp = 10,
    res = 100,--45,
    ctrl = 1,
    cyl = 10,
    u = 0.1,
    atmo = 1e99,
}

local function transfer(state, dest, src, mult, destLimit)
    if state[dest] > state[src] then return end
    if not destLimit then destLimit = state[src] end
    local rate = (state[src] - state[dest]) * (mult or defaultMult)
    local tot = state[dest] * volumes[dest] + state[src] * volumes[src]
    local newDest = math.min(destLimit or math.huge, state[dest] + rate / volumes[dest])
    state[dest] = newDest
    state[src] = (tot - newDest * volumes[dest]) / volumes[src]
end

local ResFillThresh = 0.001
local FillThresh = 0.01
local VentThresh = 0.001
local ResetThresh = 0.1

function update(state)
    transfer(state, "ctrl", "bp")
    if state.ctrl + ResFillThresh >= state.bp then
        transfer(state, "res", "bp")
    end
    local delta = state.ctrl - state.bp - state.cyl / cylScale
    --print("delta="..delta)
    if (delta > FillThresh) then
        transfer(state, "cyl", "res")
        transfer(state, "u", "bp")
    elseif (delta < VentThresh) then
        transfer(state, "atmo", "cyl")
    end
    --[[
    if (BP >= A - ResetThresh)
        vent U
    if (shiftB)
        vent R
    ]]
end

local function updateN(state, n, f)
    for i=1,n do
        if f then f() end
        update(state)
    end
end

local state = {
    atmo = 0,
    bp = 5,
    res = 0,
    ctrl = 0,
    cyl = 0,
    u = 0,
}
local function printState(state)
    print(string.format("BP=%f,RES=%f,CTRL=%f,CYL=%f", state.bp, state.res, state.ctrl, state.cyl))
end

while state.res < 4.99 do
    updateN(state, 50, function() state.bp = 5 end)
    printState(state)
end
print("\n\n*** charging complete ***\n")
state.bp = 4.5
while state.cyl < 1.24 do
    update(state)
    printState(state)
end
print("\n\n*** apply complete ***\n")
while state.cyl > 0.51 do
    updateN(state, 50, function() state.bp = 4.8 end)
    printState(state)
end
print("\n\n*** partial release complete ***\n")
while state.cyl > 0.01 do
    updateN(state, 50, function() state.bp = 5 end)
    printState(state)
end
print("\n\n*** full release complete ***\n")
while state.res < 4.99 do
    updateN(state, 50, function() state.bp = 5 end)
    printState(state)
end
print("\n\n*** recharge complete ***\n")
