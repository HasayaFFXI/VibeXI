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

-- result.message -- what the server says happened, and the ONLY thing that
-- decides whether result.value is damage.
--
-- ============================================================================
-- THESE ARE ALLOWLISTS. An id in neither table is not emitted at all.
--
-- The first cut of this was a denylist (`NoDamage`) built from the handful of
-- melee outcomes Metrics enumerates, which meant an unrecognised message
-- defaulted to "this is damage". It is not. `value` is a different quantity per
-- message, and on the very first live run a Protect and a Shell landed 40 and
-- 41 "damage" on the meter -- message 230 is "<target> gains the effect of
-- <status>" and its value field is the status, not a number of hit points.
--
-- Same argument Phase 0 made for the API manifest, applied to the wire: a
-- denylist only catches what someone thought of, and here the failure mode of a
-- miss is silently wrong totals. Unknown ids now drop, so the failure mode is
-- missing data instead -- and record() writes one meta line naming each unknown
-- id it saw, so growing these tables is a deliberate edit informed by evidence.
--
-- Ids and their English text come from the server's own enum -- `MsgBasic` in
-- src/map/enums/msg_basic.h of the LandSandBoat-derivative checkout named in
-- the repo README. That is authoritative and it names every id, where Metrics'
-- Ashita.Enum.Message lists only the ~40 it needed. Two of the names we had
-- inherited from it are wrong against that enum: 3 is StartsCastingSelf, not a
-- heal, and 373 is SpikesEffectRecover.
-- ============================================================================

-- Tier 1 -- result.value IS hit points of damage dealt BY this packet's actor.
E.Damage = {
    [1]   = true,   -- AttackHits
    [2]   = true,   -- MagicDamage
    [67]  = true,   -- AttackCrit
    [77]  = true,   -- UsesSangeTakesDamage
    [110] = true,   -- UsesAbilityTakesDamage
    [157] = true,   -- UsesBarrageTakesDamage
    [161] = true,   -- AddEffectHPDrained          (*)
    [163] = true,   -- AddEffectDamage             (*)
    [185] = true,   -- UsesSkillTakesDamage        (weaponskills)
    [187] = true,   -- UsesSkillHPDrained
    [197] = true,   -- UsesAbilityResistsDamage    (partial resist still deals it)
    [227] = true,   -- MagicDrainsHP
    [229] = true,   -- AddEffectAdditionalDamage   (*)
    [252] = true,   -- MagicBurstDamage
    [264] = true,   -- TargetTakesDamage
    [274] = true,   -- MagicBurstDrainsHP
    [281] = true,   -- TargetHPDrained
    [317] = true,   -- UsesJobAbilityTakeDamage
    [352] = true,   -- RangedAttackHit
    [353] = true,   -- RangedAttackCrit
    [576] = true,   -- RangedAttackSquarely
    [577] = true,   -- RangedAttackPummels
}

-- (*) NOT REACHED YET, and correct anyway -- keep them.
--
-- A result block has THREE message slots, not one. The main `message`, and two
-- optional trailers: `has_proc` -> proc_message, which is the ADDITIONAL EFFECT,
-- and `has_react` -> react_message, which is the SPIKE effect. Metrics'
-- Build_Action renames them exactly that way (add_effect_message = proc_message,
-- spike_effect_message = react_message, ashita/packets.lua:52-66), and its melee
-- handler tests add_effect_message separately from message.
--
-- The AddEffect* ids only ever appear in proc_message. vx_action decodes that
-- trailer, but record() reads `res.message` alone, so an add-effect never emits
-- today -- and the chat parser DOES count it, as kind 'addl' (parser.js RE.addl).
-- So the packet source currently undercounts a Sneak Attack proc or an enspell
-- against the log for the same fight, which is precisely the Phase 1 "done when".
--
-- Wiring the trailer is a change to record(), not to this table: these ids are
-- already the right answer for the field they belong to. Left listed so the work
-- is one edit in one place rather than a second round of research.

-- Tier 2 -- the actor acted and dealt nothing. Emitted with dmg 0 and
-- hit=false, because these are the DENOMINATOR of accuracy: drop them and
-- everybody reads as never missing. Several arrive with a non-zero `value`,
-- which is exactly why the message has to win over the value.
E.Attempt = {
    [14]  = true,   -- CounterAbsByShadow
    [15]  = true,   -- AttackMisses
    [30]  = true,   -- TargetAnticipates
    [31]  = true,   -- ShadowAbsorb
    [32]  = true,   -- TargetDodges
    [33]  = true,   -- AttackCounteredDamage   (see note below)
    [70]  = true,   -- TargetParries
    [75]  = true,   -- MagicNoEffect
    [85]  = true,   -- MagicResisted
    [158] = true,   -- AbilityMisses
    [188] = true,   -- UsesSkillMisses
    [189] = true,   -- UsesSkillNoEffect
    [282] = true,   -- TargetEvades
    [283] = true,   -- TargetNoEffect
    [284] = true,   -- MagicResistedTarget
    [323] = true,   -- UsesAbilityNoEffect
    [324] = true,   -- UsesButMisses
    [354] = true,   -- RangedAttackMiss
    [355] = true,   -- RangedAttackNoEffect
    [373] = true,   -- SpikesEffectRecover -- the hit HEALED the target
    [655] = true,   -- MagicCompleteResist
}
-- 373 is the one id where this file and Metrics could have diverged, so it is
-- worth the note. Metrics' No_Damage_Messages tests it against the MAIN message
-- (handlers/melee.lua:170-177) while LSB names it SpikesEffectRecover, which
-- says spike trailer. Whichever is right, an absorbed hit is a swing that dealt
-- nothing, so Tier 2 is the answer both ways: if it never arrives here the entry
-- is inert, and if it does the swing lands in the accuracy denominator exactly
-- as Metrics counts it. damage_meter/CLAUDE.md already says absorbs read as
-- misses, so this also keeps the two sources telling the same story.

-- Deliberately in NEITHER table, so they drop. Recorded here so the next person
-- to see one in the unknown-message meta lines does not "fix" it by accident.
--
--   REACTION DAMAGE -- the damage is real but it belongs to the OTHER entity.
--     33  AttackCounteredDamage   the counter is the target's; for the actor
--                                 this swing landed nothing, so 33 is a Tier 2
--                                 attempt and the counter damage is not
--                                 recorded at all yet
--     44  SpikesEffectDmg         the target's spikes hit the actor
--     536 RetaliateDamage         the target retaliated
--     535 RetaliateShadowAbsorbs / 592 PerfectCounterMiss
--   Emitting any of these as-is credits the victim with their attacker's
--   damage. Attribution has to be inverted first, which is a Phase 4 change --
--   see PLAN.md, "metrics that were previously impossible".
--
--   MP, NOT HP -- 162 AddEffectMPDrained, 225 UsesSkillMPDrained,
--   366 TargetMPDrained. Metrics keeps MP drain out of the damage total too.
--
--   HEALS, BUFFS, ENFEEBLES, STATUS -- 7, 24, 102, 103 (recovers HP), 230/266
--   (gains the effect of), 236/237/267 (receives the effect of), 373
--   (SpikesEffectRecover). This is a damage meter; damage_meter/CLAUDE.md says
--   so under "Known gaps". Every event carries its raw `msg`, so a later phase
--   can add healing without changing the wire format.

E.Crit = {
    [67]  = true,   -- AttackCrit
    [353] = true,   -- RangedAttackCrit
}

E.Message = {
    BURST = 252,    -- MagicBurstDamage
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
