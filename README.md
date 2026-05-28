# Data Migration Tool

An [XrmToolBox](https://www.xrmtoolbox.com/) plugin for migrating reference data between Dataverse / Dynamics 365 environments.

## Features

- Export and import Dataverse rows using JSON or Excel workbooks
- Create portable `.dmtproj` project files that keep table configs, snapshots, execution plans, mappings, ID mappings, and run history together
- Pull source records into named project snapshots, load JSON/Excel files into snapshots, and push snapshots to target environments
- Export project snapshots back to JSON or Excel
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
- Save portable legacy migration settings in `.dmt.json` files, including selected attributes, filters, mappings, Excel export configuration, and import defaults
- Build execution plans in the active project to sequence snapshot, file, export, import, and push steps

## Installation

Install directly from the **XrmToolBox Tool Library** - search for *Data Migration Tool*.

## Quick Workflow

### 1. Connect environments

Open the plugin from the source environment. Use **Environments > Connect Target** to connect one or more target environments.

### 2. Create or open a project

Use the project actions to create or open a `.dmtproj` file. The project stores table configuration, snapshots, mappings, execution plans, ID mappings, and run history in one portable SQLite file.

### 3. Load and select a table

Use **Environments > Reload Tables** to load Dataverse tables from the source environment. Select a table, then choose which attributes should be included.

### 4. Create or load a legacy settings file

Use **Settings File > New...** or **Settings File > Load...** to work with a portable `.dmt.json` settings file. The file stores the selected table, source environment info, deselected attributes, FetchXML filter, mappings, Excel configuration, and import settings.

Legacy `.settings.json` table settings can still be imported with **Import > Import legacy table settings...** and merged into the current `.dmt.json` file.

### 5. Configure filters

Enter FetchXML filter and link-entity nodes in the filter panel. You can send the current filter to FetchXML Builder or SQL 4 CDS from the toolbar integrations.

### 6. Work with snapshots

Use project snapshot actions to pull source data into a named snapshot, load JSON or Excel files into a snapshot, inspect snapshot rows, export snapshots to files, and add snapshots to an execution plan for push operations.

### 7. Export

Use **Export > To JSON** for compact migration data files.

Use **Export > To Excel** when users need a workbook/template they can review or complete manually. The Excel export wizard lets you configure:

- lookup resolution by GUID, alternate key, or custom related columns
- nested lookup key columns, with lookup selection restricted to avoid endless loops
- option set and multi-select option set export as labels
- match key for import by GUID, alternate key, or custom columns
- column order, visibility, and header hints

Excel workbooks include hidden metadata so the import wizard can preload the table, column mappings, match key, and import settings later.

### 8. Import

Use **Import > From JSON**, **Import > From Excel**, or **Import > From Last Exported**. Imports open a preview wizard where you can review row actions, warnings, matching key values, and import settings before writing to Dataverse.

For Excel imports, workbook metadata is used first. If an older workbook does not contain the latest metadata shape, it is upgraded when loaded. After a successful Excel import, the workbook is updated with the record GUIDs and resolved lookup GUIDs for rows that completed successfully.

## Project Files

The current project format is `.dmtproj`. It is a portable SQLite project file that stores:

- source and target environment identities
- table configurations
- snapshots and snapshot column metadata
- source-to-target ID mappings
- organization mappings per source/target pair
- execution plans and step configuration
- run history

## Legacy Settings Files

The legacy settings format is `.dmt.json`. It is still supported for compatibility and can be committed or shared with migration templates when useful.

A settings file contains:

- source environment identity
- selected table metadata
- deselected attributes
- FetchXML filter
- organization mappings
- Excel export configuration
- import settings such as batch size and matching key

Settings are auto-saved during legacy export/import work, including before preview/export/import, after mapping changes, and after Excel export configuration changes.

## Project Snapshots

Snapshots are named copies of table data stored inside a `.dmtproj` project. A snapshot can come from a source pull or from a loaded JSON/Excel file, can be exported back to JSON/Excel, and can be pushed to a connected target environment.

Push configuration supports:

- create/update operation selection
- payload column selection
- matching by record GUID, alternate key, or selected custom columns
- per-lookup matching by source GUID, alternate key, custom columns, or skipped lookup field
- persistent source-to-target ID mappings for later pushes and lookup resolution

## Execution Plans

An execution plan groups multiple steps in the active project and can be validated and run sequentially without manual intervention.

Each step captures:

- the operation, such as source pull, file load, file export, JSON/Excel import/export, or snapshot push
- a table snapshot with selected attributes, FetchXML filter, mappings, and import settings when applicable
- an output path template (exports) or input file path (imports)
- an optional link to a preceding export step so the import reads the output directly
- a failure policy (max failed records, max failed percent, stop on fatal error)
- an optional per-step target environment override

### Creating a plan

Open the **Execution Plan** panel on the right side of the plugin. Use **New** to create a plan in the active project. Add steps through the plan toolbar menus, by linking an import to an existing export step, or by adding a snapshot from the Deploy/snapshot actions. The panel shows status (Ready / Warning / Error) and validation messages for each step.

### Linked steps

When adding an import step, choose **Use output from an execution plan export step** to link it to an earlier export in the same plan. The import reads the export's output file path at execution time, so no manual file selection is needed.

### Executing a plan

Enable or disable individual steps using the checkboxes, then click **Execute**. The plan runs each enabled step in order. A results dialog opens at the end showing per-step status, record counts, and errors. Use **Refresh Counts** when you want heavier preview/count analysis without slowing down structural validation.

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

Manual mappings can be defined for lookup fields. Organization mappings can also be used during import or push when configured.

In project workflows, mappings are stored in the active `.dmtproj` per source/target pair and can be reviewed from **Deploy > Configure Mappings...**.

## Notes

- Default Excel import batch size is capped to reduce Dataverse two-minute timeout risk on tables with plugins or heavy business logic.
- Large Excel imports show row-count warnings before the expensive workbook read starts.
- Result dialogs show failed rows by default, with a checkbox to show all rows and an option to retry failed rows only.

## Release Notes

### 2026.5.28.x
- [NEW] Snapshot refresh can now update one or all snapshots, including Dataverse pulls using saved table/filter/attribute settings and file snapshots from their original JSON or Excel source
- [NEW] File snapshot refresh now prompts for a replacement source file when the saved path is missing, moved, or unavailable on the current machine
- [FIX] Execution plan and snapshot controls now use clearer global versus selected-step action grouping with standardized move arrows and renamed import actions
- [FIX] The DMT working dialog is scoped to the active DMT tab instead of staying above other XrmToolBox plugin tabs
- [FIX] Execution plan and startup layouts were polished, including a 30/70 default split, smaller external-editor buttons, centered startup choices, and less cramped toolbar spacing

### 2026.5.27.x
- [NEW] Project-backed migration workflows are now centered on portable `.dmtproj` files with table configs, snapshots, mappings, execution plans, ID mappings, and run history stored together
- [NEW] Snapshot actions now support pulling source data, loading files into project snapshots, exporting snapshots to JSON or Excel, and pushing snapshots to target environments
- [NEW] Push step configuration now separates payload column selection from GUID, alternate-key, and custom-column matching keys
- [NEW] Push lookup matching can now be configured per lookup column using source GUIDs, alternate keys, custom columns, or skipped fields
- [FIX] Plan validation is faster and separated from explicit count refreshes for heavier preview analysis
- [FIX] Project-local plan file paths are stored relatively and resolved from the `.dmtproj` location
- [FIX] Reconfigure is now available consistently for selected plan steps and no longer depends on preview state
- [FIX] Clean installations now include the SQLitePCLRaw batteries assembly required to create and open `.dmtproj` project files
- [FIX] Legacy mapping UI/code paths and obsolete mapping settings were removed from the active plugin surface

### 2026.5.25.x
- [NEW] Execution plan steps can now be cloned to another target environment from the plan panel or step context menu
- [FIX] Duplicating linked import steps now preserves the source export link and captured configuration while resetting validation for the cloned step

### 2026.5.20.x
- [NEW] Execution plan steps can now be executed manually from the selected step actions
- [NEW] Execution plan step actions are available from a right-click context menu
- [NEW] Added Save As to execution plan actions
- [FIX] Execution plan actions are now grouped into global and step-specific toolbars with state-aware enablement
- [FIX] Execution plan results now use the standard dialog style and show per-step error details with copy support
- [FIX] Execution plan import failures now include the source row and record context for each failed record
- [FIX] Execution plan import previews now honor configured match keys against prior plan files before checking the target environment

### 2026.5.19.x
- [NEW] Execution plan imports can resolve lookups against records imported by earlier steps in the same target environment
- [NEW] Execution plan steps can now be previewed individually and reconfigured before execution, including import settings, match keys, mappings, and file columns
- [FIX] Whole-plan validation now avoids expensive full Excel preview reads and uses lightweight workbook metadata for large files
- [FIX] Step preview now hydrates prior import files when needed so chained lookup dependencies are considered before execution
- [FIX] Adding or reloading Excel import steps now hydrates prior plan imports so lookup dependencies are resolved before the step is added
- [FIX] Excel import setup and step preview now only read prior import files required for lookup matching instead of every previous Excel import
- [FIX] Selecting an execution plan step now selects the referenced table and loads its captured settings file or plan snapshot
- [FIX] JSON and Excel execution-plan imports now persist and honor the captured match key during validation and execution
- [FIX] Loaded execution plans now hydrate table attributes before validation and import preview, preventing null-source validation errors
- [FIX] Stability refactor moved more import/export and execution-plan logic out of the main plugin control for better maintainability
- [NEW] Added tests for plan lookup resolution, import match-key snapshots, and file preview columns

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
