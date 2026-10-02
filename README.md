# Euro Truck Simulator 2 / American Truck Simulator Map Renderer

This Application reads ATS/ETS2 files to draw roads, prefabs, map overlays, ferry lines and city names.

![Preview of the map](/docs/preview.jpg "Preview of the map")

### **Support for 1.59**

## Export Maps
Can now export maps as a tiled web map.

[Example with a max zoom level of 4](https://dariowouters.github.io/ts-tile-map-example/)

##### [Source](https://github.com/dariowouters/ts-tile-map-example)
## Kommandozeile mit Mods aus dem Spielerprofil (TsMap.Cli)

`TsMap.Cli` liest die **aktiven Mods inklusive Reihenfolge** direkt aus dem Spielerprofil
(`profile.sii`, auch verschlüsselt/binär) und rendert die Kachelkarte ohne GUI:

```
TsMap.Cli --game ats  --out D:\www\html\maps\ats
TsMap.Cli --game ets2 --out D:\www\html\maps\ets2 --profile "Basti" --game-version 1.58
TsMap.Cli --game ets2 --list          # nur erkannte Mods + Reihenfolge anzeigen
```

- Profil: zuletzt benutztes Profil (lokal oder Steam-Cloud) oder `--profile <Name|Ordner>`
- Spielordner und Steam-Workshop-Ordner werden über Steam (alle Bibliotheken) gefunden, sonst `--game-dir` / `--workshop-dir`
- Workshop-Mods: das passende Paket aus `versions.sii` wird gewählt (`--game-version`, sonst das neueste)
- Lokale Mods aus `Dokumente\<Spiel>\mod` als `.scs`, `.zip` oder entpackter Ordner
- Reihenfolge wie im Spiel: oben in der Liste = höchste Priorität
- Ausgabe: `Vector/` + `VectorInfo.json` (bzw. `Tiles/` mit `--format png`), `TileMapInfo.json`, `Cities.json`, `Countries.json`, `Overlays.json`, `Overlays/`
- DLC-Prüfung: nur Karten-DLCs, die als `dlc_*.scs` im Spielordner liegen, werden gezeichnet (`--dlc alle` = Standardverhalten, `--dlc-an`/`--dlc-aus <guard>` zum Übersteuern)
- Vektorkarte (Standard, `--format vector`): Straßen, Prefabs, Gebiete und Fähren als JSON-Kacheln `Vector/{z}/{x}/{y}.json` (Stufen 0 bis `--vector-zoom`, Standard 8) für eine selbst zeichnende Leaflet-Ebene; `--format png` erzeugt die bisherigen Bild-Kacheln, `--format beide` beides
- Weitere Optionen: `--zoom 0-9` (PNG, Standard), `--no-tiles`, `--no-mods`, `--exclude <Text>` (siehe `--help`)

Build: `dotnet publish TsMap.Cli -c Release -p:Platform=x64 -r win-x64` (.NET 10, Windows)

## Map mod support
It can now load map mods.

Making all/specific map mods supported won't be a priority for me.

### Tested* map mods:

ETS2:
- TruckersMP map edits (cd_changes & hq_final)
- Promods Europe + Middle-East Add-On V2.60
- Rusmap V1.8.1
- The Great Steppe V1.2
- Paris Rebuild V2.3
- ScandinaviaMod V0.4
- Emden V1.02c (Doesn't show city name)
- Sardinia map V0.9 (Can't load some dds files)
- PJ Indo Map v2.5 (Can't load an overlay)

ATS:
- ProMods Canada V1.2.2
- Coast to Coast V2.6 (Can't load some dds files)
- US Expansion V2.4 (C2C Compatible)
- CanaDream Open Beta (ATS 1.32)
- Tonopah REBUILT V1.0.2
- Mexico Extremo 2.0.5
- Viva Mexico v2.4.8

\* The tested mods load and get drawn but I haven't looked at anything specific so it's always possible there will be some items missing or things will get drawn that shouldn't.

## Supported maps / DLC
- ATS
    - All map DLCs up to and including Louisiana.
- ETS2
    - All map DLCs up to and including Nordic Horizons.

#### Dependencies
- [Newtonsoft.Json (NuGet)](https://www.nuget.org/packages/Newtonsoft.Json)
- [NVIDIA libdeflate](https://github.com/NVIDIA/libdeflate/tree/3bb5c6924b32a91e6e6a8f54ba00a21f037a8db5)

#### Based on
[Original project](https://github.com/nlhans/ets2-map)
