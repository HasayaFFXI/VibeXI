// Per-job constants from LandSandBoat src/map/grades.cpp JobGrades and
// sql/skill_ranks.sql. These are fixed server tables, not per-mob data, so they
// can't come from the species/mob SQL dump that mob-data.js was built from.
window.FFXI = window.FFXI || {};
FFXI.data = FFXI.data || {};

FFXI.data.JOB_NAMES = ["", "WAR", "MNK", "WHM", "BLM", "RDM", "THF", "PLD", "DRK", "BST", "BRD", "RNG", "SAM", "NIN", "DRG", "SMN", "BLU", "COR", "PUP", "DNC", "SCH", "GEO", "RUN"];

// VIT rank (1=A ... 7=G) per job, "VIT" column of JobGrades. Same index order as
// JOB_NAMES (0 = no job).
FFXI.data.JOB_VIT_RANK = [0, 4, 1, 4, 6, 5, 4, 1, 3, 4, 4, 4, 3, 3, 3, 6, 5, 5, 4, 5, 5, 5, 5];

// AGI rank, the "AGI" column of the same table. Mob evasion is base-eva +
// floor(AGI/2) (battle_entity.cpp:1661), so a hit-rate estimate needs this too.
FFXI.data.JOB_AGI_RANK = [0, 3, 6, 5, 3, 5, 2, 7, 4, 6, 6, 1, 4, 2, 4, 4, 5, 2, 3, 2, 4, 4, 2];

// Evasion *skill* rank per job, the 'evasion' row of sql/skill_ranks.sql
// (id 29). This is a skill rank on a 1..11 scale, NOT the 1..7 stat grade above
// -- the two are different scales and must not be mixed.
FFXI.data.JOB_EVA_SKILL_RANK = [0, 7, 3, 10, 10, 9, 1, 7, 7, 7, 9, 10, 3, 2, 4, 10, 8, 9, 4, 3, 10, 9, 3];

// mobutils.cpp JobSkillRankToBaseEvaRank: take the better (lower) evasion skill
// rank of main and sub, then collapse the 1..11 skill scale onto the 1..5 rank
// the base-eva curve is defined for. A- no longer exists, which is why 1 and 2
// both land on A.
FFXI.data.evaSkillRankToBaseRank = function (mainRank, subRank) {
  const best = Math.min(mainRank || 99, (subRank === 0 || subRank === undefined) ? (mainRank || 99) : subRank);
  if (best <= 2) return 1;   // A, A+
  if (best <= 5) return 2;   // B+, B, B-
  if (best <= 8) return 3;   // C+, C, C-
  if (best === 9) return 4;  // D
  if (best === 10) return 5; // E
  return 3;                  // the server's own fallback
};

FFXI.data.rankToLetter = function (n) {
  return ['A', 'B', 'C', 'D', 'E', 'F', 'G'][n - 1] || 'C';
};

// Fills a <select> with the job abbreviations. Values are the abbreviation itself
// ("SAM"), not the numeric id, because the callers that need a job here are
// matching against ability requirements by name. Index 0 is the "no job" slot and
// becomes an explicit "none" option, which is what a subjob-less setup looks like.
FFXI.data.populateJobSelect = function (select, selectedAbbrev) {
  if (!select) return;
  select.innerHTML = '';
  FFXI.data.JOB_NAMES.forEach((abbrev, i) => {
    const opt = document.createElement('option');
    opt.value = abbrev;
    opt.textContent = i === 0 ? '— none —' : abbrev;
    // defaultSelected as well as selected, so a form reset or the browser's
    // form-state restore falls back here (same reason as populateWeaponSelect).
    if (abbrev === selectedAbbrev) { opt.selected = true; opt.defaultSelected = true; }
    select.appendChild(opt);
  });
};
