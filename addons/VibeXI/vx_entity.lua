-- Reads the client's own entity and party tables.
--
-- This is the module that makes the packet data useful: the packet carries
-- numeric ids, and everything that turns an id into "Hasaya, a party member" or
-- "Goblin Pathfinder, a monster" happens here. `spawn_flags` states outright
-- whether an entity is a player, a monster or a pet, so nothing is guessed.
--
-- Every call in here is a getter. Nothing in this file can write to game memory
-- or to the network -- see dev/ALLOWED_APIS.txt.

local E = require('vx_enums')

local M = {}

-- name -> true, for everyone currently in the party or alliance. ALL 18 SLOTS:
-- the client keeps one 18-member structure (partymember_t Members[18] in
-- Ashita's plugins/sdk/ffxi/party.h), so slots 0-5 are our own party and 6-17
-- are the other two parties of the alliance. Nothing below distinguishes them,
-- and nothing should -- "ours" means the whole alliance.
M.party = {}
-- The entity index of each of those members, from the same pass: the short
-- list M.pet_owner searches instead of the party table's eighteen slots.
M.member_index = {}
M.party_stamp = 0
M.party_dirty = true         -- read the table on the first action, not 3s in
local PARTY_TTL = 3          -- seconds

-- name -> { main, main_lvl, sub, sub_lvl }, for everyone we have ever seen a
-- job for. Kept for the whole session rather than rebuilt per refresh: see
-- M.note_job for why a member who left is not forgotten.
M.jobs = {}

local MAX_ENTITY_INDEX = 2303

-- The players' block of the entity table. Below it are the zone's own NPCs and
-- monsters, above it the dynamic slots -- pets, trusts, fellows. It sets the
-- ORDER of the scan below and nothing else: every slot is still scanned, so
-- were the layout ever different the cost would be speed, never an answer.
local PC_FIRST = 0x400
local PC_LAST  = 0x6FF

-- server id -> the index the linear scan last found it at.
--
-- The low-bits guess below only works for ids that encode their index, which
-- monsters and NPCs do and PLAYERS DO NOT: a player's server id is their
-- character id. Only the linear scan finds a player, at up to 2303 SDK calls,
-- and every swing by a party member and every action by a stranger in range
-- needs one. Cached, the scan runs once per entity per zone.
--
-- An entry is only a hint: it is used only after the slot is read back and
-- still holds that id, so a stale one (a zone, a despawn) costs one call and
-- falls through to the scan. Bounded by clearing outright, which only costs a
-- rescan per entity.
local index_cache = {}
local index_cache_size = 0
local INDEX_CACHE_MAX = 4096

-- Ids the scan failed to find, for the one wall-clock second they failed in.
--
-- A miss is a full scan, all 2303 slots, and an id that is not in the table
-- now -- despawned, or not spawned yet -- is usually asked about again within
-- the same packet or the next one. The price is that an entity which appears
-- partway through that second reads as absent until it ends. Emptied when the
-- second turns over, so it never holds more than one second's misses.
local miss_second = 0
local misses = {}

--- The entity table, or nil while the client has none to hand out.
local function entity_manager()
    local mm = AshitaCore:GetMemoryManager()
    if not mm then return nil end
    return mm:GetEntity()
end

local function scan(em, id, first, last)
    for i = first, last do
        if em:GetServerId(i) == id then return i end
    end
    return nil
end

--- Entity index for a server id, or 0.
---
--- An id that encodes its index carries it in the LOW 12 BITS --
--- 0x01000000 | zone << 12 | index, so 17195010 is 0x1066002, index 2 of zone
--- 102 -- and that guess is tried first. Twelve bits, not eleven: indices run
--- to 2303 (0x8FF), and `% 2048` would send every slot from 2048 up to the
--- scan. `% 4096` is the same as masking 0xFFF and avoids pulling in the bit
--- library.
---
--- Then the cache, then the second's misses, and only then the scan, players'
--- block first since players are what the guess cannot find.
local function index_of(em, id)
    -- 0 is every empty slot's id, so it names no one entity.
    if not id or id == 0 then return 0 end

    local guess = id % 4096
    if guess <= MAX_ENTITY_INDEX and em:GetServerId(guess) == id then return guess end

    local cached = index_cache[id]
    if cached and em:GetServerId(cached) == id then return cached end

    local now = os.time()
    if now ~= miss_second then
        miss_second = now
        misses = {}
    end
    if misses[id] then return 0 end

    local i = scan(em, id, PC_FIRST, PC_LAST)
           or scan(em, id, 1, PC_FIRST - 1)
           or scan(em, id, PC_LAST + 1, MAX_ENTITY_INDEX)
    if not i then
        misses[id] = true
        return 0
    end

    if not cached then
        if index_cache_size >= INDEX_CACHE_MAX then
            index_cache = {}
            index_cache_size = 0
        end
        index_cache_size = index_cache_size + 1
    end
    index_cache[id] = i
    return i
end

--- Entity record for an index. Returns nil when the slot is empty, which the
--- callers treat as "skip this action" rather than as an error.
---
--- Only what something reads: this runs for the actor and every target of
--- every action in range, and each field is an SDK call.
local function record_of(em, index)
    if not index or index <= 0 then return nil end

    local name = em:GetName(index)
    if not name or name == '' then return nil end

    return {
        name        = name,
        index       = index,
        spawn_flags = em:GetSpawnFlags(index),
    }
end

--- Entity record for a server id, or nil. The entity table is fetched once
--- here and handed to both halves of the lookup.
function M.by_id(id)
    local em = entity_manager()
    if not em then return nil end
    return record_of(em, index_of(em, id))
end

--- Is a single spawn-flag bit set?
--- Arithmetic rather than bit.band for the same reason vx_bitreader is: it keeps
--- the addon's external surface one API smaller (see ALLOWED_APIS.txt), and the
--- masks here are single bits, so a divide-and-mod is exactly a band.
local function has(flags, mask)
    return math.floor(flags / mask) % 2 == 1
end

--- 'player' | 'pet' | 'mob' | 'npc' | 'other'
---
--- ORDER MATTERS, because the bits overlap. Trusts and pets are both NPC|ALLY;
--- only the TRUST bit separates them, so it has to be tested first. Everything
--- ALLY that is not a trust (pets, adventuring fellows) is a pet.
function M.kind(ent)
    if not ent or not ent.spawn_flags then return 'other' end
    local f = ent.spawn_flags
    local SF = E.SpawnFlags

    if has(f, SF.TRUST)   then return 'player' end   -- trusts count as our damage
    if has(f, SF.ALLY)    then return 'pet' end
    if has(f, SF.MONSTER) then return 'mob' end
    if has(f, SF.PLAYER)  then return 'player' end
    if has(f, SF.NPC)     then return 'npc' end
    return 'other'
end

--- Record a member's jobs, and say whether that is news.
---
--- Returns the record when something CHANGED and nil when it did not, which is
--- what keeps the emitter quiet: the party table is read every three seconds
--- and nobody wants a line in the file every three seconds saying the same
--- warrior is still a warrior.
---
--- A MAIN JOB OF ZERO NEVER OVERWRITES A REAL ONE. The party table reports 0
--- for a member who is zoning, out of range, or mid-update, and taking that at
--- face value would blank a character's job every time they crossed a zone
--- line. The first real answer wins and only another real answer replaces it.
function M.note_job(name, main, main_lvl, sub, sub_lvl)
    if not name or name == '' then return nil end

    main     = main     or 0
    main_lvl = main_lvl or 0
    sub      = sub      or 0
    sub_lvl  = sub_lvl  or 0

    local had = M.jobs[name]
    if had and main == 0 then return nil end
    if had and had.main == main and had.main_lvl == main_lvl
           and had.sub == sub and had.sub_lvl == sub_lvl then
        return nil
    end

    local rec = { name = name, main = main, main_lvl = main_lvl,
                  sub = sub, sub_lvl = sub_lvl }
    M.jobs[name] = rec
    return rec
end

--- The server says the roster changed; re-read it on the next action packet.
---
--- Called from the 0x0C8 (alliance update) and 0x0DD (party member update)
--- handlers. It sets a flag rather than reading the party table there and then,
--- and that is deliberate: the client's party memory does not update in the same
--- breath as the packet that announced the change, so a read taken on arrival
--- can still hand back the OLD roster. Flag now, read on the next action.
---
--- WHAT THIS BUYS. Without it the only thing that ever refreshes the roster is
--- the 3-second TTL, and that TTL is only consulted when an ACTION packet
--- arrives. So for up to three seconds after an alliance forms, a party is
--- merged into one, or somebody joins mid-fight, the new members are absent
--- from M.party -- and classify() in vibexi.lua drops every action they take in
--- that window. On a pull that starts the moment the alliance is assembled,
--- that window is the opening of the fight.
function M.party_changed()
    M.party_dirty = true
end

--- Refresh the party/alliance name set. Cheap enough to call per action, but
--- TTL-gated because it walks 18 slots and touches the entity table for each.
---
--- Returns the members whose jobs changed on THIS pass, or nil. The party table
--- is the only place a job is readable, so reading it is already happening here
--- and the extra four getters per occupied slot ride along for free.
function M.refresh_party(now, force)
    if not force and not M.party_dirty
       and (now - M.party_stamp) < PARTY_TTL then return nil end
    M.party_dirty = false
    M.party_stamp = now

    local mm = AshitaCore:GetMemoryManager()
    if not mm then return nil end
    local pt = mm:GetParty()
    if not pt then return nil end

    local fresh = {}
    local indices = {}
    local changed = nil
    for slot = 0, 17 do
        if pt:GetMemberIsActive(slot) == 1 then
            local name = pt:GetMemberName(slot)
            if name and name ~= '' then
                fresh[name] = true
                local index = pt:GetMemberTargetIndex(slot)
                if index and index > 0 then indices[#indices + 1] = index end
                local rec = M.note_job(name,
                    pt:GetMemberMainJob(slot),  pt:GetMemberMainJobLevel(slot),
                    pt:GetMemberSubJob(slot),   pt:GetMemberSubJobLevel(slot))
                if rec then
                    changed = changed or {}
                    changed[#changed + 1] = rec
                end
            end
        end
    end
    M.party = fresh
    M.member_index = indices
    return changed
end

function M.in_party(name)
    if not name then return false end
    return M.party[name] == true
end

--- The owner entity of a pet, or nil: whichever party member's entity names
--- the pet as its own.
---
--- Searches M.member_index rather than the party table, so it costs one SDK
--- call per member actually there instead of up to three per slot of
--- eighteen -- and it runs for every pet in range, a stranger's included. The
--- full record is built only for the member that matches.
---
--- Those indices are as old as the last refresh, and in that time a member can
--- zone out and a stranger take the slot. A match therefore counts only when
--- the owner's name is in the party as well.
function M.pet_owner(pet)
    if not pet or not pet.index then return nil end

    local em = entity_manager()
    if not em then return nil end

    local indices = M.member_index
    for i = 1, #indices do
        local index = indices[i]
        if em:GetPetTargetIndex(index) == pet.index then
            local owner = record_of(em, index)
            if owner and M.party[owner.name] then return owner end
        end
    end
    return nil
end

--- Our own character's name, used to name the output file.
function M.me()
    local p = GetPlayerEntity()
    if not p or not p.Name or p.Name == '' then return nil end
    return p.Name
end

--- Are we mid-zone? Actions during a zone are stale/duplicated, so we skip them.
---
--- `GetIsZoning()` returns a NUMBER -- confirmed twice over: `uint32_t
--- GetIsZoning(void)` in plugins/sdk/Ashita.h, and `---@return number` on
--- IPlayer:GetIsZoning in Ashita's own LuaLS annotations. So `~= 0` is the
--- correct test and the number branch below is the live one.
---
--- The type dispatch stays anyway, because the failure mode it guards is silent
--- and total. In Lua `false ~= 0` is TRUE (values of different types are never
--- equal), so a bare `return x ~= 0` against a boolean-returning build would
--- report "always zoning" and the addon would record nothing at all, with no
--- error to explain it. Everything unrecognised fails open to not-zoning.
function M.is_zoning()
    local mm = AshitaCore:GetMemoryManager()
    if not mm then return false end
    local pl = mm:GetPlayer()
    if not pl then return false end

    local z = pl:GetIsZoning()
    if z == nil then return false end
    if type(z) == 'boolean' then return z end
    if type(z) == 'number' then return z ~= 0 end
    return false
end

return M
