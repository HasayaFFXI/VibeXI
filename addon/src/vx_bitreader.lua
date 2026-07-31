-- LSB-first bit reader over a packet string.
--
-- Written from the XiPackets description of server packet 0x0028, NOT copied
-- from Ashita's reader -- that file is GPL-3.0 and this project is not (see
-- addon/PLAN.md, Licensing). The algorithm is the obvious one and is not the
-- part anyone owns; the point is that no GPL text is vendored here.
--
-- Two deliberate differences from the reference implementation:
--
--   1. NON-DESTRUCTIVE. Ashita's reader shifts each byte right as it consumes
--      bits, so the buffer is destroyed by reading it. This one tracks an
--      absolute (byte, bit) cursor and leaves the string alone, which means a
--      packet can be re-read -- useful when debugging a field that decoded
--      wrong.
--
--   2. ARITHMETIC, NOT BITOPS. A 32-bit read through LuaJIT's `bit` library
--      comes back as a SIGNED int32, so any value with the top bit set (and
--      entity ids do reach there) would arrive negative. Accumulating into a
--      Lua number instead is exact to 2^53 and simply cannot produce that bug.
--      Reads here are a few hundred bits per packet; the cost is irrelevant.
--
-- Dot-calls only, per PLAN.md Decision 8.

local M = {}

-- string.byte is 1-indexed; `pos` is a 0-based byte offset, so index is pos+1.
local POW = {1, 2, 4, 8, 16, 32, 64, 128}

--- Create a reader over a packet data string.
function M.new(data)
    return { data = data, pos = 0, bit = 0 }
end

--- Move to an absolute byte offset, resetting the bit cursor.
function M.seek(r, byte_pos)
    r.pos = byte_pos
    r.bit = 0
end

--- Read `bits` bits, least-significant first, advancing the cursor.
--- Reads past the end of the buffer yield zero bits rather than erroring: a
--- truncated packet should degrade to junk values that the dispatcher rejects,
--- never throw inside the packet callback.
function M.read(r, bits)
    local ret   = 0
    local scale = 1
    local data  = r.data
    for _ = 1, bits do
        local b = string.byte(data, r.pos + 1)
        if b then
            -- bit `r.bit` of byte `r.pos`
            local v = math.floor(b / POW[r.bit + 1]) % 2
            ret = ret + v * scale
        end
        scale = scale * 2
        r.bit = r.bit + 1
        if r.bit == 8 then
            r.bit = 0
            r.pos = r.pos + 1
        end
    end
    return ret
end

--- True once the cursor has run off the end. The action parser checks this
--- rather than trusting the packet's own target/result counts, which are the
--- fields a malformed packet gets wrong.
function M.overrun(r)
    return r.pos >= #r.data
end

return M
