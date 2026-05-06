# Data Migration Tool

An [XrmToolBox](https://www.xrmtoolbox.com/) plugin for migrating reference data between Dataverse / Dynamics 365 instances.

## Features

- Export records to JSON format and import them into another environment
- Supports **Create**, **Update**, and **Delete** operations
- Filter exported data using **FetchXML** queries
- Native integration with the [FetchXML Builder](https://www.xrmtoolbox.com/plugins/Cinteros.Xrm.FetchXmlBuilder/) plugin
- Select specific attributes to include in the migration
- Map records between environments using **automatic** or **manual** mapping
- Save and reload per-table settings (filter + attribute selection) as `.settings.json` files

## Installation

Install directly from the **XrmToolBox Tool Library** — search for *Data Migration Tool*.

## Usage

### 1. Connect

Connect to your **source** environment using the XrmToolBox connection panel. Optionally connect a second **target** environment for import operations.

### 2. Load Tables

Click **Load Tables** to retrieve all entities from the source environment. Use the filter box to narrow the list.

### 3. Select a Table

Click a table to load its attributes. Use the checkboxes to include or exclude specific attributes from the migration.

### 4. Configure Filters

Enter a **FetchXML** filter in the filter panel to restrict which records are exported. Use the FetchXML Builder integration for a visual query builder experience.

### 5. Export

Click **Export** to save the selected records to a `.json` file.

### 6. Import

Click **Import** and select a previously exported `.json` file to load records into the target environment.

### 7. Save / Load Settings

Use **Save Settings** to export the current table's filter and attribute selection to a `.settings.json` file. Use **Load Settings** to restore them in a future session — useful for repeatable migrations.

## Mapping

When a target environment is connected, the tool can automatically map:

- **Users** — matches by name between source and target
- **Teams** — matches by name between source and target
- **Business Units** — maps the root BU

Manual mappings can also be defined for any lookup field.

## Release Notes

### 2026.5.6.x
- [NEW] Import result dialogs now show row numbers, success/failed summaries, failed-row filtering, and retry for failed rows
- [NEW] Excel and JSON import previews now show source row numbers and row-level warnings
- [NEW] Long-running preview, export, and import operations now report clearer progress and log detailed errors
- [FIX] Preview now validates missing FetchXML link-entity aliases and shows a friendly error instead of crashing
- [FIX] Result and import preview tables now sort row numbers numerically and support cleaner copy/sort behavior
- [FIX] Import wizard layout was polished and user/team auto-map options were removed in favor of explicit mappings

### 2026.5.5.x
- [NEW] Excel lookup key fields can now populate related columns during export
- [NEW] Nested lookup key fields can be resolved by GUID or selected custom attributes
- [FIX] Related option set key fields now export as labels and import back to option values
- [FIX] Blank nullable lookup key fields are now treated as null conditions during import resolution
- [NEW] Excel export configuration now includes a Columns tab to reorder, hide, and customize hint text
- [NEW] Excel imports can match records by a selected custom match key for upsert scenarios
- [NEW] Excel exports now use filter-friendly header notes for hints and highlight related-table columns
- [FIX] Column manager sorting/reordering and row-level Excel import errors now behave consistently
- [NEW] Excel and JSON imports now use a preview wizard with import settings, mappings, and record actions
- [NEW] Import matching can now use GUIDs, alternate keys, or multiple selected custom columns
- [FIX] Import preview now formats option set match values and supports sorting and copying rows
- [FIX] Data and settings JSON exports now prompt for separate filenames instead of a folder

### 2026.5.4.x
- [NEW] Added Excel export and import support for table data
- [NEW] Excel export configuration supports lookup resolution by GUID, alternate keys, or selected custom attributes
- [NEW] Option set and multi-select option set values can be exported/imported using labels or raw values
- [NEW] Added Switch Source / Target action to swap active connections and reload tables
- [FIX] Results dialog now uses a fixed dialog size to avoid layout resizing issues

### 2026.4.30.x
- [NEW] SQL 4 CDS button now shows instructions for manually applying query changes back to Data Migration Tool
- [NEW] Integration with SQL 4 CDS — filter can now be opened and edited in SQL 4 CDS
- [NEW] Filter now supports link-entity nodes for filtering by related tables
- [FIX] XML parsing error when sending filters with link-entity nodes to external query builders
- [FIX] External plugin calls now properly isolated — prevents crashes if target plugin is not open
- [FIX] Import error (InvalidDataContractException) when record contains Money attributes
- [FIX] Import type mismatch on Decimal attributes in non-English locales

### 2026.4.29.x
- [FIX] Loading a settings file now correctly selects the saved table
- [FIX] Loading a settings file now correctly restores deselected attributes
- [FIX] Deselected attributes are now stored by logical name (silent migration from legacy display name format)
- [UPGRADE] Upgraded to .NET Framework 4.8 and XrmToolBox 1.2025.10.74

### 2023.4.20.x
- [NEW] Updated select directory dialog
- [NEW] Refactored logging

## License

[MIT](LICENSE.txt)

## Author

[Rui Sousa](https://github.com/rsousa88)
