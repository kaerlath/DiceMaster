# DiceMaster

Shared animated dice for Final Fantasy XIV / Dalamud. Roll together in relay rooms with eleven finishes, including marble, walnut, nebula, volcanic stone, frost and burgundy damask.

**Early release: v0.3.1, Dalamud API 15.**

## Install

1. In Dalamud's settings, open Experimental and add this Custom Plugin Repository URL:

   ```text
   https://raw.githubusercontent.com/kaerlath/DiceMaster/main/repo.json
   ```

2. Save, open the Plugin Installer, find **DiceMaster**, and install it.
3. Use `/dicemaster`, enter a display name, and create or join a room. Share the room code with friends.

The default relay is already configured. All participants should use the same plugin version for matching skins and animations. Ordinary players leave **Server Authentication** blank.

Manual developer installation is also available from [Releases](https://github.com/kaerlath/DiceMaster/releases): extract the complete ZIP, including its Assets directory, and add the DLL as a Dalamud development plugin.

## Features

- Roll 1–20 d4, d6, d8, d10, d12, d20 or d100. Percentile results use paired d10s.
- Dedicated Dice Window with shaded, textured polyhedral dice and varied spin, bounce and settling times.
- Server-scheduled 2.4–5 second rolls. Everyone sees the roller's chosen skin, not their own preference applied to someone else's roll.
- Five color finishes and six texture skins: Moonstone Marble, Elderwood, Astral Glass, Obsidian Relic, Frostbound and Crimson Velvet.
- Shared room roster and recent roll history.


## Build and checks

Requires .NET 10, Dalamud API 15 development files, and Node.js 24 for the relay tests.

```powershell
dotnet build plugin/DiceMaster.csproj -c Release
node --test relay/test/*.test.mjs
dotnet run --project tests/GeometryChecks
dotnet run --project tests/CredentialChecks -- ./private/credential-test
```

Run the credential check in a normal Windows user session; restricted sandbox profiles may not support DPAPI. Release output is under `plugin/bin/Release/DiceMaster/latest.zip`.

See [relay operator setup](docs/RELAY.md) for self-hosting and [protocol](docs/PROTOCOL.md) for API details.

## Current limits

This is intended for small groups: 50 simultaneous rooms, 32 participants per room, 12-hour room lifetimes and 90-second membership leases. Display names are not verified FFXIV identities.

Animation is procedural rather than rigid-body physics; glass and frost use opaque textures and highlights, not true refraction. Timing uses approximate server-clock synchronization. Further in-game visual and network testing is welcome. Remembered authentication has been built but its Windows DPAPI round-trip could not be verified inside the development sandbox.

## Contributions and assets

Please describe reproducible issues without credentials or personal logs. The six bundled material textures were generated for this project with OpenAI image generation and mapped onto code-generated geometry. No Dalamud/game binaries are included in this repository.
