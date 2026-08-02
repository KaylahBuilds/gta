# On The Blade

A roster- and territory-management game mode for **GTA V single player**, built on
ScriptHookVDotNet 3.

You run a business rather than play a minigame: recruit crew, post them to zones,
collect an hourly take, and drive out to handle the problems the operation
generates. Tone follows the base game — abstracted, fade-to-black, no explicit
content.

> **Single player only.** Loading script mods into GTA Online will get the account
> banned. Do not run this with the game connected to Online.

**[Install guide](INSTALL.md)** · **[Website](docs/index.html)** — `docs/` holds a
self-contained static page covering install, controls, the full config reference
and troubleshooting. Point GitHub Pages at the `/docs` folder to publish it.

## Compatibility

One build runs on **both GTA V Legacy and GTA V Enhanced**.

That works because the mod targets
[ScriptHookVDotNet Enhanced](https://github.com/Chiheb-Bacha/ScriptHookVDotNetEnhanced)
(SHVDNE) rather than upstream SHVDN. SHVDNE is a drop-in replacement for SHVDN on
Legacy and supports both editions from the same binaries, so a script written for
Legacy runs as-is on Enhanced — with one documented caveat: *scripts that use
their own memory patterns*. This mod uses none. Everything goes through the SHVDN
API and game natives, so there are no edition-specific code paths to maintain.

Both editions use Alexander Blade's ScriptHookV as the base hook; Enhanced does
not need a different one, just the Enhanced build of it.

`Game.Version` is written to `OnTheBlade.log` on startup, because the first
question on any bug report is which edition it ran on.

> **Enhanced ships BattlEye** for GTA Online. Launch Enhanced in story mode
> without BattlEye before loading script mods.

## Build

Requires the .NET SDK on Windows. Visual Studio is *not* needed — the project
pulls `Microsoft.NETFramework.ReferenceAssemblies` so the SDK alone can build a
`net48` target.

First, put three DLLs in `lib/` — see [lib/README.md](lib/README.md) for exactly
which. The build fails with a pointer to that file if any is missing.

```bash
dotnet build OnTheBlade.csproj -c Release
```

Builds clean: **0 errors, 0 warnings**, verified against .NET SDK 8.0.423.

References are local rather than NuGet on purpose: the `ScriptHookVDotNet3` NuGet
package targets Legacy, whereas building against the SHVDNE DLL yields one
assembly that loads on both editions.

`ScriptHookVDotNet3.dll` is reference-only (`Private=false`) — the game provides
it at runtime and shipping a second copy breaks loading. `LemonUI.SHVDN3.dll` and
`iFruitAddon2.dll` must ship. `bin/Release` should contain exactly those two plus
`OnTheBlade.dll`.

## Install

Short version — the full guide with troubleshooting is in
**[INSTALL.md](INSTALL.md)**.

1. Install [ScriptHookV](http://www.dev-c.com/gtav/scripthookv/) for your edition
   and [SHVDNE](https://github.com/Chiheb-Bacha/ScriptHookVDotNetEnhanced/releases)
   into the game root.
2. Copy everything from `bin/Release/` except the `.pdb` into
   `<game root>/scripts/` — `OnTheBlade.dll`, `LemonUI.SHVDN3.dll` and
   `iFruitAddon2.dll`.
3. Launch, load a single-player save, press **F5**.

On first run the mod creates `<game root>/scripts/OnTheBlade/` containing
`config.json`, `save.json` and `OnTheBlade.log`. Paths resolve off the game
root, so Legacy and Enhanced installs keep independent saves and balance configs
without any extra work.

## Controls

| Key | Action |
| --- | --- |
| `F5` | Open/close the operations menu (rebindable in `config.json`) |
| `F1` | Open the phone menu — also reachable via the **On The Blade** contact in the in-game phone |
| `Insert` | SHVDN script reload — safe, releases all owned peds first |

## How it works

```
src/
  Main.cs                    Script lifecycle: tick dispatch, keybind, cleanup
  Core/
    Config.cs                Tunables, serialised to config.json
    Notify.cs                Single point of contact with the notification feed
    WorkerData.cs            Authoritative crew record (never holds an entity handle)
    Zones.cs                 Static zone table + post-position maths
    Regions.cs               Zone groupings + stash houses
    RecruitAreas.cs          Hotspot zone codes + per-area tier weighting
    RivalCrew.cs             Rival crews + the opening territory board
    Upgrades.cs              One-time global purchases
    EnforcerData.cs          Hired muscle (abstract — no ped, no AI)
    GameState.cs             Roster, territory, upgrades — the save payload
  Runtime/
    WorkerRuntime.cs         Disposable in-world view of a WorkerData
    SpawnManager.cs          Distance-based ped streaming
    ProspectSpotter.cs       Blips prospects while you stand in a hotspot
    Factions.cs              Shared relationship groups
  Systems/
    EconomyTick.cs           Hourly resolve: payout, stamina, loyalty, heat
    Subscriptions.cs         Follower accrual + the weekly deposit
    Recruiter.cs             Nearby ped -> roster record
    Poaching.cs              Claim rolls + the tension they create
    IncidentRoller.cs        Hourly roll for a problem, priority-ordered
    MissionController.cs     Runs one incident at a time, handles cleanup
    Incidents/
      Incident.cs            Base: blip, countdown, resolve, guaranteed cleanup
      BadClientIncident.cs   Hostile client — combat resolution
      ViceStingIncident.cs   Undercover cop — pull them off the post in time
      WalkOffIncident.cs     Loyalty bottomed out — pay up or lose them
      TurfBattleIncident.cs  Zone-scoped fight, both attacking and defending
      RetaliationIncident.cs Her old crew turns up where you signed her
  Persistence/
    SaveManager.cs           Atomic JSON via DataContractJsonSerializer (no Newtonsoft)
  UI/
    UiRoot.cs                LemonUI menus — operations, roster, territory
    UiRoot.Business.cs       Same class: upgrades, property, muscle, phone
    PhoneBridge.cs           iFruitAddon2 contact, isolated and lazily loaded
```

**The one design rule:** workers are *data*, peds are a *view*. A real `Ped` is
only instantiated within `SpawnRadius` (150m) and deleted beyond `DespawnRadius`
(220m); everything else is simulated numerically. Holding 20+ persistent peds
tanks framerate and loses a permanent fight with population culling.

### Economy

Two revenue streams that compete for the same worker-hours.

**Street take** — resolved once per in-game hour (~2 real minutes at default
timescale):

```
income = baseRate[tier] x zone.demand x (loyalty/100) x (1 - heat)
         x (stamina/100) x nightBonus x ownedZoneBonus
         x saturation x traits x vehicle
```

Stamina drains on shift and recovers off duty. Exhausted workers bleed loyalty,
and loyalty scales payout — so over-working the roster is self-defeating. Heat
rises per worker per hour and decays everywhere, including zones you have pulled
out of.

**Saturation.** Each worker's yield on a corner is divided by
`1 + others × 0.35`, counting only crew actually out that hour. Without it,
stacking the roster on the highest-demand zone was strictly optimal and territory
was decoration. With six workers:

| | Stack the best zones | One per zone |
| --- | ---: | ---: |
| Without saturation | **10.65** | 8.20 |
| With saturation | 7.53 | **8.20** |

Stacking used to win by 30%; spreading now wins by 9%. Holding ground is the
point again.

### Shifts

Assignment (`ZoneId`) is persistent; the **shift** decides which hours it is
actually worked — `Always`, `Days` (06–20), `Nights` (20–06) or `Off`.

This exists because the balance simulation found night-split to be the optimal
strategy and the game then had no way to express it: you had to hand-toggle every
worker on and off twice a game day. Now it is one decision per worker. Off-shift
hours build followers and stamina, so a shift is a real allocation between the
two revenue streams rather than an on/off switch.

`Always` is deliberately enum value 0 so saves written before shifts existed
deserialise to the old behaviour instead of benching the whole roster.

### Traits

Zero to two per worker, rolled on recruit. Every one is a trade — nothing here is
strictly good.

| Trait | Effect |
| --- | --- |
| Hustler | Street ×1.25, follower growth ×0.6 |
| Camera-ready | Follower growth ×1.5, street ×0.8 |
| Connected | Zone heat gain ×0.6 |
| Fragile | Loyalty drain when exhausted ×1.75 |
| Loyal | Never walks off; loyalty drain ×0.5 |
| Magnetic | Street ×1.15, but bad-client odds ×1.4 |

Before these, the roster was a spreadsheet — everyone on the same continuous axes,
differing only in magnitude. Traits give each worker a shape, which is what makes
assignment a puzzle and what makes poaching interesting: you are stealing a
specific person, not a tier number.

**Subscription deposits** — a weekly payout per worker, unlocked by the *Ring
light and a laptop* upgrade:

```
followers += gainPerHour x (stamina/100) x (loyalty/100) x tierAppeal   // off duty
followers -= decayPerHour                                              // on the street
deposit    = followers x revenuePerFollower x (loyalty/100)            // every 7 game days
```

This is the safe stream: it cannot fail, generates no heat and never triggers an
incident. It is paid for in worker-hours *not* spent earning on the street —
followers only build while a worker is off it, and decay while they are on it.

That is the whole point. The roster stops being a throughput problem and becomes
an allocation one: who earns now and hot, who earns later and safe. It reinforces
the stamina tension rather than bypassing it, because a worker parked off duty to
build an audience is also resting, and a rested worker builds faster.

Tuned by simulating 12 game weeks for a tier-2 worker at 70 loyalty:

| Strategy | Street / wk | Subscriptions / wk | Total / wk |
| --- | ---: | ---: | ---: |
| Street whenever rested | $16,937 | $1,784 | $18,721 |
| Never on the street | $0 | $7,970 | $7,970 |
| Night shift, rest by day | $14,737 | $5,611 | **$20,348** |

The mixed strategy wins, which is the result the design wants. Street still pays
more per hour; subscriptions pay less but carry no risk at all.

All coefficients live in `config.json`; nothing needs a rebuild to rebalance.

### Incidents

One roll per economy tick, one active incident at a time, then a cooldown. The
cap is deliberate — two blipped emergencies on opposite sides of the map is not a
choice, it is a guaranteed failure.

| Incident | Trigger | Resolution | Cost of failing |
| --- | --- | --- | --- |
| Walk-off | Loyalty below 25 | Reach them within 8m with the retention fee | Off the roster permanently |
| Vice sting | `zoneHeat × 0.25` | Reach the post within 18m before the timer | Bail, +0.25 zone heat, −15 loyalty |
| Bad client | Flat 8% per worker | Put the client down, then reach the worker | −22 loyalty, −35 stamina, pulled off post |

Priority is walk-off → turf defence → sting → bad client. A walk-off outranks
everything because it is the only incident the player caused directly and the
only one that costs a crew member for good; a turf defence outranks the rest
because losing a zone costs the most.

Incidents may fire while the player is across the map. That state is valid: the
blip and countdown run, and antagonist peds are only created once the worker
actually streams in. Every incident owns its blip and peds and cleans them up on
success, failure, *and* script reload — `MissionController.Abort()` also un-sticks
the `InTrouble` flag so it never reaches the save file.

### Recruiting

Eligibility is a **curated model list**, matched by hash so no hash-to-name
lookup is ever needed. Keeping the pool deliberate is what makes the hotspots
below matter: eligible peds are uncommon enough that where you look is a real
decision.

The list lives in `config.json` as `RecruitModels`, so it can be widened without
a rebuild:

```
s_f_y_hooker_01, s_f_y_hooker_02, s_f_y_hooker_03,
a_f_y_hipster_02, a_f_y_beachvesp_01,
a_f_y_vinewood_01, a_f_y_vinewood_04
```

The remaining exclusions are practical, not editorial: peds in vehicles (deleting
one strands the car), peds another script or mission already owns
(`IsPersistent`), and crew already on the books.

Two kinds of hotspot make recruiting easy:

| Area | Search radius | Tier roll (1 / 2 / 3) |
| --- | ---: | --- |
| Gang turf | 70m | 70% / 25% / 5% |
| Vinewood | 70m | 25% / 45% / 30% |
| Anywhere else | 35m | 60% / 30% / 10% |

Gang turf turns up volume at the lower tiers; Vinewood turns up fewer people but
a better class of them.

Standing in a hotspot also **blips nearby prospects** — the mod cannot make the
game spawn more people, so instead it points at the ones already there. Ordinary
areas get no blips, which is what keeps a hotspot feeling like one.

Crew and prospects both blip **pink**, told apart by size and label: crew are the
larger blip named for the worker and their post, prospects are smaller and just
say "Prospect". Both colours are `config.json` settings
(`WorkerBlipColourName`, `ProspectBlipColourName`) and accept any
`GTA.BlipColor` name, falling back to pink if the name is not recognised.

#### Taking someone else's earner

Some prospects already work for somebody — 45% on gang turf, 30% around
Vinewood, 12% anywhere else. Signing one is the cheapest way to get an
experienced earner and the fastest way to start a war.

| | Effect |
| --- | --- |
| She comes in a tier higher | She already knows the job |
| Loyalty −15 | She has been through something and doesn't trust you yet |
| Her crew: aggression +0.15, strength +5 | They pull people in |
| Every other crew: aggression +0.04 | Word travels — a pimp who poaches is everyone's problem |
| 55% chance | They arrive within the minute (`RetaliationIncident`) |

The retaliation anchors to **where the deal was struck**, not to her post — a
worker signed seconds ago hasn't got one, and "they came to find you" lands
harder than a blip across the map. Clear them and her crew backs off (strength
−10, aggression −0.05) and she sees you show up (loyalty +15). Fail and they take
her back permanently.

Rival aggression feeds the existing turf-contest roll, so poaching is the lever
that turns the map hostile. Modelled against that roller: with no poaching a
contest lands roughly every 9 real minutes, rising to about every 5.6 after ten
poaches. It cannot spiral, because aggression clamps at 1.

Once every rival is broken there is nobody left to poach from, so late-game
recruiting quietly becomes safe. That falls out of the design rather than being
special-cased.

> Hotspots are matched on the game's own zone codes (`GET_NAME_OF_ZONE`), not
> coordinates, so boundaries follow the map. The code lists in `RecruitAreas.cs`
> are from memory and **need verifying in-game** — the recruit menu prints the
> code you are currently standing in for exactly that reason. An unrecognised
> code falls through to "anywhere else", so a wrong entry costs a hotspot rather
> than crashing.

### Territory

Every zone is held by you, a rival crew, or nobody.

| State | Post workers? | Payout | Rival pressure |
| --- | --- | --- | --- |
| Neutral | Yes — and doing so claims it | Base | None |
| Yours | Yes | ×1.15 | Contested while staffed |
| Rival-held | No — take it first | — | — |

Working a neutral corner claims it. That is the only way to gain turf without a
fight, and it is what switches on both the ownership bonus and rival attention —
without it the two starter zones stay inert and the game never begins.

Four crews, each with **strength** (0–100) and **aggression** (0–1) and nothing
else. A rival that needs more state than that is a rival the player cannot reason
about. Strength sets how many enforcers they field (2 at the floor, 5 at full)
and drops when you beat them; at zero they stop contesting for good.

The opening board leaves the two tier-1 coastal zones neutral so there is
somewhere to operate before you can fight anyone. Everything above that has to be
taken — Vinewood Boulevard, the ×2.2 demand zone, belongs to the Lost MC at 85
strength.

Rivals only attack turf you hold *and have staffed*. Being attacked over an empty
corner you happen to own reads as noise rather than pressure.

`TurfBattleIncident` covers both directions. Attacking and defending differ only
in what happens at the end, so two classes would duplicate the spawn and combat
handling for nothing. Failing an attack costs you only the attempt; failing a
defence loses the zone and puts everyone posted there back on the bench.

### Upgrades, property and muscle

Four one-time **upgrades**: two roster expansions (4 → 8 slots), a legal retainer
(bail halved, stings a quarter less frequent), and laundering (heat decays half
again as fast). Effects are read by querying ownership at the point of use rather
than being baked into stored numbers, so a save never carries a stale bonus.

**Stash houses** are bought per region rather than per zone — buying six of
everything is bookkeeping, not a decision. One covers its region's zones with
double heat decay, gives the whole roster better off-duty stamina recovery, and
serves as a fast-travel point. Fast travel moves your vehicle with you and
refuses to fire mid-incident.

**Enforcers** are deliberately abstract: no ped, no pathing, no AI. An enforcer
is a probability that a routine problem never reaches you, which is the whole
point — late game should stop being a queue of errands. They cover bad clients
only, never stings, walk-offs or turf. If muscle could handle those there would
be no game left. A failed roll still falls through to a real incident, so they
buy convenience rather than immunity. Wages are charged at midnight; miss the
bill and they all walk.

### Phone contact

The phone menu is on `F1` and also registers a contact named **On The Blade** in the
in-game phone via [iFruitAddon2](https://github.com/Bob74/iFruitAddon2) (3.0.2+,
the version where one file covers both editions). It offers a status report,
"everyone in", and fast travel to any stash house you own.

Every reference to iFruitAddon2 is confined to `PhoneBridge`, in two `NoInlining`
methods called only from inside a `try`/`catch`. The CLR loads an assembly when
it JITs a method that needs it, so a missing or incompatible `iFruitAddon2.dll`
disables the phone contact and logs why, instead of stopping the mod from
loading. That matters more than usual here: one binary serves two game editions,
and a third-party dependency is the likeliest thing to break on one of them after
a patch.

### Heat has a ceiling: raids

Past 95% heat a staffed zone gets **turned over** — everyone off the street, the
corner locked for 3 game days, heat reset to 50%, and a $5,000 fine. Heat used to
be a soft tax with no terminal state; this is what makes laundering and stash
houses read as insurance rather than nice-to-haves.

**Bribes** are the counter-play: pay from the Territory menu to dump half a point
of heat. Cost scales with how hot the corner actually is, so it is cheap early and
expensive exactly when you need it. Bribes must be paid in cash — nobody takes an
IOU.

### Debt, and the only way to lose

Money used to be one-directional: income minus voluntary spending, with bail and
wages silently waived when you couldn't afford them. Nothing could ever end a run.

Now every outgoing cost routes through one `Charge` path. Whatever you can't cover
becomes **debt**, which accrues 8% a game day. At $150,000 the people you owe come
collecting: the roster empties, every zone is given up, enforcers walk, and the
debt clears.

It is a setback rather than a game over — property and upgrades survive, and the
save is intact. Deleting someone's progress outright is a worse outcome than
making them rebuild the street operation.

### Vehicles

One per region, $28,000. Demand +10% and 25% less stamina drain in that region —
they stop walking the whole shift.

### Milestones

Ten goals from *Someone to look after* (sign your first worker) to *Nobody left to
fight* (break every rival crew), each paying out once. Every system in the mod
generated numbers, but nothing named the arc. Viewable from the phone menu.

### Save versioning

`GameState.SaveVersion` with ordered, idempotent migration steps, so a save can
jump several versions at once. The shape has already changed four times; without
this a reshaped save fails half-populated and silently rather than loudly.

## Status

Phases 1–5 are built. Full loop: recruit → assign → claim → stream → earn →
incident → turf war → reinvest → save/load.

## Known rough edges

- Zone anchors in `Zones.cs` and stash positions in `Regions.cs` are approximate.
  Each needs verifying in-game — kerb positions for the former, somewhere you can
  actually arrive by car for the latter.
- The crew-detail post list shows rival-held zones as selectable and rejects them
  on activation, rather than hiding them. Filtering would break the display-name
  lookup that maps a selection back to a zone.
- `DataContractJsonSerializer` writes `Dictionary<string,float>` as an array of
  key/value pairs. It round-trips correctly but `save.json` is ugly to hand-edit.
- Recruitment matches a fixed model list; peds outside it are ignored.
- Antagonist models (`g_m_y_lost_01`, `a_m_y_business_01`) are hardcoded in the
  incident classes. `Incident.SpawnAntagonist` returns null on an invalid model
  and the incident retries next tick, so a bad name degrades to "nothing spawns"
  rather than a crash — but check them in-game.
- **It compiles and loads, but has not been run in-game.** The build is clean and
  the assembly satisfies SHVDN's script contract, but no behaviour has been
  observed yet. Balance, positions and mission pacing are all unplayed.
- LemonUI on Enhanced is reported working but the evidence is anecdotal (menus
  render in existing mods). If the UI misbehaves there, it is the most likely
  culprit — `UiRoot` is the only pair of files that touches LemonUI, so swapping
  the menu layer would not reach any game logic.
- SHVDNE pins memory offsets per game build and has previously shipped
  point releases to chase Enhanced patches. If the mod stops loading after a game
  update, check for a newer SHVDNE before debugging anything here.
