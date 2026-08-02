# lib/

Build-time references. Not checked in — drop these three files here before your
first build. The build fails with a pointer back to this file if any is missing.

| File | Where from | Verified with |
| --- | --- | --- |
| `ScriptHookVDotNet3.dll` | [ScriptHookVDotNet Enhanced](https://github.com/Chiheb-Bacha/ScriptHookVDotNetEnhanced/releases) — root of the release zip | v1.1.0.6 (assembly `ScriptHookVDotNet3` 3.9.0.0) |
| `LemonUI.SHVDN3.dll` | [LemonUI releases](https://github.com/LemonUIbyLemon/LemonUI/releases) — `LemonUI.zip`, **`SHVDN3/` folder** | v2.2 |
| `iFruitAddon2.dll` | [iFruitAddon2](https://github.com/Bob74/iFruitAddon2/releases) or NuGet — **3.0.2 or newer**, the version where one file covers Legacy and Enhanced | 3.1.1 |

Note the LemonUI filename: the SHVDN3 build is `LemonUI.SHVDN3.dll`, and its
assembly name is `LemonUI.SHVDN3` — not `LemonUI`. The zip contains one DLL per
platform; taking the wrong folder produces a reference that will not resolve.

## Why not NuGet

The `ScriptHookVDotNet3` NuGet package targets Legacy. Building against the DLL
from SHVDNE instead produces a single assembly that loads on **both** Legacy and
Enhanced, because SHVDNE is a drop-in replacement for SHVDN on Legacy and keeps
an identical public API surface.

One reference, one build, two editions. The alternative — two build
configurations producing two DLLs — buys nothing here, because this mod uses no
memory patterns and therefore has no edition-specific code paths.
