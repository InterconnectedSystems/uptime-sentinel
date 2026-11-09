# Uptime Sentinel: user guide

Project page: https://github.com/InterconnectedSystems/uptime-sentinel

Uptime Sentinel is a lightweight Windows uptime monitor. The programs it installs are named
**SiteMonitor** (`SiteMonitor.exe` and `SiteMonitorService.exe`). It checks websites, domain names and IP addresses on a schedule
you choose. When a site or machine fails **3 checks in a row** (you can change this number), it sends you an email.
It emails again when the site comes back up.

- **Background Windows Service.** Starts automatically at boot and keeps monitoring when nobody is logged in.
- **Settings app** (`SiteMonitor.exe`). Add, edit or pause monitors, set up email, see live status, and install, start or stop the service.
- **Low resource use.** Checks run asynchronously in one small process. Idle CPU is about 0%.
  In testing on Linux, the service used about 36 MB of memory, of which about 9 MB was its own working data
  (the rest is read-only program code). Task Manager's figure on Windows will differ.

## Install

1. Unzip `SiteMonitor-win-x64.zip`.
2. Right-click **PowerShell** → *Run as administrator*, then run:
   ```powershell
   cd <unzipped folder>
   powershell -ExecutionPolicy Bypass -File .\install.ps1
   ```
   This copies the files to `C:\Program Files\SiteMonitor`, registers and starts the **Site Monitor** service,
   adds a Start Menu shortcut, and opens the settings app.

   *(Alternative: run `SiteMonitor.exe` directly and click **Install service**. The service will then run from the
   folder you unzipped to, so don't delete that folder.)*

No .NET install is needed. Both executables are self-contained.

## Set up

1. **Email (SMTP) settings.** Pick a provider from the dropdown, or choose *Custom*. Enter your username and password or API key,
   a From address, and one or more recipients. Then click **Send test email**.
2. **Add monitor.** Enter a URL (`https://example.com`), a domain (`example.com`) or an IP (`192.168.1.10`).
3. Make sure the service shows **Running** at the bottom of the window.

Changes are saved and picked up by the service automatically. You don't need to restart anything.

### SMTP provider dropdown

No legitimate free *anonymous* SMTP relay exists. Open relays get blacklisted as spam sources.
The dropdown lists providers that have **free tiers**. Choosing one fills in the server, port and encryption, and you sign up for an
account to get credentials:

| Provider | Free tier (as of Nov 2026) | Notes |
|---|---|---|
| SMTP2GO | 1,000/month (200/day) | Verify your sender address/domain |
| Brevo | 300/day | Password is an *SMTP key*, not your login password |
| Mailjet | 200/day | Username = API key, password = secret key |
| Gmail | ~500/day | Needs 2-Step Verification + an **App Password** |
| Zoho Mail | Free plan | `smtp.zoho.eu` for EU accounts |
| Microsoft 365 | With a paid mailbox | Password SMTP is being disabled by default from Dec 2026; may not work |
| Custom | — | Your ISP, company mail server, or an internal relay (leave username blank for IP-based relays) |

Providers change their limits, so check their sites.

## Check types

| Type | What it does | Use for |
|---|---|---|
| **Auto** (default) | URL → HTTP. Host/IP → Ping, then TCP 443, then TCP 80 if ping is blocked | Most things |
| Ping | ICMP echo | Machines on your network |
| TCP port | Connects to a port (e.g. 3389 RDP, 1433 SQL, 25 mail) | Specific services |
| HTTP/HTTPS | GET/HEAD request. Up = 2xx/3xx, or the exact status you specify. Optionally the page must contain some text | Websites, APIs, health endpoints |

Each monitor can use the global defaults or its own values for:

- how often to check (minimum 10 seconds)
- the timeout
- how many consecutive failures trigger the alert

**Default settings** also controls:

- recovery emails
- optional "still down" reminder emails every N hours

## How alerts work

- Each failed check adds 1 to the monitor's failure count, and any successful check resets it to 0.
- When the count reaches the threshold (default 3), the service sends **one** `[DOWN]` email, listing the failures and when they happened.
- When the site responds again, it sends one `[RECOVERED]` email with the outage duration.
- If sending fails (for example, the SMTP server is unreachable), it retries after 1, 2, 4, 8 and 16 minutes, then every 30 minutes.
  The settings app shows the email error next to the monitor.

## Files

| Path | Contents |
|---|---|
| `C:\Program Files\SiteMonitor\` | `SiteMonitor.exe` (settings), `SiteMonitorService.exe` (service) |
| `C:\ProgramData\SiteMonitor\config.json` | Your monitors and settings. The SMTP password is encrypted with Windows DPAPI |
| `C:\ProgramData\SiteMonitor\status.json` | Live status written by the service |
| `C:\ProgramData\SiteMonitor\logs\service.log` | Log (rolls at 1 MB, keeps 3 old files) |

The data folder is restricted to Administrators and SYSTEM, except `logs\`, which signed-in users can read (it contains no passwords).
Warnings and errors also go to the Windows **Event Log**
(Application, source `SiteMonitor`).

## Uninstall

```powershell
powershell -ExecutionPolicy Bypass -File "C:\Program Files\SiteMonitor\uninstall.ps1"            # keeps settings
powershell -ExecutionPolicy Bypass -File "C:\Program Files\SiteMonitor\uninstall.ps1" -RemoveData  # removes everything
```

## Build from source

Requires the .NET 8 SDK (Windows, Linux or macOS. It cross-compiles):

```bash
./build/publish.sh          # runs tests, then writes dist/SiteMonitor/ and dist/SiteMonitor-win-x64.zip
dotnet test                 # tests only
```

Project layout:

- `src/SiteMonitor.Core`: checks, alert logic, SMTP presets and config. Cross-platform and unit tested.
- `src/SiteMonitor.Service`: the Windows Service (.NET Worker Service).
- `src/SiteMonitor.Gui`: the WinForms settings app (runs as administrator).
- `tests/SiteMonitor.Core.Tests`: xUnit tests, including real TCP, HTTP and SMTP round-trips against local listeners.
