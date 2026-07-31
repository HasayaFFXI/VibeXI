/*
 * parser.js -- FFXI chat log line -> damage events.
 *
 * DOM-free on purpose: everything here is callable from the console against a
 * pasted array of log lines, which is how parse rules get validated.
 *
 *   DPS.parser.create(dateFromFilename) -> stateful line-at-a-time parser
 *   DPS.parser.parseAll(lines, date)    -> one-shot, returns { events, unparsed }
 *
 * Two log-format facts drive the whole design:
 *
 *  1. A game message that spans several sentences is written across several
 *     lines, and only the FIRST carries a "[HH:MM:SS]" stamp. So a weaponskill is
 *
 *         [10:18:25] Hasaya uses Tachi: Jinpu.
 *         The Goblin Pathfinder takes 723 points of damage.
 *
 *     The damage is on the following line and the skill name is not repeated.
 *     Hence `pending`: an announced action waiting for its damage line.
 *
 *  2. Nothing in the log says who is a player and who is a monster. The article
 *     does: mobs are addressed as "the Goblin Pathfinder", players never are.
 *     Named NMs ("Leaping Lizzy") get no article, so `DPS.roster` also
 *     propagates through combat relationships -- see roster.rebuild.
 *
 * Every event carries a `use` id. One announced action is one use of it however
 * many targets it reached, so an AoE's victims -- which arrive as separate
 * damage lines -- share an id and `stats.collapse` folds them back into a
 * single swing. See the `takes` branch.
 */
(function (global) {
  'use strict';

  var DPS = global.DPS || (global.DPS = {});

  // ------------------------------------------------------------------ helpers

  var NUM = /^[\d,]+$/;

  function toInt(s) {
    return parseInt(String(s).replace(/,/g, ''), 10) || 0;
  }

  /* Splits a leading article off a name. `the` is the monster tell. */
  function ent(raw) {
    var s = String(raw).trim();
    var m = /^([Tt]he)\s+(.+)$/.exec(s);
    if (m) return { name: m[2], article: true };
    return { name: s, article: false };
  }

  /* "Hasaya's" -> "Hasaya" (possessive forms appear in ranged/pet lines). */
  function unpossess(s) {
    return String(s).replace(/'s$/, '');
  }

  /*
   * "Dags's ranged attack" -> { name: 'Dags', ranged: true }.
   * The crit announcement names the *attack*, not the attacker
   * ("Dags's ranged attack scores a critical hit!"), so without this the
   * damage that follows lands under an actor called "Dags's ranged attack".
   */
  function unranged(s) {
    var m = /^(.+?)'s ranged attack$/.exec(String(s));
    if (m) return { name: m[1], ranged: true };
    return { name: unpossess(s), ranged: false };
  }

  // ------------------------------------------------------------------ filters

  /*
   * Lines that can never be combat. Chat is filtered FIRST and hard: a player
   * typing "I hit the crab for 900 points of damage" must not become an event.
   */
  var IGNORE = [
    /^\[[A-Za-z0-9_]+\]/,             // [Ashita] [XIUI] [LuAshitacast] [Addons]
    /^\[\d+\]</,                      // [1]< Linkshell: Name >
    /^===/,                           // === Area: Lower Jeuno ===
    /^<<</,                           // <<< Welcome to HorizonXI! >>>
    /^\S+\[[A-Za-z' ]+\]:/,           // Lollipops[LowJeuno]: shout text
    /^\S+\s?:\s/,                     // Moogle : ...   /  Name: say text
    /^>>/,                            // >>Name: tell
    /^\S+\s?>>/                       // Name>> tell
  ];

  /*
   * Combat lines that would otherwise be eaten by the "Name: text" chat rule.
   * "Skillchain: Fusion." is the one that actually bites.
   */
  var COMBAT_HINT = /^(?:Skillchain:|Magic Burst!|Additional effect:)/;

  function isIgnorable(body) {
    if (COMBAT_HINT.test(body)) return false;
    for (var i = 0; i < IGNORE.length; i++) {
      if (IGNORE[i].test(body)) return true;
    }
    return false;
  }

  // ------------------------------------------------------------------ pattern

  var RE = {
    stamp:   /^\[(\d{1,2}):(\d{2}):(\d{2})\]\s?(.*)$/,

    // Prefix modifiers that ride in front of the real sentence.
    burst:   /^Magic Burst!\s*/,
    critical:/^(.+?) scores a critical hit!\s*/,
    // A counter is the one message the client sometimes writes with both
    // sentences on a single line, so this has to peel like a prefix rather than
    // match to end-of-line. The damage belongs to the defender who countered.
    counter: /^(.+?)'s attack is countered by (.+?)\.\s*/,

    // Direct damage, actor and amount on one line.
    ranged:  /^(.+?)'s ranged attack hits (.+?) for ([\d,]+) points? of damage\.?$/,
    // The two ranged critical phrasings. They end in "!" rather than ".", and
    // "strikes true, pummeling" drops the word "hits" entirely, so neither is
    // reachable from `ranged` or `melee`.
    rngCrit: /^(.+?)'s ranged attack (?:hits (.+?) squarely|strikes true, pummeling (.+?)) for ([\d,]+) points? of damage!$/,
    melee:   /^(.+?) hits (.+?) for ([\d,]+) points? of damage\.?$/,
    // Spike / reprisal damage the target deals back to its attacker.
    spikes:  /^(.+?)'s spikes deal ([\d,]+) points? of damage to (.+?)\.?$/,

    // Announcement lines -- damage (if any) lands on a following line.
    uses:    /^(.+?) uses (.+?)\.$/,
    readies: /^(.+?) readies (.+?)\.$/,
    casts:   /^(.+?) casts (.+?)\.$/,
    skchain: /^Skillchain: (.+?)\.?$/,

    // Resolution lines for a pending announcement.
    takes:   /^(.+?) takes ([\d,]+) points? of damage\.?$/,
    addl:    /^Additional effect: ([\d,]+) points? of damage\.?$/,
    // Same message, target named. "additional points" keeps it out of `takes`.
    addlTo:  /^Additional effect: (.+?) takes ([\d,]+) additional points? of damage\.?$/,
    noeff:   /^(.+?) takes no damage\.?$/,
    resist:  /^(.+?) resists the (?:spell|effect)\.?$/,

    // Whiffs.
    misses:  /^(.+?) misses (.+?)\.$/,
    evades:  /^(.+?) evades the attack\.?$/,
    avoids:  /^(.+?) avoids? damage\.?$/,
    blocked: /^(.+?)'s attack is (?:blocked|parried)\.?$/,

    // Relationship signals (no damage, but they classify names).
    defeats: /^(.+?) defeats (.+?)\.$/,
    falls:   /^(.+?) falls to the ground\.?$/
  };

  var MELEE_ACTION = 'Attack';
  var RANGED_ACTION = 'Ranged Attack';
  var COUNTER_ACTION = 'Counter';

  /*
   * How long an announcement stays eligible to own *further* damage lines after
   * its first one landed. An AoE writes only its first victim as a continuation
   * of the announcement; the rest arrive seconds later as their own stamped
   * lines, by which time an unrelated melee swing has already cleared `pending`.
   */
  var AOE_MS = 5000;

  /*
   * How long a weaponskill stays eligible to have opened a skillchain. The
   * "Skillchain: X." line follows its closing weaponskill within a second or
   * two; beyond this the remembered actor is stale and the older lastDamager
   * guess is the better of two bad options.
   */
  var CHAIN_MS = 10000;

  // ------------------------------------------------------------------- roster
  /*
   * Decides ally vs monster for every name seen. Two hard signals, then
   * relationship propagation, then the user's manual overrides on top.
   */
  function createRoster() {
    var api = {
      owner: null,
      manual: {},       // name -> 'ally' | 'mob', set from the UI
      articled: {},     // every name ever seen with "the" -- sticky, see rebuild
      mobs: {},
      allies: {},

      isMob: function (name) {
        if (this.manual[name]) return this.manual[name] === 'mob';
        if (this.allies[name]) return false;
        return !!this.mobs[name];
      },
      isAlly: function (name) {
        return !this.isMob(name);
      },
      setManual: function (name, kind) {
        if (kind) this.manual[name] = kind;
        else delete this.manual[name];
      },

      /*
       * Rebuilt from scratch over the whole event list after every poll, so a
       * name classified late (its first article only shows up on kill #3)
       * retroactively fixes the events that came before.
       */
      rebuild: function (events) {
        var hardAlly = {};
        var i, e, k;

        if (this.owner) hardAlly[this.owner] = true;

        // The article signal accumulates and is never cleared: knowing that
        // "the Goblin Pathfinder" is a monster stays true after the meter is
        // reset, so a reset does not briefly file every monster as a party
        // member while the new event list refills.
        for (i = 0; i < events.length; i++) {
          e = events[i];
          if (e.actorArticle) this.articled[e.actor] = true;
          if (e.targetArticle) this.articled[e.target] = true;
        }
        var hardMob = {};
        for (k in this.articled) hardMob[k] = true;

        var mobs = {}, allies = {};
        for (k in hardMob) mobs[k] = true;
        for (k in hardAlly) allies[k] = true;

        // Fixed point over "who fights whom". Converges in a couple of sweeps;
        // 4 is slack. Hard signals always win over derived ones.
        //
        // `e.guess` events are skipped: their actor was inferred, not read, so
        // one bad guess propagates. A mis-credited "ally hits ally" line marks
        // the victim a monster, everyone the victim fights becomes an ally, and
        // the boss lands in the party list.
        for (var pass = 0; pass < 4; pass++) {
          var changed = false;
          for (i = 0; i < events.length; i++) {
            e = events[i];
            if (e.guess) continue;
            if (!e.actor || !e.target || e.actor === e.target) continue;

            if (mobs[e.actor] && !allies[e.target] && !hardMob[e.target]) {
              allies[e.target] = true; changed = true;
            }
            if (mobs[e.target] && !allies[e.actor] && !hardMob[e.actor]) {
              allies[e.actor] = true; changed = true;
            }
            if (allies[e.actor] && !mobs[e.target] && !hardAlly[e.target]) {
              mobs[e.target] = true; changed = true;
            }
            if (allies[e.target] && !mobs[e.actor] && !hardAlly[e.actor]) {
              mobs[e.actor] = true; changed = true;
            }
          }
          if (!changed) break;
        }

        for (k in hardMob) { mobs[k] = true; delete allies[k]; }
        for (k in hardAlly) { allies[k] = true; delete mobs[k]; }

        this.mobs = mobs;
        this.allies = allies;
        return this;
      }
    };
    return api;
  }

  // ------------------------------------------------------------------- parser

  /*
   * `baseDate` is a Date for the log's calendar day, taken from the filename
   * ("Hasaya_2026.07.30.log"). Log stamps are wall-clock only, so a stamp that
   * goes backwards means midnight rolled over and the day is bumped.
   */
  function create(baseDate, ownerName) {
    var roster = createRoster();
    roster.owner = ownerName || null;

    var state = {
      day: baseDate ? new Date(baseDate.getFullYear(), baseDate.getMonth(), baseDate.getDate())
                    : new Date(new Date().toDateString()),
      lastSec: -1,
      lastStamp: null,      // ms of the most recent [HH:MM:SS]
      pending: null,        // announced action awaiting its damage line
      aoe: null,            // announcement that already landed, still splashing
      lastDamager: null,    // for Additional effect / unannounced damage
      lastWS: null,         // last weaponskill that landed, for Skillchain
      foes: {},             // name -> { name: true }, from observed attributions
      lineNo: 0,
      // Monotonic across resets, unlike lineNo: that one indexes into the line
      // stream being re-fed, this one only has to be unique among live events.
      useSeq: 0
    };

    var events = [];
    var unparsed = [];

    function stampMs(h, m, s) {
      var sec = h * 3600 + m * 60 + s;
      if (state.lastSec >= 0 && sec < state.lastSec - 60) {
        // Wall clock went backwards by more than a minute -> next day.
        state.day = new Date(state.day.getTime() + 86400000);
      }
      state.lastSec = sec;
      return state.day.getTime() + sec * 1000;
    }

    /*
     * A parse-time sketch of who is fighting whom. `roster` answers the same
     * question far better, but only after `rebuild` has run over a completed
     * poll -- on the first load every line is fed before the first rebuild, so
     * the roster is empty exactly when the AoE echo needs it. Only attributions
     * that were actually read off a line feed this; guesses would make it
     * self-confirming.
     */
    function noteFoe(a, b) {
      if (!a || !b || a === b) return;
      (state.foes[a] || (state.foes[a] = {}))[b] = true;
      (state.foes[b] || (state.foes[b] = {}))[a] = true;
    }

    /* Two names that have ever fought the same third party are on one side. */
    function aligned(a, b) {
      if (a === b) return true;
      var fa = state.foes[a], fb = state.foes[b];
      if (!fa || !fb) return false;
      for (var k in fa) if (fb[k]) return true;
      return false;
    }

    /*
     * Guards the AoE echo. Without it the echo is worse than the lastDamager
     * guess it replaces: a nuke that just resolved would adopt the *monster's*
     * AoE damage on the caster's own party and credit it to the caster.
     */
    function couldStrike(actor, target) {
      if (!actor || !target || actor === target) return false;
      var fa = state.foes[actor];
      if (fa && fa[target]) return true;
      return !aligned(actor, target);
    }

    function push(ev) {
      ev.line = state.lineNo;
      // Anything that did not inherit an id is a use of its own: a melee swing,
      // a miss, a spike. Only the AoE echo passes one in.
      if (ev.use == null) ev.use = ++state.useSeq;
      events.push(ev);
      if (!ev.guess) noteFoe(ev.actor, ev.target);
      if (ev.hit && ev.dmg > 0) {
        state.lastDamager = { actor: ev.actor, article: ev.actorArticle, target: ev.target };
        // Remembered separately from lastDamager because everyone else's melee
        // swings land between a weaponskill and the "Skillchain:" line it
        // opened, and the chain belongs to the weaponskill.
        if (ev.kind === 'ws') {
          state.lastWS = {
            actor: ev.actor, article: ev.actorArticle, target: ev.target, t: ev.t
          };
        }
      }
      return ev;
    }

    function emit(o) {
      return push({
        t: o.t,
        kind: o.kind,
        actor: o.actor,
        actorArticle: !!o.actorArticle,
        target: o.target,
        targetArticle: !!o.targetArticle,
        action: o.action,
        dmg: o.dmg || 0,
        hit: o.hit !== false,
        crit: !!o.crit,
        burst: !!o.burst,
        guess: !!o.guess,
        use: o.use
      });
    }

    /*
     * A pending announcement survives lines the parser ignores (addon spam,
     * chat) but is consumed or dropped by the next combat line, and expires
     * after 8 seconds so a Meditate never adopts an unrelated damage number.
     */
    function clearPending(t) {
      if (state.pending && t - state.pending.t > 8000) state.pending = null;
    }

    function feed(rawLine) {
      state.lineNo++;

      var line = String(rawLine == null ? '' : rawLine).replace(/[\r\n]+$/, '');
      // Auto-translate brackets and other control bytes decode to junk; drop them.
      line = line.replace(/[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]/g, '').trim();
      if (!line) return null;

      var t = state.lastStamp;
      var m = RE.stamp.exec(line);
      var body;
      if (m) {
        t = stampMs(+m[1], +m[2], +m[3]);
        state.lastStamp = t;
        body = m[4].trim();
      } else {
        body = line;   // continuation of the previous stamped message
      }
      if (!body) return null;
      if (t == null) return null;   // damage before any timestamp: unanchored

      if (isIgnorable(body)) return null;
      clearPending(t);

      return handle(body, t);
    }

    function handle(body, t) {
      var m, a, tg, crit = false, burst = false;

      // Peel prefix modifiers; some clients inline them, some put them on
      // their own line and the modifier applies to whatever comes next.
      m = RE.burst.exec(body);
      if (m) {
        burst = true;
        body = body.slice(m[0].length).trim();
        if (!body) { state.burstFlag = true; return null; }
      }
      if (state.burstFlag) { burst = true; state.burstFlag = false; }

      m = RE.critical.exec(body);
      if (m) {
        crit = true;
        a = ent(m[1]);
        var cr = unranged(a.name);
        state.pending = {
          t: t, actor: cr.name, actorArticle: a.article,
          action: cr.ranged ? RANGED_ACTION : MELEE_ACTION,
          kind: cr.ranged ? 'ranged' : 'melee', crit: true
        };
        body = body.slice(m[0].length).trim();
        if (!body) return null;
      }

      // The counterer is named second: "Promathia's attack is countered by
      // Hasaya." The damage sentence that follows says "Promathia takes N",
      // and on this client it often shares the line -- so peel and fall
      // through, and let RE.takes resolve the pending Counter either way.
      m = RE.counter.exec(body);
      if (m) {
        a = ent(m[2]);
        state.pending = {
          t: t, actor: unpossess(a.name), actorArticle: a.article,
          action: COUNTER_ACTION, kind: 'counter'
        };
        body = body.slice(m[0].length).trim();
        if (!body) return null;
      }

      // ---- direct-damage forms -------------------------------------------
      m = RE.ranged.exec(body);
      if (m) {
        a = ent(m[1]); tg = ent(m[2]);
        state.pending = null;
        return emit({
          t: t, kind: 'ranged', action: RANGED_ACTION,
          actor: unpossess(a.name), actorArticle: a.article,
          target: tg.name, targetArticle: tg.article,
          dmg: toInt(m[3]), crit: crit, burst: burst
        });
      }

      // Deliberately not flagged as a crit: the "squarely"/"strikes true"
      // wording is the only tell, and it is not the same signal as an explicit
      // "scores a critical hit!" line. Counted as plain ranged damage.
      m = RE.rngCrit.exec(body);
      if (m) {
        a = ent(m[1]); tg = ent(m[2] || m[3]);
        state.pending = null;
        return emit({
          t: t, kind: 'ranged', action: RANGED_ACTION,
          actor: unpossess(a.name), actorArticle: a.article,
          target: tg.name, targetArticle: tg.article,
          dmg: toInt(m[4]), crit: crit, burst: burst
        });
      }

      m = RE.melee.exec(body);
      if (m) {
        a = ent(m[1]); tg = ent(m[2]);
        state.pending = null;
        return emit({
          t: t, kind: 'melee', action: MELEE_ACTION,
          actor: a.name, actorArticle: a.article,
          target: tg.name, targetArticle: tg.article,
          dmg: toInt(m[3]), crit: crit, burst: burst
        });
      }

      // Spikes fire in reaction to someone else's swing, so unlike the forms
      // above this must not consume a pending announcement -- the weaponskill
      // it interrupts is still waiting for its own damage line.
      m = RE.spikes.exec(body);
      if (m) {
        a = ent(m[1]); tg = ent(m[3]);
        return emit({
          t: t, kind: 'spikes', action: 'Spikes',
          actor: a.name, actorArticle: a.article,
          target: tg.name, targetArticle: tg.article,
          dmg: toInt(m[2])
        });
      }

      // ---- resolution of a pending announcement ---------------------------
      // Both Additional effect forms credit the last thing that dealt damage
      // and land in their own "Additional Effect" action row. The named-target
      // form is tested first; it is the one that carries a target.
      m = RE.addlTo.exec(body);
      if (m && state.lastDamager) {
        tg = ent(m[1]);
        return emit({
          t: t, kind: 'addl', action: 'Additional Effect',
          actor: state.lastDamager.actor, actorArticle: state.lastDamager.article,
          target: tg.name, targetArticle: tg.article,
          dmg: toInt(m[2])
        });
      }

      m = RE.takes.exec(body);
      if (m) {
        tg = ent(m[1]);
        var dmg = toInt(m[2]);
        var p = state.pending;
        state.pending = null;
        if (p) {
          // The announcement keeps splashing: hold it aside so the rest of an
          // AoE's victims, who arrive on their own stamped lines after other
          // combat has gone by, still land under the action that hit them.
          // The id minted here is what they inherit -- one Meteor is one use
          // of Meteor whether it lands on one target or six.
          var useId = ++state.useSeq;
          state.aoe = {
            t: t, actor: p.actor, actorArticle: p.actorArticle,
            action: p.action, kind: p.kind, burst: p.burst, use: useId, hit: {}
          };
          state.aoe.hit[tg.name] = true;
          return emit({
            t: t, kind: p.kind, action: p.action,
            actor: p.actor, actorArticle: p.actorArticle,
            target: tg.name, targetArticle: tg.article,
            dmg: dmg, crit: crit || p.crit, burst: burst || p.burst,
            use: useId
          });
        }

        // A second victim of the announcement above. One cast never hits the
        // same target twice, so `hit` keeps a repeated line (a DoT tick on the
        // one target) from being read as splash and double-counted.
        var ae = state.aoe;
        if (ae && t - ae.t <= AOE_MS && !ae.hit[tg.name] &&
            couldStrike(ae.actor, tg.name)) {
          ae.hit[tg.name] = true;
          return emit({
            t: t, kind: ae.kind, action: ae.action,
            actor: ae.actor, actorArticle: ae.actorArticle,
            target: tg.name, targetArticle: tg.article,
            dmg: dmg, crit: crit, burst: burst || ae.burst,
            use: ae.use
          });
        }

        // Damage with no announcement in front of it: damage-over-time, a
        // spike, an enspell. Credit the last thing that dealt damage. Flagged
        // `guess` -- the actor here was never read off a line, so it must not
        // reach roster.rebuild.
        if (state.lastDamager) {
          return emit({
            t: t, kind: 'other', action: 'Unattributed', guess: true,
            actor: state.lastDamager.actor, actorArticle: state.lastDamager.article,
            target: tg.name, targetArticle: tg.article,
            dmg: dmg, crit: crit, burst: burst
          });
        }
        unparsed.push({ line: state.lineNo, text: body });
        return null;
      }

      m = RE.addl.exec(body);
      if (m && state.lastDamager) {
        return emit({
          t: t, kind: 'addl', action: 'Additional Effect',
          actor: state.lastDamager.actor, actorArticle: state.lastDamager.article,
          target: state.lastDamager.target, targetArticle: false,
          dmg: toInt(m[1])
        });
      }

      // ---- announcements ---------------------------------------------------
      // Whether "uses X" is a weaponskill/TP move or a plain job ability is
      // only knowable from what follows, so nothing is emitted here.
      m = RE.uses.exec(body);
      if (m) {
        a = ent(m[1]);
        state.pending = {
          t: t, actor: a.name, actorArticle: a.article,
          action: m[2].trim(), kind: 'ws', burst: burst
        };
        return null;
      }

      m = RE.casts.exec(body);
      if (m) {
        a = ent(m[1]);
        state.pending = {
          t: t, actor: a.name, actorArticle: a.article,
          action: m[2].trim(), kind: 'magic', burst: burst
        };
        return null;
      }

      // "readies" normally precedes a matching "uses" that overwrites this,
      // but some monster TP moves go straight from readies to damage. Claiming
      // the pending slot here beats falling through to the lastDamager guess.
      m = RE.readies.exec(body);
      if (m) {
        a = ent(m[1]);
        state.pending = {
          t: t, actor: a.name, actorArticle: a.article,
          action: m[2].trim(), kind: 'ws', burst: burst
        };
        return null;
      }

      // A skillchain is credited to whoever closed it -- the last actor to land
      // a WEAPONSKILL, not the last actor to deal damage. Ordinary "X hits Y
      // for N" lines from the rest of the party land between the weaponskill
      // and this line and would otherwise steal the credit.
      m = RE.skchain.exec(body);
      if (m) {
        var sc = (state.lastWS && t - state.lastWS.t <= CHAIN_MS)
          ? state.lastWS : state.lastDamager;
        if (!sc) return null;
        state.pending = {
          t: t, actor: sc.actor, actorArticle: sc.article,
          action: 'Skillchain: ' + m[1].trim(), kind: 'skillchain'
        };
        return null;
      }

      // ---- misses ----------------------------------------------------------
      m = RE.misses.exec(body);
      if (m) {
        a = ent(m[1]); tg = ent(m[2]);
        state.pending = null;
        return emit({
          t: t, kind: 'melee', action: MELEE_ACTION,
          actor: a.name, actorArticle: a.article,
          target: tg.name, targetArticle: tg.article,
          dmg: 0, hit: false
        });
      }

      // "The Goblin evades the attack." resolves a pending weaponskill as a whiff.
      m = RE.evades.exec(body) || RE.avoids.exec(body) || RE.noeff.exec(body);
      if (m) {
        tg = ent(m[1]);
        var pw = state.pending;
        state.pending = null;
        if (pw) {
          return emit({
            t: t, kind: pw.kind, action: pw.action,
            actor: pw.actor, actorArticle: pw.actorArticle,
            target: tg.name, targetArticle: tg.article,
            dmg: 0, hit: false
          });
        }
        return null;
      }

      m = RE.blocked.exec(body);
      if (m) { state.pending = null; return null; }

      m = RE.resist.exec(body);
      if (m) { state.pending = null; return null; }

      // ---- relationship-only lines ----------------------------------------
      m = RE.defeats.exec(body);
      if (m) {
        a = ent(m[1]); tg = ent(m[2]);
        state.pending = null;
        return push({
          t: t, kind: 'defeat',
          actor: a.name, actorArticle: a.article,
          target: tg.name, targetArticle: tg.article,
          action: 'Defeat', dmg: 0, hit: false, crit: false, burst: false
        });
      }

      m = RE.falls.exec(body);
      if (m) { state.pending = null; return null; }

      // Anything with a damage number in it that got this far is a pattern the
      // parser does not know. Surfaced in the UI's Diagnostics panel.
      if (/points? of damage/.test(body)) {
        unparsed.push({ line: state.lineNo, text: body });
      }
      return null;
    }

    return {
      feed: feed,
      events: events,
      unparsed: unparsed,
      roster: roster,
      state: state,
      reset: function () {
        events.length = 0;
        unparsed.length = 0;
        state.pending = null;
        state.aoe = null;
        state.lastDamager = null;
        state.lastWS = null;
        // `foes` is deliberately kept, same reasoning as roster.articled: who
        // fights whom stays true across a "clear the meter between pulls".
        state.lastStamp = null;
        state.lastSec = -1;
        state.lineNo = 0;
      }
    };
  }

  /* Convenience for console testing: parse a whole array in one call. */
  function parseAll(lines, baseDate, ownerName) {
    var p = create(baseDate, ownerName);
    for (var i = 0; i < lines.length; i++) p.feed(lines[i]);
    p.roster.rebuild(p.events);
    return p;
  }

  /* "Hasaya_2026.07.30.log" -> { owner: 'Hasaya', date: Date(2026-07-30) } */
  function parseFilename(name) {
    var out = { owner: null, date: null };
    if (!name) return out;
    var m = /^(.*?)_(\d{4})\.(\d{2})\.(\d{2})\.log$/i.exec(name);
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

  DPS.parser = {
    create: create,
    parseAll: parseAll,
    parseFilename: parseFilename,
    createRoster: createRoster,
    MELEE_ACTION: MELEE_ACTION,
    RANGED_ACTION: RANGED_ACTION,
    COUNTER_ACTION: COUNTER_ACTION,
    CHAIN_MS: CHAIN_MS,
    AOE_MS: AOE_MS,
    RE: RE
  };
})(window);
