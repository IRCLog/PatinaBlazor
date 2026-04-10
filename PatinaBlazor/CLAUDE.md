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

---

## Working Checkpoint

*Last updated: 2026-04-10*

### Current State
- Collections feature exists and is functional
- IRC Chat monitoring page is implemented for Admins, with real-time updates via a SignalR-backed notifier
- IRC events are logged via REST API (`POST /api/irc/events`) from a Python bot
- Chat view has a mIRC-style retro UI with a channel sidebar, network selector, and unread indicators (red channel names for activity on non-selected channels)

### Recent Decisions
- Removed channel dropdown from the titlebar; channel selection is now only via the sidebar list
- Network selector moved into the channel list panel, centered above the channel list
- Unread channel tracking is keyed by `(network, channel)` tuple so switching networks preserves unread state for all networks

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
