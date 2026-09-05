/*
 * source.js -- one line of the addon's JSONL -> one damage event.
 *
 * DOM-free on purpose: everything here is callable from the console against a
 * pasted array of lines, which is how the contract gets validated.
 *
 *   DPS.source.create(ownerName)  -> stateful line-at-a-time reader
 *   DPS.source.parseAll(lines)    -> one-shot, returns { events, meta }
 *
 * THE ONE SOURCE IS THE ADDON. ../../addons/VibeXI/ reads the game's own action
 * packets (0x028) and appends one JSON object per line. There is no chat-log
 * reader any more and there is nothing to infer here: this file validates a
 * line, rescales its clock, and hands it on.
 *
 * What the packet states outright, and the chat log never could:
 *
 *   use          One action is one use of it however many targets it reached.
 *                The packet carries the whole target list, so `use` is minted
 *                in Lua off the real grouping -- it is not the old five-second
 *                AoE heuristic. stats.collapse() consumes it unchanged.
 *   actorKind    player / pet / mob / npc, straight off the entity's spawn
 *   targetKind   flags. This is what `roster` is now a thin wrapper over; there
 *                is no article heuristic and no fixed point over who-fights-whom.
 *   crit         A real flag rather than a turn of phrase.
 *   hit          A miss, a parry, a shadow and an evade are all distinct
 *                messages, so the accuracy denominator is exact.
 *   owner        A pet names its master.
 *   job          A party member's main and sub job, on a line of its own
 *                (kind:"job"). The chat log never said either.
 *
 * TIME IS SECONDS ON THE WIRE, MILLISECONDS IN HERE. The addon deliberately
 * does not link LuaSocket just to get a sub-second clock (addon-dev/PLAN.md,
 * Decision 1), so `t` arrives as os.time() -- whole seconds -- with `seq`
 * ordering events inside one second. Nothing in the UI resolves finer than a
 * second, but everything downstream is written in milliseconds, so `t` is
 * scaled once here and never again.
 */
(function (global) {
  'use strict';

  var DPS = global.DPS || (global.DPS = {});

  /* Actions that get their own row rather than being folded into a swing. */
  var ADDL_ACTION = 'Additional Effect';

  /* Kinds whose damage counts as the party's. Everything else is the enemy. */
  var OURS = { player: true, pet: true };

  // ------------------------------------------------------------------- roster

  /*
   * Ally vs monster for every name seen.
   *
   * This used to be the hardest thing in the app -- the chat log never said who
   * was a player, so it was derived from the article ("the Goblin Pathfinder"),
   * then propagated through a fixed point over who fought whom to catch
   * article-less NMs, and a single bad guess could put a boss in the party list.
   * All of that is gone. The addon reads the entity's spawn flags and states the
   * answer, so this is a lookup.
   *
   * The manual override survives, because a classification the user disagrees
   * with should still be theirs to fix -- but it should now essentially never be
   * needed.
   */
  function createRoster() {
    var api = {
      owner: null,
      manual: {},       // name -> 'ally' | 'mob', set from the UI
      kinds: {},        // name -> 'player' | 'pet' | 'mob' | 'npc' | 'other'
      jobs: {},         // name -> { main, mainId, mainLevel, sub, subId, subLevel }

      /*
       * First real answer wins. A target the addon could not resolve in the
       * entity table arrives as 'other' (named "Unknown"), and one of those must
       * never overwrite a name already classified from a good lookup.
       */
      note: function (name, kind) {
        if (!name || !kind) return;
        if (kind === 'other' && this.kinds[name]) return;
        if (!this.kinds[name] || this.kinds[name] === 'other') this.kinds[name] = kind;
      },

      kindOf: function (name) {
        return this.kinds[name] || 'other';
      },

      /*
       * A party member's jobs, from the addon's kind:"job" lines.
       *
       * LAST WRITE WINS, unlike `note` above. A job line is only written when
       * the job actually changed, so a second one for the same character is a
       * real change -- somebody swapped to their sub -- and the newest answer is
       * the true one. (`note` is the other way round because a spawn-flag
       * classification cannot change, so there the FIRST good answer is kept and
       * a later 'other' is a failed lookup rather than news.)
       */
      noteJob: function (name, rec) {
        if (!name || !rec) return;
        this.jobs[name] = rec;
      },

      jobOf: function (name) {
        return this.jobs[name] || null;
      },

      /*
       * "WAR/NIN", or "WAR" with no sub-job, or '' if the addon never reported
       * one. The empty string is the honest answer for a character whose job we
       * do not know -- a trust, a pet, or anyone the party table never listed --
       * and every caller renders it as a dash rather than inventing a job.
       */
      jobLabel: function (name) {
        var j = this.jobs[name];
        if (!j || !j.main || j.main === 'NON') return '';
        return j.sub && j.sub !== 'NON' ? j.main + '/' + j.sub : j.main;
      },

      /* The same pair with levels, for a tooltip: "WAR75 / NIN37". */
      jobTitle: function (name) {
        var j = this.jobs[name];
        if (!j || !j.main || j.main === 'NON') return '';
        var s = j.main + (j.mainLevel ? String(j.mainLevel) : '');
        if (j.sub && j.sub !== 'NON') s += ' / ' + j.sub + (j.subLevel ? String(j.subLevel) : '');
        return s;
      },

      /*
       * Anything not positively ours is treated as a monster, so an unresolved
       * entity is left out of the party's totals rather than silently added to
       * them. Under-counting a stranger beats crediting one.
       */
      isMob: function (name) {
        if (this.manual[name]) return this.manual[name] === 'mob';
        return !OURS[this.kinds[name]];
      },
      isAlly: function (name) {
        return !this.isMob(name);
      },
      setManual: function (name, kind) {
        if (kind) this.manual[name] = kind;
        else delete this.manual[name];
      }
    };
    return api;
  }

  // ------------------------------------------------------------------- reader

  function bool(v) { return v === true; }

  function num(v) {
    var n = +v;
    return isFinite(n) ? n : 0;
  }

  function str(v) {
    return typeof v === 'string' ? v : '';
  }

  /*
   * `ownerName` is the character the file belongs to, taken from its name
   * ("Hasaya_2026.07.30.jsonl"). It is only used to pin that character's colour
   * slot; nothing about parsing depends on it.
   */
  function create(ownerName) {
    var roster = createRoster();
    roster.owner = ownerName || null;

    var events = [];
    /*
     * Lines the addon wrote that are not events: its startup environment probe,
     * and one notice per message id it saw and did not recognise. Surfaced in
     * the UI's Diagnostics panel, because a silently dropped message id is
     * indistinguishable from a bug. Malformed lines land here too.
     */
    var meta = [];

    var state = {
      lineNo: 0,
      bad: 0            // lines that were not JSON at all
    };

    function feed(line) {
      state.lineNo++;
      if (!line) return null;

      var raw;
      try {
        raw = JSON.parse(line);
      } catch (err) {
        state.bad++;
        meta.push({ line: state.lineNo, bad: true, text: String(line).slice(0, 300) });
        return null;
      }

      if (!raw || typeof raw !== 'object') return null;

      if (raw.kind === 'meta') {
        meta.push({ line: state.lineNo, data: raw });
        return null;
      }

      /*
       * A job line is a fact about a CHARACTER, not an event: it carries no
       * damage, no target and no `use`, and letting one into `events` would put
       * a zero-damage row into every count in the app. It goes on the roster and
       * the reader moves on, exactly like the meta lines above.
       *
       * A party member who never swings gets one of these and nothing else,
       * which is deliberate -- the meter lists the party, not only the
       * characters who dealt damage.
       */
      if (raw.kind === 'job') {
        var who = str(raw.actor);
        if (who) {
          /*
           * A job line comes off a PARTY SLOT, so it is also positive evidence
           * that this name is one of ours -- and for a member who never acts it
           * is the ONLY such evidence, because they appear as the actor of no
           * event and the spawn-flag classification never gets a chance to run.
           * Without this the white mage reads as an unclassified stranger in
           * Diagnostics, which is exactly backwards.
           */
          roster.note(who, 'player');
          roster.noteJob(who, {
            main: str(raw.main) || 'NON',
            mainId: num(raw.mainId),
            mainLevel: num(raw.mainLvl),
            sub: str(raw.sub) || 'NON',
            subId: num(raw.subId),
            subLevel: num(raw.subLvl)
          });
        }
        return null;
      }

      var actor = str(raw.actor);
      var target = str(raw.target);
      if (!raw.kind || !actor) return null;

      roster.note(actor, str(raw.actorKind));
      roster.note(target, str(raw.targetKind));

      var e = {
        // Seconds -> milliseconds; see the header. `seq` is kept as-is: it
        // orders events inside one second and is not a clock.
        t: num(raw.t) * 1000,
        seq: num(raw.seq),
        use: raw.use == null ? null : num(raw.use),
        kind: str(raw.kind),
        actor: actor,
        actorKind: str(raw.actorKind),
        action: str(raw.action) || ADDL_ACTION,
        actionId: num(raw.actionId),
        target: target,
        targetKind: str(raw.targetKind),
        dmg: num(raw.dmg),
        hit: bool(raw.hit),
        crit: bool(raw.crit),
        burst: bool(raw.burst),
        msg: num(raw.msg),
        line: state.lineNo
      };
      // Only present on a pet's own rows, so they stay off every other event.
      if (raw.owner) e.owner = str(raw.owner);
      if (raw.pet) e.pet = str(raw.pet);

      events.push(e);
      return e;
    }

    return {
      feed: feed,
      events: events,
      meta: meta,
      roster: roster,
      state: state,
      /*
       * Drops the events and keeps the roster: which names are monsters does not
       * stop being true because the meter was cleared between pulls. The manual
       * overrides and the recorded jobs survive for the same reason -- and jobs
       * especially, because the addon only writes a job line when the job
       * CHANGES, so a cleared job map would stay empty until somebody swapped.
       */
      reset: function () {
        events.length = 0;
        meta.length = 0;
        state.lineNo = 0;
        state.bad = 0;
      }
    };
  }

  /* Convenience for console testing: read a whole array in one call. */
  function parseAll(lines, ownerName) {
    var s = create(ownerName);
    for (var i = 0; i < lines.length; i++) s.feed(lines[i]);
    return s;
  }

  /* "Hasaya_2026.07.30.jsonl" -> { owner: 'Hasaya', date: Date(2026-07-30) } */
  function parseFilename(name) {
    var out = { owner: null, date: null };
    if (!name) return out;
    var m = /^(.*?)_(\d{4})\.(\d{2})\.(\d{2})\.jsonl$/i.exec(name);
    if (m) {
      out.owner = m[1] || null;
      out.date = new Date(+m[2], +m[3] - 1, +m[4]);
    } else {
      var d = /(\d{4})[.\-_](\d{2})[.\-_](\d{2})/.exec(name);
      if (d) out.date = new Date(+d[1], +d[2] - 1, +d[3]);
      var o = /^([A-Za-z]+)_/.exec(name);
      if (o) out.owner = o[1];
    }
    return out;
  }

  DPS.source = {
    create: create,
    parseAll: parseAll,
    parseFilename: parseFilename,
    createRoster: createRoster,
    ADDL_ACTION: ADDL_ACTION,
    OURS: OURS
  };
})(window);
