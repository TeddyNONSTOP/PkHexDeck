# Gen 3 ticket-linked save states

These mappings describe save-state flags and Gen 3 key-item IDs. Event files, distribution
data, Wonder Cards and downloads are separate and are not included. Editing these values
does not execute game scripts. Normal story prerequisites still apply in the game.

| Games | Ticket / destination | Item ID | Travel gate | Shown to crew | Receipt marker |
|---|---|---:|---:|---:|---:|
| Ruby, Sapphire | Eon / Southern Island | 275 | `0x853` | — | — |
| Emerald | Eon / Southern Island | 275 | `0x8B3` | `0x1AE` | — |
| Emerald | Mystic / Navel Rock | 370 | `0x8E0` | `0x1DB` | `0x13B` |
| Emerald | Aurora / Birth Island | 371 | `0x8D5` | `0x1AF` | `0x13A` |
| Emerald | Old Sea Map / Faraway Island | 376 | `0x8D6` | `0x1B0` | `0x13C` |
| FireRed, LeafGreen | Mystic / Navel Rock | 370 | `0x84A` | `0x2F0` | `0x2A8` |
| FireRed, LeafGreen | Aurora / Birth Island | 371 | `0x84B` | `0x2F1` | `0x2A7` |

Ruby/Sapphire's harbor script also checks the post-game flag and rejects travel once
`FLAG_ENCOUNTERED_LATIAS_OR_LATIOS` (`0xCE`) is set. This encounter state is distinct
from owning the ticket or enabling its gate. The other games' harbor scripts check
both the ticket item and the corresponding enabled-route flag. Their shown-to-crew
flags control first-time dialogue. Receipt markers belong to the gift flag range and
are not a substitute for the item and route gate.

Primary sources used for verification:

- [Ruby/Sapphire flags](https://github.com/pret/pokeruby/blob/5784633ce4ef7ade1a7f2d2d0c288e3d5e6cdd7f/include/constants/flags.h),
  [items](https://github.com/pret/pokeruby/blob/5784633ce4ef7ade1a7f2d2d0c288e3d5e6cdd7f/include/constants/items.h), and
  [Lilycove harbor script](https://github.com/pret/pokeruby/blob/5784633ce4ef7ade1a7f2d2d0c288e3d5e6cdd7f/data/maps/LilycoveCity_Harbor/scripts.inc).
- [Emerald flags](https://github.com/pret/pokeemerald/blob/fe1d8e51b265851676b65885818b1f5f62586135/include/constants/flags.h),
  [items](https://github.com/pret/pokeemerald/blob/fe1d8e51b265851676b65885818b1f5f62586135/include/constants/items.h), and
  [Lilycove harbor script](https://github.com/pret/pokeemerald/blob/fe1d8e51b265851676b65885818b1f5f62586135/data/maps/LilycoveCity_Harbor/scripts.inc).
- [FireRed/LeafGreen flags](https://github.com/pret/pokefirered/blob/037335f4c725d7c9aecdac87066f2002b4bd7e14/include/constants/flags.h),
  [items](https://github.com/pret/pokefirered/blob/037335f4c725d7c9aecdac87066f2002b4bd7e14/include/constants/items.h), and
  [Vermilion harbor script](https://github.com/pret/pokefirered/blob/037335f4c725d7c9aecdac87066f2002b4bd7e14/data/maps/VermilionCity/scripts.inc).

The Core mirror independently lists these IDs in each game's Key Items pouch. Do not
infer travel gates for unsupported items or copy Emerald offsets into another game.
