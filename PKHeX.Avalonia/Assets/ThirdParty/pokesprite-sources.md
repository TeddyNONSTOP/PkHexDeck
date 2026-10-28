Five outlined 32×32 key-item sprites are copied unchanged from
[PokéSprite](https://github.com/msikma/pokesprite) commit
`c5aaa610ff2acdf7fd8e2dccd181bca8be9fcb3e`.

The source `data/item-map.json` maps the modern item IDs to these files under
`items-outline/key-item/`:

| Bundled file | Source file |
| --- | --- |
| `bitem_632.png` | `shiny-charm.png` |
| `bitem_700.png` | `elevator-key.png` |
| `bitem_765.png` | `prison-bottle.png` |
| `bitem_847.png` | `zygarde-cube.png` |
| `bitem_1278.png` | `rotom-catalog.png` |

The existing context-aware item resolver retains save IDs and maps sprite identities.
Other items keep their existing fallback when individual artwork is unavailable.
The source license is retained alongside this file as `pokesprite-LICENSE.txt`.
