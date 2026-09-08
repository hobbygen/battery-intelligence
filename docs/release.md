# Release Runbook

Status: 1.0.0. Covers spec §58 (distribution, data preservation) and §69 (the
completed doc set). Phase 15.

This is the step-by-step for cutting a release. Items marked **(clean VM)** need a
second machine and cannot be done from the dev box.

---

## 1. Pre-flight gates

All must pass on the release branch before anything is packaged.

| Gate | Command | Bar |
|---|---|---|
| Build | `dotnet build BatteryIntelligence.slnx -c Debug` and `-c Release` | 0 warnings, 0 errors |
| Tests | `dotnet test BatteryIntelligence.slnx -c Debug` | all green (run the integration suite twice — it has SQLite-file-lock flakiness right after a build) |
| No simulation in Release | `powershell -File tools/verify-no-simulation.ps1` | `PASS` |
| Version | `Directory.Build.props` `<Version>` and `Package.appxmanifest` `<Identity Version>` agree | e.g. `1.0.0` / `1.0.0.0` |
| Manual QA | `docs/qa-checklist.md` §A–§H | signed off for this build |
| Docs | every `docs/*.md` `Status:` header current; `CHANGELOG.md` has this version | — |

`verify-no-simulation.ps1` builds `-c Release` and checks the shipping assemblies
(`BatteryIntelligence*.dll`, `.Battery.dll`, `.Core.dll`) contain no
`SimulatedBatteryProvider` / `BatterySimulationScenario` / `Fake*` type — the
`SIMULATION` symbol is Debug-only and every simulation file is `#if SIMULATION`,
so Release is clean by construction; this is the belt-and-braces check (prd.md N9).

---

## 2. Versioning

One version, two files (there is a comment in each pointing at the other):

1. `Directory.Build.props` — `<Version>`, `<AssemblyVersion>`, `<FileVersion>`,
   `<InformationalVersion>`.
2. `src/BatteryIntelligence.App/Package.appxmanifest` — `<Identity Version="x.y.z.0">`
   (four-part; the revision stays `0`).

Then add a `## x.y.z` section to `CHANGELOG.md` and update the `Status:` line in
`docs/roadmap.md`.

---

## 3. Unpackaged distribution (the tested path for 1.0)

Framework-dependent — the Windows App Runtime 1.8 is a prerequisite on the target.

```
dotnet publish src/BatteryIntelligence.App/BatteryIntelligence.App.csproj `
  -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=false
```

Output: `src/BatteryIntelligence.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/`.
Ship the folder as a zip. It contains `BatteryIntelligence.exe`, the managed
assemblies, `Assets/`, and the SkiaSharp native binaries (`libSkiaSharp.dll`,
`libHarfBuzzSharp.dll`).

First run:
- If the Windows App Runtime is missing, WinUI shows a dialog with a download
  link. Bundle the runtime installer or document the winget/Store link in the
  release notes.
- The app creates `%LocalAppData%\BatteryIntelligence` on first launch.
- "Start with Windows" (Settings) registers a Run-key shortcut for the
  unpackaged build.

### Self-contained variant (no prerequisites)

For a machine with **neither** .NET 10 nor the Windows App Runtime:

```
dotnet publish src/BatteryIntelligence.App/BatteryIntelligence.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:WindowsAppSDKSelfContained=true -p:PublishReadyToRun=false `
  --output dist/self-contained
```

Bundles the .NET runtime (`coreclr.dll`) and the Windows App SDK
(`Microsoft.WindowsAppRuntime.dll`, `CoreMessagingXP.dll`, `Microsoft.ui.xaml.dll`).
~247 MB on disk (~95 MB zipped) vs ~69 MB / ~22 MB for the framework-dependent
build. Runs on any x64 Windows 10 17763+ with nothing pre-installed. This is the
right artifact for a "download and run" release; the framework-dependent build is
smaller when the target already has the runtimes (e.g. a dev box).

---

## 3a. Setup.exe installer (recommended for "install on any laptop")

A single self-contained, **machine-wide** installer built with Inno Setup 6 over
the §3 self-contained publish. Runs on any x64 Windows 10 17763+ / 11 with
**nothing pre-installed** — the .NET 10 runtime and the Windows App SDK are
bundled. No code-signing certificate is needed to produce it (an unsigned
installer shows a SmartScreen prompt on first run — "More info → Run anyway";
sign it for a public release).

One UAC prompt at install time (`PrivilegesRequired=admin`); the app itself
still runs unelevated. The uninstall entry then shows in **both** the classic
Control Panel → Programs and Features **and** Settings → Apps. (A per-user build —
`PrivilegesRequired=lowest` — only shows in Settings → Apps; that was the 1.0.0
first cut and was changed because users expect the Control Panel entry.)

One-shot build:

```
winget install --id JRSoftware.InnoSetup      # once
powershell -ExecutionPolicy Bypass -File tools/build-installer.ps1
```

That publishes self-contained, then compiles `tools/installer/BatteryIntelligence.iss`.

Output: `dist/BatteryIntelligence-Setup-<version>.exe` (~67 MB) + `dist/SHA256SUMS.txt`.

What it does on the target:
- Installs to `C:\Program Files\Battery Intelligence` (override on the wizard's
  directory page or with `/DIR=`).
- All-users Start-menu shortcut; optional desktop shortcut (unticked by default).
- Registers a machine-wide uninstall entry (classic Control Panel + Settings → Apps).
- **Closes a running `BatteryIntelligence.exe` first** — on both install and
  uninstall. The app minimises to the notification area, so a plain window close
  is turned into a hide (`MainWindow.OnAppWindowClosing`); the installer therefore
  `taskkill`s it (politely, then `/F`) in `PrepareToInstall` / `CurUninstallStepChanged`.
  Without this the running exe locks its own files and the uninstall silently
  leaves the folder and the tray icon behind.
- `[UninstallDelete]` removes the whole `{app}` folder after the tracked files,
  as a backstop against a late-clearing lock.
- **Never creates or touches `%LOCALAPPDATA%\BatteryIntelligence`** (the database
  + settings) — that stays the app's, and uninstall leaves it intact (spec §58).
- "Start when I sign in" is left to the app's own Settings toggle, not the
  installer.

A `taskkill /F` can leave a **ghost tray icon** until the mouse passes over the
notification area (Windows removes dead-process icons lazily) — cosmetic, clears
on hover or next sign-in. Closing the app first (tray → Exit) avoids it.

Silent install / uninstall (for imaging or scripted rollout — run elevated):

```
BatteryIntelligence-Setup-1.0.0.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
"C:\Program Files\Battery Intelligence\unins000.exe" /VERYSILENT
```

Verified 2026-09-08 on the reference machine (per-user test build): silent
install → 525 files, shortcuts, uninstall key; silent uninstall **with the app
running** → process killed, install dir and uninstall key removed,
`%LOCALAPPDATA%\BatteryIntelligence` preserved.

### Publishing a release

1. `powershell -ExecutionPolicy Bypass -File tools/build-installer.ps1`
   (publishes self-contained, compiles the installer, writes `dist/SHA256SUMS.txt`).
2. Re-zip the self-contained folder if you ship that too:
   `Compress-Archive dist/self-contained/* dist/BatteryIntelligence-1.0.0-win-x64-self-contained.zip`.
3. `gh release create v<version> dist/BatteryIntelligence-Setup-<version>.exe
   dist/BatteryIntelligence-1.0.0-win-x64-self-contained.zip dist/SHA256SUMS.txt
   --title "Battery Intelligence <version>" --notes-file <notes> --latest`.
4. The website (`website/`, deployed to `battery-intelligence.netlify.app` — a
   static site, no build step) links the button to
   `…/releases/download/v<version>/BatteryIntelligence-Setup-<version>.exe`.
   Bump the version, hash and size in `website/index.html` and re-deploy
   (`netlify deploy --dir website --prod`).

The repository is **public** so the release asset downloads anonymously.

---

## 4. MSIX build

Off by default; opt in with `EnablePackaging`:

```
dotnet build src/BatteryIntelligence.App/BatteryIntelligence.App.csproj `
  -c Release -p:EnablePackaging=true -p:Platform=x64
```

or, for the full appx pipeline (needs the MSIX build tools / VS):

```
msbuild src/BatteryIntelligence.App/BatteryIntelligence.App.csproj `
  /p:Configuration=Release /p:Platform=x64 /p:EnablePackaging=true `
  /p:GenerateAppxPackageOnBuild=true /p:AppxBundle=Never
```

Output: `…/AppPackages/BatteryIntelligence_x.y.z.0_x64_Test/` with the `.msix`.

The manifest (`Package.appxmanifest`) declares only `runFullTrust` — the same
access the unpackaged app has (ACPI / power-management interfaces, process
enumeration). No broad or sensitive capability. It also registers a
`windows.startupTask` so "Start with Windows" works the packaged way.

**Visual assets** (`app.ico`, the MSIX tile PNGs, and the website icons) are
generated by `tools/generate-app-icon.ps1` from
`branding/Battery_Intelligence_Icon.png` — it rescales the source art to each
target size and flood-fills the white border to transparency. Re-run it if the
branding art changes.

---

## 5. Signing

MSIX must be signed; the certificate's subject **must equal** the manifest's
`Publisher` (`CN=Battery Intelligence` is the placeholder — change both together
for a real cert).

Development / sideload:
```
New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=Battery Intelligence" `
  -KeyUsage DigitalSignature -FriendlyName "Battery Intelligence Dev" `
  -CertStoreLocation "Cert:\CurrentUser\My" `
  -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")

signtool sign /fd SHA256 /a /f BatteryIntelligence.pfx /p <pwd> `
  BatteryIntelligence_x.y.z.0_x64.msix
```

Public release: use a real code-signing certificate (OV or EV) from a CA. The
signing cert's root must be trusted on the target machine (self-signed → import
into `Trusted People` / `Trusted Root` for sideload testing).

---

## 6. Install / upgrade / uninstall verification  **(clean VM)**

Use a fresh Windows 11 VM (and repeat the render checks on Windows 10 —
`qa-checklist.md` §B7).

1. **Fresh unpackaged install.** Unzip, run `BatteryIntelligence.exe`. Confirm:
   `%LocalAppData%\BatteryIntelligence\battery.db` is created; the app records
   samples; Settings → data folder shows that real path (not a `Packages\…` path).
2. **Let it collect a few minutes of history**, note the sample count on
   Diagnostics.
3. **Install the MSIX over it.** Confirm: the app still opens the **same
   database** — the history from step 2 is intact, settings are intact. (This is
   the point of resolving the data path from `%LOCALAPPDATA%` rather than the
   `SpecialFolder` API — an MSIX would otherwise get a package-private
   `LocalCache` folder and appear to have lost everything.)
4. **Upgrade.** Bump the version (§2), rebuild + sign, install over the top.
   Confirm: version shown on About updated; history and settings intact; if the
   build carries a new schema version, `battery.db.bak-vNNN` was written and no
   rows were lost (`MigrationTests` covers the mechanism; verify the file appears).
5. **Uninstall** the MSIX. Confirm: the app is gone from Start; **`%LocalAppData%\BatteryIntelligence`
   still exists with the database** (spec §58 — uninstall must not destroy user
   history).
6. **Re-install** and confirm it picks the old database back up.

Record pass/fail against `qa-checklist.md` §G4/§G5 and here.

---

## 7. Rollback

The MSIX is versioned; installing an older signed package over a newer one
requires an uninstall first (Windows blocks a downgrade). User data is unaffected
either way — it is never in the package. For the unpackaged build, rollback is
just shipping the previous zip. A schema that moved forward (`V00N`) is
forward-only; the `battery.db.bak-vNNN` from the upgrade is the recovery point if
a migration is later found to be faulty.

---

## 8. Known release-time deviations (1.0.0)

- **Working set** ~230–270 MB with the window open vs a 150 MB target. This is
  the WinUI 3 + SkiaSharp + Windows App SDK runtime baseline (~120–160 MB of it
  is native runtime the app does not control). Reducing it would mean going
  self-contained + trimmed (large download, trimming risk with WinUI) or dropping
  the charting library. **Accepted for 1.0**; the Diagnostics footprint section
  reports the real number honestly, and adaptive sampling keeps CPU and disk well
  under budget. Revisit if a future WinAppSDK meaningfully lowers the floor.
- **The MSIX is authored but not built/signed/installed in-house** for the 1.0
  cut — no CA certificate. The manifest, build config, asset generator and this
  runbook are complete; §6 must be executed by whoever holds the cert.
- **Distribution is unpackaged-first.** For "install on any laptop" the tested
  artifact is the self-contained `Setup.exe` from §3a (no prerequisites,
  machine-wide, one UAC prompt at install). The framework-dependent zip (§3)
  stays the smallest option when the target already has the runtimes. The
  MSIX (§4) is for whoever holds a cert.
