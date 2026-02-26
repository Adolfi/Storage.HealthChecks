# Storage Health Checks for Umbraco

A collection of health checks for Umbraco CMS that help you monitor and maintain your media storage. These checks identify potential issues with media files, helping you keep your Umbraco installation clean and efficient.

## Requirements

- Umbraco CMS 17.0 or later
- .NET 10.0 or later

## Installation

Install the NuGet package:

```bash
dotnet add package Storage.HealthChecks
```

That's it! The health checks will automatically appear in the Umbraco backoffice under **Settings → Health Check → Media Storage**.

> **Note:** The package uses an Umbraco Composer to auto-register the configuration. No code changes are required in your `Program.cs`.

## Health Checks

### Duplicate Media Items
Identifies duplicate media files based on filename and file size. Helps you find files that have been uploaded multiple times, wasting storage space.

**Status:** Info  
**What it checks:**
- Groups media items by filename and file size
- Identifies groups with more than one file
- Calculates wasted storage space from duplicates

[See example](https://github.com/Adolfi/Storage.HealthChecks/blob/main/Storage.HealthChecks/Demo/duplicate.png?raw=true)

---

### Large Media Items
Finds media files that exceed a configurable size threshold (default: 5 MB). Large files can slow down page load times and consume excessive storage.

**Status:** Info  
**What it checks:**
- Scans all media items for files larger than the configured threshold
- Reports total excess storage used
- Lists files sorted by size (largest first)

[See example](https://github.com/Adolfi/Storage.HealthChecks/blob/main/Storage.HealthChecks/Demo/large.png?raw=true)

---

### Missing Media Files
Detects media items in the database that are missing their physical files on disk. This is a critical issue that can cause broken images on your website.

**Status:** Error  
**What it checks:**
- Compares database entries with physical files
- Identifies media items where the file no longer exists
- Common causes: failed migrations, disk issues, manual file deletion

[See example](https://github.com/Adolfi/Storage.HealthChecks/blob/main/Storage.HealthChecks/Demo/missing.png?raw=true)

---

### Orphaned Media Files
Finds physical files in the `/media` folder that have no corresponding database entry. These files take up space but are not managed by Umbraco.

**Status:** Warning  
**What it checks:**
- Scans physical media folder
- Compares with database entries
- Identifies files that can be safely removed

[See example](https://github.com/Adolfi/Storage.HealthChecks/blob/main/Storage.HealthChecks/Demo/orphaned.png?raw=true)

---

### Unused Media Items
Identifies media items that have no tracked references from any Umbraco content. These may be candidates for deletion.

**Status:** Warning  
**What it checks:**
- Uses Umbraco's tracked references service
- Finds media not used in any content properties
- Note: May still be used via hardcoded URLs in templates

[See example](https://github.com/Adolfi/Storage.HealthChecks/blob/main/Storage.HealthChecks/Demo/unused.png?raw=true)

---

### Disallowed Media File Extensions
Detects physical media files whose extensions are listed in Umbraco's `DisallowedUploadedFileExtensions` setting. These files may pose a security risk and should be reviewed.

**Status:** Warning  
**What it checks:**
- Scans all physical files in the configured media storage
- Compares each file's extension against `ContentSettings.DisallowedUploadedFileExtensions`
- Reports files that bypassed Umbraco's upload validation
- Common causes: direct FTP/blob uploads, older configs that permitted the extension, custom upload endpoints, migrations

[See example](https://github.com/Adolfi/Storage.HealthChecks/blob/main/Storage.HealthChecks/Demo/disallowed.png?raw=true)

---

## Configuration

### Ignore Lists

You can configure an ignore list for the **Unused Media** and **Large Media** health checks by specifying media item GUIDs. Add the following to your `appsettings.json`:

```json
{
  "StorageHealthChecks": {
    "IgnoredMediaIds": [
      "00000000-0000-0000-0000-000000000000",
      "11111111-1111-1111-1111-111111111111"
    ],
    "LargeMediaThresholdMB": 5.0
  }
}
```

#### Configuration Options

| Option | Type | Description |
|--------|------|-------------|
| `IgnoredMediaIds` | `Guid[]` | Array of media item GUIDs to ignore |
| `LargeMediaThresholdMB` | `double` | Maximum file size in MB before considered "large" (default: 5.0) |
| `DisallowedExtensionsScanMaxFiles` | `int` | Maximum number of files to scan for disallowed extensions before aborting (default: 50,000) |
| `DisallowedExtensionsScanTimeBudgetSeconds` | `int` | Time budget in seconds for the disallowed extensions scan before aborting (default: 5) |



