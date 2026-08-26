# Ki Battle

1v1 wizard beam-clash duels (Godot 4.7.1, C#). Two casters lock beams; the clash
point pushes back and forth. Manage mana, counter the enemy's element, and shove
the clash into their face.

## Run

```
E:\Godot_v4.7.1\Godot_v4.7.1-stable_mono_win64.exe --path E:\Projects\ki-battle
```

## How to play

Draft **3 of 8 elements** (the pick order becomes your in-fight keys 1/2/3),
choose a starting beam and difficulty, then FIGHT.

| Input | Action |
|---|---|
| hold **Space** / LMB | Channel your beam |
| hold **Shift** / RMB (while channeling) | Overdrive: +60% push, 2.2x mana drain |
| **1 / 2 / 3** | Switch element (costs 10 mana, 3s cooldown) |
| **Q / W / E** | Beam type: Single / Twin / Pinpoint (6 mana, 2s cooldown) |
| **F1** | Debug: cycle your element/beam combo |

- Every element beats 3 others and loses to 3 (x1.30 / x0.77 push). Watch the
  **matchup arrow** under the enemy card and switch to counter.
- **Single** = efficient workhorse. **Twin** = +35% push but heavy drain — a
  punish/finisher. **Pinpoint** = cheap, and its *pierce* halves elemental
  advantage both ways — your escape hatch when countered.
- Mana hits 0 → **exhausted 1.5s**, beam dies. Punish enemy exhaustion hard.
- After ~30s pushes escalate; at 75s mana regen stops (sudden death); at 120s
  the judge awards whoever holds clash territory.

## Development

- Gameplay constants: `src/ElementDb.cs` (`Tuning`), matchup table in the same file.
- All 24 beam looks come from one shader: `shaders/beam.gdshader`.
- Verify headless (no window):
  `Godot_v4.7.1-stable_mono_win64_console.exe --headless --path . -- --smoke`
  (AI/mechanics invariants) and `-- --smoke-flow` (menu flow).
- Visual check: run with `-- --shots` to save screenshots of representative
  matchups to `%APPDATA%\Godot\app_userdata\Ki Battle\`.
