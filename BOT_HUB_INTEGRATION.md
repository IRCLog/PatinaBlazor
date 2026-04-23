# IRC Bot — SignalR Hub Integration Guide

This document describes how Python IRC bots should connect to and interact with the PatinaBlazor SignalR hub. The hub is the preferred way to send IRC events to the web app and is the transport over which the app will send commands back to bots in the future.

> **Note:** The legacy REST endpoint (`POST /api/irc/events`) remains available for bots that have not been updated to use the hub.

---

## Requirements

```
pip install signalrcore
```

---

## Connecting

The hub is located at `/hubs/ircbot`. Authentication uses the same API key as the REST endpoint, passed via the `X-Api-Key` header.

> **Important:** Do not pass the key as a query string parameter (`?apiKey=...`). Query strings are URL-decoded by the server, which will corrupt keys containing characters like `%`, `+`, or `&`.

```python
from signalrcore.hub_connection_builder import HubConnectionBuilder

API_KEY = "your-api-key-here"
HUB_URL = "https://your-domain/hubs/ircbot"

hub = (
    HubConnectionBuilder()
    .with_url(HUB_URL, options={"headers": {"X-Api-Key": API_KEY}})
    .with_automatic_reconnect({
        "type": "interval",
        "keep_alive_interval": 10,
        "intervals": [0, 2, 5, 10, 30]
    })
    .build()
)

hub.start()
```

If the API key is missing or invalid the connection will be rejected immediately.

---

## Registering Channels

After connecting, call `Register` once for each channel the bot monitors. This tells the hub which channels this bot can receive commands for.

```python
def on_open():
    hub.send("Register", ["Libera.Chat", "#mychannel"])
    hub.send("Register", ["Libera.Chat", "#anotherchannel"])

hub.on_open(on_open)
```

**How registration works:**

- The first bot to register for a channel becomes the **primary** — it will receive any commands the app sends to that channel.
- Subsequent bots that register for the same channel are queued as **standby**.
- When the primary disconnects, the first standby is automatically promoted to primary. No action is required from the bot — commands simply start arriving at the new primary's connection.
- If the original primary reconnects and re-registers, it joins the standby queue (the promoted bot remains primary).

Bots do not need to know whether they are primary or standby. Register for every channel you monitor and let the hub handle routing.

---

## Logging Events

Call `LogEvent` for each IRC event the bot observes. It returns the database ID of the saved event.

```python
event_id = hub.send("LogEvent", [{
    "action": "MESSAGE",
    "network": "Libera.Chat",
    "channel": "#mychannel",
    "sender": "somenick",
    "user": "somenick!ident@host.example.com",
    "message": "Hello, world!"
}])
```

### Event Fields

| Field | Type | Required | Max Length | Description |
|-------|------|----------|------------|-------------|
| `action` | string | Yes | — | Event type (see valid actions below) |
| `network` | string | Yes | 100 | IRC network name (e.g. `Libera.Chat`, `EFnet`) |
| `timestamp` | string (ISO 8601 UTC) | No | — | Defaults to server time if omitted |
| `channel` | string | No | 200 | Channel where the event occurred |
| `target` | string | No | 200 | Target of the action (e.g. kicked user) |
| `message` | string | No | 4000 | Message or reason text |
| `sender` | string | No | 100 | Nick of the user who triggered the event |
| `user` | string | No | 100 | Full `nick!ident@host` string if available |

### Valid Actions

| Action | When to use |
|--------|-------------|
| `MESSAGE` | Normal channel or private message |
| `ACTION` | `/me` action (`CTCP ACTION`) |
| `JOIN` | User joined a channel |
| `PART` | User left a channel |
| `QUIT` | User disconnected from the network |
| `KICK` | User was kicked from a channel |
| `MODE` | Channel or user mode change |
| `NOTICE` | NOTICE message |
| `TOPIC` | Topic was changed |
| `CONNECT` | Bot connected to the IRC network |
| `NICK` | User changed their nick |
| `CTCP` | CTCP request (other than ACTION) |

Action strings are case-insensitive.

---

## Receiving Commands

The hub sends commands to the primary bot for a channel using the `ReceiveCommand` method. Register a handler at startup.

### BotCommand fields

| Field | Type | Description |
|-------|------|-------------|
| `action` | string | What the bot should do. Currently always `"MESSAGE"` (send message to channel). |
| `user` | string | Username of the web app user who sent the command. |
| `message` | string | The text to send to the channel. |
| `channel` | string | The channel the message should be sent to. |
| `target` | string \| null | Optional target user (for future directed commands). |
| `timestamp` | string (ISO 8601 UTC) | When the command was issued. |
| `source` | string | Always `"silzell.net"`. Identifies the origin. |
| `messageId` | string (GUID) | Unique ID for this command, for future reply correlation. |

### Python handler

```python
def on_receive_command(args):
    cmd       = args[0]
    action    = cmd["action"]       # e.g. "MESSAGE"
    user      = cmd["user"]
    message   = cmd["message"]
    channel   = cmd["channel"]
    target    = cmd.get("target")   # may be None
    source    = cmd["source"]
    messageId = cmd["messageId"]

    if action == "MESSAGE":
        irc_connection.privmsg(channel, f"<{user}@{source}> {message}")

hub.on("ReceiveCommand", on_receive_command)
```

---

## Reconnection

`signalrcore`'s automatic reconnect will re-establish the connection after a drop. When the connection comes back up, `on_open` fires again — make sure your `on_open` handler re-registers all channels.

```python
def on_open():
    for channel in MONITORED_CHANNELS:
        hub.send("Register", [NETWORK, channel])

hub.on_open(on_open)
```

---

## Complete Minimal Example

```python
from signalrcore.hub_connection_builder import HubConnectionBuilder

API_KEY  = "your-api-key-here"
NETWORK  = "Libera.Chat"
CHANNELS = ["#mychannel", "#anotherchannel"]
HUB_URL  = "https://your-domain/hubs/ircbot"

hub = (
    HubConnectionBuilder()
    .with_url(HUB_URL, options={"headers": {"X-Api-Key": API_KEY}})
    .with_automatic_reconnect({
        "type": "interval",
        "keep_alive_interval": 10,
        "intervals": [0, 2, 5, 10, 30]
    })
    .build()
)

def on_open():
    for channel in CHANNELS:
        hub.send("Register", [NETWORK, channel])

def on_receive_command(args):
    command = args[0]
    params  = args[1] if len(args) > 1 else None
    # TODO: handle command

hub.on_open(on_open)
hub.on("ReceiveCommand", on_receive_command)
hub.start()

# Log an event
hub.send("LogEvent", [{
    "action":   "CONNECT",
    "network":  NETWORK,
    "sender":   "mybot",
    "message":  f"Connected to {NETWORK}"
}])
```
