-- Constants read off the wire. No logic here.
--
-- Sourced from Metrics (ashita/_enums.lua) and the XiPackets documentation for
-- server packet 0x0028. Values are the game's, not ours.

local E = {}

-- Packet ids we subscribe to. Everything else is ignored in the dispatcher.
E.Packet = {
    ACTION   = 0x028,   -- every swing, weaponskill, spell, ability, TP move
    MESSAGE  = 0x029,   -- kills and deaths
    ZONE_IN  = 0x00A,
    ZONE_OUT = 0x00B,
    PARTY    = 0x0DD,
    ALLIANCE = 0x0C8,
}

-- action.category, the packet's cmd_no field.
E.Category = {
    MELEE        = 1,
    RANGED       = 2,
    WEAPONSKILL  = 3,   -- also carries some job abilities (Mug, Jump, ...)
    MAGIC        = 4,
    ITEM         = 5,
    ABILITY      = 6,
    MOB_TP_START = 7,
    MAGIC_START  = 8,
    ITEM_START   = 9,
    ABILITY_START= 10,
    MOB_TP       = 11,
    RANGED_START = 12,
    PET_ABILITY  = 13,
    DANCE        = 14,
}

-- Which categories we emit at all. The *_START ones announce an action that has
-- not resolved yet and carry no damage, so they are dropped rather than emitted
-- as zero-damage events.
E.EmitCategory = {
    [E.Category.MELEE]       = 'melee',
    [E.Category.RANGED]      = 'ranged',
    [E.Category.WEAPONSKILL] = 'ws',
    [E.Category.MAGIC]       = 'magic',
    [E.Category.ABILITY]     = 'ability',
    [E.Category.MOB_TP]      = 'mobtp',
    [E.Category.PET_ABILITY] = 'pet',
}

-- Entity spawn flags. This is what replaces the chat log's article heuristic.
--
-- IT IS A BITFIELD, not an enumeration -- confirmed against Ashita's own
-- plugins/sdk/ffxi/enums.h (EntitySpawnFlags) and against how Ashita's shipped
-- addons read it (chamcham and skeletonkey both use bit.band, never `==`).
-- Metrics' table lists composite *observed* values instead, which is why the
-- first cut of vx_entity.kind() compared with `==` and would have misclassified
-- any entity carrying a bit combination nobody happened to write down.
E.SpawnFlags = {
    PLAYER      = 0x0001,
    NPC         = 0x0002,
    PARTY       = 0x0004,
    ALLIANCE    = 0x0008,
    MONSTER     = 0x0010,
    OBJECT      = 0x0020,
    ELEVATOR    = 0x0040,
    AIRSHIP     = 0x0080,
    ALLY        = 0x0100,   -- pets, fellows and trusts all carry this
    LOCALPLAYER = 0x0200,
    FELLOW      = 0x0800,
    TRUST       = 0x1000,
}

-- The composites Metrics recorded, kept only as a decoding key for anyone
-- reading the probe line or a Metrics CSV. Nothing compares against these.
--
--    525 = 0x20D  PLAYER|PARTY|ALLIANCE|LOCALPLAYER   (ourselves)
--     13 = 0x00D  PLAYER|PARTY|ALLIANCE               (a party member)
--      9 = 0x009  PLAYER|ALLIANCE                     (an alliance member)
--     16 = 0x010  MONSTER
--    258 = 0x102  NPC|ALLY                            (a pet)
--   4366 = 0x110E NPC|PARTY|ALLIANCE|ALLY|TRUST       (a trust)

-- result.animation on a melee action: which hand / limb swung.
E.Animation = {
    MELEE_MAIN    = 0,
    MELEE_OFFHAND = 1,
    MELEE_KICK    = 2,
    MELEE_KICK2   = 3,
    DAKEN         = 4,
}

-- result.message. The outcome of a single hit.
E.Message = {
    HIT         = 1,
    MOBHEAL3    = 3,
    MOB_KILL    = 6,
    MISS        = 15,
    DEATH_FALL  = 20,
    SHADOWS     = 31,
    DODGE       = 32,
    COUNTER     = 33,
    SPIKE_DMG   = 44,
    CRIT        = 67,
    PARRY       = 70,
    NO_EFFECT   = 75,
    RESIST      = 85,
    DEATH       = 97,
    ENDRAIN     = 161,
    ENASPIR     = 162,
    MOBHEAL373  = 373,
    MISS_TP     = 188,
    ENSPELL     = 229,
    BURST       = 252,
    RESIST_2    = 284,
    RANGEHIT    = 352,
    RANGECRIT   = 353,
    RANGEMISS   = 354,
    COMP_RESIST = 655,
}

-- Messages that mean "this swing landed nothing", so they count toward the
-- attempt but never toward damage. From Metrics handlers/melee.lua:170-178 --
-- some of these arrive WITH a non-zero value field, which is why the message
-- has to win over the value.
E.NoDamage = {
    [E.Message.MISS]       = true,
    [E.Message.DODGE]      = true,
    [E.Message.SHADOWS]    = true,
    [E.Message.PARRY]      = true,
    [E.Message.NO_EFFECT]  = true,
    [E.Message.RESIST]     = true,
    [E.Message.RESIST_2]   = true,
    [E.Message.COMP_RESIST]= true,
    [E.Message.MISS_TP]    = true,
    [E.Message.RANGEMISS]  = true,
    [E.Message.MOBHEAL3]   = true,   -- damage that HEALS the target
    [E.Message.MOBHEAL373] = true,
}

E.Crit = {
    [E.Message.CRIT]      = true,
    [E.Message.RANGECRIT] = true,
}

-- Ability ids in the action packet are offset by 512 from the resource table.
--
-- Still the one number here that is inferred rather than read off a definition.
-- The Ashita tree does not ship abils.dat (it lives in the FFXI install), so the
-- table's own layout cannot be inspected from source. Two things support it:
-- Ashita's recast addon scans GetAbilityById(0..2048), i.e. the table is far
-- larger than the ~300 job abilities and therefore segmented; and blusets does
-- the exact analogous `GetSpellById(id + 512)` for the BLU spell list. The probe
-- line reports the same id looked up both with and without the offset, so one
-- session settles it -- see probe() in vibexi.lua.
E.ABILITY_ID_OFFSET = 512

return E
