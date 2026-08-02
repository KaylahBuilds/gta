# Installing On The Blade

Same steps for **GTA V Legacy** and **GTA V Enhanced** — one build covers both.

> **Single player only.** Loading script mods into GTA Online will get the
> account banned. Launch story mode, and on Enhanced make sure BattlEye is not
> running.

## Requirements

| | |
| --- | --- |
| GTA V | Legacy or Enhanced, story mode |
| [Script Hook V](http://www.dev-c.com/gtav/scripthookv/) | Alexander Blade's, matching your edition |
| [ScriptHookVDotNet Enhanced](https://github.com/Chiheb-Bacha/ScriptHookVDotNetEnhanced/releases) | v1.1.0.6 or newer |
| .NET Framework | 4.8 or newer |
| Visual C++ Redistributable | 2019 x64 |

**Install SHVDNE even if you are on Legacy.** It is a drop-in replacement for
ScriptHookVDotNet that runs the same binaries on both editions. Stock SHVDN will
not load this mod on Enhanced.

## Steps

**1. Script Hook V**

Get the build for your edition and copy its files into the game root — the folder
containing `GTA5.exe` or `GTA5_Enhanced.exe`.

**2. ScriptHookVDotNet Enhanced**

From the SHVDNE release zip, copy into the same game root:

- `ScriptHookVDotNet.asi`
- `ScriptHookVDotNet2.dll`
- `ScriptHookVDotNet3.dll`

Create a `scripts` folder in the game root if one doesn't exist yet.

**3. The mod**

Copy all three DLLs into `scripts/`:

```
<game root>/scripts/
  OnTheBlade.dll
  LemonUI.SHVDN3.dll
  iFruitAddon2.dll
```

Do **not** put `ScriptHookVDotNet3.dll` in `scripts/` — a second copy there
breaks script loading. It belongs in the game root only.

`iFruitAddon2.dll` powers the in-game phone contact. If it is missing the mod
still loads and logs why; you just lose the phone entry. The `F1` menu works
either way.

**4. Launch**

Start story mode — on Enhanced, without BattlEye. Load a single-player save and
let the world finish streaming.

**5. Check it loaded**

You should get an "On The Blade loaded" notification, and `F5` should open the
operations menu.

## Controls

| Key | Action |
| --- | --- |
| `F5` | Operations menu — roster, territory, upgrades, property, muscle |
| `F1` | Phone menu — status report, recall everyone, fast travel |
| `Insert` | SHVDN script reload. Safe — releases every owned ped and saves first |

There is also a contact named **On The Blade** in the in-game phone that opens the
same phone menu. Both keys are rebindable in `config.json`.

## Files it creates

```
<game root>/scripts/OnTheBlade/
  config.json        balance and keybinds
  save.json          roster, territory, upgrades
  OnTheBlade.log   startup and error log
```

Paths resolve off the game root, so Legacy and Enhanced installs keep completely
independent saves and configs. Copy `save.json` between them if you would rather
share progress.

Every balance value lives in `config.json`. Edit it and press `Insert` to reload
— no rebuild needed.

## Troubleshooting

Start with `scripts/OnTheBlade/OnTheBlade.log`. It records the game file
version at startup, which is the first thing worth knowing.

**Nothing happens when I press F5**

- Check `ScriptHookVDotNet.log` in the game root. If `OnTheBlade.dll` isn't
  listed, it isn't being loaded at all.
- Confirm all three DLLs are in `scripts/`, not the game root.
- Confirm you installed SHVDNE, not stock ScriptHookVDotNet.
- Confirm `ScriptHookVDotNet3.dll` is *not* also in `scripts/`.

**It stopped working after a game update**

SHVDNE pins memory offsets per game build and ships point releases to chase
patches. Check for a newer SHVDNE before assuming the mod is at fault — that is
the usual cause, especially on Enhanced.

**The phone contact is missing**

The log will say why. You need iFruitAddon2 3.0.2 or newer — that is the release
where one file covers both editions.

**Someone is stuck "in trouble"**

Press `Insert` to reload. That aborts any active incident, releases its peds and
clears the stuck state before saving.

## Uninstalling

Delete `OnTheBlade.dll` from `scripts/`. Leave the other two DLLs if other mods
use them. The `OnTheBlade/` data folder can go too, or stay if you might come
back to the save.
