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

### 2026.4.29.1
- [FIX] Loading a settings file now correctly selects the saved table
- [FIX] Loading a settings file now correctly restores deselected attributes
- [FIX] Deselected attributes are now stored by logical name (silent migration from legacy display name format)
- [UPGRADE] Upgraded to .NET Framework 4.8 and XrmToolBox 1.2025.10.74

### 2023.4.20.2
- [NEW] Updated select directory dialog
- [NEW] Refactored logging

## License

[MIT](LICENSE.txt)

## Author

[Rui Sousa](https://github.com/rsousa88)
