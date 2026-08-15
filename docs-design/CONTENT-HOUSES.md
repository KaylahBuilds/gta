# Content houses

Four live-in production houses, from a Chamberlain Hills trap house to a
fifteen-room place in Vinewood Hills. A worker posted to one sees no clients at
all: she is paid a flat hourly rate the moment she walks in, multiplied by the
audience she built while the corners were too hot to work.

Structurally it is a `HouseDef` with a bool. That single decision inherits about
fifteen existing call sites untouched — the post selector, `AssignHouse`,
`RoomsFree`, `WorkersInHouse`, `ClearHouse`, `DailyRentBill`, `CheckHouseRaids`,
Law custody, `DecayRegulars`' posted test, the never-spawn rule, incident
immunity and the blip loop — and means none of the ten places that null a
posting can be missed, because there is no fourth posting field.

## 1. What R2 actually buys, stated honestly

The brief asked for "a way to still earn revenue when territories are too hot".
The brothels already are that: their payout reads `GetHouseHeat`, never zone
heat, indoor workers are excluded from `IncidentRoller` and never spawned.
Pretending otherwise would have been the easy answer and the wrong one.

The real hole is **room count**. The three brothels hold 2 + 3 + 4 = 9 rooms
total, gated at $120k/$260k/$520k and reputation 0/250/500, against a roster that
caps at 8. A hot map manufactures idle worker-hours in bulk — and today an idle
hour produces followers into a stock hard-clamped at 25,000.

So the content house answers R2 by having somewhere to *put* people, with an
entry rung at **$60,000** — below the cheapest brothel — that pays from the first
hour with no ring light and no ramp.

## 2. The ladder

| rung | area | cost | rooms | rent/day | roster |
|---|---|---|---|---|---|
| The Chamberlain place | Chamberlain Hills / Davis | $60,000 | 4 | $700 | +1 |
| The Mirror Park flat | Mirror Park | $210,000 | 7 | $1,900 | +1 |
| The Rockford Hills place | Rockford Hills | $560,000 | 11 | $5,200 | +2 |
| The Vinewood Hills house | Vinewood Hills | $950,000 | 15 | $8,800 | +3 |

Each requires the one below. Base 4 + RosterA 2 + RosterB 2 + the ladder's 7
comes to exactly 15, which is the top house's capacity — so the ladder terminates
with no dead rooms and no roster you cannot house. That is the only coherent
reading of "15+ girl house" against a roster that caps at 8 today.

## 3. Heat, and the one rule everything is solved from

`HeatGain` is per **resident** per hour, not per producing hour, and every value
comes from one rule:

```
HeatGain = ContentHeatDecayPerHour / (0.70 x Rooms)
```

The quiet line is therefore 70% of the rooms at every rung, the decision is
always "is this person worth what she costs to keep quiet", and the shift
selector cannot be used to duck heat — which is exactly how the brothel ladder
got away with being heat-immune at full capacity for two versions.

Full capacity drifts +0.0617/day at every rung: about 15 game days from cold to a
raid, 44 with discretion bought, 88 with a houseful of Connected residents on top.

## 4. The findings, and what changed

Two adversarial critics ran against the design before a line was written. Both
independently found the same two fatal faults.

**The audience equilibrium was derived for the wrong shift.** `F* = 283g` assumes
a worker producing 24 hours a day — the Always shift the design itself forbids.
On Days the true steady state is `521g` and on Nights `750g`, which clamps almost
every worker worth housing at `MaxFollowers`. The audience factor would have been
a constant 2.20, the banked-audience loop inert, and both studios worth exactly
nothing — the precise "upgrade that buys nothing" fault the feature was meant to
repair.

Fixed by charging churn per hour of **residency** rather than per producing hour.
The equilibrium lands at 304g on Days and 312g on Nights — near shift-independent
by construction, so this class of fault cannot come back through the shift lever.
Verified: a producing tier-2 at 70 loyalty goes $237 → $273 → $317/hr as each
studio is bought, so both stay live.

**"The place is discreet" deleted the raid.** At a 0.60 heat multiplier against a
0.70 quiet fraction, the `Rooms` term cancels out of the drift entirely and every
content house at every rung was permanently cold at full capacity — for one
$70,000 purchase, repaying in 8 days. A houseful of Connected residents
reproduced it for free.

Fixed at 0.80, plus `ContentHeatReductionFloor = 0.75` as a backstop at the point
of use so gear and traits stacked together can never cross it. Repriced to
$85,000.

**The reputation trickle awarded literally zero.** `min(3, residents/4)` in
integer arithmetic pays 0/day at the entry rung's own recommended occupancy, and
at best needed ~200 game days to clear the first gate on its own ladder. Now
`residents x 0.8`, floored at 1 per occupied house, capped at 10/day, and gated
on `ReputationEnabled` like every other award site: 0 → 700 in about 136 game
days.

**Producing residents were paid twice.** `TryPayout` iterates the whole roster
with no post filter, so the same follower stock would have paid hourly through
the audience factor *and* weekly through the deposit — about $15,000 a game-day
at the top of the ladder. Content residents are now excluded, and
`WeeklyEstimate` agrees so the readout never promises money that will not arrive.

**The brothel heat loop caught content houses.** Unbranched, `DecayHeat` applied
`HouseHeatDecayPerHour` plus the laundromat on top of the content decay, making
every content house permanently cold — a quiet line of 245% of capacity at the
entry rung, 333% with the laundromat.

**The catalogue had to be paid inside the existing off-duty branch.** That block
is a fallthrough, not a guard: a content branch inserted above it takes the
off-shift resident's stamina recovery with it, and both her rate and her follower
equilibrium scale by stamina, so she would have spiralled to nothing over about
two game days.

**The condition-shut had no latch.** `IsContentShut` is a pure function of stored
wear and nothing lowers it but a repair, so the blocking notification would have
fired every game hour — roughly thirty per real hour — until the player noticed.

Also fixed: a repair call-out fee, because without one the steady-state cost is
identical whatever threshold you repair at and the optimal policy is "press the
button as often as the menu allows"; `A proper set` repriced $38,000 → $14,000,
because it could not repay in a fortnight at any rung on any shift;
`LifetimeStreetTake` now subtracts house, booking and content take, all three of
which have been reported as street income since v7; and the blip signature
carries condition, so a derelict house stops rendering as open.

## 5. What the simulation said afterwards

The one thing the critics could not check, because it depended on their own
fixes: raising the equilibrium raised income, and the base rate was never
re-derived. At `ContentBaseRatePerHour = 200` the content house beat the street
at every single tier — 92% / 169% / 121% / 196% of the same worker's night corner
per worker-day — with none of the heat and none of the incidents.

Solved to **100**, measured per worker-day (content runs 14 hours, a night corner
10) with `Traits.StreetMultiplier`'s Camera-ready 0.80 and zone saturation
applied to the street side rather than omitted:

| worker | content/day | vs her night corner |
|---|---|---|
| tier 1, Chamberlain | $977 | 46% |
| tier 2, Mirror Park flat | $2,666 | 85% |
| tier 3, Rockford Hills | $4,901 | 61% |
| tier 3 Camera-ready, Vinewood Hills | $6,341 | 98% |

The better the worker, the worse the trade — which is the sorting the whole
feature exists for. Camera-ready is the one person who belongs in there, and she
still pays 0.80 on a kerb, keeps regulars she earns nothing from, cannot be
booked, and does not draw the weekly deposit.

Marginal payback, run full with the heat bill paid and charging the cumulative
rent the prerequisite actually forces: **5 / 17 / 33 / 53 game days**. Escalating,
so every rung is a bigger commitment than the last. Total rent of $16,600/day
stays above the ~$10,600/day that seven extra benched workers would produce in
follower deposits, so buying the ladder empty purely for the roster slots never
pays for itself.

## 6. Still open

- Content residents get no anti-poach cover: `EnforcerFor(worker.ZoneId)` returns
  null for anyone off the street, while poach targeting scans the whole roster
  with no post filter.
- `Crew.RollExits` was tuned against a book of at most eight. Fifteen is
  unsimulated over a long run.
- The four door coordinates are guessed from map knowledge and have not been
  walked — the same caveat every existing anchor carries. Nothing spawns at them,
  so a wrong one misplaces a blip rather than breaking the house.
- Nothing in the mod sells a property, so a superseded rung charges rent forever.
  Deliberate — it is what prices out the buy-empty play — but it means the
  Chamberlain place is a $700/day toll once the top house is full.
