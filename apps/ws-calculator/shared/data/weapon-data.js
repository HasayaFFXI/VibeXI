// Per-weapon-type pDIF cap and hit-rate cap. Previously lived as data-cap /
// data-hrcap attributes on the <option> elements, which meant only one page
// could ever have them.
window.FFXI = window.FFXI || {};
FFXI.data = FFXI.data || {};

FFXI.data.WEAPON_TYPES = [
  { id: 'handtohand',   label: 'Hand-to-Hand', pdifCap: 3.5,  hitRateCap: 99 },
  { id: 'dagger',       label: 'Dagger',       pdifCap: 3.25, hitRateCap: 99 },
  { id: 'sword',        label: 'Sword',        pdifCap: 3.25, hitRateCap: 99 },
  { id: 'greatsword',   label: 'Great Sword',  pdifCap: 3.75, hitRateCap: 95 },
  { id: 'axe',          label: 'Axe',          pdifCap: 3.25, hitRateCap: 99 },
  { id: 'greataxe',     label: 'Great Axe',    pdifCap: 3.75, hitRateCap: 95 },
  { id: 'scythe',       label: 'Scythe',       pdifCap: 4.0,  hitRateCap: 95 },
  { id: 'polearm',      label: 'Polearm',      pdifCap: 3.75, hitRateCap: 95 },
  { id: 'katana',       label: 'Katana',       pdifCap: 3.25, hitRateCap: 99 },
  { id: 'greatkatana',  label: 'Great Katana', pdifCap: 3.5,  hitRateCap: 95 },
  { id: 'club',         label: 'Club',         pdifCap: 3.25, hitRateCap: 99 },
  { id: 'staff',        label: 'Staff',        pdifCap: 3.75, hitRateCap: 95 },
  { id: 'archery',      label: 'Archery',      pdifCap: 3.25, hitRateCap: 95 },
  { id: 'marksmanship', label: 'Marksmanship', pdifCap: 3.5,  hitRateCap: 95 },
  { id: 'throwing',     label: 'Throwing',     pdifCap: 3.25, hitRateCap: 95 },
];

FFXI.data.weaponType = function (id) {
  return FFXI.data.WEAPON_TYPES.find(w => w.id === id) || null;
};

// Fills a <select> with the weapon list. Keeps the data-cap / data-hrcap
// attributes populated so existing readParams-style code keeps working.
FFXI.data.populateWeaponSelect = function (select, selectedId) {
  if (!select) return;
  select.innerHTML = '';
  FFXI.data.WEAPON_TYPES.forEach(w => {
    const opt = document.createElement('option');
    opt.value = w.id;
    opt.textContent = `${w.label} (${w.pdifCap})`;
    opt.dataset.cap = w.pdifCap;
    opt.dataset.hrcap = w.hitRateCap;
    // defaultSelected (the `selected` attribute), not just the property, so a
    // form reset or the browser's form-state restore falls back to this option.
    if (w.id === selectedId) { opt.selected = true; opt.defaultSelected = true; }
    select.appendChild(opt);
  });
};
