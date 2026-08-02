# Validating On The Blade

The mod compiles clean and its assembly satisfies SHVDN's script contract, but
nothing in it has been observed running. This is the order to find that out in,
cheapest checks first — each stage unblocks the next, so don't skip ahead.

## Your setup

Detected on this machine:

| | |
| --- | --- |
| Game | GTA V **Legacy**, `GTA5.exe` 1.0.3889.0, Steam |
| Path | `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V` |
| Loader | **SHVDNE 1.1.0.6** — exactly what the mod was built against |
| LemonUI | 2.2.0.0 installed, 2.2.0.0 referenced — match |
| iFruitAddon2 | **2.1.1.0 installed, 3.1.1.0 referenced — mismatch** |

Two things about that install matter more than anything else:

**Keybinds were already taken.** F3 is Simple Trainer, F4 the SHVDN console, F7
PSRP's radical menu, F8 Menyoo. Defaults are now **F5** (operations) and **F1**
(phone). F12 was left alone because Steam uses it for screenshots.

**Do not overwrite `iFruitAddon2.dll`.** Other mods here use 2.1.1 and dropping
3.1.1 on top may break them. `EnablePhoneContact` is off in the test config, which
means the assembly is never loaded at all — the `F1` menu still works. This is
what `PhoneBridge`'s lazy-load guard was built for; if you do leave the contact
enabled, expect it to disable itself and log why rather than take the mod down.

## Stage 0 — Deploy

Close the game first. Only one file needs copying; LemonUI and iFruitAddon2 are
already present and correct.

```bash
cp "/c/Users/kayla/OneDrive/Documents/SECRES/gta5-on-the-blade/bin/Release/OnTheBlade.dll" "/c/Program Files (x86)/Steam/steamapps/common/Grand Theft Auto V/scripts/"
```

Then drop the accelerated config in, so the slow systems are observable:

```bash
mkdir -p "/c/Program Files (x86)/Steam/steamapps/common/Grand Theft Auto V/scripts/OnTheBlade" && cp "/c/Users/kayla/OneDrive/Documents/SECRES/gta5-on-the-blade/docs/config.test.json" "/c/Program Files (x86)/Steam/steamapps/common/Grand Theft Auto V/scripts/OnTheBlade/config.json"
```

## Stage 1 — Does it load?

Start the game, load a story-mode save, then check the loader log before touching
anything:

```
<game root>/ScriptHookVDotNet.log
```

Look for `Found 1 script(s) in OnTheBlade.dll`. If it isn't there the mod never
loaded and nothing below matters.

Then check the mod's own log:

```
<game root>/scripts/OnTheBlade/OnTheBlade.log
```

It records the game file version at startup. You should also see a
"On The Blade loaded" ticker in-game.

**Expected failure mode here:** a name collision. SHVDN refuses to load two
scripts with the same class name — that is exactly what the existing
`ghstha.ghstha` warnings in your log are. If you see one naming `OnTheBlade`,
that is a real conflict with another mod, not a bug in this one.

## Stage 2 — Menus open, config round-trips

1. Press **F5**. The operations menu should appear.
2. Walk through every submenu: Roster, Territory, Upgrades, Property, Muscle.
   You are checking they *render*, not that they do anything yet.
3. Press **F1**. The phone menu should appear.
4. Press **Insert** to reload scripts. Nothing should be left behind, and the
   mod should re-announce itself.

If menus do not render, LemonUI is the suspect — it is the only external UI
dependency, and `UiRoot` is the only pair of files that touches it.

## Stage 3 — Recruiting

This is the first system that can genuinely fail, because it depends on which
peds the game happens to spawn.

The test config widens `RecruitModels` from the shipped 31 to the same 31
ambient ones and pushes the search radius to 60m (100m in a hotspot), so
prospects should be findable anywhere. Open the menu and read the **recruit item
description** — it prints:

- the area type (Gang turf / Vinewood / Nowhere in particular)
- **the raw zone code you are standing in**, e.g. `[WVINE]`
- the search radius and a live count of prospects nearby

Walk around Davis, Strawberry, Chamberlain Hills, then Vinewood and Hawick, and
**write down the codes**. The hotspot lists in `src/Core/RecruitAreas.cs` are from
memory and this is how you correct them. An unrecognised code just falls through
to "Nowhere in particular" — nothing breaks.

Also check: prospects should be blipped **pink** while you stand in a hotspot,
and not blipped at all outside one.

Recruit someone. With the test config every prospect is claimed and every claim
retaliates, so you should immediately get:

- "She was working for X" ticker
- a red blip where you are standing
- 2–4 hostile peds arriving

Kill them and confirm the incident resolves and cleans up its bodies.

## Stage 4 — Posting and streaming

Assign a worker to a zone, then drive away and back.

- She should appear at her post and stand there in a prostitute scenario.
- Past ~220m she should despawn; inside ~150m she should reappear **at the same
  spot** (post positions are deterministic).
- Her blip should be pink and named for her and the zone.

**The zone anchors in `src/Core/Zones.cs` are guessed coordinates.** This stage is
where you find out whether she is standing on a kerb or in the middle of a road.
Note anything wrong; the fix is a coordinate edit, no rebuild needed for the rest.

## Stage 5 — Economy

Wait ~2 real minutes for a game hour to tick.

- A green `+$N Street take (HH:00)` ticker.
- Player money actually increases.
- Stamina falls on shift and recovers off duty (check the roster menu).

## Stage 6 — Incidents

The test config makes these fire aggressively: bad clients at 60% per worker per
hour, walk-offs at 90%, stings scaled straight off heat, cooldown down to 15s.

Work through each and confirm it starts, blips, resolves both ways, and cleans up:

| Incident | How to force it |
| --- | --- |
| Bad client | Just wait with someone posted |
| Vice sting | Let heat build — keep several workers on one zone |
| Walk-off | Overwork someone until loyalty drops below 25 |
| Turf battle (attack) | Territory menu → activate a rival-held zone |
| Turf defence | Own and staff a zone, then wait |
| Retaliation | Recruit anyone (guaranteed in the test config) |

The thing to watch on all of them: **press Insert mid-incident**. Everything the
incident spawned should vanish and nobody should be left stuck "in trouble".

## Stage 7 — The slow systems

Two things cannot be observed in a short session even accelerated:

- **Subscription deposits** — 1 game day in the test config is still ~48 real
  minutes.
- **Enforcer wages** — charged at midnight.

You have Menyoo and Simple Trainer installed; use either to **advance the game
clock** past midnight. The economy tick fires on hour change and the payout
anchor is an ordinal date, so skipping days works correctly — a skip of 7+ days
should produce one deposit, not seven.

Buy the *Ring light and a laptop* upgrade first, or there is no stream to pay out.
The test config sets follower gain absurdly high (2000/hr) so a worker banks a
visible audience within a couple of game hours.

## Stage 8 — Shifts, traits and saturation

**Traits** are rolled on recruit and shown in the worker detail menu. Sign four or
five people and confirm you see variety — roughly a third should have none, and
you should occasionally draw two.

**Shifts** are the row under Post. Set someone to `Nights` during the day: she
should leave her corner within a couple of seconds (the menu forces a re-stream).
Set her to `Always` and she should come straight back. Off-shift hours build
followers and stamina instead of money.

**Saturation** is the one worth measuring. Post one worker on a zone and note the
hourly ticker. Add a second to the same zone and the *total* for that corner should
rise by well under double. The maths, at the default 0.35 falloff:

| Workers on one zone | Total yield |
| ---: | ---: |
| 1 | 1.00 |
| 2 | 1.48 |
| 3 | 1.77 |
| 4 | 1.95 |

Spreading four workers across four zones should out-earn stacking them on the
best one. If it doesn't, saturation isn't firing.

## Stage 9 — Raids, bribes and debt

The test config sets `RaidHeatThreshold` to 0.35 and nearly stops heat decay, so a
raid is reachable in a few game hours rather than never.

**Raid:** put two or three workers on one zone and leave them. Watch heat climb in
the Territory menu. At 35% the corner should be turned over — everyone off the
street, a fine, and the zone showing `Raided 1d` and refusing new assignments.

**Bribe:** before it raids, activate an owned or neutral zone in the Territory menu
to pay heat down. Cheap in the test config (`BribeCostPerHeat` 2000). Confirm the
cost scales with how hot the corner actually is, and that it's refused when you
don't have the cash — bribes never go on credit.

**Debt and collapse** — do this last, or on a save you don't mind losing.

`EnforcerDailyWage` is 60,000 in the test config specifically so this is
reachable. Hire all four enforcers ($1,000 each here), spend your cash down to
near zero, then skip past midnight. The shortfall becomes debt, interest is 50% a
day, and the operation folds at $120,000 — roster emptied, every zone given up,
enforcers gone. Property and upgrades survive and the save is intact.

Check on the way through that the *Pay down what you owe* item appears in the
Upgrades menu, and that bail from a failed vice sting also becomes debt when you
can't cover it.

**Milestones** are on the phone menu under *Where this is going*. With the boosted
tier rates most of the early ones should land within a game day.

## Stage 10 — Interference

You are running ~30 other scripts, several in the same subject area
(`The pimp game`, `OnTheBlock`, `The gangs`). Worth specifically checking:

- Does anything else delete or claim the same ambient peds?
- Do two mods fight over the same worker ped? `SpawnManager` marks its peds
  persistent, and `Recruiter` skips `IsPersistent` peds so it will not steal
  another mod's — but the reverse is not guaranteed.
- Framerate with a full roster streamed in.

If something behaves oddly, the fastest triage is to move the other pimp-adjacent
DLLs out of `scripts/` temporarily and retest in isolation.

## When you are done

Copy the real `config.json` back (delete the test one and let the mod regenerate
defaults), and record anything you corrected — zone codes, anchors, model names —
so it goes back into the source rather than living only in your install.
