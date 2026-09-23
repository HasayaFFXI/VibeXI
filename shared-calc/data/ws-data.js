// Weaponskill constants, transcribed from the server rather than hand-entered
// on a page, plus the two accuracy helpers a weaponskill page needs to turn
// stats into a hit rate.
//
// Each entry is the wsParams table its scripts/actions/weaponskills/*.lua builds,
// with the sql/weapon_skills.sql row's identifying columns alongside. Keys match
// the server's spelling (ftpMod, str_wsc, accVaries, atkVaries) so a row can be
// diffed against the Lua by eye.
window.FFXI = window.FFXI || {};
FFXI.data = FFXI.data || {};

FFXI.data.WS = {
  penta_thrust: {
    id: 116,
    name: 'Penta Thrust',
    weapon: 'polearm',            // weapon_skills.sql type 8
    skillLevel: 150,
    element: 0,                   // "Element: None"
    primarySC: 2,                 // COMPRESSION (Lv1 Dark) -> Shadow Gorget/Belt
    jobs: ['WAR', 'PLD', 'SAM', 'DRG'],
    numHits: 5,
    ftpMod: [1.0, 1.0, 1.0],
    wsc: { str: 0.2, dex: 0.2 },
    accVaries: [0, 30, 60],
    atkVaries: [0.875, 0.875, 0.875],
    critVaries: null,             // absent -> WS hits never crit naturally
    multiHitFTP: false,
    hybridWS: false,
    source: 'scripts/actions/weaponskills/penta_thrust.lua',
    // modules/era/lua/actions/weaponskills/polearm.lua:89 overrides the 3000 TP
    // accuracy anchor to 50. Both the base file and the override carry a TODO
    // saying the number is unverified.
    eraAccVaries: [0, 30, 50],
  },

  tachi_jinpu: {
    id: 118,
    name: 'Tachi: Jinpu',
    weapon: 'greatkatana',
    numHits: 2,
    // Non-Adoulin values, matching the live server -- see the note on
    // USE_ADOULIN_WEAPON_SKILL_CHANGES in the app CLAUDE.md files.
    ftpMod: [1.0, 1.0, 1.0],
    wsc: { str: 0.4 },
    accVaries: null,
    atkVaries: null,
    critVaries: null,
    multiHitFTP: false,
    hybridWS: true,
    source: 'scripts/actions/weaponskills/tachi_jinpu.lua',
  },
};

// GetAccFromSkill, battle_entity.cpp:1278. Weapon skill converts to accuracy
// 1:1 up to 200, then at a worsening rate -- which is why the last hundred
// points of polearm skill are worth less accuracy than the first hundred.
FFXI.data.accFromSkill = function (skill) {
  if (skill > 600) return Math.floor((skill - 600) * 0.9) + 540;
  if (skill > 400) return Math.floor((skill - 400) * 0.8) + 380;
  if (skill > 200) return Math.floor((skill - 200) * 0.9) + 200;
  return skill;
};

// accuracyAndEvasionToHitRate + the caps, physical_hit_rate.lua:149 and :51.
// Returns a *percentage*, because that is what the engine's hitRate parameter
// is in.
//
// opts: { acc, eva, aLvl, tLvl, levelCorrection, cap }
//
// The level term is a PC penalty only: a player below the target's level loses
// 4 accuracy per level, but gains nothing for being above it. `cap` is the
// weapon's hit-rate ceiling (95 two-handed, 99 one-handed main, from
// weapon-data.js), and the floor is always 20.
FFXI.data.hitRateFromAccEva = function (opts) {
  let acc = opts.acc;
  if (opts.levelCorrection && opts.aLvl < opts.tLvl) {
    acc += (opts.aLvl - opts.tLvl) * 4;
  }
  const raw = 75 + (acc - opts.eva) / 2;
  return Math.min(Math.max(raw, 20), opts.cap === undefined ? 95 : opts.cap);
};
