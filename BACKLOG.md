# Backlog

Things that have been thought through and deliberately not built yet.

Nothing here is promised. It is a list of what has been considered, so that an
idea does not have to be re-argued from scratch every time it comes up — and so
that anyone suggesting something can see whether it is already on the pile.

**Suggest something:** open an issue at
[github.com/KaylahBuilds/gta/issues](https://github.com/KaylahBuilds/gta/issues),
or leave it on the [Patreon](https://www.patreon.com/gtaontheblade) or under a
video. Several systems in the mod started as a comment.

Last reviewed: 3 August 2026, after v0.3.0.

---

## Next up

Roughly in the order it would get done.

- **Play v0.3.0 properly.** Eighteen features landed in one release and were
  verified by simulation and a short session. The bugs simulation cannot catch —
  menus, blips, ped spawning, incidents colliding — need real hours.
- **Rebalance the recruit tier roll.** Recruiting in Vinewood rolls tier 3 at 30%
  and tier 2 at 45%, which was fine when tier never moved. Now that it can be
  earned and bought, starting most of the crew near the ceiling leaves the
  progression system with nowhere to go. Needs care: it changes an existing save's
  difficulty.
- **Verify the remaining coordinates.** Mirror Park, Del Perro and Vinewood corner
  anchors have never been walked in-game, and neither have the three house doors.
  A wrong house door misplaces a map pin rather than breaking anything; a wrong
  corner anchor puts people in the road. The Diagnostics menu exists for this.

## Systems

### Upgrades

- **Per-corner upgrades.** Everything you can buy is global. A lookout, a paid-off
  patrol, a room upstairs, or regulars bought for *one* corner would make holding
  ground an investment instead of a flat ownership bonus. The state shape already
  supports it.
- **Second tiers.** Ring light to a studio, burner network to a dispatch. Cheap to
  build, obvious progression.
- **Upgrades that cost something.** Everything purchasable is currently a strict
  improvement, so the only question is order. Wire the block (big heat reduction,
  rivals contest you more because you are visible), the good product (better take,
  far more stings), franchise (recruits arrive at tier 2 with less loyalty).

### Muscle

- **Backgrounds.** Enforcers have one axis where workers have six traits. Ex-cop
  (heat decays faster, poor at turf), leg-breaker (strong deterrence, generates
  heat), fixer (cheap bail, no use in a fight), local (cheap, low ceiling, better
  recruiting), veteran (starts high, expensive, never improves).
- **Skill growth.** `Handled` increments on every enforcer and is now shown, but
  still does not feed back into anything. Skill should climb with it, capped by
  background — a cheap bad roll becomes a project instead of a mistake.
- **Wage scaling.** Wage is flat regardless of skill, so a good roll is strictly
  better than a bad one at the same price. Scaling it makes "hire cheap and train"
  compete with "buy a veteran".
- **Bodyguard duty.** Assign an enforcer to a *worker* instead of a region. She
  takes far less trouble; the rest of the region goes uncovered.

### The roster

- **Spend followers.** They cap and then produce nothing. Letting them be spent
  for an instant payout turns the ceiling into a resource rather than a dead end.
- **Loyalty above 85 sends someone your way.** Referrals exist, but only through
  the exit flow. High loyalty should be worth pushing toward, not just worth
  defending.
- **A late window.** Nights pay 1.6x from 20:00. A 02:00–05:00 band paying more
  still and roughly doubling incident odds would give the shift system a third
  setting with teeth.

## Known issues

- **Balance is simulated, not played.** Every number in v0.3.0 was checked by
  arithmetic against the payout formula before shipping, and that caught real
  faults — but simulation cannot tell you whether something *feels* right.
- **`RaidHeatThreshold` interacts oddly with pricing.** Cut price raises heat 40%,
  which on an already-hot corner shortens the time to a raid considerably. Worth
  watching whether that reads as a trade or as a trap.
- **The war contest rate is the most aggressive number in the mod.** Roughly one
  contest every four game hours. It self-terminates, and there is a fourteen-day
  ceiling so a losing player is not stuck — but it may still be too much.

## Bigger, and less likely

- **Extract the framework.** Roughly eleven of the mod's systems — saves,
  migration, config, incidents, streaming, zones, the economy loop, reputation,
  factions — have nothing to do with the theme. Extracting them would make a
  second mod cheap. Worth doing *while* porting to a second use, never before.
- **A protection company.** The highest code overlap of any spin-off: guards are
  enforcers, client sites are zones, risk is heat, every incident type already
  exists. Flip the polarity — you are paid to stop trouble rather than causing it.
- **A shared backbone.** One bank balance, one street reputation, one heat model
  across several mods, so they feel like one world. Only realistic across mods
  with the same author.

## Site and release

- **The YouTube description is written against v0.2.0** and undersells the current
  build badly — no houses, no clients, no law.
- **The landing page hero still has a footage placeholder** instead of a video.
- **The site fetches React and Babel from unpkg at runtime.** If unpkg is down or
  blocked, every page renders blank. Vendoring both would remove the only
  third-party dependency the site has.

## Done

Kept so that suggestions already built are visible.

- Tier progression, and per-worker investment — v0.3.0
- Armed muscle, and muscle that turns up in person — v0.3.0
- An indoor operation — v0.3.0
- Regulars, bookings, connected clients, screening — v0.3.0
- Managers, mentoring, burnout and leaving — v0.3.0
- A police retainer, an informant, custody — v0.3.0
- Crews poaching from you, war, alliances — v0.3.0
- Borrowing, per-corner pricing, collectors — v0.3.0
- Protection deals, street reputation, demand events — v0.2.0
- Territory on the map, worker traits, shifts, saturation — v0.2.0
