# PatinaBlazor Project Notes

## Project Purpose & Context

### What PatinaBlazor Is
A personal web app with modular, independent features. The name "Patina" reflects the core concept — aged, valuable antique items. Features are designed to be self-contained so each area of the app can grow independently without coupling.

### Features
- **Collections / Collectables** — The original and core feature. Allows tracking of antique collections. Still being refined.
- **Chat (IRC Bridge)** — A mIRC-style chat interface that bridges to IRC networks. A Python IRC bot logs events to the app via REST API; admins can monitor live IRC activity in a retro terminal-style UI.
- *(Additional features are expected to be added over time in a modular way)*

### Design Philosophy
- Features should be modular and independent where it makes sense
- Primarily personal use, but potentially shared with others
- Prefer building on existing patterns in the codebase rather than introducing new ones

### External Documentation
- **`/BOT_HUB_INTEGRATION.md`** (repo root) — Integration guide for the Python IRC bot project. Describes how bots connect to the SignalR hub, register channels, log events, and receive commands. **Keep this file up to date whenever the hub API changes** (new hub methods, changes to `IrcEventRequest`, new `ChatAction` values, or changes to the `ReceiveCommand` contract).

---

## Working Checkpoint

*Last updated: 2026-08-29*

### Current State
- Collections feature exists and is functional
- IRC Chat monitoring page is implemented for Admins, with real-time updates via a SignalR-backed notifier
- IRC events are logged via REST API (`POST /api/irc/events`) from a Python bot
- Chat view has a mIRC-style retro UI with a channel sidebar, network selector, and unread indicators (red channel names for activity on non-selected channels)
- Dev environment now also builds/runs on Linux (Linux Mint 22.3), in addition to the original Windows setup
- **UI has been fully migrated from Bootstrap to MudBlazor** (6.21.0). Bootstrap CSS/Icons and the vendored `wwwroot/bootstrap/` files have been removed entirely — every page now renders through MudBlazor components. Custom dark theme is defined in `Components/Layout/AppMudTheme.cs`, mapped 1:1 from `app.css`'s existing `:root` CSS variables; providers live in `Components/Layout/MudProviders.razor`. The app shell (`MudAppBar`/`MudDrawer`/`MudNavMenu`) is `Components/Layout/AppShell.razor`, invoked as a single `@rendermode="InteractiveServer"` island from `MainLayout.razor` — this replaced the old hand-rolled sidebar/mobile-nav and its `IJSRuntime.InvokeVoidAsync("eval", ...)` margin-toggle hack entirely.
- **Important constraint discovered during the migration**: `MudTextField`/`MudCheckBox`/other Mud form inputs do NOT render an HTML `name` attribute, so they cannot bind through Blazor's static-SSR `EditForm method="post"` + `[SupplyParameterFromForm]` model. All of ASP.NET Core Identity's scaffolded `/Account` pages render as static SSR (required for cookie-setting POST forms) — so those pages keep the native `InputText`/`InputCheckbox`/etc. (with their original Bootstrap-era CSS classes) for every actually-bound form field, and only use MudBlazor components for layout/buttons/alerts/headings around them. Same reasoning applies to a handful of native `<button name=... value=...>`/`<button form="...">` elements elsewhere in the Account area (external-login provider selection, `Email.razor`'s send-verification button) — deliberately left un-migrated since they rely on raw form attributes MudButton doesn't replicate. If a future page needs form-bound inputs under static SSR, don't reach for Mud input components there.
- **Post-migration bug fixed (2026-08-30)**: after the MudBlazor migration, the app loaded but the hamburger menu/drawer was completely unresponsive. Root cause: `MudPopoverProvider` (inside `Components/Layout/MudProviders.razor`) threw `InvalidOperationException: Duplicate MudPopoverProvider detected` on every page load, which crashed the entire Blazor Server circuit immediately after connecting (visible in browser console as "unhandled exception on the current circuit" + silent disconnect) — killing ALL interactivity on the page, not just the menu, since the whole circuit died. Root topology cause: `MainLayout.razor` renders multiple independent `@rendermode="InteractiveServer"` islands side by side (`MudProviders`, `AppShell`, and `@Body`'s own per-page render mode) — a valid but non-default arrangement that trips MudBlazor's duplicate-provider guard as a false positive even though `MudPopoverProvider` is declared exactly once. Fixed in `Program.cs` by configuring `AddMudServices(config => config.PopoverOptions.ThrowOnDuplicateProvider = false)` — this is MudBlazor's own documented escape hatch for this exact check (named directly in its error message). Also fixed in the same pass: `AppMudTheme.cs`'s `PaletteDark` had no `Secondary` color set, so every `MudText`/`MudButton` etc. using `Color="Color.Secondary"` (used throughout for muted text) rendered in MudBlazor's stock pink/magenta default instead of the intended muted gray — fixed by setting `Secondary = "#8b949e"` to match `TextSecondary`. If new MudBlazor circuit-crash or stray-default-color symptoms show up after further UI changes, check these two spots first.
- **Post-migration bug fixed (2026-08-30)**: the drawer, when opened, was partially covered by the app bar at the top instead of sliding out fully below it. Root cause, confirmed via computed styles in the browser: MudBlazor's default z-index is `Drawer=1100` vs `AppBar=1300` (both `position:fixed`), AND separately the `mud-drawer-clipped-always` CSS rule that's supposed to offset the drawer's `top` to `var(--mud-appbar-height)` wasn't matching in this app's DOM structure — the drawer's computed `top` was `0px` even though `--mud-appbar-height: 64px` was correctly set at the layout root, so the drawer extended full-height from the very top of the viewport, behind the app bar. Fixed with an explicit override in `wwwroot/app.css`: `.mud-drawer.mud-drawer-clipped-always { top: var(--mud-appbar-height) !important; height: calc(100% - var(--mud-appbar-height)) !important; }`. This is a plain CSS file — changes here take effect on a browser refresh, no rebuild needed. `AppShell.razor` uses `ClipMode="DrawerClipMode.Always"` on the `MudDrawer`; if that's ever changed, re-verify this override still matches.
- **Post-migration bug fixed (2026-08-30)**: on short-content pages (e.g. a simplified `Home.razor`), the drawer/nav sidebar and dark background only extended as far as the page content instead of reaching the bottom of the viewport. Root cause: MudBlazor's `.mud-layout { height: 100% }` only resolves to a real pixel value if its ancestor chain (`body` → `html`) has a *definite* height — `app.css`'s `html, body` rule never set one, so the chain collapsed to content-height (`auto`) instead of the viewport. Fixed by adding `height: 100%;` to the existing `html, body` rule in `wwwroot/app.css`. Verified via `getBoundingClientRect()` in the browser: drawer/layout/body/html heights all now match `window.innerHeight` exactly regardless of content length, on both interactive (MudLayout) and static-SSR (Identity/AccountLayout) pages. Static CSS file — takes effect on refresh, no rebuild needed.
- **Sticky page-footer pattern established (2026-08-30)**: to let a page pin something (e.g. `HitCounter`) to the bottom of the viewport regardless of content length, `wwwroot/app.css` makes `.mud-main-content` a flex column with `min-height: calc(100vh - var(--mud-appbar-height))`, and `.mud-main-content > .content` (the `<article class="content">@Body</article>` wrapper in `MainLayout.razor`) a flex column filling it via `flex: 1 1 auto`. A page opts in by rendering a `<footer class="page-footer">...</footer>` as a **sibling** of its main content (not nested inside it) — `.page-footer`'s `margin-top: auto` then pushes it to the bottom of the available space. See `Components/Pages/Home.razor` for the reference usage (`<MudContainer>...</MudContainer>` followed by `<footer class="page-footer"><HitCounter PagePath="/" /></footer>` as two separate root-level elements). No Blazor plumbing (no `SectionOutlet`/cascading service) was needed since this is pure CSS — reuse the same `<footer class="page-footer">` sibling pattern on any other page that wants a sticky footer.
- **User preference noted (2026-08-30)**: lean on MudBlazor component props (e.g. `MudStack`'s `Justify`/`AlignItems`) instead of adding custom CSS when the component already exposes the needed behavior natively. Came up when centering `HitCounter`'s content — `.page-footer`'s `text-align: center` in `app.css` was inert (doesn't affect a flex row's main-axis alignment) and was replaced with `Justify="Justify.Center"` on `HitCounter.razor`'s `MudStack`, and the dead CSS rule was deleted. Saved as a standing preference in memory (`feedback_mudblazor_native_styling`) — check whether a MudBlazor prop already covers a styling need before reaching for `app.css`.
- The mIRC retro chat theme (`.mirc-*` classes in `app.css`) was preserved by applying the same class names onto MudBlazor component wrappers (`MudPaper`/`MudToolBar`/`MudText`) rather than rewriting the CSS — the riskiest elements for this trick (the network `<select>` and the message `<input>`) were deliberately left as native HTML elements rather than swapped for `MudSelect`/`MudTextField`, since those add wrapping DOM that could fight the `!important`-heavy retro CSS and couldn't be visually verified (no browser automation was available in that session).
- **The stock `dotnet new blazor` example "Auth Required" page was removed (2026-08-30)**: deleted `Components/Pages/Auth.razor` and its `MudNavLink` in `NavMenu.razor`. It was fully self-contained (grepped, nothing else referenced it).
- **`/admin/allUsers` redesigned (2026-08-30)**: the old `MudTable` inline-editing UI was architecturally broken — the edit fields (`editUserEmail`/`editUserName`/`editPassword`/`editUserLocked`) were single shared scalar fields (not per-row state) with only one inconsistent `@key` in the whole row template, so editing state could bleed across rows. Replaced with a modal dialog, `Components/Pages/EditUserDialog.razor` — a new, page-specific component following `ImageModal.razor`'s convention (`@ref` + public `ShowModal(user)` method, inline-bound `<MudDialog @bind-IsVisible>`). This also adds a **Roles tab** (a `MudTabs` alongside "General") with a toggle switch per `IdentityRole` (only "Admin" exists today — no role-creation UI was added, by design) — the first UI in the app that lets an admin actually grant/revoke roles; previously roles were seed-only (`Services/DatabaseSeeder.cs`) and displayed read-only. `AllUsers.razor` itself is now purely a read-only list + "Edit"/"Activate" action buttons; all the old inline-edit fields/methods and the dead `SignInManager`/`IJSRuntime` injections were removed.
  - **Reusable pattern for a submit button living in a `MudDialog`'s `DialogActions` (outside the `DialogContent`)**: give the `<EditForm>` an `id` (its `AdditionalAttributes` — confirmed via reflection — passes it through to the rendered `<form>`), then use the HTML `form="<that id>"` attribute (also passes through MudButton's own unmatched-attribute capture) on the `<MudButton ButtonType="ButtonType.Submit" form="...">` placed in `DialogActions`. This is required because `DialogActions` must be a sibling of `DialogContent` (both direct children of `<MudDialog>`, not nested inside each other), so a submit button in `DialogActions` can't be a descendant of the `<EditForm>` in `DialogContent` — the `form=` attribute is the standard HTML mechanism for a button to submit a form it isn't nested inside. See `EditUserDialog.razor` for the reference implementation.
  - Not independently browser-verified end-to-end (opening the dialog, toggling roles, saving) — doing so requires signing in as the seeded Admin account, and entering that account's password into the login form is something Claude won't do regardless of whose local app it is. Verified instead via build success, HTTP-level routing checks (`/admin/allUsers` still correctly redirects unauthenticated, `/auth` now 404s), and code review. **Give this page a manual pass** the next time you're signed in as Admin.

### Recent Decisions
- Removed channel dropdown from the titlebar; channel selection is now only via the sidebar list
- Network selector moved into the channel list panel, centered above the channel list
- Unread channel tracking is keyed by `(network, channel)` tuple so switching networks preserves unread state for all networks
- Repo layout is nested: git root is `PatinaBlazor/`, which contains `PatinaBlazor.sln` and a `PatinaBlazor/` subfolder holding `PatinaBlazor.csproj`. `.vscode/` lives next to the `.sln` (one level below git root) — `workspaceFolder` in VS Code should be that `.sln`-containing folder, not the git root
- Fixed `.vscode/tasks.json`'s `build` task, which had a duplicated `PatinaBlazor/` path segment in the csproj target (`${workspaceFolder}/PatinaBlazor/PatinaBlazor/PatinaBlazor.csproj` → `${workspaceFolder}/PatinaBlazor/PatinaBlazor.csproj`) — this caused F5 debugging to fail with `MSB1009: Project file does not exist` even though Ctrl+Shift+B / manual builds worked. Same class of bug as the earlier `launch.json` fix noted below; both files reference paths relative to `workspaceFolder` and are easy to get out of sync when the nested repo layout changes.
- On Linux, local SQL Server is provided via Docker (`mcr.microsoft.com/mssql/server:2022-latest`, Developer edition) rather than SQL Server Express — keeps the same engine/EF Core SqlServer provider and existing migrations unchanged, so no separate migration history was needed
- The Linux-local SQL Server connection string is set via `dotnet user-secrets` (not committed to `appsettings.Development.json`), so the Windows machine's `Trusted_Connection=True` / named-instance config in that file is left untouched for when that machine is used

### Linux Dev Environment Setup (for reference / new machines)
- Docker installed via Docker's official apt repo (Ubuntu `noble`); user added to `docker` group
- SQL Server container: `docker run -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=... -e MSSQL_PID=Developer -p 1433:1433 --name patina-sql --restart unless-stopped -v patina-sql-data:/var/opt/mssql -d mcr.microsoft.com/mssql/server:2022-latest`
- Connection string set locally via `dotnet user-secrets set "ConnectionStrings:SqlServerConnection" "Server=localhost,1433;Database=DB_175541_silzellnet;User Id=sa;Password=...;TrustServerCertificate=True;"` (run from the `PatinaBlazor/PatinaBlazor/` project folder)
- `.vscode/launch.json` previously had a duplicated path segment in `program`/`cwd` (pointing at a nonexistent build output path) that broke F5 debugging — fixed to `${workspaceFolder}/PatinaBlazor/bin/Debug/net8.0/PatinaBlazor.dll`

### Next Steps / In Progress
- *(Update this before clearing context)*

---

## IRC Event Logging API

### Overview
REST API endpoint for logging IRC events from Python bots to the SQL Server database.

### Endpoint
```
POST /api/irc/events
```

### Authentication
The API uses a static API key passed in the `X-Api-Key` header. Valid keys are configured in `appsettings.json` under `IrcApi.ApiKeys`.

### Request Headers
| Header | Required | Description |
|--------|----------|-------------|
| `Content-Type` | Yes | Must be `application/json` |
| `X-Api-Key` | Yes | Valid API key from configuration |

### Request Body
```json
{
  "action": "MESSAGE",
  "network": "Libera.Chat",
  "channel": "#mychannel",
  "target": "targetuser",
  "message": "Hello, world!",
  "sender": "botuser",
  "user": "someuser",
  "timestamp": "2025-10-10T12:00:00Z"
}
```
Note: `timestamp` is optional. If omitted, it defaults to the current UTC time.

### Fields
| Field | Type | Required | Max Length | Description |
|-------|------|----------|------------|-------------|
| `action` | string | Yes | - | Event type (see valid actions below) |
| `network` | string | Yes | 100 | IRC network name |
| `timestamp` | DateTime | No | - | UTC timestamp of the event (defaults to current UTC time if not supplied) |
| `channel` | string | No | 200 | Channel where event occurred |
| `target` | string | No | 200 | Target of the action |
| `message` | string | No | 4000 | Message content |
| `sender` | string | No | 100 | User who triggered the event |
| `user` | string | No | 100 | Additional user context |

### Valid Actions
`JOIN`, `PART`, `MESSAGE`, `QUIT`, `KICK`, `MODE`, `ACTION`, `NOTICE`, `CONNECT`, `TOPIC`

### Response
- **201 Created**: Event logged successfully, returns `{ "id": <int> }`
- **400 Bad Request**: Invalid action value
- **401 Unauthorized**: Missing or invalid API key

### Sample curl Call
```bash
curl -X POST https://localhost:5001/api/irc/events \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: your-api-key-here" \
  -d '{
    "action": "MESSAGE",
    "network": "Libera.Chat",
    "channel": "#mychannel",
    "message": "Hello from the bot!",
    "sender": "mybot"
  }'
```

### Sample Python Call
```python
import requests

response = requests.post(
    "https://your-domain/api/irc/events",
    headers={
        "X-Api-Key": "your-api-key-here",
        "Content-Type": "application/json"
    },
    json={
        "action": "MESSAGE",
        "network": "Libera.Chat",
        "channel": "#mychannel",
        "message": "Hello from the bot!",
        "sender": "mybot"
    }
)

if response.status_code == 201:
    event_id = response.json()["id"]
    print(f"Event logged with ID: {event_id}")
```

### Related Files
- Entity: `PatinaBlazor/Data/IrcEvent.cs`
- Enum: `PatinaBlazor/Data/ChatAction.cs`
- Service: `PatinaBlazor/Services/IrcEventService.cs`
- Endpoint: `PatinaBlazor/Endpoints/IrcEventEndpoints.cs`
- Config: `PatinaBlazor/Services/IrcApiSettings.cs`
