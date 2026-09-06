// Per-job constants from LandSandBoat src/map/grades.cpp JobGrades[][4].
// These are fixed server tables, not per-mob data, so they can't come from the
// species/mob SQL dump that mob-data.js was built from.
window.FFXI = window.FFXI || {};
FFXI.data = FFXI.data || {};

FFXI.data.JOB_NAMES = ["", "WAR", "MNK", "WHM", "BLM", "RDM", "THF", "PLD", "DRK", "BST", "BRD", "RNG", "SAM", "NIN", "DRG", "SMN", "BLU", "COR", "PUP", "DNC", "SCH", "GEO", "RUN"];

// VIT rank (1=A ... 7=G) per job, "VIT" column of JobGrades. Same index order as
// JOB_NAMES (0 = no job).
FFXI.data.JOB_VIT_RANK = [0, 4, 1, 4, 6, 5, 4, 1, 3, 4, 4, 4, 3, 3, 3, 6, 5, 5, 4, 5, 5, 5, 5];

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
