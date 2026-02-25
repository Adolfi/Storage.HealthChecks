# Agents.md

This file describes the preferences, coding style, and preferred structure for this repository. All agent-assisted development should follow these guidelines.

## Project Overview

**Storage.HealthChecks** is a NuGet package that provides a collection of health checks for Umbraco CMS to help monitor and maintain media storage. It targets **.NET 10.0** and **Umbraco CMS 17.x**.

## Repository Structure

```
Storage.HealthChecks/           # Main project directory
├── Composers/                  # Umbraco IComposer implementations for DI registration
├── Configuration/              # Configuration model classes bound to appsettings.json
├── Extensions/                 # IUmbracoBuilder extension methods
├── HealthChecks/               # Individual health check implementations
└── Storage.HealthChecks.csproj
```

- **No test project exists** — do not add one unless explicitly requested.
- Build artifacts (`obj/`, `bin/`) are gitignored and must never be committed.
- The solution file (`*.sln`) and any `*.Web` / `*.Umbraco` companion projects are gitignored.

## Coding Style

### C# Conventions

- Use **file-scoped namespaces** (`namespace Storage.HealthChecks.HealthChecks;`).
- **Nullable reference types** are enabled — all reference types must be null-safe.
- **Implicit usings** are enabled — do not add redundant `using` directives that are already globally imported.
- Use `var` for local variable declarations where the type is obvious from context.
- Use `string.Empty` instead of `""` for empty string literals.
- Use `const` for compile-time constant values.
- Private fields use an **underscore prefix** (`_mediaService`, `_logger`).
- Public members use **PascalCase**; local variables and parameters use **camelCase**.
- Prefer **LINQ** for data querying and transformation.
- Keep methods small and focused on a single responsibility.

### Comments and Documentation

- Add **XML doc comments** (`/// <summary>`) on public members and configuration properties.
- Add inline comments only when logic is non-obvious; match the style of existing comments.
- Do not add redundant comments that merely restate the code.

### Error Handling

- Wrap health check logic in `try/catch` blocks.
- Log errors using the injected `ILogger` instance (`_logger.LogError(ex, "...")`).
- Return a `HealthCheckStatus` with `ResultType = StatusResultType.Error` on failure.

### Dependency Injection

- Use **constructor injection** for all dependencies.
- Register services in `Composers/` using Umbraco's `IComposer` pattern.
- Bind configuration sections using `IOptions<T>` and register in `StorageHealthChecksComposer`.

## Health Check Pattern

Every health check must follow this structure:

```csharp
[HealthCheck(
    "<new-unique-guid>",
    "<Human readable name>",
    Description = "<Short description of what it checks.>",
    Group = "Media Storage")]
public class MyHealthCheck : HealthCheck
{
    private const int PageSize = 500;

    private readonly IMediaService _mediaService;
    private readonly ILogger<MyHealthCheck> _logger;

    public MyHealthCheck(
        IMediaService mediaService,
        ILogger<MyHealthCheck> logger)
    {
        _mediaService = mediaService;
        _logger = logger;
    }

    public override Task<IEnumerable<HealthCheckStatus>> GetStatusAsync()
    {
        var status = CheckMyThing();
        return Task.FromResult<IEnumerable<HealthCheckStatus>>(new[] { status });
    }

    public override HealthCheckStatus ExecuteAction(HealthCheckAction action)
    {
        return new HealthCheckStatus("No actions available. Please review manually.")
        {
            ResultType = StatusResultType.Info
        };
    }

    private HealthCheckStatus CheckMyThing()
    {
        try
        {
            // ... check logic ...
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during my health check");
            return new HealthCheckStatus($"Error: {ex.Message}")
            {
                ResultType = StatusResultType.Error
            };
        }
    }
}
```

- All health checks belong in the `HealthChecks/` folder.
- All checks use `Group = "Media Storage"`.
- Each check gets a **new unique GUID** — never reuse an existing one.
- Result HTML messages are built with `StringBuilder` and may include basic inline HTML (`<strong>`, `<ul>`, `<li>`, `<a>`, `<em>`, `<br/>`).
- Use private **nested classes** (not records) for internal data models (e.g., `MediaInfo`, `DuplicateGroup`).
- Paged queries against `IMediaService` use `PageSize = 500` and the standard `while (true) { ... break; }` loop pattern.
- Skip media items with `ContentType.Alias == "Folder"` (case-insensitive).

## Configuration

- Configuration is bound from the `"StorageHealthChecks"` section in `appsettings.json`.
- New configuration options must be added to `StorageHealthCheckConfiguration` with an XML doc comment and a sensible default value.
- Use `_settings.ShouldIgnore(media.Key)` to respect the ignore list where applicable.

## Naming Conventions

| Artifact | Convention | Example |
|---|---|---|
| Health check class | `<Feature>HealthCheck` | `LargeMediaHealthCheck` |
| Health check file | Same as class name | `LargeMediaHealthCheck.cs` |
| Composer class | `<Feature>Composer` | `StorageHealthChecksComposer` |
| Configuration class | `<Feature>Configuration` | `StorageHealthCheckConfiguration` |
| Extension class | `<Feature>Extensions` | `StorageHealthChecksExtensions` |
| Private field | `_camelCase` | `_mediaService` |
| Constant | `UPPER_CASE` or `PascalCase` (prefer `PascalCase` for `const`) | `PageSize`, `SectionName` |

## CI/CD

- Publishing to NuGet is handled by `.github/workflows/publish.yml` and is **manually triggered**.
- Do not add automated publish triggers without explicit approval.
- Package version is set in `Storage.HealthChecks.csproj` — bump it according to semantic versioning when releasing.

## General Preferences

- Make **minimal, surgical changes** — do not refactor unrelated code.
- Do not introduce new NuGet dependencies without explicit approval.
- Do not add a test project, logging framework, or additional CI workflows unless explicitly requested.
- Keep the public API surface small; internal implementation details should remain `private` or `internal`.
- All health check messages are in **English only** — localization is not supported.
