# Eight Souls: Catalyst — element system v2

Replaces the rock-paper-scissors matchup table (v1) entirely. Design synthesized from three
proposals (Eight Souls / Catalyst / Open Wheel) plus an adversarial critique pass; the
gather→reform→impact timeline is the load-bearing mechanic.

> **Source of truth**: every number below mirrors `Tuning` in `src/ElementDb.cs` as of the
> last sync. When they disagree, the code is right — update this doc, not the constants.

## Core loop
All 8 elements are live on keys 1-8 every fight (no draft). Each element is a SOUL:
a passive identity that bends existing systems, plus a signature ENTRANCE that detonates
at the clash when a switched beam lands. Element choice answers the FIGHT STATE (mana,
clash position, enemy tier); switch TIMING is the skill expression. Nothing about the
enemy's element ever turns your play off.

## The switch timeline
- Press (key 1-8): pay cost, GATHER begins — 0.25 s visible windup (beam brightens at the
  muzzle, enemy HUD flashes the incoming element). Old element still pushes at full power.
- Gather end: element changes, REFORM begins — 0.65 s at ×0.5 push (beam re-styles).
- Impact (press + 0.90 s): the ENTRANCE resolves at the clash orb.
- VULNERABLE the whole way: from press until 0.25 s after reform ends, incoming entrances
  SHATTER you (×1.5). Reactive shatter is impossible by construction (0.90 + ε > 0.90);
  shatters are earned by prediction and baits, or by punishing a panic dodge.
- Beam switches (Q/W/E) keep their own 0.40 s reform, cause no entrance, but are shatterable.

## Catalyst pip
One pip per caster, charges in 7 s (Light: ×1.35 faster), lit at round start, PUBLIC on the
HUD. Switching with a lit pip = full entrance (shove + effect rider); without = ×0.40 shove,
no rider. ECHO rule: re-entering an element you left <4 s ago fires NO entrance. Returning
to your KEYSTONE costs 0 mana and never fires an entrance — home is safety, not value.

## Timing modifiers at impact
- SHATTER ×1.5 (impulse and effect magnitude; Conduct: duration only): victim in
  gather/reform, or within 0.25 s after.
- BRACE ×0.5: victim in Overdrive at impact — the defender's paid, timed answer.
- PINPOINT (either side): all identity magnitudes and entrance effects damped ×0.5 toward
  neutral — pierce survives as the "turn the souls off" tool.
- Exhausted victim: impulses and debuffs land, mana operations skip entirely (floor 10
  everywhere, leech pauses).
- Impulse = shove × charged(1 / 0.4) × modifiers × min(escalation, 2), hard-capped at
  0.15 clashX.

## The 8 souls (passive / entrance)
| # | Soul | Passive (while channeling it) | Entrance (at impact, charged) |
|---|------|-------------------------------|-------------------------------|
| 1 | FIRE — Ignition | Empowered+: beam FLARES ×1.45 for 0.6 s every 2.5 s, 0.5 s telegraphed windup; entering Overdrive fast-forwards the next flare | COMBUST: shove +0.08 |
| 2 | ICE — Permafrost | While dominant ≥1 s: enemy element+beam cooldowns tick ×0.6 | GLACIATE: shove +0.035, +2 s to the enemy's currently-running cooldowns |
| 3 | WATER — Flow | Own drain ×0.85; incoming DISCRETE spikes (flares, crits, entrance shoves) damped ×0.65 — never sustained overdrive, never effect riders | RIPTIDE: shove +0.035, burn 10 enemy mana; overdriving victim gets overdrive locked 2 s |
| 4 | LIGHTNING — Overload | Empowered+: every 5 s of channel arms a CRIT ("CRIT ARMED"), fires ×1.8/0.35 s at the enemy's next vulnerability window (gather/reform/1.5 s after), self-fires at 6 s. Deterministic, no RNG | CONDUCT: shove +0.05, enemy drain ×1.5 for 3 s |
| 5 | DARKNESS — Devour | While dominant ≥1 s: leech 3 mana/s enemy→you (floor 10, pauses vs exhausted) | ECLIPSE: no shove; channel-steal 8 mana over 2 s (breaks if you reform/exhaust) |
| 6 | POISON — Corrosion | +1 stack per Empowered+ second (max 8), +2% push per stack — builds AND applies only at Empowered+; decays 1/s once you leave | ENVENOM: +3 stacks instantly |
| 7 | HOLY — Sanctuary | +3 mana/s regen, exhaust duration halved | PURGE: shove +0.05, cleanses debuffs on you + 2 s immunity |
| 8 | LIGHT — Velocity | Pip charges ×1.35; switching into Light costs 6 | FLASH: shove +0.035, refunds 3 s of pip charge |

## Switch economy
Keys 1-8 direct-select (canonical enum order = HUD strip order). Costs: 7 mana, 6 into
Light, 0 returning to keystone. Global element cooldown 3 s. Anti-spam = echo rule +
flat costs + cooldown (no fatigue arithmetic — prices stay memorizable).

## Pre-fight
LoadoutScreen becomes SOUL SELECT: pick 1 keystone (starting element, free returns).
Each button teaches its soul's two lines. Difficulty + starting beam unchanged.

## AI
4 Hz utility frame, hysteresis, personalities, delayed perception all survive; matchup
terms replaced by state-fit scores (behind→Holy/Water, ahead→Ice/Darkness, mana
war→Darkness/Water, pressure→Fire/Lightning, tempo→Light). New GATHER perception
channel: Easy 0.90 s (never defends — eats every entrance), Normal 0.55 s (defends late,
panic-dodges 15% — the learnable flaw that hands you shatters), Hard 0.30 s (braces
reliably, never psychic-dodges). AI keystone picked by personality.

## Cut from v1 (deliberately)
Blind/telegraph scramble, Fire momentum rider, Holy debuff-tick-rate, crit-vs-exhausted
doubling, attunement lerp, fatigue pricing, radial slow-mo wheel.
