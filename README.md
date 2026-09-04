# Claude Allow Helper

Windows tray helper for safely automating Claude JavaScript permission prompts on allowlisted local development sites.

**Unofficial community project.** Claude Allow Helper is not affiliated with, sponsored by, or endorsed by Anthropic or Claude.

## What it is

Claude Allow Helper is a small Windows system-tray utility. When Claude Desktop (including Cowork / the built-in preview browser) asks:

> Allow Claude to execute JavaScript on *&lt;host&gt;*?

the helper can press **Allow once** (Ctrl+Enter) — **only** when that host is on your explicit allowlist and several safety checks still pass.

It is intended for repetitive local-development prompts (for example a `.test` site, `localhost`, or `127.0.0.1`) where Claude cannot persist a site-level JavaScript grant.

## Why it exists

On some local or private-network origins, Claude shows a per-action JavaScript prompt and states that site-level permissions are disabled. The dialog typically offers **Allow once** / **Deny** only. Existing Claude Desktop settings do not reliably persist that JavaScript grant.

This helper does **not** modify Claude Desktop files or settings. It watches Claude’s local log and, when Windows UI Automation can see the overlay, the on-screen dialog. If both the permission type and the host match the allowlist, it sends Ctrl+Enter to the already-focused Claude window.

## Supported behavior

The helper automates **one** permission type:

| Automated | Not automated |
| --- | --- |
| JavaScript execution (`javascript_tool`) for an **explicitly allowlisted** host | Site “Access” prompts (`open_site` / “Allow Claude to access …”) |
| | Terminal / shell / Bash / PowerShell |
| | File delete, folder access, or other filesystem prompts |
| | Downloads |
| | Arbitrary browser or computer actions (click, scroll, read page, fetch, and similar) |
| | External or production websites that are not on the allowlist |
| | Any other Claude permission dialog |

Default allowlist (you can edit it; do not treat it as a recommendation to allow production sites):

- `analitikcms.test`
- `localhost`
- `127.0.0.1`

## Safety model

Auto-approve is **off** until you enable it.

A candidate is **never** approved on first sight. After a short debounce (configurable, clamped to 100–300 ms), the helper runs a **final verification** immediately before any keypress:

- Helper still **Enabled**
- Host still on the **allowlist**
- Same origin/host as detection
- Expected **Claude window still foreground** (the helper does not steal focus)
- UI Automation, when it can see the overlay, still shows the JavaScript prompt with **Allow once**
- When Chromium hides the overlay, a matching `javascript_tool` log event must still be the live request (helper ingest time, not Claude’s whole-second log stamp alone)
- An **unanswered** request id may remain eligible after the short freshness window **only while that same request is still outstanding**
- **Stale** means Claude has moved on (response recorded, newer unrelated permission, different dialog, or window no longer Claude) — not merely “more than two seconds elapsed”
- Identified non-JavaScript dialogs are rejected
- Newer unrelated permission events are rejected

It does not click by screen coordinates. When UI Automation exposes the **Allow once** button, it may invoke that button; otherwise it may send Ctrl+Enter only if Claude is already focused.

After an approval attempt it checks Claude’s local permission response or that the JavaScript dialog is gone.

This is a local convenience tool with a narrow allowlist. It is **not** a security boundary and does not make untrusted sites safe.

## How JavaScript approval works

1. Claude writes a per-action approval to its local `main.log` (`toolName: 'javascript_tool'` and an origin such as `http://analitikcms.test`).
2. The helper tails that log and/or reads the accessibility tree of the Claude window.
3. If the host is allowlisted and the helper is enabled, it records a candidate and waits the debounce.
4. Final verification runs (gates above).
5. If verification passes, it invokes **Allow once** or sends Ctrl+Enter.
6. It looks for Claude’s `once` response for `browser:javascript_tool` or for the dialog to disappear.

## What it does not do

- Does not automate Claude **Access** (open site) permissions
- Does not approve terminal, filesystem, download, or computer/browser-control prompts
- Does not add a generic “auto-allow everything for this site” rule
- Does not edit Claude Desktop
- Does not send telemetry, analytics, or any application network traffic
- Does not upload logs

It only reads Claude’s **local** log file and the local UI Automation tree, and writes its own config/log under `%AppData%\ClaudeAllowHelper\`.

## Enabled / emergency off

The helper starts with **Enabled = false** the first time (default config).

Turn it on from the tray: **Enabled**.

Turn it off immediately with any of:

- Tray → uncheck **Enabled**
- Tray → **Emergency Off**
- **Ctrl+Alt+Shift+Q**

## Installation

There is no official installer. Build from source (see below) or run a binary you built yourself.

1. Place `ClaudeAllowHelper.exe` anywhere you like.
2. Start it. A tray icon appears.
3. Right-click → **Enabled**.
4. Keep Claude Desktop focused when a matching JavaScript prompt appears.

Requirements at runtime: Windows 10/11, 64-bit. The published build is a self-contained .NET 9 Windows executable.

## Running

```text
ClaudeAllowHelper.exe
```

Diagnostic (writes Claude’s accessibility tree locally; does not approve anything):

```text
ClaudeAllowHelper.exe --dump-ui
```

Output: `%AppData%\ClaudeAllowHelper\ui-dump.txt`

Only one instance runs at a time.

## Configuration / allowlist

Config file (created on first run):

`%AppData%\ClaudeAllowHelper\config.json`

Example:

```json
{
  "Enabled": false,
  "AllowedSites": [
    "analitikcms.test",
    "localhost",
    "127.0.0.1"
  ],
  "PollIntervalMs": 500,
  "SafetyDebounceMs": 200,
  "HiddenDialogMaxAgeMs": 2000,
  "StartWithWindows": false
}
```

You can also edit the list from the tray: **Allowed Sites…**

Host matching is exact (optional `*.example.test` wildcards). `http://analitikcms.test/path` matches `analitikcms.test`. Unknown hosts are ignored.

Local diagnostic log: `%AppData%\ClaudeAllowHelper\helper.log` (never sent anywhere).

**Start with Windows** (optional, tray menu) adds or removes a per-user Run key. Turn that off before uninstalling.

## Building from source

Requirements: [.NET 9 SDK](https://dotnet.microsoft.com/download).

```bat
build.cmd
```

Or:

```bat
dotnet test tests\ClaudeAllowHelper.Tests\ClaudeAllowHelper.Tests.csproj -c Release
dotnet publish src\ClaudeAllowHelper\ClaudeAllowHelper.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts\win-x64
```

Output: `artifacts\win-x64\ClaudeAllowHelper.exe` (this folder is not part of the source tree).

## Tests

```bat
dotnet test tests\ClaudeAllowHelper.Tests\ClaudeAllowHelper.Tests.csproj
```

Coverage includes dialog parsing, allowlist matching, approval vs ignore, unrelated permissions, hidden-dialog freshness (helper ingest time and outstanding request ids), config validation, and logging.

## Project structure

```text
ClaudeAllowHelper.sln
build.cmd
src/ClaudeAllowHelper.Core/     Decision logic, log parser, allowlist, config
src/ClaudeAllowHelper/          WinForms tray app, UI Automation, keyboard
tests/ClaudeAllowHelper.Tests/  xUnit tests
```

## Security considerations

- Only enable the helper while you are using it for local development you trust.
- Keep the allowlist small. Do not add production domains.
- The helper can send Ctrl+Enter to Claude when the gates pass. If a different permission were misclassified as JavaScript, that would be harmful — the parser and log checks exist to prevent that, but they are not perfect if Claude’s UI copy or log format changes.
- UI Automation of Chromium overlays is incomplete; the log path is the more stable signal and is intentionally strict.
- Review `%AppData%\ClaudeAllowHelper\helper.log` if something is approved or ignored unexpectedly.
- Uninstall: Exit the tray app, optionally disable Start with Windows first, delete the exe folder, and delete `%AppData%\ClaudeAllowHelper\`.

## Limitations

- Claude still has no official persist API for this prompt; the helper repeats **Allow once**.
- Electron/Chromium often hides the overlay from UI Automation.
- Claude UI or log format changes can break matching.
- Windows only.
- Not a substitute for reviewing what JavaScript Claude is about to run.

## Contributing

Issues and pull requests that preserve the narrow safety model are welcome.

Please do **not** submit changes that:

- auto-approve Access, terminal, filesystem, or computer/browser-control permissions
- add a generic “allow everything for this site” rule
- add telemetry or network calls
- click by screen coordinates
- modify Claude Desktop files

Keep the allowlist explicit and permission-type-specific.

## License

MIT. See [LICENSE](LICENSE).
