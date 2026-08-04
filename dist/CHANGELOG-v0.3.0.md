# On The Blade — v0.3.0

The biggest release so far. Eighteen features across six systems, and the short
version is that the mod stops being only about corners.

You can get off the street entirely. Clients stop being a coin flip between
nothing and a fight. Your crew can be promoted, invested in, and can ask to
leave. The police become people rather than a number. Rival crews do things back
to you. And money can finally be borrowed as well as lost.

Existing saves carry forward — the save format went from version 4 to 12 and
migrates automatically, one step at a time, logging each one.

---

## Indoors

A house is what the street pays for.

- **Three properties** — the Strawberry walk-up ($120,000, 2 rooms), the Del
  Perro parlour ($260,000, 3 rooms) and the Vinewood house ($520,000, 4 rooms).
  The last two need reputation before anyone will hand you the keys.
- Indoor work pays **2x to 3x** the street rate and generates almost no heat.
- **Rooms are a hard ceiling.** No crowding curve, no diminishing returns — when
  the rooms are full, they are full.
- **No night bonus.** The street pays 1.6x after dark and a house pays the same
  around the clock, so the same worker is worth more indoors by day and more on a
  corner by night.
- **Rent is due whether the rooms were used or not**, and routes through the same
  system as every other bill — an idle house becomes debt.
- Workers posted indoors are never spawned as peds. They are inside.

**A house is a bigger target than a corner.** Run every room flat out and it goes
over in about four game days: everyone out, five days shut, a $25,000 fine,
eighteen loyalty off everyone who was in it, and the rent still running. Hold one
room back and it stays quiet almost indefinitely.

## Clients

Until now exactly one client was modelled: the one who turns violent.

- **Regulars.** Clients who come back for her specifically, paying on top of
  every hour she works. They build faster at higher tier and fall away if you
  bench her. The only income in the mod that rewards leaving somebody in one
  place instead of chasing demand.
- **Bookings.** Somebody asks for a named worker. She is out four to nine hours
  earning nothing else, and it pays roughly 2.3x what those hours were worth on
  the street — if it goes well. Tier, traits and loyalty decide who gets asked
  for.
- **Connected clients.** A vice lieutenant, somebody's lawyer, a producer. Take
  the money, or take the favour: heat off every corner you hold, seven days of
  peace with whoever hates you most, or six thousand followers and a name. Each
  is worth far more than the cash in the situation it fixes and nothing
  otherwise.
- **Somebody who checks** — a new $11,000 upgrade. Without it a booking's risk
  reads *unknown*. With it you get the number before you say yes.

Offers arrive at the top of the phone menu and expire in six hours.

## The crew

The roster had two endings: walk off at low loyalty, or get released. Nothing in
between.

- **Managers.** A tier-3 worker can run a corner instead of working it. She earns
  nothing; everyone else there earns up to 75% more and draws 35% less heat, both
  scaled by her own loyalty. Tier 3 finally has somewhere to go.
- **Mentoring.** A junior working alongside somebody of higher tier gains double
  experience — tier 1 to 2 drops from 30 hours to 15. The senior gains nothing,
  so who you post *together* is a decision.
- **Burnout.** Hours are now tracked across her whole time with you and never
  reset by a promotion. Past four hundred she may ask to get out. Anyone can also
  want out for reasons that have nothing to do with you.
- **Three answers, none of them free.** Let her go on good terms and it builds
  your name, with a fair chance she sends somebody your way. Pay to talk her
  round and it buys time, not a change of mind. Refuse and she stays, thirty
  loyalty down. Ignore it for three days and she walks anyway.

## The law

Heat used to have exactly two levers, both of them menu buttons.

- **A man at the station.** $2,500 a day, bought a week at a time. A corner about
  to be raided becomes a phone call instead — six hours of notice naming the
  zone. He warns; he does not cancel. Let the arrangement lapse and he takes your
  hottest corner straight to the top of somebody's list, and charges 35% more
  every time you want him back.
- **Somebody talking.** A worker below 55 loyalty starts helping the police, and
  the corner she works runs 1.6x hot. You are told the moment it starts, but not
  who. Fourteen thousand has everyone checked and names her. Guess instead, and
  being wrong costs you her anyway, twenty loyalty across the whole roster, and
  your reputation.
- **Court, not a turnstile.** Without the bail fund a vice bust now means two to
  six days in custody depending on how hot the corner was, plus a fine. She earns
  nothing and cannot be posted while she is inside. A lawyer buys the remaining
  days, and she knows who paid.

The **bail fund** and **legal retainer** upgrades both got sharper as a result:
the fund means she never sees a cell, and the retainer halves the fine, the
lawyer and the sweep, and takes a day off the sentence.

## The crews

The relationship ran one way in every respect. It doesn't now.

- **They take one of yours.** Rolled daily and weighted toward whichever crew you
  have been provoking. At 70 loyalty she refuses outright and gains ten for
  telling you about it; below that it scales to certain at zero. Armed muscle on
  her ground gets a chance to move them on first. **Loyalty is now a defence.**
- **War.** Aggression used to top out at 1 and mean "contests slightly more
  often". At 85% a crew stops testing you: contests 2.2x as often, two extra
  bodies per fight, and they will not take the ordinary price for peace. It ends
  when you beat them under eighteen strength, when you buy out at triple, or when
  it burns itself out after a fortnight.
- **Alliances.** With four hundred reputation a crew can be brought all the way
  over rather than merely bought off. They stop coming for you and send three
  people to fight beside you when you go and take a corner off somebody else.

## Money

- **A loan shark.** Debt only ever happened *to* you. Now you can borrow on
  purpose: cash in hand, $1.45 on the book for every dollar, compounding at 8% a
  day like everything else. The ceiling shrinks as your debt climbs, so nobody
  hands you enough to end the run in one transaction.
- **Set your own rate, per corner.** Cut price earns 20% less an hour and runs
  hotter, but builds regulars twice as fast and lets a corner carry a crowd.
  Premium earns 30% more and stays quiet, but grows almost no client base and two
  people on the same corner get in each other's way. Premium suits a thin corner,
  cut price suits a full one.
- **Collectors.** Past $82,500 owed, people come for it in person — where you are
  standing, not somewhere you have to drive to. See them off and interest stops
  for three days. Don't, and they take your best worker and write a third off the
  book.

## Muscle

- **An armoury.** A bat, a pistol, a micro SMG or a carbine. An armed enforcer
  **turns up in person** at incidents in the region he covers, and fights — gangs
  and bad clients both.
- The weapon also counts when you never see him: it raises his effective skill,
  so he turns away more rivals and handles more client trouble off-screen.
- **He can be carried out of it.** Two days off the corner, still on the wage,
  deterring nobody.
- Off-screen handling is deliberately capped so a well-armed man still lets some
  through for you to watch him deal with.

## Progression

- **Tier can finally be raised.** It was rolled once at recruit and never moved
  again, despite being the largest single term in the payout formula. Thirty
  street hours and 65 loyalty earns tier 2; ninety earns tier 3.
- **Invest in one person.** A wardrobe buys the promotion outright. A doctor
  restores stamina and loyalty. Studio time permanently raises her followers.
  Papers buy her the Connected trait. None of it survives her leaving, which is
  the point — the roster becomes something you own rather than cycle.

---

## Fixes

- **`config.json` lost every new setting, silently.** The serialiser builds the
  object without running field initialisers, so any key added after your config
  was written loaded as zero rather than its default — not as an error, just as a
  wrong number. On an existing install this release would have promoted every
  worker to tier 3 within two in-game hours and multiplied follower gain by zero.
  Your file is now read for the keys it actually contains, missing ones are filled
  in with their defaults, and it is rewritten so the new options are visible and
  editable. Anything you had deliberately set to `0` is left exactly as you set it.
- **Relationship groups were half-wired.** Hired muscle and hostile crews share
  one setup path guarded by a single flag, so whichever was requested first marked
  the work done and the other never received its relationships at all.
- **Day-shift workers bled regulars every night.** The daily check ran at midnight
  and asked whether she was working *at that moment* — a Days worker never is.
- Turf takeovers, raids and incidents all now agree on whether somebody indoors,
  in custody, or on a booking counts as posted.
- Replaced a deprecated combat task; the build carries no warnings.

## Notes for upgrading

Keep your `save.json`. It migrates from any earlier version, one step at a time,
and writes each step to the log. Back the `scripts/OnTheBlade/` folder up first if
the save matters to you.

Your `config.json` is kept too, and gains around ninety new settings — every
number in this release is tunable without a rebuild.
