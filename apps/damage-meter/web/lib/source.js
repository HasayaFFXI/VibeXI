/*
 * source.js -- one line of the addon's JSONL -> one damage event.
 *
 * DOM-free on purpose: everything here is callable from the console against a
 * pasted array of lines, which is how the contract gets validated.
 *
 *   DPS.source.create(ownerName)  -> stateful line-at-a-time reader
 *   DPS.source.parseAll(lines)    -> one-shot, returns the reader, every line fed
 *   DPS.source.exportParse(reader, session, opts) / stringifyParse(doc)
 *                                 -> a paused parse, as an export document
 *   DPS.source.importParse(text)  -> { source, session, ... } read back from one
 *
 * THE ONE SOURCE IS THE ADDON. addons/VibeXI/ reads the game's own action
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

    var state = {
      lineNo: 0
    };

    function feed(line) {
      state.lineNo++;
      if (!line) return null;

      var raw;
      try {
        raw = JSON.parse(line);
      } catch (err) {
        return null;   // not JSON at all; skipped
      }
      return ingest(raw);
    }

    /*
     * One record that is already an object. An exported parse comes back in
     * through here (`importParse`), and it is the same door `feed` uses, so a
     * record read back from an export cannot be read differently from the line
     * it started out as.
     */
    function feedRecord(raw) {
      state.lineNo++;
      return ingest(raw);
    }

    function ingest(raw) {
      if (!raw || typeof raw !== 'object') return null;

      /*
       * The addon's own notices -- its startup environment probe, and one line
       * per message id it saw and did not recognise. Not events, and nothing
       * here reads them; they stay in the file for anyone searching it.
       */
      if (raw.kind === 'meta') return null;

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
           * Without this the white mage reads as an unclassified stranger,
           * which is exactly backwards.
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
      feedRecord: feedRecord,
      events: events,
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
        state.lineNo = 0;
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

  // ------------------------------------------------------------ export file

  /*
   * A PAUSED PARSE, SAVED FOR ANOTHER COPY OF THE METER TO OPEN.
   *
   * The file is the addon's own records plus the three things a reader cannot
   * recover from them: the session clock, the roster (jobs are written once, on
   * change, so most of them sit before the Start press and are not in the event
   * list at all), and the manual overrides. Events and job lines are stored in
   * the WIRE format -- seconds, `mainLvl` -- and read back through `feedRecord`,
   * which is the same `ingest` a live line goes through. So an imported parse
   * is not a second source that could disagree with the first; it is the first
   * source, replayed.
   *
   * `.json`, never `.jsonl`: the server follows the newest *.jsonl in the events
   * directory, and an export saved there must not be mistaken for today's file.
   */
  var PARSE_FORMAT = 'vibexi-parse';
  var PARSE_VERSION = 1;

  /* A normalised event -> the record the addon wrote. The inverse of `ingest`. */
  function toRecord(e) {
    var r = {
      t: e.t / 1000, seq: e.seq, use: e.use, kind: e.kind,
      actor: e.actor, actorKind: e.actorKind,
      action: e.action, actionId: e.actionId,
      target: e.target, targetKind: e.targetKind,
      dmg: e.dmg, hit: e.hit, crit: e.crit, burst: e.burst, msg: e.msg
    };
    // Same rule as the addon: only a pet's own rows carry these.
    if (e.owner) r.owner = e.owner;
    if (e.pet) r.pet = e.pet;
    return r;
  }

  /* A roster job -> the kind:"job" line it came from, sub trio omitted when there
     is no sub-job, exactly as `vx_entity.note_job` writes it. */
  function jobRecord(name, j) {
    var r = { kind: 'job', actor: name, main: j.main, mainId: j.mainId, mainLvl: j.mainLevel };
    if (j.sub && j.sub !== 'NON') { r.sub = j.sub; r.subId = j.subId; r.subLvl = j.subLevel; }
    return r;
  }

  function isMap(v) {
    return !!v && typeof v === 'object' && !Array.isArray(v);
  }

  function copyMap(m) {
    var out = {};
    for (var k in m) if (Object.prototype.hasOwnProperty.call(m, k)) out[k] = m[k];
    return out;
  }

  function finite(v) {
    return typeof v === 'number' && isFinite(v);
  }

  /*
   * reader + session -> the export document, as a plain object.
   *
   *   opts.keep(e)   which events go in; default all of them
   *   opts.file      the event file they were read from, kept for the record
   *
   * Everything is COPIED, so the document is a snapshot: a poll landing while a
   * save dialog is open cannot change what gets written.
   */
  function exportParse(reader, session, opts) {
    opts = opts || {};
    var roster = reader.roster;
    var keep = opts.keep || function () { return true; };
    return {
      format: PARSE_FORMAT,
      version: PARSE_VERSION,
      exported: new Date(opts.now == null ? Date.now() : opts.now).toISOString(),
      file: opts.file || null,
      owner: roster.owner,
      session: {
        armedAt: session.armedAt,
        startedAt: session.startedAt,
        spans: session.spans.map(function (s) { return { from: s.from, to: s.to }; }),
        pausedAt: session.pausedAt
      },
      kinds: copyMap(roster.kinds),
      manual: copyMap(roster.manual),
      jobs: Object.keys(roster.jobs).map(function (n) { return jobRecord(n, roster.jobs[n]); }),
      events: reader.events.filter(keep).map(toRecord)
    };
  }

  /*
   * The document -> text. The head is indented for reading; the events are one
   * per line, the way the addon's own file has them, so a long parse stays
   * something a person can scroll through and a diff can line up.
   */
  function stringifyParse(doc) {
    var head = {};
    for (var k in doc) if (k !== 'events') head[k] = doc[k];
    var events = doc.events || [];
    return JSON.stringify(head, null, 2).slice(0, -2) + ',\n  "events": [' +
      (events.length
        ? '\n    ' + events.map(function (e) { return JSON.stringify(e); }).join(',\n    ') + '\n  '
        : '') +
      ']\n}\n';
  }

  /*
   * A session read back from a file: shape-checked, and REQUIRED TO BE PAUSED.
   * Export is only offered while paused, so an unpaused session is not one this
   * meter wrote -- and it would be meaningless if it were: a running clock would
   * go on counting from the moment of import, over a file with nothing new in it.
   */
  function readSession(s) {
    if (!isMap(s)) return null;
    if (!finite(s.startedAt) || !finite(s.pausedAt) || s.pausedAt < s.startedAt) return null;
    var list = Array.isArray(s.spans) ? s.spans : [];
    var spans = [];
    for (var i = 0; i < list.length; i++) {
      var p = list[i];
      if (!p || !finite(p.from) || !finite(p.to) || p.to < p.from) return null;
      spans.push({ from: p.from, to: p.to });
    }
    return {
      armedAt: finite(s.armedAt) ? s.armedAt : s.startedAt,
      startedAt: s.startedAt,
      spans: spans,
      pausedAt: s.pausedAt
    };
  }

  /* An addon event file opened by mistake: its first line is a record on its own. */
  function looksLikeEventFile(text) {
    try {
      var o = JSON.parse(String(text).split('\n', 1)[0]);
      return isMap(o) && !!o.kind;
    } catch (e) { return false; }
  }

  var NOT_PARSE = 'That file is not a Damage Meter parse export.';
  var EVENT_FILE = 'That is an addon event file (.jsonl), not an exported parse. ' +
                   'Import opens a file made with Export.';

  /*
   * Text -> { source, session, skipped, file, exported }, or throws an Error
   * whose message is written for the user.
   *
   * `source` is an ordinary reader, the same object `create` returns, so nothing
   * downstream can tell an import from a live file. `skipped` counts event
   * records that did not survive `ingest`.
   */
  function importParse(text) {
    var doc;
    try {
      doc = JSON.parse(text);
    } catch (err) {
      throw new Error(looksLikeEventFile(text) ? EVENT_FILE : NOT_PARSE);
    }
    if (!isMap(doc) || doc.format !== PARSE_FORMAT) {
      throw new Error(isMap(doc) && doc.kind ? EVENT_FILE : NOT_PARSE);
    }
    if (+doc.version > PARSE_VERSION) {
      throw new Error('That parse was exported by a newer version of the meter. Update this copy to open it.');
    }
    var session = readSession(doc.session);
    if (!session) throw new Error('That parse has no complete session, so there is no clock to measure it on.');
    if (!Array.isArray(doc.events)) throw new Error('That parse has no event list.');

    var r = create(str(doc.owner) || null);
    var k;
    // Classifications first, so the events' own kinds meet the same
    // first-answer-wins rule they met when they were live.
    if (isMap(doc.kinds)) {
      for (k in doc.kinds) if (Object.prototype.hasOwnProperty.call(doc.kinds, k)) r.roster.note(k, str(doc.kinds[k]));
    }
    if (isMap(doc.manual)) {
      for (k in doc.manual) {
        if (doc.manual[k] === 'ally' || doc.manual[k] === 'mob') r.roster.setManual(k, doc.manual[k]);
      }
    }
    (Array.isArray(doc.jobs) ? doc.jobs : []).forEach(function (j) {
      if (isMap(j) && j.kind === 'job') r.feedRecord(j);
    });

    var skipped = 0;
    for (var i = 0; i < doc.events.length; i++) {
      var ev = doc.events[i];
      // A job or meta record here would be filed, not counted; nothing Export
      // writes puts one in this list, so it is refused rather than obeyed.
      if (!isMap(ev) || ev.kind === 'job' || ev.kind === 'meta' || !r.feedRecord(ev)) skipped++;
    }

    // An older export's `meta` list is ignored: nothing reads the addon's notices.

    return {
      source: r,
      session: session,
      skipped: skipped,
      file: str(doc.file) || null,
      exported: str(doc.exported) || null
    };
  }

  DPS.source = {
    create: create,
    parseAll: parseAll,
    parseFilename: parseFilename,
    exportParse: exportParse,
    stringifyParse: stringifyParse,
    importParse: importParse,
    PARSE_FORMAT: PARSE_FORMAT,
    createRoster: createRoster,
    ADDL_ACTION: ADDL_ACTION,
    OURS: OURS
  };
})(window);
