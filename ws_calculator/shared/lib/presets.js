// Slot-based preset store + UI.
//
// App-agnostic by design: it discovers the fields to save by scanning the DOM for
// inputs/selects/textareas with ids, minus an exclude list, so a page that grows
// a new field can never silently leave it out of a preset. The only page-specific
// wiring is the `onApply` callback (previously hardcoded calls to the calculator's
// two render functions) and `applyFirst`, for fields whose change handler
// populates other fields.
FFXI.presets = (function () {
  const { escapeHtml, resolve } = FFXI.core;

  // config:
  //   storageKey    localStorage key
  //   slotCount     number of slots (default 10)
  //   excludeIds    array of element ids that are not part of a preset
  //   metricLabel   label for the optional per-slot recorded number
  //   exportName    download filename
  //   applyFirst    ids to restore first, with their change event fired, because
  //                 their handlers write into other fields (e.g. zone -> mob list)
  //   onApply       called after a preset is loaded into the form
  //   mount         { slots, status, name, slotPick, saveBtn, exportBtn,
  //                   importBtn, importFile, warning } elements or ids
  function create(config) {
    const slotCount = config.slotCount || 10;
    const storageKey = config.storageKey;
    const excludeIds = new Set(config.excludeIds || []);
    const applyFirst = config.applyFirst || [];
    const metricLabel = config.metricLabel || null;
    const onApply = config.onApply || function () {};
    const m = FFXI.core.resolveAll(config.mount || {});

    // The preset widget's own controls are never part of a preset.
    ['name', 'slotPick', 'importFile'].forEach(k => {
      if (m[k] && m[k].id) excludeIds.add(m[k].id);
    });

    let presetsMemory = {};
    let storageAvailable = true;

    // Slot currently loaded into the form, plus a fingerprint of the form taken
    // when it was loaded/saved. Anything differing from it is an unsaved change.
    let activeSlot = null;
    let activeSnapshot = null;

    const slotNumbers = Array.from({ length: slotCount }, (_, i) => i + 1);

    function fieldElements() {
      return Array.from(document.querySelectorAll('input[id], select[id], textarea[id]'))
        .filter(el => !excludeIds.has(el.id) && el.type !== 'file' && el.type !== 'button');
    }

    function updateStorageWarning() {
      if (m.warning) m.warning.style.display = storageAvailable ? 'none' : 'inline';
    }

    function loadRaw() {
      try {
        const raw = window.localStorage.getItem(storageKey);
        return migrate(raw ? JSON.parse(raw) : {});
      } catch (e) {
        storageAvailable = false;
        updateStorageWarning();
        return presetsMemory;
      }
    }

    // Presets saved before this was a shared component stored the recorded number
    // under the calculator-specific name `avgDamage`. Read it forward so existing
    // slots keep their badge.
    function migrate(presets) {
      Object.keys(presets).forEach(k => {
        const p = presets[k];
        if (p && p.metric == null && p.avgDamage != null) p.metric = p.avgDamage;
      });
      return presets;
    }

    function saveRaw(presets) {
      try {
        window.localStorage.setItem(storageKey, JSON.stringify(presets));
      } catch (e) {
        storageAvailable = false;
        presetsMemory = presets;
      }
      updateStorageWarning();
    }

    function collect() {
      const data = {};
      fieldElements().forEach(el => {
        data[el.id] = el.type === 'checkbox' ? el.checked : el.value;
      });
      return data;
    }

    function apply(data) {
      // applyFirst ids are restored with their change events so dependent fields
      // get rebuilt (picking a zone rebuilds the mob list, so the saved mob value
      // has somewhere to land). Restoring the rest afterwards lets the preset's
      // own values win over whatever those handlers filled in.
      applyFirst.forEach(id => {
        const el = document.getElementById(id);
        if (!el || !(id in data)) return;
        const val = String(data[id]);
        if (el.tagName === 'SELECT' && val !== '' && !Array.from(el.options).some(o => o.value === val)) return;
        el.value = data[id];
        el.dispatchEvent(new Event('change'));
      });

      fieldElements().forEach(el => {
        if (applyFirst.indexOf(el.id) !== -1) return;
        if (!(el.id in data)) return;
        if (el.type === 'checkbox') el.checked = !!data[el.id];
        else el.value = data[el.id];
      });

      onApply();
    }

    // Order-independent fingerprint, so comparisons don't depend on the order
    // collect() happened to walk the DOM in.
    function fingerprint(data) {
      return JSON.stringify(Object.keys(data).sort().map(k => [k, data[k]]));
    }

    function isDirty() {
      if (activeSlot === null || activeSnapshot === null) return false;
      return fingerprint(collect()) !== activeSnapshot;
    }

    function markActive(slot) {
      activeSlot = slot;
      activeSnapshot = fingerprint(collect());
      renderSlots();
    }

    function clearActive() {
      activeSlot = null;
      activeSnapshot = null;
      renderSlots();
    }

    function loadIntoForm(slot) {
      const p = loadRaw()[slot];
      if (!p) return;
      apply(p.data || {});
      markActive(slot);
      if (m.slotPick) m.slotPick.value = slot;
      if (m.name) m.name.value = p.name;
    }

    function saveToSlot(slot, name) {
      const presets = loadRaw();
      const existing = presets[slot];
      presets[slot] = {
        name,
        data: collect(),
        // Keep the recorded metric only when overwriting the same preset with the
        // same numbers; otherwise it would describe a build that no longer exists.
        metric: (existing && existing.name === name && String(slot) === String(activeSlot) && !isDirty())
          ? existing.metric : undefined,
        savedAt: Date.now(),
      };
      saveRaw(presets);
      markActive(slot);
      if (m.slotPick) m.slotPick.value = slot;
    }

    // Stamps a number (e.g. a simulated average) onto the loaded preset, but only
    // while the form still matches it -- otherwise the slot would advertise a
    // number for a build the preset doesn't hold.
    function recordMetric(value) {
      renderSlotStates();
      if (activeSlot === null || isDirty()) return;
      const presets = loadRaw();
      if (!presets[activeSlot]) return;
      presets[activeSlot].metric = Math.round(value);
      saveRaw(presets);
      renderSlots();
    }

    function renderStatus() {
      const el = m.status;
      if (!el) return;
      const none = '<span>No preset loaded — current values are unsaved.</span>';
      if (activeSlot === null) { el.innerHTML = none; return; }

      const p = loadRaw()[activeSlot];
      if (!p) {
        activeSlot = null;
        activeSnapshot = null;
        el.innerHTML = none;
        return;
      }
      if (isDirty()) {
        el.innerHTML = `<span class="dirty">● Unsaved changes</span> to <b>${escapeHtml(p.name)}</b> (slot ${activeSlot})` +
          `<button class="link-btn" data-preset-action="quicksave">Save to slot ${activeSlot}</button>` +
          `<button class="link-btn" data-preset-action="revert">Revert</button>`;
        el.querySelector('[data-preset-action="quicksave"]').addEventListener('click', () => saveToSlot(activeSlot, p.name));
        el.querySelector('[data-preset-action="revert"]').addEventListener('click', () => loadIntoForm(activeSlot));
      } else {
        el.innerHTML = `<span class="clean">● Loaded</span> <b>${escapeHtml(p.name)}</b> (slot ${activeSlot}) — no unsaved changes`;
      }
    }

    // Refreshes only the loaded/dirty affordances. Cheap enough to run on every
    // keystroke, and it leaves the rest of the slot DOM alone.
    function renderSlotStates() {
      const dirty = isDirty();
      if (m.slots) {
        m.slots.querySelectorAll('.preset-slot').forEach(slot => {
          const isActive = activeSlot !== null && slot.dataset.slot === String(activeSlot);
          slot.classList.toggle('active', isActive);
          slot.classList.toggle('dirty', isActive && dirty);
          const state = slot.querySelector('.slot-state');
          if (state) state.textContent = isActive ? (dirty ? '● UNSAVED CHANGES' : '● LOADED') : '';
        });
      }
      renderStatus();
    }

    function populateSlotPicker(presets) {
      if (!m.slotPick) return;
      m.slotPick.innerHTML = '';
      slotNumbers.forEach(i => {
        const opt = document.createElement('option');
        opt.value = i;
        const p = presets[i];
        opt.textContent = p ? `${i}. ${p.name} (overwrite)` : `${i}. — empty —`;
        m.slotPick.appendChild(opt);
      });
      // Default the save target to the loaded preset, so "Save" overwrites what
      // you are editing; only fall back to the first empty slot when nothing is
      // loaded.
      if (activeSlot !== null && presets[activeSlot]) {
        m.slotPick.value = activeSlot;
        return;
      }
      const firstEmpty = slotNumbers.find(i => !presets[i]);
      if (firstEmpty) m.slotPick.value = firstEmpty;
    }

    function renderSlots() {
      const presets = loadRaw();
      if (!m.slots) { renderStatus(); return; }
      m.slots.innerHTML = '';

      slotNumbers.forEach(i => {
        const p = presets[i];
        const slot = document.createElement('div');
        slot.className = 'preset-slot ' + (p ? 'occupied' : 'empty');
        slot.dataset.slot = i;
        slot.innerHTML = `
          <div class="slot-num">SLOT ${i}</div>
          <div class="slot-name">${p ? escapeHtml(p.name) : 'empty'}</div>
          ${p && p.metric != null && metricLabel ? `<div class="slot-avg">${metricLabel}: ${Math.round(p.metric).toLocaleString()}</div>` : ''}
          ${p ? '<div class="slot-state"></div>' : ''}
          ${p ? '<button class="slot-rename" title="Rename preset">✎</button>' : ''}
          ${p ? '<button class="slot-clone" title="Clone preset">⧉</button>' : ''}
          ${p ? '<button class="slot-del" title="Delete preset">✕</button>' : ''}
        `;

        if (p) {
          slot.addEventListener('click', (e) => {
            if (e.target.matches('.slot-del, .slot-clone, .slot-rename')) return;
            loadIntoForm(i);
          });

          slot.querySelector('.slot-del').addEventListener('click', (e) => {
            e.stopPropagation();
            const cur = loadRaw();
            delete cur[i];
            saveRaw(cur);
            if (String(activeSlot) === String(i)) {
              activeSlot = null;
              activeSnapshot = null;
            }
            renderSlots();
          });

          slot.querySelector('.slot-rename').addEventListener('click', (e) => {
            e.stopPropagation();
            const cur = loadRaw();
            if (!cur[i]) return;
            const newName = prompt('Rename preset:', cur[i].name);
            if (newName === null) return;
            const trimmed = newName.trim();
            if (!trimmed) return;
            cur[i].name = trimmed;
            saveRaw(cur);
            renderSlots();
          });

          slot.querySelector('.slot-clone').addEventListener('click', (e) => {
            e.stopPropagation();
            const cur = loadRaw();
            const emptySlot = slotNumbers.find(n => !cur[n]);
            if (!emptySlot) {
              alert('No empty slot available — delete or free one up before cloning.');
              return;
            }
            cur[emptySlot] = { name: `${p.name} (copy)`, data: { ...p.data }, metric: p.metric, savedAt: Date.now() };
            saveRaw(cur);
            renderSlots();
          });
        }
        m.slots.appendChild(slot);
      });

      populateSlotPicker(presets);
      renderSlotStates();
    }

    // --- wiring ---

    // Any edit to any field can flip the dirty state, so watch the whole
    // document. Bubble phase, not capture: handlers like the mob picker's write
    // into other fields, and this needs to see the form after they have run.
    ['input', 'change'].forEach(evt => {
      document.addEventListener(evt, (e) => {
        const el = e.target;
        if (!el || !el.id || excludeIds.has(el.id)) return;
        if (!(el instanceof HTMLInputElement || el instanceof HTMLSelectElement || el instanceof HTMLTextAreaElement)) return;
        renderSlotStates();
      });
    });

    if (m.saveBtn) {
      m.saveBtn.addEventListener('click', () => {
        const slot = m.slotPick ? m.slotPick.value : 1;
        const presets = loadRaw();
        let name = m.name ? m.name.value.trim() : '';
        if (!name) name = (presets[slot] && presets[slot].name) || `Preset ${slot}`;
        saveToSlot(slot, name);
      });
    }

    if (m.exportBtn) {
      m.exportBtn.addEventListener('click', () => {
        const blob = new Blob([JSON.stringify(loadRaw(), null, 2)], { type: 'application/json' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = config.exportName || 'presets.json';
        a.click();
        URL.revokeObjectURL(url);
      });
    }

    if (m.importBtn && m.importFile) {
      m.importBtn.addEventListener('click', () => m.importFile.click());
      m.importFile.addEventListener('change', (e) => {
        const file = e.target.files[0];
        if (!file) return;
        const reader = new FileReader();
        reader.onload = () => {
          try {
            const imported = JSON.parse(reader.result);
            const presets = loadRaw();
            Object.keys(imported).forEach(k => { presets[k] = imported[k]; });
            saveRaw(presets);
            // An import can replace the slot under the loaded preset, so the
            // snapshot no longer describes anything on disk.
            clearActive();
          } catch (err) {
            alert('Could not read that file as a presets export.');
          }
        };
        reader.readAsText(file);
        e.target.value = '';
      });
    }

    renderSlots();

    return {
      render: renderSlots,
      refreshStates: renderSlotStates,
      load: loadIntoForm,
      save: saveToSlot,
      recordMetric,
      isDirty,
      collect,
      apply,
      get activeSlot() { return activeSlot; },
    };
  }

  return { create };
})();
