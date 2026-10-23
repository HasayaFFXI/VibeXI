-- Event -> one line of ASCII JSON -> appended to a local file.
--
-- This is the addon's only output. It is one-way by construction: the addon
-- writes a file, and nothing that reads it can send anything back into the
-- game no matter what happens to it.
--
-- WHERE THE FILE GOES, and why it is not %TEMP%:
--
--   %LOCALAPPDATA%\VibeXI\events\<Character>-<YYYYMMDDHHMMSS>.jsonl
--
--   One file per load of the addon: the stamp is the local date and time the
--   file was named.
--
--   The folder is the EventDir setting in vibexi.ini, and what follows is why
--   its default is what it is -- and so what to weigh before pointing it
--   somewhere else. The file name is not a setting: readers find a character's
--   sessions by it.
--
--   * NOT %TEMP%. Tempting, but wrong: Storage Sense and Disk Cleanup delete
--     files there on their own schedule, and this file is not scratch -- it is
--     the session's record, and what survives an FFXI crash. Losing it
--     mid-session loses the parse.
--   * NOT %APPDATA% (Roaming). Roams with the profile on a domain; a file
--     written several times a second is the last thing you want syncing.
--   * NOT anywhere under OneDrive or another synced folder. A hot append-only
--     file there hands the sync client a file changing continuously for hours.
--   * NOT the Ashita install dir. It can itself sit under a roaming or synced
--     folder, and it is Ashita's tree, not ours.
--
--   %LOCALAPPDATA% is the documented Windows location for per-user,
--   machine-local application data that should not roam. That is exactly this.
--
-- ASCII-ONLY OUTPUT is deliberate: ASCII bytes read the same whatever a reader
-- decodes the file as, so nothing that reads it has an encoding to get wrong.
--
-- NOTHING IN HERE MAY THROW. It runs inside the packet callback, on the game's
-- thread. Every failure is swallowed and counted; a broken emitter must degrade
-- to "no data" and never to a stuttering or crashing client.

local Config = require('vx_config')

local M = {}

M.stats     = { written = 0, failed = 0, opened = 0 }
local handle = nil
local open_failed_for = nil    -- path we already failed on; stop retrying it
local session_path = nil       -- this load's file, named once; a reopen appends to it

-- ---------------------------------------------------------------- JSON

local ESCAPES = {
    ['"']    = '\\"',
    ['\\']   = '\\\\',
    ['\b']   = '\\b',
    ['\f']   = '\\f',
    ['\n']   = '\\n',
    ['\r']   = '\\r',
    ['\t']   = '\\t',
}

--- Escape a string to ASCII-safe JSON. Bytes >= 0x7F become \uXXXX rather than
--- being passed through, which is what keeps the file decodable as CP932, UTF-8
--- or plain ASCII identically. Entity names are ASCII in practice; this is a
--- guard, not a transcoder.
---
--- So the common case is a string with nothing to escape, and it goes back as
--- it came after one pattern search. The pattern is exactly the set of bytes the
--- loop below would change: control bytes, '"', '\', and 0x7F and up.
local NEEDS_ESCAPE = '[%z\1-\31"\\\127-\255]'

local function esc(s)
    s = tostring(s)
    if not string.find(s, NEEDS_ESCAPE) then return s end
    local out = {}
    for i = 1, #s do
        local c = string.sub(s, i, i)
        local b = string.byte(c)
        local e = ESCAPES[c]
        if e then
            out[#out + 1] = e
        elseif b < 0x20 or b >= 0x7F then
            out[#out + 1] = string.format('\\u%04X', b)
        else
            out[#out + 1] = c
        end
    end
    return table.concat(out)
end

--- Integers only; the game deals in whole damage and whole seconds, and every
--- number written here already is one -- bit-reader fields, counters, os.time.
--- So no conversion is done: `..` writes an integer-valued number exactly as
--- %d would (up to 1e14, far past any of them), without a format call.
local function num(n)
    return n or 0
end

--- A field from a fixed vocabulary of plain words -- a row kind, an entity
--- kind, a healing list: 'melee', 'player', 'magic'. None of them can need
--- escaping, so they skip esc's scan. A missing one reads 'nil', as esc would.
local function word(s)
    return s or 'nil'
end

local function boolean(b)
    if b then return 'true' end
    return 'false'
end

--- Encode one event. Field order is fixed so the file diffs and greps sanely.
--- Optional fields are omitted rather than written null, keeping lines short.
---
--- The optional tail is built first so the line itself is ONE concatenation,
--- which LuaJIT does into a single buffer.
local function optional_tail(e)
    local tail = '}'
    if e.pet   then tail = ',"pet":"'   .. esc(e.pet)   .. '"' .. tail end
    if e.owner then tail = ',"owner":"' .. esc(e.owner) .. '"' .. tail end
    return tail
end

function M.encode(e)
    return '{"t":'           .. num(e.t)
        .. ',"seq":'         .. num(e.seq)
        .. ',"use":'         .. num(e.use)
        .. ',"kind":"'       .. word(e.kind) .. '"'
        .. ',"actor":"'      .. esc(e.actor) .. '"'
        .. ',"actorKind":"'  .. word(e.actorKind) .. '"'
        .. ',"action":"'     .. esc(e.action) .. '"'
        .. ',"actionId":'    .. num(e.actionId)
        .. ',"target":"'     .. esc(e.target) .. '"'
        .. ',"targetKind":"' .. word(e.targetKind) .. '"'
        .. ',"dmg":'         .. num(e.dmg)
        .. ',"hit":'         .. boolean(e.hit)
        .. ',"crit":'        .. boolean(e.crit)
        .. ',"burst":'       .. boolean(e.burst)
        .. ',"msg":'         .. num(e.msg)
        .. optional_tail(e)
end

--- Encode one party-member job line.
---
--- NOT AN EVENT, and deliberately its own `kind` rather than a field on every
--- damage row. A job is a property of the character, not of the swing: repeating
--- it on every line would be the same eleven bytes a few times a second for the
--- life of a session, and it would still be wrong for the one case that matters
--- -- a character who changes job mid-session, whose earlier rows would then
--- disagree with their later ones. One line when it changes says it once and
--- says it in order.
---
--- The abbreviation AND the id both go out: the abbreviation is what a person
--- reads, and the id is what survives a name table this file does not have.
--- The sub-job trio is omitted entirely when there is no sub-job, which is the
--- same rule `owner` and `pet` follow above.
function M.encode_job(e)
    local p = {}
    p[#p + 1] = '{"kind":"job","t":' .. num(e.t)
    p[#p + 1] = ',"actor":"'  .. esc(e.actor) .. '"'
    p[#p + 1] = ',"main":"'   .. esc(e.main) .. '"'
    p[#p + 1] = ',"mainId":'  .. num(e.mainId)
    p[#p + 1] = ',"mainLvl":' .. num(e.mainLvl)
    if e.subId and e.subId > 0 then
        p[#p + 1] = ',"sub":"'   .. esc(e.sub) .. '"'
        p[#p + 1] = ',"subId":'  .. num(e.subId)
        p[#p + 1] = ',"subLvl":' .. num(e.subLvl)
    end
    p[#p + 1] = '}'
    return table.concat(p)
end

--- Append one job line. Never throws; see M.write.
function M.write_job(e)
    return M.write_raw(M.encode_job(e))
end

--- Encode one healing line: one target's result of one heal.
---
--- ITS OWN KIND AND ITS OWN AMOUNT FIELD, and that is the safety of it. A heal
--- carries `hp`, never `dmg` and never `hit`: those are the two fields that
--- are summed and counted as damage, so a heal line read by anything that
--- treats it as an event still adds nothing to a damage total.
---
--- `via` is which healing list matched -- 'magic', 'ability' or 'pet' -- so a
--- reader can total them separately (see E.healing).
--- `use` is one per ACTION, shared by every target: a Curaga is one cast.
function M.encode_heal(e)
    return '{"kind":"heal","t":' .. num(e.t)
        .. ',"seq":'         .. num(e.seq)
        .. ',"use":'         .. num(e.use)
        .. ',"via":"'        .. word(e.via) .. '"'
        .. ',"actor":"'      .. esc(e.actor) .. '"'
        .. ',"actorKind":"'  .. word(e.actorKind) .. '"'
        .. ',"action":"'     .. esc(e.action) .. '"'
        .. ',"actionId":'    .. num(e.actionId)
        .. ',"target":"'     .. esc(e.target) .. '"'
        .. ',"targetKind":"' .. word(e.targetKind) .. '"'
        .. ',"hp":'          .. num(e.hp)
        .. ',"msg":'         .. num(e.msg)
        .. optional_tail(e)
end

--- Append one healing line. Never throws; see M.write.
function M.write_heal(e)
    return M.write_raw(M.encode_heal(e))
end

-- ---------------------------------------------------------------- file

--- Make sure a folder is there, creating it if need be.
---
--- One call, not one per level: `ashita.fs.create_dir` is an alias for
--- `create_directory`, which Ashita documents as "Creates all missing folders
--- within the path" -- it is recursive, and it takes the same absolute
--- backslash paths Ashita's own addons hand it.
local function usable(dir)
    if not ashita.fs.exists(dir) then ashita.fs.create_dir(dir) end
    return ashita.fs.exists(dir)
end

--- Build the output directory. Returns nil when no usable root can be found, in
--- which case the addon simply never writes.
---
--- The folder named in vibexi.ini first. One that cannot be created -- a drive
--- that is not there, a share that is offline -- falls through to the default
--- rather than to nothing: a session written somewhere unexpected can be found,
--- one never written cannot.
local function ensure_dir()
    local dir = Config.event_dir
    if dir and usable(dir) then return dir end

    local root = os.getenv('LOCALAPPDATA')
    if not root or root == '' then
        -- Fall back to Ashita's own install tree. Not preferred (see header)
        -- but better than not recording at all.
        root = AshitaCore:GetInstallPath()
    end
    if not root or root == '' then return nil end

    dir = root .. '\\VibeXI\\events'
    if not usable(dir) then return nil end
    return dir
end

--- Open (or reopen) the output file for the given character. The file is named
--- on the first call after the addon loads and that name is kept until it
--- unloads: append mode, so a reopen after a failed write never truncates the
--- session, and never starts a second file for it either.
function M.open(character)
    M.close()

    -- This load's file already refused to open, and is not retried. Said
    -- before ensure_dir, which would otherwise touch the disk again on every
    -- action packet for the rest of the session just to reach this answer.
    if session_path and open_failed_for == session_path then return false end

    local dir = ensure_dir()
    if not dir then return false end

    session_path = session_path or
        (dir .. '\\' .. (character or 'Unknown') .. '-' .. os.date('%Y%m%d%H%M%S') .. '.jsonl')
    local path = session_path

    local f = io.open(path, 'a')
    if not f then
        open_failed_for = path
        return false
    end

    handle = f
    open_failed_for = nil
    M.stats.opened = M.stats.opened + 1
    return true
end

-- The file calls, as plain functions so pcall can take them directly and a
-- line allocates no closure.
--
-- EACH RETURNS WHAT THE FILE CALL RETURNED, because that is where an I/O
-- failure shows up. write, flush and close do not raise when the disk is full
-- or the file is gone: they return nil and a message. pcall only catches what
-- IS raised -- a closed handle, an encode error -- so a caller needs both
-- answers, and `succeeded` below asks for both. Nothing here raises on purpose.
local function put_event(h, e) return h:write(M.encode(e), '\n') end
local function put_line(h, line) return h:write(line, '\n') end
local function flush_handle(h) return h:flush() end
local function close_handle(h) return h:close() end

--- Did a pcall'd file call work? `ok` is pcall's own answer, `res` the call's.
local function succeeded(ok, res)
    return ok and res ~= nil and res ~= false
end

-- Lines written since the last flush.
local pending = false

--- Close the handle and forget it. Closing flushes first, so whatever the
--- current packet buffered still reaches the file if the handle can manage it.
function M.close()
    if handle then pcall(close_handle, handle) end
    handle = nil
    pending = false
end

--- Run one write against the handle. A failure usually means the handle died
--- (disk full, file removed underneath us): close it so the next event tries a
--- clean reopen.
local function append(fn, v)
    if not handle then
        M.stats.failed = M.stats.failed + 1
        return false
    end
    if succeeded(pcall(fn, handle, v)) then
        pending = true
        return true
    end
    M.stats.failed = M.stats.failed + 1
    M.close()
    return false
end

--- Append one event. Never throws.
--- Buffered until M.flush, which the packet callback calls once per packet.
function M.write(e)
    local ok = append(put_event, e)
    if ok then M.stats.written = M.stats.written + 1 end
    return ok
end

--- Append a pre-built line verbatim: the job and heal lines, which are encoded
--- by their own functions above.
function M.write_raw(line)
    return append(put_line, line)
end

--- Push everything written since the last flush to the file. Never throws.
---
--- ONCE PER PACKET. Every line of a packet is written in the same instant, so
--- anything tailing the file sees a packet's rows as soon as it is processed,
--- and an AoE that writes ten rows costs one flush to the OS. A tailer should
--- hold back a partial trailing line until its newline arrives.
function M.flush()
    if not pending then return end
    pending = false
    if not handle then return end
    if not succeeded(pcall(flush_handle, handle)) then
        M.stats.failed = M.stats.failed + 1
        M.close()
    end
end

function M.is_open()
    return handle ~= nil
end

return M
