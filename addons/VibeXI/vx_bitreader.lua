-- LSB-first bit reader over a packet string.
--
-- Written from the XiPackets description of server packet 0x0028, NOT copied
-- from Ashita's reader -- that file is GPL-3.0 and this project is not.
-- The algorithm is the obvious one and is not the
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
--
-- Dot-calls only.

local M = {}

-- string.byte is 1-indexed; `pos` is a 0-based byte offset, so index is pos+1.
-- POW[n + 1] is 2^n, up to a whole byte.
local POW = {1, 2, 4, 8, 16, 32, 64, 128, 256}

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
---
--- A byte at a time: each pass takes every bit the field still needs from the
--- current byte, so a 32-bit read is four or five passes.
function M.read(r, bits)
    local ret   = 0
    local scale = 1
    local data  = r.data
    local pos   = r.pos
    local bit   = r.bit
    while bits > 0 do
        local take = 8 - bit
        if take > bits then take = bits end
        local b = string.byte(data, pos + 1)
        if b then
            -- bits `bit` .. `bit + take - 1` of byte `pos`
            ret = ret + (math.floor(b / POW[bit + 1]) % POW[take + 1]) * scale
        end
        scale = scale * POW[take + 1]
        bits  = bits - take
        bit   = bit + take
        if bit == 8 then
            bit = 0
            pos = pos + 1
        end
    end
    r.pos = pos
    r.bit = bit
    return ret
end

--- Advance the cursor past `bits` bits without decoding them, for the fields
--- the parser has no use for. Leaves the cursor exactly where read() would.
function M.skip(r, bits)
    local total = r.bit + bits
    r.pos = r.pos + math.floor(total / 8)
    r.bit = total % 8
end

--- True once the cursor has run off the end. The action parser checks this
--- rather than trusting the packet's own target/result counts, which are the
--- fields a malformed packet gets wrong.
function M.overrun(r)
    return r.pos >= #r.data
end

return M
