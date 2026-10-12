# Mediaspot Ingest

Desktop tool (.NET 10, WPF) that prepares the ingest of archive media into Mediaspot through the **Legacy Importer**.
It replaces Yan Rocheteau's *DaletToMediaspot* ClickOnce tool, without its hard-coded paths, clients and credentials.
First use: the EuropaCorp LTO archive takeover.

## Workflow

1. **Configuration**: one profile per Mediaspot platform. It holds:
   - the platform id, provider id and API login;
   - the paths: LTO restore area, the provider's `temp` ingest folder, the done folder and the exports folder;
   - the platform's Legacy Importer **template**;
   - default cell values.

   The template drives the columns: row 1 tagName, row 2 layer, row 3 requirement (`M`/`O`/`A`/`M if Season`), row 4 allowed values, row 5 label, data from row 6.
2. **Lots → Nouveau lot depuis le registre**: import the registry export CSV, then pick the titles. Elements already in a batch are skipped. Each item is pre-filled from the registry metadata and located on disk.
3. **Titres**: fill in Title ID (`titleCustomerId`), title and year. Saving copies them into every item of the title.
4. Fix the cells still flagged. The status turns to **Ready**.
5. **Transférer vers l'ingest**: copy or move the files into the provider's `temp` folder. A plan is shown first and nothing is overwritten.
6. **Exporter le XLSX**, then run the import manually in Mediaspot (Legacy Importer).
7. **Suivi Mediaspot**: finds the created assets (by `streamId` = registry element id, else `customerOriginalFileName`) and reads their status. Then **Déplacer vers done**.

## Registry export

`tools/registry-to-csv.ps1` turns a dump of the registry database (collections `titles`, `catalog`, `decisions`) into the CSV the application imports:

```
powershell -ExecutionPolicy Bypass -File tools\registry-to-csv.ps1 -Dump <dump folder> -Output registre.csv
```

An element is ingested when the client decided `ingest` (mediaspot®), or, without a decision, when VDM's proposal is `ingest`.

## Storage and secrets

- Settings: `%APPDATA%\MediaspotIngest\settings.json`.
- Passwords and the MongoDB connection string are encrypted with DPAPI for the current Windows user.
- Batches, items and titles live either in local JSON files or in MongoDB, in collections `{prefix}.batches|items|titles`. They are kept separate from the old `daletToMediaspot.*` collections.

## Build and test

```
dotnet build MediaspotIngest.slnx
dotnet test tests/MediaspotIngest.Core.UnitTests
```
