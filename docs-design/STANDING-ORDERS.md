# Standing orders — paying the crew to hold the block

A plan for letting hired muscle fight turf battles without you.

Status: **plan only.** No code written.

---

## 1. Why this does not break the rule it looks like it breaks

The muscle system carries an explicit design rule, written into `Armoury.cs`:

> He still does not win it for you. The original rule holds — if muscle could
> settle a turf battle alone there would be no reason to drive out.

That rule was correct **when On The Blade was the only mod running.** Removing
the reason to drive out would have removed the only content the mod had.

The premise has changed. With The Trap Star sharing the same city, "drive to
Vespucci" now competes with a burner meet, a plug who is only around for a few
more days, and a warehouse that is getting warm. The player is not idle and
looking for something to do — they are busy, and a turf battle is an interruption
in a business they are already running.

So the rule gets amended rather than deleted:

> Muscle does not win it for free. It wins it for **money, standing and
> equipment** — all three, and enough of each that turning up yourself remains
> the cheap option.

The feature is a way to convert a **time cost into a money cost**. That is a
legitimate thing to sell a player who is out of time. It is not a way to make
territory free.

---

## 2. What it is

A **standing order**, set per corner, from the Muscle menu.

When a rival contests a corner with a standing order on it, the contest resolves
off-screen instead of spawning a `TurfBattleIncident`. You get a notification
telling you what happened and what it cost.

Three states per corner:

| | |
|---|---|
| **Off** | Contests fire as they do today. You drive out or you lose it. |
| **Defend** | They hold the corner if somebody comes for it. |
| **Defend and take** | They also go and take a contested corner when you order it, without you. |

"Defend and take" is deliberately a separate, more expensive setting. Holding
ground you already own is what a wage buys; going and taking somebody else's is
a different job and should be priced like one.

---

## 3. Where the guarantee comes from

You asked for the price to guarantee the block is held. It should — but the
guarantee has to come from **what you have built**, not from paying more at the
moment of the fight. A cash button that buys certainty is a cash button that
deletes the system.

So: the outcome is decided by a **crew rating against a rival rating**, and above
a margin it is not a roll at all.

```
crewRating  = Σ over enforcers covering the region:
                  EffectiveSkill
                × background deterrence multiplier
                × loadout multiplier
              + vehicle bonus

rivalRating = rival.Strength × enforcerCount × (war ? WarMultiplier : 1)
```

| Ratio | Outcome |
|---|---|
| ≥ 1.6 | **Held, guaranteed.** No roll. Light costs. |
| 1.1 – 1.6 | Held, but a casualty roll and higher costs. |
| 0.7 – 1.1 | Coin flip, and losing costs you the corner as normal. |
| < 0.7 | They will lose. The menu says so before you set the order. |

**The menu must show the ratio and the verdict before you commit.** A player
paying a retainer for a guarantee has to be able to see whether they are actually
buying one. That readout is the feature — the rest is arithmetic behind it.

The route to a guarantee is therefore: hire good muscle, arm them properly, buy
the vehicle, and keep them out of hospital. Which is exactly the upgrade path
below.

---

## 4. Stage 1 upgrades

### Guns — mostly already built

`Armoury` already ships four loadouts with a `Muscle` rating (bat 8, pistol 18,
SMG 30, carbine 45), armour and accuracy. Stage 1 needs the loadout to feed the
crew rating rather than only the deterrence roll and the in-person spawn.

One addition worth making: **ammunition as a per-battle cost**, scaling with the
loadout. A carbine that holds a corner every night should have a running cost, or
buying the best gun once is a permanent solution to a recurring problem.

| | Muscle | Per-battle ammunition |
|---|---:|---:|
| Bat | 8 | $0 |
| Pistol | 18 | ~$150 |
| Micro SMG | 30 | ~$450 |
| Carbine | 45 | ~$900 |

### Protection — new, and the cheapest thing on this list

Body armour is currently bundled into the loadout. Stage 1 splits it out so it
can be bought independently, because the thing that makes a standing order
sustainable is not winning — it is **not losing people**.

| | Cost | Effect |
|---|---:|---|
| Vests | ~$9,000 per region | Halves the casualty roll |
| Plates | ~$26,000 per region | Casualties become rare; small rating bonus |

Injury already exists (`MuscleInjuryDays`, still on the wage while laid up), so
this plugs into machinery that is built and tested.

### Armoured vehicles — new, and the flagship

One per region, like the stash house and the car in the property menu.

| | Cost | Effect |
|---|---:|---|
| A van | ~$45,000 | +15 crew rating in that region, and they arrive together |
| Armoured van | ~$120,000 | +35 rating, casualties reduced again, survives being shot at |

The vehicle is the difference between "we went round" and "we turned up
mob-handed", and it is the cleanest way to push a marginal region over the
guarantee line without hiring anybody new.

**Vehicles should be losable.** A defence that goes badly can write one off. That
is what stops the top of the upgrade tree being a one-time purchase.

---

## 5. What it costs, and why turning up stays cheaper

Three costs, deliberately:

**A retainer, per corner, per day.** Roughly $600/day on top of the enforcer's
existing wage. This is what makes a standing order a commitment rather than a
switch you flip when a blip appears — and it is charged through `EconomyTick.Charge`,
so an operation that cannot cover it turns into debt like everything else.

**A fee per battle**, scaled to the fight. Somewhere around $2,000–4,000
depending on how badly outnumbered they were.

**Reputation.** This is the important one. A corner held by your people while you
were elsewhere is worth **less reputation than one you held yourself** — perhaps
a third. Everybody knows the difference, and reputation gates tier-3 prospects,
protection prices and plug introductions in the other mod.

That third cost is what stops this being strictly better than driving out. The
money is affordable; the name is not free.

### Is it worth it?

Rough figures, to be confirmed by simulation before any code:

- A staffed corner earns in the region of **$700 an hour** across three workers.
- Losing one costs that income plus a three-day lockout plus everyone off the
  street — call it **$20,000+** before the reputation hit.
- A standing order costs ~$600/day plus ~$3,000 when it fires.

So the answer should be a clear yes, and the design question is not whether it
pays but whether it pays *too obviously*. If simulation says a standing order on
every corner is always correct, the retainer is too cheap — raise it until
holding six corners on retainer is a real strain on a mid-game operation and only
an endgame one can do it everywhere.

---

## 6. What it reuses

Very little of this is new machinery:

| Needs | Already exists |
|---|---|
| People to fight | `EnforcerData`, backgrounds, skill growth, injury |
| Weapons and armour | `Armoury`, four loadouts with ratings |
| Regions to cover | `Regions`, one enforcer per region |
| Rival strength and war | `RivalCrew`, `Rivals.ContestMultiplier` |
| A contest to intercept | `IncidentRoller.RollRivalContest` |
| Charging money that can become debt | `EconomyTick.Charge` |
| A vehicle per region | The property menu already does exactly this |

The genuinely new parts are the crew-rating calculation, the standing-order
state, the readout in the menu, and armour and vehicles as purchasables.

---

## 7. Where it hooks in

`IncidentRoller.RollRivalContest` currently does:

1. Pick a held, staffed zone
2. `DeterredByMuscle` — a roll that may wave them off entirely
3. Otherwise start a `TurfBattleIncident`

Step 3 becomes: **if a standing order covers this zone, resolve it off-screen
instead.** Deterrence still happens first, because a crew that turns them away
without a fight costs nothing and should stay the best outcome.

Everything else — the incident, the drive, the blip — is untouched for corners
without an order on them.

---

## 8. Open questions

1. **Does a standing order also cover the house?** Houses have no turf battles
   today, but they are the fattest target in the mod. Probably a later stage.
2. **Should the crew be able to lose a corner you would have won?** Currently a
   player who drives out can win a fight the numbers say they should lose. An
   off-screen resolution cannot. That asymmetry is arguably the point, but it
   should be a decision rather than an accident.
3. **Retaliation and war.** A crew at war contests every few game hours. A
   standing order during a war might be the only thing that makes war survivable
   for a busy player — or it might trivialise the war state entirely. Simulate
   before deciding.
4. **Does this want to be visible in The Trap Star?** A corner held on retainer is
   a corner still producing for both businesses. The world file already carries
   ownership, so nothing new is needed — but it is worth checking that a
   standing-order hold updates ownership through the same path a manual win does.

---

## 9. What the simulation actually said

Thirty game days, six corners on order, five seeds, all four rival crews rolling
hourly against the shipped constants. Every number below was pulled out of the
built DLL by reflection rather than read off this document, because two of them
turned out not to be what this document assumed.

### 9.1 The rating maths was wrong, and it was wrong in the worst direction

The first version read:

```csharp
float rating = enforcer.EffectiveSkill * Profile.DeterrenceMultiplier * weapon;
```

`EffectiveSkill` is already `Skill + weapon muscle`. So a carbine both pushed a
man to the 100 ceiling **and** multiplied the result by 1.45. Stacked on a
leg-breaker's 1.40, a merely competent enforcer cleared the 1.6 guarantee
threshold against every crew in the game:

| build | old rating | vs the Lost, at war |
|---|---|---|
| leg-breaker 70, SMG, vests + van (~$54k) | 200 | **guaranteed** |
| leg-breaker 92, carbine, plates + armoured van (~$168k) | 246 | **guaranteed** |

Two consequences, both fatal to the feature:

- **The top of the equipment ladder was dead money.** Plates and the armoured
  van — $146,000, the flagship purchase this whole document is built around —
  did not move a single cell of the verdict table. Everything above the van was
  already guaranteed.
- **War was an invoice.** 41 fights, 41 wins, zero injuries across a 30-day war.
  Answering §8.3: it trivialised the war state completely.

Rating is now `Skill × (0.7 + 0.3 × background) + weapon + armour + vehicle` —
the weapon counted once, and the background nudging rather than multiplying.

### 9.2 The corrected curve

Verified against the DLL. `g` guaranteed, `h` held at a cost, `c` coin flip,
`l` they lose it. Peace / war.

| build | rating | Ballas | Vagos | Families | Lost |
|---|---|---|---|---|---|
| local 42, bat, nothing | 50 | 0.97c / 0.57l | 0.64l / 0.39l | 0.55l / 0.33l | 0.45l / 0.27l |
| journeyman 55, pistol, nothing | 73 | 1.41h / 0.83c | 0.94c / 0.56l | 0.80c / 0.48l | 0.66l / 0.40l |
| journeyman 65, pistol, vests | 86 | 1.66g / 0.98c | 1.10h / 0.66l | 0.95c / 0.57l | 0.78c / 0.47l |
| leg-breaker 70, SMG, vests + van | 126 | 2.44g / 1.43h | 1.62g / 0.98c | 1.39h / 0.84c | 1.14h / 0.69l |
| leg-breaker 85, carbine, plates + van | 163 | 3.15g / 1.85g | 2.09g / 1.26h | 1.79g / 1.08c | 1.48h / 0.89c |
| leg-breaker 92, carbine, everything | 191 | 3.69g / 2.17g | 2.45g / 1.47h | 2.10g / 1.26h | 1.73g / **1.04c** |

Every purchase now moves at least one cell. Nothing in the game guarantees a
corner against the Lost at war — the best crew money can buy is a coin flip, and
gets there by being deterred out of 84% of contests in the first place rather
than by winning fights.

### 9.3 §8.3 answered: war is survivable, not safe

War compounds twice — `WarExtraEnforcers` puts two more bodies on the street
*and* `OrderWarMultiplier` scales the result, so the Lost go from 110 to 184.
Contest frequency goes up 2.2× on top of that.

The result at the top of the ladder: **84% of fights won, ~$9,300/day, and you
still lose corners.** That is the answer — a standing order is what lets a busy
player keep operating through a war, and it is not what lets them ignore one.

One extra change was needed to get there. A rout previously could not hurt
anybody, so a crew past the guarantee threshold was never injured again for the
rest of the run. `OrderCasualtyOnRout = 0.10` means even a one-sided night
occasionally costs you somebody, which is what stops a long war being free.

### 9.4 §5 answered: the retainer stays at $600

Six corners on order costs **$6,900/day at peace and $9,300/day at war**, of
which only $3,600 is retainer — the rest is battle fees, which scale with how
often you are actually attacked and are therefore self-limiting.

Against a mid operation's gross that is most of a night's takings, so six
corners on retainer is an endgame position rather than a default. No raise
needed.

### 9.5 Three other faults the simulation surfaced

- **`DefendAndTake` did nothing.** It charged double the retainer and was
  otherwise a byte-for-byte copy of `Defend` — strictly worse, always. It now
  pushes back: after a rout, a 22% chance the crew follows them home and takes a
  corner that rival holds in the same region, costing them 8 strength and
  raising their aggression by 0.10. Loud, and it makes them come back angrier.
- **A bodyguard was silently doubling as a garrison.** `Resolve` looked up
  `EnforcerInRegion`, which includes a man assigned to mind one specific worker;
  deterrence used `EnforcerFor`, which does not. So a region whose only enforcer
  was on bodyguard duty got no deterrence but did fight turf battles. Both now
  go through `StandingOrders.Holder`.
- **Ignoring a turf battle already forfeits the corner**, which is what makes a
  losing order coherent rather than a trap: the outcome is the same as doing
  nothing, plus a fee, plus a 10% floor chance of holding it anyway. The menu
  still says *they will lose* in red before you commit.

§8.2 resolves as a decision rather than an accident: yes, the crew can lose a
corner you would have won. That is what you are buying — the conversion of your
time into their odds, at a discount you can read on the menu before you agree
to it.
