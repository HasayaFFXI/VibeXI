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

-- (*) THESE ARRIVE IN THE PROC TRAILER, not in the main message.
--
-- A result block has THREE message slots, not one. The main `message`, and two
-- optional trailers: `has_proc` -> proc_message, which is the ADDITIONAL EFFECT
-- (or a SKILLCHAIN -- see E.skillchain below), and `has_react` -> react_message,
-- which is the SPIKE effect. Metrics' Build_Action renames them exactly that way
-- (add_effect_message = proc_message, spike_effect_message = react_message,
-- ashita/packets.lua:52-66), and its melee handler tests add_effect_message
-- separately from message.
--
-- record() reads the trailer separately from the main message and emits an
-- additional effect as its own `kind:'addl'` row with its own `use`, so an
-- enspell or a Sneak Attack proc is counted without being folded into the swing
-- it rode on. The ids are listed here because Tier 1 is the right answer for
-- that field too: proc_value is hit points, same as res.value.
--
-- The react (spike) trailer is read too, but NOT here: its damage belongs to the
-- entity being attacked rather than to the actor on the packet, so it goes
-- through E.SpikeReaction and record_reactions() with the two ends swapped. See
-- the REACTION DAMAGE block further down.

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
-- as Metrics counts it. apps/zerg/RULES.md already says absorbs read as
-- misses, so this also keeps the two sources telling the same story.

-- Deliberately in NEITHER table, so they drop. Recorded here so the next person
-- to see one in the unknown-message meta lines does not "fix" it by accident.
--
--   REACTION DAMAGE moved out of this list -- see E.Reaction below. 33, 44 and
--   536 are now emitted with the two ends swapped rather than dropped. What is
--   still unwired is the reaction ATTEMPTS: 535 RetaliateShadowAbsorbs, 592
--   PerfectCounterMiss and 14 CounterAbsByShadow. Each is a reaction that dealt
--   nothing, and each would be the honest denominator for a Counter or
--   Retaliation accuracy -- but which side of the packet a given one belongs to
--   has not been measured against a live client, and a guess here inflates a
--   party member's swing count with reactions that were never theirs. So the
--   damage is counted and the accuracy on those rows reads 100%, which is a
--   known overstatement and better than a fabricated one.
--
--   MP, NOT HP -- 162 AddEffectMPDrained, 225 UsesSkillMPDrained,
--   366 TargetMPDrained. Metrics keeps MP drain out of the damage total too.
--
--   BUFFS, ENFEEBLES, STATUS -- 230/266 (gains the effect of), 236/237/267
--   (receives the effect of), 373 (SpikesEffectRecover).
--
--   HEALS -- 7, 24, 102, 103 (recovers HP) -- stay out of BOTH tables, but they
--   are no longer dropped. Healing is recognised by ACTION ID, not by message,
--   and written as its own kind:"heal" line; see the HEALING block below. A
--   heal reaching these tables would be summed as damage.

E.Crit = {
    [67]  = true,   -- AttackCrit
    [353] = true,   -- RangedAttackCrit
}

E.Message = {
    BURST     = 252,    -- MagicBurstDamage
    WS_DAMAGE = 185,    -- UsesSkillTakesDamage
    WS_MISS   = 188,    -- UsesSkillMisses
}

-- ============================================================================
-- JOB ABILITIES ON THE WEAPONSKILL CATEGORY -- consulted only with Horizon=1
-- in vibexi.ini.
--
-- The server sends these twelve abilities as category 3, and their ids are
-- weaponskill ids too: 168 is Blade Bash and Hexa Strike, 66 is Jump and Gale
-- Axe. The weaponskill table is all but dense from 1 to 255, so looking a
-- category 3 id up there first names every one of them after the weaponskill
-- it collides with. The right-hand column is what each was being written as.
--
-- The ids are the rows with actionType 3 in sql/abilities.sql of the server
-- checkout named in the repo README, cut to the ones a 75-era server has. Eight
-- of them (26, 41, 45, 46, 66, 67, 77, 168) have also been seen arriving this
-- way in real event files; 57, 68, 150 and 170 rest on the SQL alone.
--
-- THE ID ALONE DECIDES NOTHING -- a Dragoon's Jump and a Beastmaster's Gale Axe
-- are the same packet up to the result. What separates them is the message: a
-- weaponskill reports 185 or 188, and these report something else (110 for the
-- bashes, 317 and 324 for the Jumps, 125/153 for Steal). That is Metrics' test
-- (isWeaponskillAbility, handlers/tp_action.lua), over a longer list: Metrics'
-- own Res.WS.Abilities has no 150, 168 or 170 and so calls Blade Bash a Hexa
-- Strike too.
--
-- The message has to be the action's FIRST result. An AoE weaponskill reports
-- 185 on its main target and 264 on the others, so asking each result in turn
-- would call Spinning Axe a weaponskill on one mob and a Super Jump on the next.
--
-- KNOWN MISREAD: a real weaponskill with one of these ids whose first result is
-- neither 185 nor 188 -- absorbed by shadows (31), no effect (189) -- is taken
-- for the ability. The packet carries nothing that could tell them apart.
E.WsAbilities = {
    [26]  = true,   -- Eagle Eye Shot    Mercy Stroke
    [41]  = true,   -- Steal             Swift Blade
    [45]  = true,   -- Mug               Atonement
    [46]  = true,   -- Shield Bash       Expiacion
    [57]  = true,   -- Shadowbind        Scourge
    [66]  = true,   -- Jump              Gale Axe
    [67]  = true,   -- High Jump         Avalanche Axe
    [68]  = true,   -- Super Jump        Spinning Axe
    [77]  = true,   -- Weapon Bash       Ruinator
    [150] = true,   -- Tomahawk          Tachi: Yukikaze
    [168] = true,   -- Blade Bash        Hexa Strike
    [170] = true,   -- Angon             Randgrith
}

--- Is this category 3 action a job ability rather than a weaponskill?
--- `message` is the message of the action's first result.
function E.ws_ability(param, message)
    if not param or not message then return false end
    if not E.WsAbilities[param] then return false end
    return message ~= E.Message.WS_DAMAGE and message ~= E.Message.WS_MISS
end

-- ============================================================================
-- REACTION DAMAGE -- damage the DEFENDER dealt, riding on the ATTACKER's packet.
--
-- Counter, Retaliation and Spikes share one shape: the packet's actor is the
-- one swinging, and the damage in the result belongs to the entity being swung
-- AT. Read straight, every one of them credits a victim with their attacker's
-- damage, which is why they are in neither E.Damage nor E.Attempt. record()
-- never touches them; record_reactions() emits them with the two ends swapped:
--
--     actor  = the RESULT'S TARGET (whoever reacted)
--     target = the PACKET'S ACTOR  (whoever swung into it)
--
-- That rule is symmetric, so neither side needs a special case. A monster
-- swinging into our spikes inverts to us damaging the monster, and is counted.
-- Us swinging into a MONSTER's spikes inverts to the monster damaging us, and
-- is dropped by the same is_ours test every other bit of monster damage is.
--
-- 33 IS IN E.Attempt AS WELL, and that is not a contradiction: the two readings
-- never apply to the same packet. On a packet WE are the actor of, 33 means our
-- swing was countered and dealt nothing -- a Tier 2 attempt, and the counter
-- damage in res.value is the monster's. On a packet we are not the actor of, 33
-- is the counter OUR side landed. Same id, opposite ends, decided by who swung.
--
-- TWO SLOTS, TWO TABLES. Counter and Retaliation replace the swing's own
-- outcome and arrive in the MAIN message with the damage in res.value. Spikes
-- fire in addition to the swing, so they arrive in the REACT (spike) trailer
-- with the damage in res.react_value -- the trailer Metrics calls
-- spike_effect_message. Keeping them apart is what stops a main-slot 44 from
-- being read as spike damage on the strength of the number alone.
E.Reaction = {
    [33]  = 'Counter',
    [536] = 'Retaliation',
}

E.SpikeReaction = {
    [44] = 'Spikes',
}

-- ============================================================================
-- SKILLCHAINS
--
-- A skillchain is not a packet of its own and not a message of its own. It
-- rides on the closing weaponskill's result, in the PROC trailer:
--
--     res.proc_message   the skillchain id
--     res.proc_value     the skillchain's own damage, separate from the
--                        weaponskill's own res.value
--
-- METRICS IS THE SOURCE OF TRUTH FOR THIS TABLE. What follows is
-- `Res.WS.Skillchains` from resources/weapon_skills_curated.lua, copied
-- verbatim, and E.skillchain() is its `Res.WS.Get_Skillchain` -- a plain
-- lookup, nothing more.
--
-- DO NOT DERIVE THESE IDS ARITHMETICALLY. An earlier cut of this file computed
-- them as `287 + effect` / `384 + effect` from the LandSandBoat server's
-- `action_result_t::recordSkillchain`. That formula disagrees with Metrics in
-- two places -- it puts Radiance and Umbra at 302/303 rather than 767/768, and
-- it reads the 385/386 pair as "absorbed" rather than as Light/Darkness -- and
-- Metrics is the parser with a track record against this server. If the two
-- ever have to be reconciled, that is a measurement against a live client, not
-- a reading of either source.
--
-- THE TABLE IS ONLY CONSULTED ON A WEAPONSKILL (category 3). That is the single
-- context Metrics consults it in: `H.TP.Skillchain_Parse` is called from
-- `H.TP.Action` and from nowhere else (handlers/tp_action.lua:41). Monster TP
-- moves and pet abilities never reach it, and there is a comment at
-- tp_action.lua:151 saying BST pet abilities cannot skillchain here anyway.
--
-- That context is also what resolves 229, which is in two tables at once. On a
-- weaponskill Metrics calls it 'DRG Jump Effect'; everywhere else it reads the
-- same trailer as `Message.ENSPELL` (ashita/_enums.lua) and files it as an
-- additional effect. So the id means one thing on a weaponskill and another on
-- a melee swing, and record() reproduces both -- see the proc-trailer branch
-- there. This is why the lookup is gated on `kind` rather than being global.
E.Skillchains = {
    [229] = 'DRG Jump Effect',
    [288] = 'Light',       [289] = 'Darkness',
    [290] = 'Gravitation', [291] = 'Fragmentation', [292] = 'Distortion', [293] = 'Fusion',
    [294] = 'Compression', [295] = 'Liquefaction',  [296] = 'Induration', [297] = 'Reverberation',
    [298] = 'Transfixion', [299] = 'Scission',      [300] = 'Detonation', [301] = 'Impaction',
    [385] = 'Light',       [386] = 'Darkness',
    [767] = 'Radiance',    [768] = 'Umbra',
}

--- The skillchain's English name, or nil when this message is not one.
--- Metrics' Res.WS.Get_Skillchain, and deliberately nothing more than a lookup.
function E.skillchain(message)
    if not message then return nil end
    return E.Skillchains[message]
end

-- ============================================================================
-- HEALING -- recognised by ACTION ID, never by message.
--
-- THE RULE IS METRICS', AND ONLY METRICS'. Nothing here is a judgement of ours:
-- every id below is copied verbatim from Metrics' resource tables, and record()
-- does with them exactly what Metrics' handlers do. A heal Metrics does not
-- count is not counted here.
--
-- How Metrics decides (handlers/spells.lua, handlers/abilities.lua):
--
--   * By the action's id against a curated list -- `Res.Spells.Get_Healing`,
--     `Res.Abilities.Get_Player_Healing` / `Get_Pet_Healing`,
--     `Res.Avatar.Get_Healing`, `Res.Pets.Get_Healing_Wyvern_Breath`. The
--     result's message is not consulted at all.
--   * The amount is the result's value (Metrics' `result.param`), for every
--     result on every target the client can resolve. A cure on a full-HP
--     target is a 0 -- still a cast, and what overcure is measured from.
--   * Healing never enters the damage total
--     (`DB.Catalog.Include_Total_Damage` excludes every healing trackable).
--     So a healing id is written as kind:"heal" and NEVER as a damage row, even
--     where the message would otherwise read as damage.
--
-- WHICH CATEGORY EACH LIST IS CONSULTED ON matters as much as the ids, because
-- the id spaces overlap (649 is Repair as an ability and Sand Breath as a pet
-- ability). Each list is looked up only on the category Metrics routes it from:
--
--   category 4  (magic)        E.HealingSpells      key = act.param
--   category 6  (ability)      E.HealingAbilities   key = act.param + 512
--   category 13 (pet ability)  E.PetHealing         key = act.param
--
-- NOT consulted on category 14. Metrics' dispatcher does nothing there
-- ("Unblinkable Job Ability; Waltz", metrics.lua) -- so a waltz that arrives on
-- 14 is not counted by Metrics and is not counted here, even though the Curing
-- Waltz ids are on the ability list for category 6.
--
-- Metrics' Divine Seal clamp (`DB.Healing_Max`) limits only a spell's recorded
-- MAX, which is the reference overcure is measured against; the totals take the
-- raw value. So the raw value is what goes on the wire, as for damage.

-- Res.Spells.Healing, resources/spells_curated.lua.
E.HealingSpells = {
    [1]   = 'Cure',       [2]   = 'Cure II',     [3]  = 'Cure III',
    [4]   = 'Cure IV',    [5]   = 'Cure V',      [6]  = 'Cure VI',
    [7]   = 'Curaga',     [8]   = 'Curaga II',   [9]  = 'Curaga III',
    [10]  = 'Curaga IV',  [11]  = 'Curaga V',
    [549] = 'Pollen',     [578] = 'Wild Carrot', [581] = 'Healing Breeze',
    [593] = 'Magic Fruit', [645] = 'Exuviation', [658] = 'Plenilune Embrace',
    [690] = 'White Wind', [711] = 'Restoral',
}

-- Res.Abilities.Healing + Res.Abilities.Pet_Healing, resources/abilities.lua.
-- Keyed as Metrics keys them: the packet's param PLUS the 512 offset
-- (H.Enum.Offsets.ABILITY), which is E.ABILITY_ID_OFFSET below. The pet ones
-- (Reward, Spirit Link, Repair) are cast BY the player ON the pet, and Metrics
-- credits them to the player as ability healing.
E.HealingAbilities = {
    [541] = 'Spirit Surge',
    [550] = 'Chakra',
    [702] = 'Curing Waltz',   [703] = 'Curing Waltz II',
    [704] = 'Curing Waltz III', [705] = 'Curing Waltz IV',
    [707] = 'Divine Waltz',
    [590] = 'Reward',
    [592] = 'Spirit Link',
    [649] = 'Repair',
}

-- Res.Avatar.Healing (resources/avatars.lua) and Res.Pets.Healing_Wyvern_Breath
-- (resources/pets.lua). A PET's action, credited to its owner with the pet's
-- name kept -- record() already does that for every pet row.
E.PetHealing = {
    [869] = 'Whispering Wind',
    [906] = 'Healing Ruby',
    [911] = 'Healing Ruby II',
    [639] = 'Healing Breath IV',
    [640] = 'Healing Breath',
    [641] = 'Healing Breath II',
    [642] = 'Healing Breath III',
}

--- The heal's English name when this action is one Metrics counts as healing,
--- else nil. `via` is which list matched: 'magic', 'ability' or 'pet' -- the
--- browser needs it to split the totals the way Metrics' columns do (spell
--- HEALING + ABILITY_HEALING is its Healing column; PET_HEAL is separate).
function E.healing(category, param)
    if not param then return nil end
    if category == E.Category.MAGIC then
        local n = E.HealingSpells[param]
        if n then return n, 'magic' end
    elseif category == E.Category.ABILITY then
        local n = E.HealingAbilities[param + E.ABILITY_ID_OFFSET]
        if n then return n, 'ability' end
    elseif category == E.Category.PET_ABILITY then
        local n = E.PetHealing[param]
        if n then return n, 'pet' end
    end
    return nil
end

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

-- Job id -> the three-letter abbreviation the game itself uses.
--
-- The party table hands out job IDS, and an id is unreadable in a JSON file and
-- unusable as a colour key without a second table on the browser side. Resolving
-- it here costs 24 lines and makes the event file self-describing: "WAR/NIN" is
-- in the line, not reconstructed from two integers by whoever reads it.
--
-- Ids are the game's, matching Metrics' Res.Jobs.List. 20-23 cannot appear on a
-- 75-era server; they are listed so an unexpected id resolves to a name rather
-- than to nil.
E.Jobs = {
    [0]  = 'NON',
    [1]  = 'WAR',  [2]  = 'MNK',  [3]  = 'WHM',  [4]  = 'BLM',
    [5]  = 'RDM',  [6]  = 'THF',  [7]  = 'PLD',  [8]  = 'DRK',
    [9]  = 'BST',  [10] = 'BRD',  [11] = 'RNG',  [12] = 'SAM',
    [13] = 'NIN',  [14] = 'DRG',  [15] = 'SMN',  [16] = 'BLU',
    [17] = 'COR',  [18] = 'PUP',  [19] = 'DNC',  [20] = 'SCH',
    [21] = 'GEO',  [22] = 'RUN',  [23] = 'MON',
}

--- Job abbreviation for an id. Unknown ids degrade to 'NON' rather than to nil,
--- so nothing downstream has to test for a missing name.
function E.job(id)
    if not id then return 'NON' end
    return E.Jobs[id] or 'NON'
end

return E
