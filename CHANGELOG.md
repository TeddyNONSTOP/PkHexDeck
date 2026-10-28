# Changelog

## 1.0.0 — 2026-10-27

The first PkHexDeck release. It is based on PKHeX-Avalonia 1.96.2 (PKHeX.Core 26.08.26) and adds the following.
See the [user manual](docs/USER-MANUAL.md) for how to use each feature.

### Program

- Renamed to PkHexDeck, with its own settings and data folders. On first start it copies PKHeX-Avalonia's
  settings, Bank and backups across, leaving the originals untouched.
- No update checks: it never replaces itself with an upstream release.
- README, user manual and this changelog.

### Bank and transfers

- **Bank**: cross-game storage outside any save, with boxes, names, wallpapers, export/import and its own
  backups. Shared between app windows.
- Deposits and withdrawals stay pending until the save is written; closing without saving cancels them, so a
  Pokémon is never lost.
- Withdraw several marked Pokémon at once, and relay Pokémon between two open saves through the Bank.
- **Transfer preview**: before a Pokémon from another format is written, shows what the conversion changes and
  whether it stays legal (Settings › Preview transfers).

### Organizing

- **Organizer**: Multi-select marks Pokémon across boxes; Actions moves, copies, duplicates, sends to the Bank,
  exports, legalizes or releases them in one undoable step.
- **Living Dex tracker**: owned, missing and shiny species and forms, optionally counting the Bank.
- **Living Dex autopilot**: arranges your boxes in dex order, with gaps for missing species if wanted.
- **Legalize** in place from a slot's right-click menu (Auto Legality Mod).

### Challenge runs

- **Hardcore mode**: browsing, moving, releasing, the Bank, exports and backups stay available; editing,
  creating and copying are blocked, and every save is checked so that only moves reach the disk.
- **Nuzlocke report**: first encounter per location in the order reached, extra catches, dupes (with the dupes
  clause applied), hatched Eggs filed where received.

### Steam Deck and saving

- Game controller support with a Steam Deck-friendly layout (Settings › Controller input).
- Emulator save discovery: RetroArch, mGBA, melonDS, DeSmuME, Dolphin, Azahar/Citra/Lime3DS, Ryujinx and Yuzu
  forks, including Flatpak and EmuDeck folders on an SD card.
- Atomic saving: written to a temporary file, verified, then swapped in.
- Read-only open; 20 backups per save by default (1–100 in Settings).

### Luminescent Platinum

- Opens Luminescent Platinum saves, with its Pokédex and trainer data editors.
- Legality checks species and form availability for the hack instead of failing on every Pokémon.
- The Batch editor works on Luminescent Platinum Pokémon (it used to fail on every one).

### Event flags

- Names for most BDSP and Luminescent Platinum event flags (1,754 of 4,000 flags, 965 system flags, 439 work
  values), with places and descriptions, and Luminescent Platinum's own flags.
- Descriptions decoded from internal names, e.g. "Visited Hall of Origin - Pearl", shown in [brackets].
- Unnamed slots are hidden behind a toggle.

### Batch editor

- A status line says what Run Batch will do, why it is unavailable, or "Done" after a run.
- The Instructions box stays usable in short windows (the page scrolls).
- Max IVs, Make Shiny and Heal All now work; Max Friendship, Max Contest Stats and Relearn Moves were added.

### Other fixes and additions

- Rerolling a PID keeps the Pokémon's shininess.
- Gen 4 Battle Video viewer with import and export.
- The Legends: Arceus Move Shop editor is reachable.
- Item gifts show in the Mystery Gift Database.
