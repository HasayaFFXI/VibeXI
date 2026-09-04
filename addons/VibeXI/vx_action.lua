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
-- directly observable, instead of inferred from the order of chat lines.
--
-- Cross-checked field for field and width for width against the parser in
-- Ashita's shipped `actionparse` addon: same byte-5 start, same 32/6/4/4/32/32
-- header, same 3/2/12/5/5/17/10/31 result block, same two optional trailers.
-- That is an independent confirmation of the layout, not a source for it -- the
-- code below is still written from the XiPackets description, since Ashita's is
-- GPL-3.0 and this tree is not (see addon-dev/PLAN.md, Licensing).

local BR = require('vx_bitreader')

local M = {}

-- A target_count of 0 is a junk packet the server sends; Metrics drops these
-- too. The upper bounds are sanity rails, not protocol limits -- a corrupt
-- length field would otherwise spin the result loop for a very long time
-- inside the packet callback.
local MAX_TARGETS = 64
local MAX_RESULTS = 64

--- Parse an action packet. Returns nil for anything that does not decode
--- cleanly; the caller treats nil as "ignore this packet", never as an error.
function M.parse(data)
    if not data or #data < 10 then return nil end

    local r = BR.new(data)
    BR.seek(r, 5)

    local act = {}
    act.actor_id     = BR.read(r, 32)
    act.target_count = BR.read(r, 6)
    local _res_sum   = BR.read(r, 4)
    act.category     = BR.read(r, 4)
    act.param        = BR.read(r, 32)
    act.recast       = BR.read(r, 32)
    act.targets      = {}

    if act.target_count == 0 or act.target_count > MAX_TARGETS then return nil end

    for _ = 1, act.target_count do
        if BR.overrun(r) then return nil end

        local target = { id = BR.read(r, 32), results = {} }
        local count  = BR.read(r, 4)
        if count > MAX_RESULTS then return nil end

        for _ = 1, count do
            if BR.overrun(r) then return nil end

            local res = {}
            res.reaction  = BR.read(r, 3)
            res.kind      = BR.read(r, 2)
            res.animation = BR.read(r, 12)
            res.effect    = BR.read(r, 5)
            res.stagger   = BR.read(r, 5)
            res.value     = BR.read(r, 17)
            res.message   = BR.read(r, 10)
            local _unk    = BR.read(r, 31)

            if BR.read(r, 1) > 0 then
                res.has_proc     = true
                res.proc_kind    = BR.read(r, 6)
                res.proc_info    = BR.read(r, 4)
                res.proc_value   = BR.read(r, 17)
                res.proc_message = BR.read(r, 10)
            end

            if BR.read(r, 1) > 0 then
                res.has_react     = true
                res.react_kind    = BR.read(r, 6)
                res.react_info    = BR.read(r, 4)
                res.react_value   = BR.read(r, 14)
                res.react_message = BR.read(r, 10)
            end

            table.insert(target.results, res)
        end

        table.insert(act.targets, target)
    end

    return act
end

--- Parse the action-message packet 0x0029. Byte-aligned, so no bit tricks --
--- but the same reader works and keeps the offsets in one style.
function M.parse_message(data)
    if not data or #data < 24 then return nil end

    local r = BR.new(data)
    BR.seek(r, 4)

    return {
        actor        = BR.read(r, 32),
        target       = BR.read(r, 32),
        param1       = BR.read(r, 32),
        param2       = BR.read(r, 32),
        actor_index  = BR.read(r, 16),
        target_index = BR.read(r, 16),
        message      = BR.read(r, 16),
    }
end

return M
