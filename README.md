# Data Migration Tool

An [XrmToolBox](https://www.xrmtoolbox.com/) plugin for migrating reference data between Dataverse / Dynamics 365 environments.

## Features

- Export and import Dataverse rows using JSON or Excel workbooks
- Create portable `.dmtproj` project files that keep table configs, snapshots, execution plans, mappings, ID mappings, and run history together
- Pull source records into named project snapshots, load JSON/Excel files into snapshots, and push snapshots to target environments
- Configure project-specific environment tags so push steps and execution plans are easier to scan across DEV/UAT/PROD-style targets
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

Use the left-side command strip to **Reload Tables** from the source environment. Select a table, then choose which attributes should be included.

### 4. Create or load a legacy settings file

Use **Settings File > New...** or **Settings File > Load...** to work with a portable `.dmt.json` settings file. The file stores the selected table, source environment info, deselected attributes, FetchXML filter, mappings, Excel configuration, and import settings.

Legacy `.settings.json` table settings can still be imported with **Import > Import legacy table settings...** and merged into the current `.dmt.json` file.

### 5. Configure filters

Enter FetchXML filter and link-entity nodes in the filter panel. You can send the current filter to FetchXML Builder or SQL 4 CDS from the filter panel integrations.

### 6. Work with snapshots

Use the **Snapshots** strip to pull source data into a named snapshot, import JSON or Excel files into a snapshot, inspect snapshot rows, refresh one or all snapshots, export snapshots to files, and add snapshots to an execution plan for push operations.

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

Open the **Execution Plan** panel on the right side of the plugin. Use **New** to create a plan in the active project. Add steps through the plan action strip, by linking an import to an existing export step, or by adding a snapshot from the **Snapshots** strip. The panel shows status (Ready / Warning / Error), target environment tags, and validation messages for each step.

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

In project workflows, mappings are stored in the active `.dmtproj` per source/target pair and can be reviewed while configuring or executing push/import workflows.

## Notes

- Default Excel import batch size is capped to reduce Dataverse two-minute timeout risk on tables with plugins or heavy business logic.
- Large Excel imports show row-count warnings before the expensive workbook read starts.
- Result dialogs show failed rows by default, with a checkbox to show all rows and an option to retry failed rows only.

## Release Notes

### 2026.6.1.x
- [NEW] Add to Plan now shows a checkbox environment picker allowing multiple target environments to be selected; one push step is added per checked environment with shared push configuration
- [FIX] Selecting a step in the execution plan no longer drives the left-side table, attribute, and filter selection
- [NEW] Rowcraft connector now points to the production domain (rowcraft.io)
- [NEW] Opening a snapshot in Rowcraft for the first time shows a one-time beta disclaimer confirming no data leaves the device and nothing is stored externally
- [FIX] Snapshot toolbar reordered: Pull / Import / Export, then Rowcraft (Beta), Add to Plan, Refresh, move arrows, with the expand button pinned to the far right
- [FIX] Refresh and Refresh All are now grouped under a single Refresh dropdown
- [FIX] Snapshot viewer is now a right-aligned expand icon (⛶) instead of a text button

### 2026.5.29.x
- [NEW] Added Rowcraft connector integration for opening project snapshots in Rowcraft through a local authenticated bridge without uploading `.dmtproj` data to Rowcraft cloud storage
- [NEW] Rowcraft can stage snapshot row creates, updates, and deletes back to DMT while DMT remains the system of record and applies changes only when the user clicks Apply Rowcraft
- [NEW] Snapshot lists now show pending Rowcraft change counts and include actions to open, apply, or discard Rowcraft edits
- [FIX] Rowcraft snapshot actions are now grouped under a Rowcraft toolbar button with icon, and the startup guide/tips are shorter and easier to scan
- [FIX] Applying Rowcraft changes updates snapshot row counts and marks dependent execution-plan previews stale before push
- [NEW] Project-specific environment tags can now be configured and used in push step names, execution plan target cells, and target selectors
- [NEW] Configure Push Step now lets users change the target environment and updates the step name with the selected environment tag
- [FIX] The top command bar now contains only global actions, with table actions moved to the left-side strip and snapshot/plan actions kept in their local strips
- [FIX] Startup instructions and working tips now describe the project, snapshot, environment tag, and execution plan workflow
- [FIX] Removed the trailing separator from the Project dropdown

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

## License

[MIT](LICENSE.txt)

## Author

[Rui Sousa](https://github.com/rsousa88)
