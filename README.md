# PkHexDeck

A Pokémon save editor built and played on the Steam Deck, with a cross-game Bank, collection tools and
challenge-run tools.

PkHexDeck is a fork of [PKHeX-Avalonia](https://github.com/realgarit/PKHeX-Avalonia) by Realgar, the Avalonia
port of [PKHeX](https://github.com/kwsch/PKHeX) by Kurt (kwsch) and the PKHeX contributors. It is not
affiliated with either project, or with Nintendo, Game Freak or The Pokémon Company.

**[User manual](docs/USER-MANUAL.md)** · **[Changelog](CHANGELOG.md)**

## Features

Everything PKHeX-Avalonia does: edit Pokémon, boxes, party, trainer, bag and game-specific save data for every
main-series game from Red/Blue to Legends: Z-A, check legality, import Showdown sets and more (see
[docs/UPSTREAM-README.md](docs/UPSTREAM-README.md)). On top of that:

| Feature | What it does |
|---|---|
| **Bank** | Stores Pokémon outside any save and carries them between games. Deposits and withdrawals only become final when the save is written, so closing without saving never loses a Pokémon. |
| **Transfer preview** | Shows what moving a Pokémon to another game's format changes, and whether it stays legal, before anything is written. |
| **Organizer** | Mark Pokémon across boxes and move, copy, bank, export, legalize or release them in one undoable step. |
| **Living Dex** | A tracker of owned, missing and shiny species and forms, and an autopilot that arranges your boxes in dex order. |
| **Nuzlocke report** | First encounter per location in the order you reached them, extra catches and dupes. |
| **Hardcore mode** | For challenge runs: moving, releasing, the Bank, exports and backups stay available; editing, creating and copying are blocked, and every save is checked before it is written. |
| **Luminescent Platinum** | Opens saves from the BDSP ROM hack, with species/form availability checks. |
| **Event names** | Names and descriptions for most BDSP and Luminescent Platinum event flags, searchable by place. |
| **Batch editor fixes** | Works on Luminescent Platinum, explains why Run Batch is unavailable, and all Quick Actions work. |
| **Steam Deck** | Full controller support, emulator save discovery (EmuDeck, Flatpak, SD card), atomic saving, read-only open and 20 backups per save. |

## Installing on the Steam Deck

PkHexDeck is a single Linux AppImage; nothing else needs installing.

1. Put `PkHexDeck.AppImage` in a folder such as `~/Applications` and make it executable
   (right-click › Properties › Permissions › Is executable, or `chmod +x PkHexDeck.AppImage`).
2. Double-click it to start. **Settings › Linux desktop integration › Add to application menu** puts it in
   the Desktop Mode menu.
3. To use it from Game Mode, add it to Steam as a non-Steam game (Steam › Add a Game › Add a Non-Steam
   Game…). Controller input then works automatically.

The first time it starts, PkHexDeck copies an existing PKHeX-Avalonia installation's settings, Bank and backups
into its own folders and leaves the originals alone. It does not check for updates.

## Quick start

1. **File › Open…** and pick a save, or use the **Save File Browser**'s Emulator Saves tab. Close the game or
   emulator first.
2. Click a Pokémon to edit it, then right-click its slot › **Set** to write it back.
3. **File › Save**. The previous version is kept as a backup automatically.

The [user manual](docs/USER-MANUAL.md) covers each feature, the controller layout, settings and troubleshooting.

## Building from source

Requires the .NET 10 SDK.

```
dotnet build PKHeX.sln -c Release
dotnet test PKHeX.sln -c Release
```

AGENTS.md describes how the Linux AppImage is put together.

## License and credits

GPLv3, the same license as PKHeX and PKHeX-Avalonia (see [LICENSE](LICENSE)).

- PKHeX.Core: Kurt (kwsch) and the PKHeX contributors.
- PKHeX-Avalonia: Realgar and contributors.
- Auto Legality Mod (PKHeX.AutoMod): santacrab2 and the PKHeX-Plugins contributors; see `PKHeX.AutoMod/VENDORED.md`.
- Event flag and place names: fattard's FlagsEditorEXPlugin (GPL-3.0) and Team Lumi's Luminescent_ExLaunch
  (BSD-3-Clause); see `PKHeX.Application/Resources/EventsBdsp/NOTICE.md`.
- Other bundled data and art carry their own notices next to the files.
