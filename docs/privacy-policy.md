# Privacy Policy — Battery Intelligence

**Last updated: 8 September 2026**

Battery Intelligence ("the App") is a Windows desktop application created and
distributed by **Naeem Ahmad** ("we", "us"). This policy explains what the App
and its website do and do not do with your information. The short version: the
App runs entirely on your computer, collects nothing about you, and sends
nothing anywhere.

## 1. The application does not collect or transmit any data

- The App has **no account, no sign-in, and no cloud component**.
- The App performs **no network requests of any kind**. It does not phone home,
  check for updates, load remote content, or send crash reports or analytics.
- There is **no telemetry, no usage tracking, and no advertising**.

You can verify this: the App requests no network capability, and its source code
is public at
`https://github.com/hobbygen/battery-intelligence`.

## 2. What the application reads from your computer

To do its job, the App reads the following from your own system, using standard
Windows interfaces and **without administrator rights**:

- Battery information from the ACPI / power-management interfaces: charge level,
  capacity, voltage, current, cycle count, temperature (where your hardware
  exposes it), manufacturer, model, chemistry and serial number.
- System power state: whether you are on AC or battery, screen on/off, device
  lock/unlock, and sleep/resume events.
- The list of running processes and their CPU time and memory use, which the App
  uses to estimate which applications are drawing power.

This information is processed **only on your device**. It is never sent off the
device by the App.

## 3. Where the application stores data, and for how long

All data the App keeps is written to a single folder on your PC:

```
%LocalAppData%\BatteryIntelligence
```

This contains a local SQLite database (battery, power, temperature, session,
process and analytics history), your settings file, and rotated diagnostic log
files. Nothing in this folder leaves your machine unless you choose to export or
share it.

- **Retention** is controlled by you in **Settings → Data**. Raw samples are
  rolled up and pruned on a schedule you configure; sessions and health
  snapshots are kept for long-term trends.
- **You are in control.** You can export all history to CSV or JSON, or delete
  all history (Settings → Data → "Delete all battery history"), at any time.
- Uninstalling the App does **not** delete this folder, so your history survives
  a reinstall or upgrade. To remove it, delete the folder manually after
  uninstalling.

## 4. Diagnostic logs

The App writes local log files to help you (or us, if you send them) diagnose a
problem. Logs may contain your battery's hardware identifiers, file paths, and
process names. They are stored only in the folder above and are rotated
automatically. The "Copy report" feature on the Diagnostics page redacts your
user-profile path before putting text on the clipboard. Logs are shared only if
**you** send them to us.

## 5. The website

The App's website (`battery-intelligence.netlify.app`) is a set of static pages
hosted by **Netlify**. The site itself sets **no cookies** and runs **no
analytics or tracking scripts**. As with any website, Netlify's servers
automatically record standard request information (such as IP address, user
agent and requested URL) in their infrastructure logs for security and
operational purposes; this is governed by
[Netlify's Privacy Policy](https://www.netlify.com/privacy/). The installer is
downloaded from **GitHub Releases**, subject to
[GitHub's Privacy Statement](https://docs.github.com/site-policy/privacy-policies/github-privacy-statement).

## 6. Children

The App is a technical utility with no content directed at children and collects
no personal information from anyone.

## 7. Changes to this policy

If this policy changes, the "Last updated" date above will change and the new
version will be published on the website and in the project repository.
Because the App collects nothing, changes will generally be clarifications
rather than expansions of data use.

## 8. Contact

Questions about this policy: **awad.print@gmail.com**
