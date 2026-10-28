# Public Violet Indigo Disk test fixture

Source: [GBAtemp public post by Space_Goddess_Samus_Aran, 28 April 2024](https://gbatemp.net/threads/i-need-a-pokemon-violet-save-file.649967/post-10410510).
Attachment observed in the public page: `https://gbatemp.net/attachments/violet-save-zip.434348/` (`Violet Save.zip`).
Archive SHA-256: `CF264BAF6F820EE7CA6FE7304E64486A5D32775A2E8178A55F2CF76F42EAB33A`.
The fixture is the unchanged `Violet Save/ff/main` member (4,435,308 bytes).

The author explicitly states that the save has edited Pokémon and items. It is storage/workflow test data, not legality or untouched in-game provenance evidence. No schema repair or normalization is applied. Core detection, revision and ordinary save/reopen results must be verified by the tests before counting this as DLC coverage.
Fixture SHA-256: `33A831FA5B8B729895E47BF74D48D5A391BB06467DBC7B49C5BCD82B9F293EAE`.
Core verification: revision 2, active Kitakami/DLC mode 1, MaxSpeciesID 1025. Normal save/write/auto-detect reopen and byte-exact no-op tests pass.
