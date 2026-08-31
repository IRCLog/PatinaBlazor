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

*Last updated: 2026-08-30*

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
- **Two mobile-nav bugs fixed (2026-08-30), found from real device screenshots**: (1) opening the drawer on mobile made the app bar (hamburger + logo) visually disappear — MudBlazor renders the drawer's temporary-mode backdrop/scrim at `z-index: appbar+1` *by design*, and further bumps the open drawer's own z-index dynamically to sit above that scrim (observed at appbar+2, i.e. overlay+1) so its links stay clickable — meaning a small app-bar z-index bump ties with the drawer instead of clearing it. Fixed in `wwwroot/app.css` by forcing `.mud-appbar { z-index: calc(var(--mud-zindex-appbar) + 10) !important; }`, a wide-enough margin to clear both regardless of MudBlazor's exact drawer/scrim values. (2) The mobile drawer didn't auto-close after picking a nav item — `MudNavLink` renders as a plain `<div>` (not a real `<a href>`), so its `OnClick`-based close signal (`AppShell.razor`'s `OnNavigate` callback, invoked from `NavMenu.razor`'s `NotifyNavigate`) was racing the actual navigation. Fixed by also invoking `OnNavigate` from `NavMenu.razor`'s existing `Navigation.LocationChanged` subscription, which only fires once navigation has genuinely completed — a reliable signal regardless of the click-timing race. Both fixes were verified against the real Blazor Server app (not guessed) using an iframe injected into a Chrome tab at real mobile dimensions (390×844) — `resize_window`/CDP viewport emulation don't actually shrink this environment's browser viewport, but a same-origin iframe genuinely gets its own `window.innerWidth`/media-query breakpoint, which is what MudBlazor's own JS breakpoint detection reads. Within that iframe, real `computer`-tool mouse clicks didn't reliably reach nested-iframe content (a CDP/tooling limitation, not an app bug) — DOM-level `element.click()` calls inside the iframe's own document worked fine and were used for the actual click-and-observe verification.
- **The stock `dotnet new blazor` example "Auth Required" page was removed (2026-08-30)**: deleted `Components/Pages/Auth.razor` and its `MudNavLink` in `NavMenu.razor`. It was fully self-contained (grepped, nothing else referenced it).
- **`/admin/allUsers` redesigned (2026-08-30)**: the old `MudTable` inline-editing UI was architecturally broken — the edit fields (`editUserEmail`/`editUserName`/`editPassword`/`editUserLocked`) were single shared scalar fields (not per-row state) with only one inconsistent `@key` in the whole row template, so editing state could bleed across rows. Replaced with a modal dialog, `Components/Pages/EditUserDialog.razor` — a new, page-specific component following `ImageModal.razor`'s convention (`@ref` + public `ShowModal(user)` method, inline-bound `<MudDialog @bind-IsVisible>`). This also adds a **Roles tab** (a `MudTabs` alongside "General") with a toggle switch per `IdentityRole` (only "Admin" exists today — no role-creation UI was added, by design) — the first UI in the app that lets an admin actually grant/revoke roles; previously roles were seed-only (`Services/DatabaseSeeder.cs`) and displayed read-only. `AllUsers.razor` itself is now purely a read-only list + "Edit"/"Activate" action buttons; all the old inline-edit fields/methods and the dead `SignInManager`/`IJSRuntime` injections were removed.
  - **Reusable pattern for a submit button living in a `MudDialog`'s `DialogActions` (outside the `DialogContent`)**: give the `<EditForm>` an `id` (its `AdditionalAttributes` — confirmed via reflection — passes it through to the rendered `<form>`), then use the HTML `form="<that id>"` attribute (also passes through MudButton's own unmatched-attribute capture) on the `<MudButton ButtonType="ButtonType.Submit" form="...">` placed in `DialogActions`. This is required because `DialogActions` must be a sibling of `DialogContent` (both direct children of `<MudDialog>`, not nested inside each other), so a submit button in `DialogActions` can't be a descendant of the `<EditForm>` in `DialogContent` — the `form=` attribute is the standard HTML mechanism for a button to submit a form it isn't nested inside. See `EditUserDialog.razor` for the reference implementation.
  - Not independently browser-verified end-to-end (opening the dialog, toggling roles, saving) — doing so requires signing in as the seeded Admin account, and entering that account's password into the login form is something Claude won't do regardless of whose local app it is. Verified instead via build success, HTTP-level routing checks (`/admin/allUsers` still correctly redirects unauthenticated, `/auth` now 404s), and code review. **Give this page a manual pass** the next time you're signed in as Admin.
- **`EditUserDialog.razor` updated (2026-08-30)**: added an "Email Confirmed" switch next to "Account Locked" on the General tab (wired to `ApplicationUser.EmailConfirmed`). Also fixed the dialog resizing to fit whichever tab was shorter (Roles, with only one role today, is much shorter than General) — `MudTabs` got a `PanelClass="edit-user-tab-panel"`, and `app.css` sets `min-height: 281px` on it (measured from the General tab's actual rendered height), so switching tabs no longer changes the dialog's size. **Technique for testing auth-gated components without logging in**: temporarily added an unauthenticated `@page` (no `[Authorize]`) that rendered `EditUserDialog` directly with a synthetic in-memory `ApplicationUser` (never persisted — `UserManager.GetRolesAsync`/`RoleManager.Roles` work fine against a non-existent user id, just return empty), verified in the browser, then deleted the temp page before committing. Avoids ever needing the seeded Admin account's password. Also: browser CSS caching can mask a static file's real content even when the server is confirmed serving the updated version — a `link.href += '?v=' + Date.now()` swap forces a true reload of just that stylesheet without a full page refresh.
- **Roles tab switched from toggles to a drag-and-drop `MudDropContainer` (2026-08-30)**: replaced the per-role `MudSwitch` list on `EditUserDialog.razor`'s Roles tab with MudBlazor's documented "Basic Usage" dropzone pattern — two `MudDropZone<RoleDropItem>` zones ("Available"/"Assigned") inside one `MudDropContainer<RoleDropItem>`, keyed by a `RoleDropItem.Identifier` string field (`ItemsSelector="(item, zone) => item.Identifier == zone"`, updated on drop via `ItemDropped`). `ShowModal` now builds `_roleItems` from all role names with `Identifier` set to "Assigned"/"Available" based on `_originalRoles`; `HandleSave`'s add/remove-role diff loop is unchanged in spirit, just reads `item.Identifier == "Assigned"` instead of a dictionary bool. Note the nested-generic-RenderFragment gotcha: `MudDropContainer`'s `ItemRenderer` needed an explicit `Context="roleItem"` since its implicit `context` collides with the enclosing `EditForm`'s `ChildContent` context — a `RZ9999` compile error otherwise. Verified end-to-end in the browser with the same synthetic-user temp-page technique above (drag "Admin" between zones, confirmed it moves); the existing `min-height: 281px` dialog-height fix held up fine with the new content. Re-hit the browser CSS caching gotcha from above (`link.href += '?v=' + Date.now()` fixed it again) — worth remembering this bites every time a CSS change is being verified live.
- **`/admin/allUsers` rebuilt again (2026-08-30), this time as a card grid**: replaced the `MudTable` with a responsive `MudGrid` of `MudCard`s (one per user) — `MudCardHeader` has a `MudAvatar` showing the user's first-initial (from `DisplayName`, falling back to `UserName`/`Email`) plus name/email; `MudCardContent` holds Created date + Locked/Confirmed/Role chips; `MudCardActions` has just an Edit and a Delete `MudIconButton` (wrapped in `MudTooltip` for the hover label — the codebase had no prior tooltip-on-icon-button convention). The old bulk-select checkboxes + "Activate Selected" bar were removed entirely per explicit user instruction — email confirmation is handled solely through `EditUserDialog`'s existing "Email Confirmed" toggle now, no per-card or bulk activate action exists on this page. A plain `MudTextField` (`Immediate="true"`, `Clearable="true"`, search-icon adornment) does live client-side filtering across `DisplayName`/`UserName`/`Email` (case-insensitive `Contains`) — first search-box pattern in the app, nothing to reuse existed.
  - **New reusable `Components/ConfirmDialog.razor`** — first "are you sure?" `MudDialog` in the codebase (everything prior used raw JS `confirm()`/`alert()` via `IJSRuntime`, or a fake no-op stub in `Collectables.razor`). Follows the same `@ref` + `ShowModal(...)` + `EventCallback` convention as `EditUserDialog`/`ImageModal`: `ShowModal(string title, string message, string confirmText, Color confirmColor)` sets state and opens; `OnConfirmed` (parameterless `EventCallback`) fires on the confirm button. Generic/page-agnostic by design — reuse it for other destructive confirmations instead of writing another one-off.
  - **Delete is a real hard delete** (`UserManager.DeleteAsync`) — first admin-facing user deletion in the app (previously `UserManager.DeleteAsync` only existed in the self-service `DeletePersonalData.razor` flow). `Collectable`/`CollectableCollection` both cascade-delete on their `UserId` FK (`ApplicationDbContext.cs`), so deleting a user also wipes their collections/collectables/images — the confirm dialog's message says this explicitly so the admin isn't surprised. `AllUsers.razor` stores the pending user in `_userPendingDelete` when the delete icon is clicked, then commits the deletion from `ConfirmDialog`'s `OnConfirmed` callback.
  - Verified end-to-end in the browser against the **real** seeded Admin account (not a synthetic user this time, since deleting/editing needed to be checked against real data) by temporarily commenting out `@attribute [Authorize(Roles = "Admin")]` to view the page unauthenticated, then restoring it before the final build/commit. Confirmed: card renders correctly, live search filtering works, Edit dialog still opens correctly, and the Delete confirm dialog shows the correct cascade-delete warning text — **deliberately never clicked the dialog's actual Delete button**, since the only user in the local dev DB is the real seeded Admin account; only Cancel was exercised. No admin-role or multi-user deletion path has been exercised with real data as a result — worth a manual smoke test with a disposable test user before relying on this in production.
- **`/admin/allUsers` card layout refined (2026-08-30)**: moved the Edit/Delete `MudIconButton`s into `MudCardHeader`'s `CardHeaderActions` slot (top-right of the card, next to the avatar/name, instead of a separate footer row — `MudCardActions` was removed from the card entirely since it had no other content). Reordered `MudCardContent` so the Locked/Confirmed status chips render above the Created-date line (previously date was first). Header content now shows three separate lines — `UserName` (with a small `Icons.Material.Filled.AdminPanelSettings` badge icon next to it, shown only when `userRoles` contains "Admin", via a new `IsAdmin(user)` helper), then `DisplayName` (only rendered when set), then `Email` — instead of the old single "display name or fallback" title line. The search box (`FilteredUsers` in the code-behind) already matched against `DisplayName` before this change, so no filtering logic needed updating, only the missing on-card visibility. Not independently browser-verified for the `DisplayName` line specifically — the only local user (seeded Admin) has no `DisplayName` set and `EditUserDialog` has no field to set one — but it's the same simple `string.IsNullOrWhiteSpace` conditional pattern already proven elsewhere on this page (Admin badge, roles-chip fallback), so it's confidently correct. Admin badge and header-action relocation were both directly verified in the browser against the real seeded Admin account (same temporary-`[Authorize]`-removal technique as above).
- **Admin indicator switched to a `MudBadge` + card-header overflow bug fixed (2026-08-30)**: per MudBlazor's [badge playground docs](https://mudblazor.com/components/badge#playground), the plain `MudIcon` next to the username was replaced with `<MudBadge Icon="@Icons.Material.Filled.AdminPanelSettings" Color="Color.Warning" Origin="Origin.TopLeft" Overlap="true">` wrapping the username `MudText`, itself wrapped in a `MudTooltip Text="Admin"`. The wrapped `MudText` needed `Class="pt-3 ps-4"` **padding** (not margin) on the `MudBadge` itself — margin would've shifted the whole anchor box without creating room *inside* it, so the badge circle would still sit directly over the first letters; padding pushes the visible text inward, leaving clear space for the corner badge. Also removed the Roles chip list from `MudCardContent` entirely (was redundant now that Admin has its own badge, and no other roles exist in the app).
  - **Real bug found and fixed**: the Edit/Delete icons in `CardHeaderActions` were rendering past the card's right edge on cards with long email addresses. Root cause (confirmed via `getBoundingClientRect()`/`getComputedStyle()` in the browser, not guessed): MudBlazor's `.mud-card-header-content{flex:1 1 auto}` is a flex item whose default `min-width` is `auto`, which — per the standard CSS flexbox behavior — stops it from ever shrinking below its own content's intrinsic width. An unbroken email string has no wrap points, so the content refused to shrink and pushed `.mud-card-header-actions` outside the card. Setting `min-width:0` on a *descendant* div did nothing, since the actual flex item is `.mud-card-header-content` itself, generated by MudBlazor — not something `CardHeaderContent`'s child markup can reach directly. Fixed by adding `Class="user-card-header"` to `MudCardHeader` and a scoped rule in `wwwroot/app.css`: `.user-card-header .mud-card-header-content { min-width: 0; }` (confirmed via grep this is the only page using `MudCardHeader`, so the rule can't affect anything else). Combined with the existing `white-space: nowrap; overflow: hidden; text-overflow: ellipsis;` inline styles on the header's `MudText`s, the email now truncates with an ellipsis instead of overflowing. **Re-hit the recurring browser-CSS-caching gotcha** verifying this — `curl`ing `/app.css` showed the server had the fix immediately, but the open tab kept rendering with the stale rule until forcing a true reload via `link.href += '?v=' + Date.now()`; this has now bitten on three separate CSS-only verification passes in this app and is worth remembering reflexively whenever a live CSS change "isn't working."

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
