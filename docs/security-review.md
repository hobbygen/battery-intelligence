# Security Review

Status: Phase 14. Version 1.0.0. Covers spec §46 and requirement **R-100**.

Battery Intelligence has a deliberately small attack surface: a local desktop
utility, no server, no account, no network. This review walks the properties
spec §46 requires and records the result of auditing each against the code as of
Phase 14.

## 1. No administrator privileges (N2)

- `src/BatteryIntelligence.App/app.manifest` declares
  `requestedExecutionLevel level="asInvoker" uiAccess="false"`. There is no
  elevation prompt and no `runas`.
- Grep for elevation APIs (`ShellExecute … runas`, `AdjustTokenPrivileges`,
  `SeDebugPrivilege`) across `src/` — **none present**.
- Every capability that would need elevation (ETW per-process energy, `powercfg
  /requests`, SRUM) is reported **Unavailable** on the Diagnostics page rather
  than attempted — see `capability-matrix.md` C21–C23.
- Verified live: the Diagnostics "Application" section shows `Elevated: No (as
  designed)` on the reference machine.

## 2. Parameterised SQL — no injection surface

Every `SqliteCommand.CommandText` in `src/BatteryIntelligence.Data` was audited.

- **Values** are *always* bound with `$`-parameters (`AddWithValue` /
  `Parameters.Add`). No user-supplied value is ever concatenated into SQL.
- **Identifiers** (table and column names) are interpolated in three readers —
  `HistoryReadStore`, `ExportDataSource`, `HistoryMaintenance` — because SQLite
  cannot parameterise an identifier. In every case the identifier comes from a
  **closed, hard-coded set**:
  - `HistoryReadStore` — a `switch` over the `HistoryMetric` / `HistoryTier`
    enums returning string literals.
  - `ExportDataSource` — a `private static readonly TableSpec[]` whose `Table` /
    `Select` / `RangeColumn` are literals in source.
  - `HistoryMaintenance` — a `private static readonly string[]` of table names in
    source.
  None of these is reachable from `settings.json`, a text box, a file the user
  supplies, or any network input. Adding a new value to those sets is a code
  change reviewed like any other.
- Repositories (`src/BatteryIntelligence.Data/Repositories/`) use static SQL
  strings with bound parameters throughout.

**Result: no SQL injection surface.** Covered going forward by `QueryPlanTests`
(which run the exact hot-path SQL) and the write-queue / store integration tests.

## 3. Safe path handling

- The database path is `AppPaths.DatabaseFile(...)`; logs are `AppPaths.LogsDirectory`;
  settings are `AppPaths.SettingsFile`. All resolve under `LocalApplicationData`
  or a directory the user explicitly configured in Settings (validated, and only
  ever used as a *directory*, never composed with untrusted segments).
- **Export** (`App.Services.ExportService`) writes only to a path returned by the
  OS `FileSavePicker`. The app never writes to a path taken from configuration,
  a text box, or a file's contents (Phase 11).
- **Log viewer** (`App.Services.LogFileReader`) only ever opens
  `app-*.log` files it enumerates inside `AppPaths.LogsDirectory` — the pattern
  and directory are both fixed.
- `process-groups.json` (the optional grouping override) is read from a fixed
  path and parsed as data; a malformed file falls back to the built-in table.

## 4. No network access (N3)

- Grep for `HttpClient`, `WebRequest`, `Socket`, `TcpClient`, `Dns`,
  `ClientWebSocket`, `new Uri("http…")` across `src/` — **none present** in any
  shipping project.
- No package in the dependency tree performs background telemetry (Serilog sinks
  are File + Debug only; LiveChartsCore / SkiaSharp render locally; H.NotifyIcon
  is a tray shell).
- The only URLs in the codebase are documentation links in comments and the
  About page's acknowledgements text — none are fetched.

## 5. No secrets, no personal data leakage

- There are no credentials, tokens or keys anywhere in the app — it authenticates
  to nothing.
- Serilog output templates (`App.xaml.cs`) carry `Timestamp`, `Level`,
  `SourceContext`, `Message`, `Exception` — no field that would contain a
  credential or a document's contents. Sample values (percentages, millivolts,
  process names) are not sensitive.
- The **"Copy report"** action on Diagnostics redacts the user-profile path to
  `%USERPROFILE%` before putting the text on the clipboard (Phase 12), so a
  pasted report carries no username.
- The database and logs live in the user's own profile with default ACLs; they
  are not shared or uploaded. The user can export or delete all of it from the
  app.

## 6. Input trust boundaries

| Input | Trust | Handling |
|---|---|---|
| `settings.json` | Semi-trusted (user-editable) | Deserialised then **validated** (`AppSettings.Validate` / per-category `Validate`); out-of-range values are clamped, a malformed file yields defaults |
| `process-groups.json` | Semi-trusted | Parsed as data; malformed → built-in table |
| SQLite database | Trusted (app-owned) but **checked** | `PRAGMA quick_check` at startup (Phase 14); a corrupt file is never deleted, the app degrades and says so |
| Battery / OS APIs | Trusted OS | Sentinel + plausibility validation before any value is stored or shown (`monitoring-dataflow.md` §4) |
| Export destination | User choice via OS picker | The only writable path outside the profile |

## 7. Findings

**None requiring a code change.** The interpolated-identifier SQL in §2 is called
out so a future contributor does not widen those closed sets to accept external
input without re-reviewing. The corrupt-database path was hardened in Phase 14
(integrity check + honest degradation) — see `testing.md` §5 and
`DatabaseFailureTests`.
