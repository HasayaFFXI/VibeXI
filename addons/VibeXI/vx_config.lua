-- Settings, read from vibexi.ini beside this file.
--
-- Read ONCE, here, when the addon loads -- never on the packet path. Every
-- failure degrades to the defaults: no file, an unreadable file, a line that is
-- not `key=value`, a value that makes no sense. A settings file must never be
-- able to stop the addon loading, or stop it recording.
--
-- The reader is deliberately small. `key=value` lines, `;` or `#` comments on
-- lines of their own, blank lines. `[section]` headers are allowed and ignored:
-- with a handful of settings one flat namespace is enough, and it means a key
-- still works if someone files it under a different heading. Keys are not case
-- sensitive.
--
-- A VALUE IS THE REST OF ITS LINE, spaces and all, because one of them is a
-- folder and `C:\Users\Jo Smith\Logs` has to survive. That is also why a
-- comment cannot share a line with a path: `;` and `#` are both legal in a
-- Windows folder name.
--
-- Not Ashita's configuration manager, which reads .ini too: its file call is
-- `:Load`, and the manifest allows a method by name whatever it is called on --
-- so listing it would also list the plugin manager's `:Load`. Reading the text
-- ourselves costs one token, `:read`, and that one cannot run anything.

local M = {}

local FILE = 'vibexi.ini'

local ON  = { ['1'] = true, ['true']  = true, ['on']  = true, ['yes'] = true }
local OFF = { ['0'] = true, ['false'] = true, ['off'] = true, ['no']  = true }

--- The file's text, or nil when it is not there to read.
local function read_file()
    if not addon or not addon.path then return nil end

    local f = io.open(addon.path .. '\\' .. FILE, 'r')
    if not f then return nil end

    local text = f:read('*a')
    f:close()
    return text
end

--- `key=value` lines as a table of lower-case key -> value as written.
local function parse(text)
    local values = {}
    for line in string.gmatch(text, '[^\r\n]+') do
        -- A comment or section line has no leading `key=` and is skipped.
        local key, value = string.match(line, '^%s*([%w_]+)%s*=%s*(.-)%s*$')
        if key then
            -- "C:\Some Folder" and C:\Some Folder are the same setting.
            value = string.match(value, '^"(.*)"$') or value
            values[string.lower(key)] = value
        end
    end
    return values
end

--- An on/off setting. Only the value's first word counts, so `Horizon=0 ; off`
--- still reads as 0. A missing, empty or unrecognised value is the default, so
--- a typo fails toward the default rather than away from it.
local function flag(value, default)
    local word = value and string.match(string.lower(value), '^%w+')
    if ON[word]  then return true end
    if OFF[word] then return false end
    return default
end

--- A folder setting as an absolute backslash path, or nil when there is nothing
--- usable in it -- nil being "use the default", which is the caller's to know.
---
--- %NAME% is replaced with that environment variable, so the shipped default can
--- be written the way Windows documents it. A variable that is not set makes
--- the whole value unusable rather than quietly collapsing to a path somewhere
--- else. RELATIVE PATHS ARE REFUSED: they would resolve against whatever the
--- game's working directory happens to be, and the file would go missing.
local function folder(value)
    if not value or value == '' then return nil end

    local unset = false
    local path = string.gsub(value, '%%([%w_]+)%%', function(name)
        local v = os.getenv(name)
        if not v or v == '' then
            unset = true
            return ''
        end
        return v
    end)
    if unset then return nil end

    path = string.gsub(path, '/', '\\')
    if not string.match(path, '^%a:\\') and not string.match(path, '^\\\\') then
        return nil
    end

    -- No trailing separator, except on a bare drive root where it is the path.
    if #path > 3 then path = string.gsub(path, '\\+$', '') end
    return path
end

local text   = read_file()
local values = text and parse(text) or {}

--- Whether vibexi.ini was found and read. Reported in the probe line, because
--- "I set it to 0 and nothing changed" is otherwise indistinguishable from the
--- file having been looked for in the wrong place.
M.found = text ~= nil

--- Horizon=1 (the default). See E.WsAbilities in vx_enums.lua for what it does.
M.horizon = flag(values.horizon, true)

--- EventDir: the folder the event files go to, or nil for vx_emit's default.
M.event_dir = folder(values.eventdir)

return M
