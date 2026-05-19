# Data Migration Tool

An [XrmToolBox](https://www.xrmtoolbox.com/) plugin for migrating reference data between Dataverse / Dynamics 365 environments.

## Features

- Export and import Dataverse rows using JSON or Excel workbooks
- Supports **Create**, **Update**, and **Delete** operations for JSON imports, and create/update workflows for Excel imports
- Preview imports before execution, including row numbers, create/update decisions, warnings, mapping count, and matching key details
- Match import rows by record GUID, Dataverse alternate keys, or multiple selected custom columns
- Export Excel templates that non-technical users can review and complete
- Resolve lookup values on Excel import by GUID, alternate key, or selected related columns, including nested lookup key columns
- Export option set and multi-select option set values as labels for easier manual editing
- Write successful record and lookup GUIDs back to Excel after import
- Filter source rows with FetchXML, including link-entity filters for related tables
- Integrate with [FetchXML Builder](https://www.xrmtoolbox.com/plugins/Cinteros.Xrm.FetchXmlBuilder/) and SQL 4 CDS
- Select the attributes included in the migration and hide invalid attributes when configuring tables
- Define organization mappings for users, teams, business units, and lookup values
- Save portable migration settings in `.dmt.json` files, including selected attributes, filters, mappings, Excel export configuration, and import defaults
- Build execution plans (`.dmtplan.json`) to sequence multiple export and import steps and run them unattended

## Installation

Install directly from the **XrmToolBox Tool Library** - search for *Data Migration Tool*.

## Quick Workflow

### 1. Connect environments

Open the plugin from the source environment. Use **Environments > Connect Target** to connect the target environment. Use **Environments > Switch Source / Target** when you need to reverse the migration direction.

### 2. Load and select a table

Use **Environments > Reload Tables** to load Dataverse tables from the source environment. Select a table, then choose which attributes should be included.

### 3. Create or load a settings file

Use **Settings File > New...** or **Settings File > Load...** to work with a portable `.dmt.json` settings file. The file stores the selected table, source environment info, deselected attributes, FetchXML filter, mappings, Excel configuration, and import settings.

Legacy `.settings.json` table settings can still be imported with **Import > Import legacy table settings...** and merged into the current `.dmt.json` file.

### 4. Configure filters

Enter FetchXML filter and link-entity nodes in the filter panel. You can send the current filter to FetchXML Builder or SQL 4 CDS from the toolbar integrations.

### 5. Export

Use **Export > To JSON** for compact migration data files.

Use **Export > To Excel** when users need a workbook/template they can review or complete manually. The Excel export wizard lets you configure:

- lookup resolution by GUID, alternate key, or custom related columns
- nested lookup key columns, with lookup selection restricted to avoid endless loops
- option set and multi-select option set export as labels
- match key for import by GUID, alternate key, or custom columns
- column order, visibility, and header hints

Excel workbooks include hidden metadata so the import wizard can preload the table, column mappings, match key, and import settings later.

### 6. Import

Use **Import > From JSON**, **Import > From Excel**, or **Import > From Last Exported**. Imports open a preview wizard where you can review row actions, warnings, matching key values, and import settings before writing to Dataverse.

For Excel imports, workbook metadata is used first. If an older workbook does not contain the latest metadata shape, it is upgraded when loaded. After a successful Excel import, the workbook is updated with the record GUIDs and resolved lookup GUIDs for rows that completed successfully.

## Settings Files

The current settings format is `.dmt.json`. It is intended to be portable and can be committed or shared with migration templates when useful.

A settings file contains:

- source environment identity
- selected table metadata
- deselected attributes
- FetchXML filter
- organization mappings
- Excel export configuration
- import settings such as batch size and matching key

Settings are auto-saved during normal work, including before preview/export/import, after mapping changes, and after Excel export configuration changes.

## Execution Plans

An execution plan (`.dmtplan.json`) groups multiple export and import steps into a single file that can be validated and run sequentially without manual intervention.

Each step captures:

- the operation (ExportToJson, ExportToExcel, ImportFromJson, ImportFromExcel)
- a table snapshot with selected attributes, FetchXML filter, mappings, and import settings
- an output path template (exports) or input file path (imports)
- an optional link to a preceding export step so the import reads the output directly
- a failure policy (max failed records, max failed percent, stop on fatal error)
- an optional per-step target environment override

### Creating a plan

Open the **Execution Plan** panel on the right side of the plugin. Use **New** to create a `.dmtplan.json` file. Add export and import steps through the toolbar menus or by linking an import to an existing export step. Steps are validated automatically — the panel shows status (Ready / Warning / Error) and validation messages for each step.

### Linked steps

When adding an import step, choose **Use output from an execution plan export step** to link it to an earlier export in the same plan. The import reads the export's output file path at execution time, so no manual file selection is needed.

### Executing a plan

Enable or disable individual steps using the checkboxes, then click **Execute**. The plan runs each enabled step in order. A results dialog opens at the end showing per-step status, record counts, and errors.

## Excel Imports

Excel files exported by the tool are self-describing. The hidden `_dmt` metadata sheet stores the table, columns, lookup resolution, option-set mode, match key, and import defaults.

Excel import supports:

- creating rows when the record GUID is blank or hidden
- updating rows when a match is found by GUID, alternate key, or custom key
- resolving lookup GUIDs from related columns selected during export
- using option-set labels instead of raw integer values
- showing row-level warnings in the preview wizard
- writing successful record and lookup GUIDs back to the workbook after import

## Mappings

Manual mappings can be defined for lookup fields. Organization mappings can also be used during import when configured in the import wizard.

Mappings are stored in `.dmt.json` settings files and can be reviewed from the **Mappings** button in the main toolbar.

## Notes

- Default Excel import batch size is capped to reduce Dataverse two-minute timeout risk on tables with plugins or heavy business logic.
- Large Excel imports show row-count warnings before the expensive workbook read starts.
- Result dialogs show failed rows by default, with a checkbox to show all rows and an option to retry failed rows only.

## Release Notes

### 2026.5.19.x
- [FIX] Loaded execution plans now hydrate table attributes before validation and import preview, preventing null-source validation errors

### 2026.5.19.x
- [NEW] Execution plan imports can resolve lookups against records imported by earlier steps in the same target environment
- [FIX] JSON execution-plan imports now persist and honor the selected match key during validation and execution
- [FIX] Excel execution-plan imports reapply the captured match key when the plan runs
- [NEW] Added tests for plan lookup resolution and import match-key snapshot persistence
- [FIX] Stability refactor moved more import/export and execution-plan logic out of the main plugin control for better maintainability

### 2026.5.15.x
- [NEW] Added execution plans with saved `.dmtplan.json` files, linked steps, validation, review, and unattended sequential execution
- [NEW] Added multi-target environment support so import steps can run against different connected target environments in one plan
- [NEW] Added an always-visible execution plan panel with per-step environment pickers, validation messages, and execution controls
- [NEW] Added startup instructions, toolbar instructions access, and custom working dialogs with rotating tips and abort support
- [NEW] Excel export configuration now uses a guided wizard with lookup, option set, column, and review steps
- [FIX] Export operations no longer ask for target environments
- [FIX] File-based imports now require a target before previewing create/update counts
- [FIX] Importing files for a different table now selects the referenced table automatically
- [FIX] Startup and plan UI rendering were adjusted to reduce visible UI hangs
- [FIX] Execution plan panel no longer throws a SplitContainer sizing error on startup

### 2026.5.13.x
- [FIX] Excel imports now write generated record GUIDs and resolved lookup GUIDs back to newly-created workbook rows
- [FIX] Excel import preview now warns about supplied record GUIDs and skips duplicate record GUID rows
- [FIX] Import preview disables Import when settings changes require a preview refresh

### 2026.5.7.x
- [FIX] Excel imports can now create new rows when the record GUID column is blank or hidden
- [FIX] Excel lookup columns using custom or alternate key fields no longer require the lookup GUID cell to be populated
- [FIX] Settings files can now be loaded before selecting a table; the matching table is selected automatically
- [FIX] Import preview refreshes only when match-key changes require re-reading the workbook
- [NEW] Large Excel imports now show row-count warnings before the expensive read starts
- [FIX] JSON imports now use JSON-specific wizard labels and expose match-key configuration
- [FIX] Import from last exported now opens the import wizard for both JSON and Excel files
- [NEW] Excel exports now store import settings in workbook metadata
- [FIX] Older Excel exports are upgraded with import settings metadata when loaded
- [FIX] Import wizard now warns when Excel-loaded import settings are overridden
- [NEW] Excel imports now write successful record and lookup GUIDs back to the workbook
- [FIX] Workbook GUID writeback failures are reported without hiding completed import results

### 2026.5.6.x
- [NEW] Settings files now save and load table configuration in the new `.dmt.json` format
- [NEW] Legacy table settings can now be imported into a settings file with table validation
- [NEW] Import result dialogs now show row numbers, success/failed summaries, failed-row filtering, and retry for failed rows
- [NEW] Excel and JSON import previews now show source row numbers and row-level warnings
- [NEW] Long-running preview, export, and import operations now report clearer progress and log detailed errors
- [FIX] Legacy settings migration now preserves filters and selected table attributes
- [FIX] Excel and JSON imports now validate the loaded settings file table before reading source rows
- [FIX] Preview now validates missing FetchXML link-entity aliases and shows a friendly error instead of crashing
- [FIX] Result and import preview tables now sort row numbers numerically and support cleaner copy/sort behavior
- [FIX] Import wizard layout was polished and user/team auto-map options were removed in favor of explicit mappings
- [FIX] Import wizard now uses safer default batch sizes for Dataverse timeout-sensitive tables
- [FIX] Import progress now shows processed record counts, percentage, and error counts during execution
- [FIX] Organization mapping option in the import wizard now has clearer wording

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
- [NEW] Integration with SQL 4 CDS - filter can now be opened and edited in SQL 4 CDS
- [NEW] Filter now supports link-entity nodes for filtering by related tables
- [FIX] XML parsing error when sending filters with link-entity nodes to external query builders
- [FIX] External plugin calls now properly isolated - prevents crashes if target plugin is not open
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
