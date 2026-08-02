# Patreon launch posts

Two posts, not one. The announcement is marketing and wants to be **public** so
it can be shared and found; the download is the thing membership actually buys
and wants to be **paid members only**. Splitting them means the announcement can
travel while the file stays gated.

---

## POST 1 — Announcement (audience: **Public**)

**Title**

> On The Blade — run a business in Los Santos, not a minigame

**Body**

I've been building a GTA V mod for story mode, and it's ready for its first
players.

**On The Blade** puts you in charge of an operation instead of a mission list.
You recruit a crew off the street, post them to corners, collect every hour —
and drive out when the whole thing generates a problem. Which it will.

It's a management sim wearing a crime game's clothes. Everything stays at the
abstraction the base game already uses: money, timers, heat, fade to black. The
interesting part was never the corner. It's deciding who works it, when, and
what you do the night a rival decides it's theirs.

**What's in it**

- **Two ways to earn.** An hourly street take that draws police heat, and a safe
  weekly deposit that only builds while a worker is *off* the street. They
  compete for the same hours, so your roster is a portfolio, not a throughput
  problem.
- **Six corners, four rival crews.** Work a neutral corner to claim it, or take a
  held one by force. Rivals come back for turf you hold — and only turf you've
  actually staffed.
- **A crew with actual shapes.** Hustlers earn on the street. Camera-ready ones
  build an audience. Fragile ones break when you push them. Loyal ones never
  walk out on you.
- **Heat that bites.** Let a corner run hot and vice turn it over — everyone off
  the street, the block shut for days. Bribe it down, launder it away, or pay
  for it.
- **Somebody else's earner.** Half the people worth signing already work for
  someone. Take one and she arrives experienced, distrustful, and with her old
  crew right behind her. Every one you poach raises the temperature with *every*
  crew on the map.
- **A real way to lose.** Wages, bail and fines you can't cover become debt, and
  debt compounds. Let it run and the people you owe come collecting.

**Runs on both editions.** One build works on GTA V Legacy and Enhanced. Story
mode only — never load script mods into GTA Online.

**Where to get it**

The build is posted here for members. Join at any paid tier and the download is
in the members-only post.

Everything else — full feature breakdown, install guide, the complete config
reference — lives at **ontheblade.com**.

**What membership actually gets you**

Beyond the build itself, members get a say in what gets made next:

- **Suggest features.** Post an idea in the comments and it goes on the real
  list. Several systems already in the mod started as "what if" questions.
- **Report what breaks.** You're playing a young build on a machine I don't
  have, next to mods I don't run. Bug reports from members go straight to the
  top of the queue.
- **Shape the balance.** Every number in the mod lives in a plain config file,
  and I'd rather tune it against how people actually play than against how I
  assume they will.
- **See it early.** New systems land here before anywhere else.

**Fair warning: this is v0.2.0.** The systems are in and working, but balance and
placement are still being tuned against real play. Expect rough edges — and
expect them to move, quickly, based on what you tell me.

Tell me what you want built.

— TheGTAMod

*Tags: GTA V, GTA 5 mods, ScriptHookVDotNet, single player, management sim*

---

## POST 2 — The download (audience: **Paid members only**)

**Title**

> On The Blade v0.2.0 — download

**Body**

Here's the build. Attached below.

**You need first**

- GTA V, Legacy or Enhanced — **story mode only**
- Script Hook V for your edition
- **ScriptHookVDotNet Enhanced** 1.1.0.6 or newer — install this even on Legacy;
  the standard build won't load the mod on Enhanced
- .NET Framework 4.8 and the Visual C++ 2019 x64 redistributable

**Install**

1. Unzip.
2. Drop `OnTheBlade.dll`, `LemonUI.SHVDN3.dll` and `iFruitAddon2.dll` into your
   `scripts` folder — the one next to `GTA5.exe`. Create it if it isn't there.
3. Don't put `ScriptHookVDotNet3.dll` in `scripts` — a second copy there stops
   scripts loading. It belongs in the game root only.
4. Load story mode and press **F5**.

If you already run other mods and F5 or F1 clash with something, both keys are
rebindable in `scripts/OnTheBlade/config.json`, which the mod writes on first
run.

`INSTALL.txt` in the zip has the full guide and troubleshooting.

**Controls**

- **F5** — operations: roster, territory, upgrades, property, muscle
- **F1** — phone: status, recall everyone, fast travel
- **Insert** — reload scripts. Safe; it releases every ped the mod owns and saves
  first

**Known rough edges in v0.2.0**

- A couple of corner positions still put workers slightly off the kerb. Being
  fixed.
- Balance is tuned by simulation, not yet by enough real play. If something feels
  broken rather than hard, say so.

Tell me what breaks and what you'd want next — both go straight onto the list.

— TheGTAMod

---

## Settings checklist

| | Post 1 | Post 2 |
| --- | --- | --- |
| Audience | **Public** | **Paid members only** (or specific tiers) |
| Attachment | none | `OnTheBlade-v0.2.0.zip` |
| Purpose | reach and discovery | the thing membership buys |

**Attach the zip, not loose DLLs.** Three separate files invites someone to grab
one and miss the others, and a bare `.dll` download trips browser and antivirus
warnings far more often than a `.zip` does.

**Pin Post 1** to your page so it's the first thing a visitor reads.

**Link back both ways** — the site's two buttons point at your Patreon, so the
announcement should point at `ontheblade.com` for anyone who wants the detail
before paying.
