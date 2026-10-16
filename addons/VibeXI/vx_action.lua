-- Server packet 0x0028 -> a plain Lua table.
--
-- Layout (bit widths), from XiPackets world/server/0x0028. Everything is
-- LSB-first and nothing is byte-aligned, which is why this needs a bit reader
-- rather than a struct cast.
--
--   byte 5:
--     actor_id       32
--     target_count    6
--     result_sum      4      (unused; the per-target count is authoritative)
--     category        4      -- cmd_no, the thing that says melee / WS / magic
--     param          32      -- cmd_arg: spell id, WS id, ability id, ...
--     recast         32
--   per target (target_count of them):
--     target_id      32
--     result_count    4
--     per result (result_count of them):
--       reaction      3      hit / miss / guard / parry / block / evade
--       kind          2
--       animation    12
--       effect        5
--       stagger       5
--       value        17      <- damage
--       message      10      <- the outcome; wins over `value` (see vx_enums)
--       unknown      31
--       has_proc      1  -> proc_kind 6, proc_info 4, proc_value 17, proc_msg 10
--       has_react     1  -> react_kind 6, react_info 4, react_value 14, react_msg 10
--
-- The nesting is the whole point: action -> targets -> results is what makes
-- multi-attack (several results on one target) and AoE (several targets)
-- directly observable.
--
-- ONLY WHAT vibexi.lua READS IS DECODED. Every other field is stepped over with
-- BR.skip, which costs nothing per bit: the actor id (M.actor_id reads it on
-- its own), recast, and per result the reaction, kind, effect and stagger,
-- plus each trailer's kind and info. Decoding one of them again is a matter of
-- turning its skip back into a read.
--
-- Cross-checked field for field and width for width against the parser in
-- Ashita's shipped `actionparse` addon: same byte-5 start, same 32/6/4/4/32/32
-- header, same 3/2/12/5/5/17/10/31 result block, same two optional trailers.
-- That is an independent confirmation of the layout, not a source for it -- the
-- code below is still written from the XiPackets description, since Ashita's is
-- GPL-3.0 and this tree is not.

local BR = require('vx_bitreader')

local M = {}

-- A target_count of 0 is a junk packet the server sends, and is dropped. The
-- upper bounds are sanity rails, not protocol limits -- a corrupt
-- length field would otherwise spin the result loop for a very long time
-- inside the packet callback.
local MAX_TARGETS = 64
local MAX_RESULTS = 64

--- The actor's id alone, without decoding anything else: enough to decide
--- whether the packet is worth decoding at all. nil for a packet too short for
--- parse() to accept. The id is byte-aligned -- bytes 5 to 8, little-endian --
--- so it is read straight off the string rather than through a reader.
function M.actor_id(data)
    if not data or #data < 10 then return nil end
    local b1, b2, b3, b4 = string.byte(data, 6, 9)
    return b1 + b2 * 0x100 + b3 * 0x10000 + b4 * 0x1000000
end

--- Does any result carry a reaction? True when a result's main message is a
--- key of `main`, or its react (spike) trailer's message is a key of `spike`.
---
--- A walk over the bits that decodes those two messages and steps over the
--- rest, so it builds no tables at all. It runs for every action packet whose
--- actor is not ours, and nearly all of them carry no reaction. The same
--- rails as parse(): a packet that runs off its end answers false.
function M.has_reaction(data, main, spike)
    if not data or #data < 10 then return false end

    local r = BR.new(data)
    BR.seek(r, 5)
    BR.skip(r, 32)                        -- actor_id
    local target_count = BR.read(r, 6)
    if target_count == 0 or target_count > MAX_TARGETS then return false end
    BR.skip(r, 72)                        -- result_sum 4, category 4, param 32, recast 32

    for _ = 1, target_count do
        if BR.overrun(r) then return false end
        BR.skip(r, 32)                    -- target_id
        local count = BR.read(r, 4)
        if count > MAX_RESULTS then return false end

        for _ = 1, count do
            if BR.overrun(r) then return false end
            BR.skip(r, 44)                -- reaction 3 .. value 17
            if main[BR.read(r, 10)] then return true end
            BR.skip(r, 31)                -- unknown
            if BR.read(r, 1) > 0 then
                BR.skip(r, 37)            -- proc trailer: kind 6, info 4, value 17, message 10
            end
            if BR.read(r, 1) > 0 then
                BR.skip(r, 24)            -- react kind 6, info 4, value 14
                if spike[BR.read(r, 10)] then return true end
            end
        end
    end
    return false
end

--- Parse an action packet. Returns nil for anything that does not decode
--- cleanly; the caller treats nil as "ignore this packet", never as an error.
function M.parse(data)
    if not data or #data < 10 then return nil end

    local r = BR.new(data)
    BR.seek(r, 5)

    BR.skip(r, 32)                        -- actor_id: see M.actor_id
    local target_count = BR.read(r, 6)
    BR.skip(r, 4)                         -- result_sum
    local category     = BR.read(r, 4)
    local param        = BR.read(r, 32)
    BR.skip(r, 32)                        -- recast

    if target_count == 0 or target_count > MAX_TARGETS then return nil end

    local targets = {}
    local act = { category = category, param = param, targets = targets }

    for t = 1, target_count do
        if BR.overrun(r) then return nil end

        local results = {}
        targets[t] = { id = BR.read(r, 32), results = results }
        local count = BR.read(r, 4)
        if count > MAX_RESULTS then return nil end

        for i = 1, count do
            if BR.overrun(r) then return nil end

            -- Read into locals first: the order of the reads IS the layout,
            -- and a table constructor does not promise to evaluate in order.
            BR.skip(r, 5)                 -- reaction 3, kind 2
            local animation = BR.read(r, 12)
            BR.skip(r, 10)                -- effect 5, stagger 5
            local value     = BR.read(r, 17)
            local message   = BR.read(r, 10)
            BR.skip(r, 31)                -- unknown

            local res = { animation = animation, value = value, message = message }

            if BR.read(r, 1) > 0 then
                res.has_proc     = true
                BR.skip(r, 10)            -- proc kind 6, info 4
                res.proc_value   = BR.read(r, 17)
                res.proc_message = BR.read(r, 10)
            end

            if BR.read(r, 1) > 0 then
                res.has_react     = true
                BR.skip(r, 10)            -- react kind 6, info 4
                res.react_value   = BR.read(r, 14)
                res.react_message = BR.read(r, 10)
            end

            results[i] = res
        end
    end

    return act
end

return M
