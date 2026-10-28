# PkHexDeck User Manual

PkHexDeck is a Pokémon save editor built for the Steam Deck. It reads and edits saves from every main-series
game from Red/Blue to Legends: Z-A, plus Luminescent Platinum, and adds tools for keeping a collection across
games and for playing challenge runs.

This manual covers PkHexDeck's own features and the everyday tasks around them. The Pokémon editor and the
game-specific save editors work as in PKHeX-Avalonia; see [UPSTREAM-README.md](UPSTREAM-README.md) for those.

## Contents

1. [Before you start: keep your saves safe](#1-before-you-start-keep-your-saves-safe)
2. [Opening and saving](#2-opening-and-saving)
3. [Finding your way around](#3-finding-your-way-around)
4. [Using a controller](#4-using-a-controller)
5. [The Bank](#5-the-bank)
6. [Organizing many Pokémon at once](#6-organizing-many-pokémon-at-once)
7. [Living Dex tracker and autopilot](#7-living-dex-tracker-and-autopilot)
8. [Nuzlocke report](#8-nuzlocke-report)
9. [Hardcore mode](#9-hardcore-mode)
10. [Batch editor](#10-batch-editor)
11. [Event flags](#11-event-flags)
12. [Luminescent Platinum](#12-luminescent-platinum)
13. [Settings](#13-settings)
14. [Where your data lives](#14-where-your-data-lives)
15. [Troubleshooting](#15-troubleshooting)

---

## 1. Before you start: keep your saves safe

- **Close the game or emulator before you save.** An emulator that is still running can write over your
  changes, or overwrite the save while PkHexDeck is writing it.
- **Work on a copy the first time** you try something new. Copy the save file somewhere else and open the copy.
- **Backups are automatic.** Every time PkHexDeck saves over a file, it first keeps the old version as a
  backup. It keeps 20 per save by default (see [Settings](#13-settings)). Restore one from
  **Tools › Save Editors › Backup Manager…**.
- **Saving is atomic.** PkHexDeck writes to a temporary file, reads it back to check it, and only then
  replaces your save. If something goes wrong mid-write, the original file is still there.

## 2. Opening and saving

### Opening a save

- **File › Open…** opens any save file.
- **File › Open Read-Only…** opens a save you only want to look at. The title bar says *(read-only)* and a
  **Read-only** badge appears. **Save** will ask for a new file name, so the original is never changed.
- The **Save File Browser** (Tools › Data, or the tool launcher) lists your **Recent Files**, **Backups** and
  **Emulator Saves**. Right-click an entry for **Open Read-Only**.

### Emulator saves

The **Emulator Saves** tab finds saves from emulators installed normally, as Flatpaks, or through EmuDeck
(including `Emulation/saves` on an SD card): RetroArch, mGBA, melonDS, DeSmuME, Dolphin (GameCube),
Azahar/Citra/Lime3DS, Ryujinx and the Yuzu family. For Ryujinx it lists only the committed save, not the
working copy the emulator keeps while running.

### Saving

- **File › Save** writes back to the file you opened. **File › Save As…** writes a new file and switches to it.
- If you try to close a save with unsaved changes, PkHexDeck asks whether to **Save**, **Discard** or
  **Cancel**.

## 3. Finding your way around

The top bar has three areas:

| Area | What's there |
|---|---|
| **Pokémon** | The Pokémon editor on the left, your boxes and party on the right. |
| **Trainer**, **Inventory**, **Save** | Trainer card, bag, and the Events, Gifts and Batch pages. |
| **Search & Reports** | Box Data Report, Living Dex Tracker, Nuzlocke Report, Legality Audit, databases. |

- **Tools** holds every other tool, grouped by purpose and by game generation. Tools that don't apply to the
  open save are hidden.
- **Find a tool** (the tool launcher) lets you type part of a tool's name and open it.
- **Window › Bank** (Ctrl+B) opens the Bank.

### Working with slots

- Click a Pokémon to load it into the editor. Make changes, then right-click its slot and choose
  **Set** to write the edited Pokémon back (or select the slot and press Ctrl+V).
- Drag a Pokémon to move it. Hold Ctrl while dragging to copy it instead.
- Right-click a slot for **View**, **Set**, **Move to Party**, **Send to Bank**, **Legalize** and **Delete**.
- **Edit › Undo** reverts slot changes, including whole Organizer and Living Dex actions.

## 4. Using a controller

PkHexDeck can be driven entirely with a controller, which matters in Game Mode.

| Button | Does |
|---|---|
| D-pad / left stick | Move focus |
| A | Activate, open a dropdown |
| B | Back; closes a dialog |
| X | Context menu (the right-click menu) |
| Y | Toggle (Space) |
| L1 / R1 | Previous / next tab |
| L2 / R2 | Decrease / increase a number or choice |
| Right stick | Scroll |
| Start | Menu bar |
| Select | Next control (Tab) |

**Settings › Controller input** chooses when PkHexDeck reads the controller itself:

- **Automatic** (default): in Game Mode, or when Steam isn't running. In Desktop Mode Steam already turns the
  controller into keyboard and mouse input, so reading it as well would double every press.
- **On** / **Off**: always or never.

PkHexDeck never takes over the Steam Deck's own controls; it reads the virtual controller Steam provides.

## 5. The Bank

The Bank stores Pokémon outside any save file, so you can carry them between games and saves. Open it with
**Window › Bank** (Ctrl+B). It stays open when you switch saves.

### Depositing

- Right-click a box Pokémon and choose **Send to Bank**, or mark several and use
  **Actions › Send marked to Bank…** (see [Organizing](#6-organizing-many-pokémon-at-once)).
- Party Pokémon have to be moved to a box first, and the party must keep at least one Pokémon that isn't an Egg.

### Withdrawing

- The Bank shows **Withdrawals go to:** with the save that's open.
- Select a Pokémon and press **Withdraw to save** (or Enter, or double-click). It goes into the first empty slot
  of the box you're viewing in the save.
- **Withdraw marked** places every marked Bank Pokémon, starting with the box you're viewing.
- When a Pokémon moves to a game with a different format, a **Transfer Preview** shows what the conversion
  changes and whether it stays legal, before anything is written.

### Nothing is final until you save

Deposits and withdrawals show a **Pending** badge until the save file is written:

- **Save** the file: the deposit or withdrawal completes.
- **Close without saving**: it's cancelled. A deposited Pokémon stays in the save; a withdrawn one stays in
  the Bank.

So you can never lose a Pokémon by closing PkHexDeck at the wrong moment.

### Boxes and backups

- **Add box** adds an empty box; **Rename** names the current one; each box can have a wallpaper.
- **Export…** writes every Bank Pokémon to a folder as files. **Import…** adds Pokémon files from a folder,
  skipping ones already in the Bank.
- The Bank keeps its own backups. Select one and press **Restore** to go back to it.
- Two PkHexDeck windows can share the Bank. Press **Refresh** (F5) to see changes made in the other window.

## 6. Organizing many Pokémon at once

1. Turn on **Multi-select** above the box.
2. Click slots to mark them (**Mark all in this box** marks a whole box). Marks stay when you change boxes.
3. Open **Actions** and choose:
   - **Move marked to this box** / **Move marked to party**
   - **Copy marked to this box** / **Copy marked to party** / **Duplicate marked**
   - **Send marked to Bank…**
   - **Export marked (.pk files)…**
   - **Legalize marked…**
   - **Release marked…**

Each action asks for confirmation, makes a restore point, and can be undone in one step with **Edit › Undo**.
Pokémon that can't be handled (no room, a protected slot, a party member that would empty the party) are skipped
and listed in the result.

## 7. Living Dex tracker and autopilot

### Tracker

**Search & Reports › Living Dex Tracker** lists every species the game can hold and shows which you own, how
many, which are shiny, and where they are.

- **Show**: All, Missing, Owned, or No shiny yet.
- **Count forms separately** tracks each form (Unown letters, regional forms) as its own entry.
- **Include the Bank** counts Pokémon stored in the Bank as owned.
- **Show in Box** (or Enter, or double-click) jumps to the Pokémon.

### Autopilot

**Arrange Boxes...** in the tracker (also **Tools › Save Editors › Arrange Living Dex**) sorts the Pokémon in
your boxes into National Dex order.

- **Start at**: the first box to fill.
- **Count forms separately** gives each form its own dex slot.
- **Leave gaps for missing** keeps an empty slot for each species you don't have yet, so every species always
  sits in the same slot.
- **Prefer shiny** puts a shiny in the dex slot when you have one.

A live preview shows the result before you press **Arrange**. Pokémon that aren't needed for the dex (extra copies,
Eggs, other forms) go after the dex. The party isn't touched, locked slots stay put, and the whole arrangement is a
single **Undo**. The autopilot only moves Pokémon; it never evolves or changes them.

## 8. Nuzlocke report

**Search & Reports › Nuzlocke Report** checks a run against the usual Nuzlocke rules. It never changes the save.

| Column | Meaning |
|---|---|
| **#** | The order you reached each location, by the date the Pokémon was met. |
| **Location** | Where it was met. Buildings count as part of their town or route. |
| **Encounter** | The Pokémon that counts for this location. |
| **Status** | **Counts**; **Egg (counts)**; **Dupe skipped: …** when the first catch was a family you already had and a later catch there counts instead; **Only dupes: …** when nothing caught there is new. |
| **Extra catches** | Everything else caught at that location. A strict run allows one per location. |

- **Show** filters to locations with extra catches, or locations with a dupe.
- Hatched Pokémon are filed where you received the Egg. Pokémon from other trainers (trades) and unhatched
  Eggs are counted separately and don't count as encounters.
- Fainted or released Pokémon leave no trace in a save, so the report only sees Pokémon you still have. Keep a
  "graveyard" box if you want your losses in the report.

## 9. Hardcore mode

Hardcore mode is for challenge runs and RetroAchievements-style play. Turn it on in
**Settings › Hardcore mode › Block data editing**. A **Hardcore** badge then shows next to the Save button.

**Still available:** browsing, moving Pokémon between slots, boxes and party, releasing, the Bank (deposits
and withdrawals), exporting, reports, backups and the Living Dex autopilot.

**Blocked:**
- Editing Pokémon. The editor still shows them, but **Set** and **Legalize** are gone.
- Creating Pokémon: generators, Showdown and QR import, file drops, Bank **Import…**.
- Copying: Ctrl-drag, Copy and Duplicate.
- The Trainer, Inventory, Events, Gifts and Batch pages, which become read-only.
- Every editing tool.

**Every save is checked.** Before a save is written, PkHexDeck compares it with the last saved version. It
saves only if every Pokémon is one the save already had (or one withdrawn from the Bank) and nothing else
in the save changed. Box names, wallpapers and the current box may change. If anything else changed, the save
is refused with a message naming the Pokémon and slot, or saying which data changed. Undo the change, or turn
Hardcore mode off.

While Hardcore mode is on, moving a Pokémon also stops updating the Pokédex and the "Pokémon caught" record,
which normal editing does in the Gen 6–9 games.

Hardcore mode keeps you honest; it isn't anti-cheat. Restoring an older backup is still allowed, so
depositing a Pokémon in the Bank and then restoring a backup from before the deposit would give you two.

## 10. Batch editor

**Save › Batch** changes many Pokémon at once with short text instructions.

```
=Species=25          only Pokémon where Species is 25 (Pikachu)
.Nickname=Sparky     set the nickname
.CurrentLevel=50     set the level
```

- Lines starting with **=** are filters; **.** lines are changes. A line without one of those is ignored.
- Tick **Boxes** and/or **Party** to choose what to edit.
- The line under the title says what will happen: *Ready: Run Batch will change N Pokémon*, or why
  **Run Batch** is unavailable (nothing typed, nothing ticked, instructions it can't read, nothing that would
  change).
- **Quick Actions** run immediately: Max IVs, Max EVs, Make Shiny, Heal All, Max Friendship, Max Contest
  Stats, Relearn Moves. Afterwards the line says *Done: changed N Pokémon*.
- The **Instruction Builder** helps you write lines: pick a property and value, then press **+ Filter** or
  **+ Modify**.
- **Reset** undoes the batch runs you made; **Edit › Undo** works too.

## 11. Event flags

**Save › Events** edits the game's story and world flags. Changes can break story progress, so make a backup
first. Nothing is written until you press **Apply**; **Reset** and **Cancel** throw edits away.

- **Data** switches between Event flags, System flags and Work values. The category list narrows the list
  (Hidden items, Field items, Item gifts, Event encounters, Luminescent Platinum, and so on).
- Search by name, place, index, hex or value, for example "Route 221" or "Ultra Ball".
- For Brilliant Diamond, Shining Pearl and Luminescent Platinum, most slots now have names:
  - **(round brackets)** hold a description from a published list, e.g. *FT_DROP_ITEM_95 (Found Ultra Ball,
    Route 221)*.
  - **[square brackets]** hold a description read from the internal name, e.g. *FLAG_ARRIVE_D05R0117
    [Visited Hall of Origin - Pearl]*. The place is exact; the rest is a best reading.
- Slots nobody has named are hidden. **Show unnamed slots** brings them back, and typing an index jumps to any
  slot.

## 12. Luminescent Platinum

PkHexDeck opens Luminescent Platinum saves (the Brilliant Diamond / Shining Pearl ROM hack), for example
`SaveData.bin` from Ryujinx.

- **Legality** only checks that the species and form exist in Luminescent Platinum; moves and encounters
  aren't checked, because no legality data exists for the hack. A Pokémon the hack doesn't have shows as
  illegal (it would appear as Ditto in game).
- **Legalize** isn't available for Luminescent Platinum Pokémon.
- The Batch editor, Bank, Organizer, Living Dex tools, Nuzlocke report and Hardcore mode all work.
- Event flags show Luminescent Platinum's own flags (infinite repel, mega evolution, incense and more) under
  the **Luminescent Platinum** category.

## 13. Settings

| Setting | What it does |
|---|---|
| **Theme**, **Density**, **Language** | Appearance and language; change immediately. |
| **Controller input** | When PkHexDeck reads the controller; see [Using a controller](#4-using-a-controller). |
| **Backups kept per save** | How many automatic backups to keep (1–100, default 20). |
| **Warn if closing modified** | Ask before closing a save with unsaved changes. |
| **Preview transfers** | Show the Transfer Preview before a Pokémon from another format is written. |
| **Hardcore mode** | See [Hardcore mode](#9-hardcore-mode). |
| **Linux desktop integration** | **Add to application menu** installs PkHexDeck into the Desktop Mode menu. |
| **Enable PKHaX Mode** | Starts in HaX mode, which lifts legality limits in the editors. Hardcore mode wins over it. |

PkHexDeck doesn't check for updates.

## 14. Where your data lives

| What | Linux / Steam Deck |
|---|---|
| Settings | `~/.config/PkHexDeck/config.json` |
| Bank, backups | `~/.local/share/PkHexDeck/` |

The first time PkHexDeck starts, it copies PKHeX-Avalonia's settings, Bank and backups into these folders,
if you had them. The originals are left untouched, and the copy happens only once.

## 15. Troubleshooting

**Run Batch stays grey.** Read the line under the Batch title; it says why. Most often the Instructions box is
empty (typing in the Instruction Builder fields alone adds nothing; press **+ Modify**), or a filter matches no
Pokémon.

**"Not saved: …" in Hardcore mode.** Something besides moves changed since the last save. The message names the
Pokémon and slot, or says data outside the boxes changed. Undo it, or turn Hardcore mode off.

**A Pokémon shows as illegal in Luminescent Platinum.** The species or form doesn't exist in the hack. See
[Luminescent Platinum](#12-luminescent-platinum).

**The Events list looks short.** Unnamed slots are hidden; tick **Show unnamed slots**.

**A withdrawn Pokémon is back in the Bank.** The save was closed without saving, so the withdrawal was
cancelled. Withdraw it again and save.

**The controller does nothing, or every press happens twice.** Check **Settings › Controller input**. Use
Automatic in most cases; Off if Steam's own controller layout already works for you in Desktop Mode.
