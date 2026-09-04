-- Event -> one line of ASCII JSON -> appended to a local file.
--
-- This is the entire bridge. It is one-way by construction: the addon writes a
-- file, the Python server reads it. Nothing downstream can send anything
-- back into the game no matter what happens to it.
--
-- WHERE THE FILE GOES, and why it is not %TEMP%:
--
--   %LOCALAPPDATA%\VibeXI\events\<Character>_<YYYY.MM.DD>.jsonl
--
--   * NOT %TEMP%. Tempting, but wrong: Storage Sense and Disk Cleanup delete
--     files there on their own schedule, and this file is not scratch -- it is
--     the app's persistence layer. Reloading the page replays it, and it is
--     what survives an FFXI crash. Losing it mid-session loses the parse.
--   * NOT %APPDATA% (Roaming). Roams with the profile on a domain; a file
--     written several times a second is the last thing you want syncing.
--   * NOT anywhere under OneDrive. The repo on this machine lives under
--     OneDrive, and putting a hot append-only file there would hand the sync
--     client a file changing continuously for hours. Actively bad.
--   * NOT the Ashita install dir. Metrics writes its CSVs there, but on this
--     machine that path is itself under AppData\Roaming, and it is their tree.
--
--   %LOCALAPPDATA% is the documented Windows location for per-user,
--   machine-local application data that should not roam. That is exactly this.
--
-- ASCII-ONLY OUTPUT is deliberate: ASCII bytes pass through the server's CP932
-- decode untouched, so `damage-meter.py` needs no encoding change at all.
--
-- NOTHING IN HERE MAY THROW. It runs inside the packet callback, on the game's
-- thread. Every failure is swallowed and counted; a broken emitter must degrade
-- to "no data" and never to a stuttering or crashing client.

local M = {}

M.path      = nil
M.stats     = { written = 0, failed = 0, opened = 0 }
local handle = nil
local open_failed_for = nil    -- path we already failed on; stop retrying it

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
local function esc(s)
    s = tostring(s)
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

local function num(n)
    if not n then return '0' end
    -- Integers only; the game deals in whole damage and whole seconds.
    return string.format('%d', math.floor(n))
end

local function boolean(b)
    if b then return 'true' end
    return 'false'
end

--- Encode one event. Field order is fixed so the file diffs and greps sanely.
--- Optional fields are omitted rather than written null, keeping lines short.
function M.encode(e)
    local p = {}
    p[#p + 1] = '{"t":'        .. num(e.t)
    p[#p + 1] = ',"seq":'      .. num(e.seq)
    p[#p + 1] = ',"use":'      .. num(e.use)
    p[#p + 1] = ',"kind":"'    .. esc(e.kind) .. '"'
    p[#p + 1] = ',"actor":"'   .. esc(e.actor) .. '"'
    p[#p + 1] = ',"actorKind":"' .. esc(e.actorKind) .. '"'
    p[#p + 1] = ',"action":"'  .. esc(e.action) .. '"'
    p[#p + 1] = ',"actionId":' .. num(e.actionId)
    p[#p + 1] = ',"target":"'  .. esc(e.target) .. '"'
    p[#p + 1] = ',"targetKind":"' .. esc(e.targetKind) .. '"'
    p[#p + 1] = ',"dmg":'      .. num(e.dmg)
    p[#p + 1] = ',"hit":'      .. boolean(e.hit)
    p[#p + 1] = ',"crit":'     .. boolean(e.crit)
    p[#p + 1] = ',"burst":'    .. boolean(e.burst)
    p[#p + 1] = ',"msg":'      .. num(e.msg)
    if e.owner then p[#p + 1] = ',"owner":"' .. esc(e.owner) .. '"' end
    if e.pet   then p[#p + 1] = ',"pet":"'   .. esc(e.pet)   .. '"' end
    p[#p + 1] = '}'
    return table.concat(p)
end

-- ---------------------------------------------------------------- file

--- Build the output directory. Returns nil when no usable root can be found, in
--- which case the addon simply never writes.
---
--- One call, not one per level: `ashita.fs.create_dir` is an alias for
--- `create_directory`, which Ashita documents as "Creates all missing folders
--- within the path" -- it is recursive, and it takes the same absolute
--- backslash paths Ashita's own addons hand it. The earlier two-step version
--- was working around a limitation that does not exist.
local function ensure_dir()
    local root = os.getenv('LOCALAPPDATA')
    if not root or root == '' then
        -- Fall back to Ashita's own install tree. Not preferred (see header)
        -- but better than not recording at all.
        root = AshitaCore:GetInstallPath()
    end
    if not root or root == '' then return nil end

    local dir = root .. '\\VibeXI\\events'
    if not ashita.fs.exists(dir) then ashita.fs.create_dir(dir) end
    if not ashita.fs.exists(dir) then return nil end
    return dir
end

--- Open (or reopen) the output file for the given character. Append mode, so a
--- reload or a zone never truncates the session.
function M.open(character)
    M.close()

    local dir = ensure_dir()
    if not dir then return false end

    local name = (character or 'Unknown') .. '_' .. os.date('%Y.%m.%d') .. '.jsonl'
    local path = dir .. '\\' .. name

    if open_failed_for == path then return false end

    local f = io.open(path, 'a')
    if not f then
        open_failed_for = path
        return false
    end

    handle = f
    M.path = path
    open_failed_for = nil
    M.stats.opened = M.stats.opened + 1
    return true
end

function M.close()
    if handle then
        pcall(function() handle:flush() end)
        pcall(function() handle:close() end)
    end
    handle = nil
end

--- Append one event. Never throws.
--- Flushes on every line so the server's tailer sees data immediately;
--- `Read-LogTail` already holds back a partial trailing line, so a flush landing
--- mid-line is safe on the reader's side.
function M.write(e)
    if not handle then
        M.stats.failed = M.stats.failed + 1
        return false
    end

    local ok = pcall(function()
        handle:write(M.encode(e) .. '\n')
        handle:flush()
    end)

    if ok then
        M.stats.written = M.stats.written + 1
    else
        M.stats.failed = M.stats.failed + 1
        -- A failed write usually means the handle died (disk full, file removed
        -- underneath us). Drop it so the next event tries a clean reopen.
        handle = nil
    end
    return ok
end

--- Append a pre-built line verbatim. Used for the one-off environment probe in
--- vibexi.lua; consumers skip any line whose "kind" is "meta".
function M.write_raw(line)
    if not handle then
        M.stats.failed = M.stats.failed + 1
        return false
    end
    local ok = pcall(function()
        handle:write(line .. '\n')
        handle:flush()
    end)
    if not ok then
        M.stats.failed = M.stats.failed + 1
        handle = nil
    end
    return ok
end

--- JSON-escape helper exposed for the probe line.
function M.escape(s)
    return esc(s)
end

function M.is_open()
    return handle ~= nil
end

return M
