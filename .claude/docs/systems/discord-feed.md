# Discord feed

Selected in-game events are copied to separate Discord channels through plain webhooks (no bot, no inbound traffic). Currently two feeds: **admin chat** and **new players** (first connection ever). Code lives in `Content.Server/_Maid/Discord`; the CVars are in `MaidCVars`.

## How it works
- `IDiscordFeedManager` has one bounded queue and one background sender per feed. `Send(feed, text)` only enqueues, so game code never waits on the network. If Discord is slow the oldest messages are dropped instead of growing memory.
- Each feed has its own webhook CVar. An empty value disables the feed: nothing is queued and nothing is sent. The webhook id and token are resolved once when the CVar changes, not per message.
- The sender pauses about 2 s between messages (below Discord's ~30 requests/min per webhook) and honours `429 Retry-After`. Messages are cut to 1900 characters.
- Mentions are neutralised twice: the payload carries an empty `allowed_mentions`, and `Sanitize` escapes `@`, markdown and `http` in player-supplied text, so `@everyone` or links in admin chat or a nickname cannot ping or embed anything.
- Admin chat: `ChatManager` calls `SendAdminChat(player, message)` right after the admin log entry (one `MAID` line). The message is only sent after the sender was verified to be an admin. It is formatted as `name (rank) (раунд N): text`, where rank is the admin's title ("Admin" if none) and N is the current round id.
- New players: the manager itself listens to session status. On `Connected` it reads the player record; the record is created by the connection manager before that moment, so "first seen less than 2 minutes ago" means a new player. A set of already announced users stops a quick reconnect from announcing twice (cleared on server restart, which is harmless because the window has long passed).

## Second-account warning (new players)
There is no dedicated alt detector in the game code; the signal comes from the Wizden auth server. During login the engine asks the auth server (`hasJoined`) and gets back, besides the account, a set of modern HWIDs for the connection and a trust score (0.5 neutral, 1 fully trusted). The engine documents that a match between any HWID of two connections means the auth server believes it is the same user. This only works for authenticated (non-local) connections, with `net.hwid` on (default) and the default auth server; with an unauthenticated or local connection the HWID list is empty and the warning never fires.
- Every connection (accepted or denied) is written to the connection log together with the first HWID. The warning compares the HWIDs of the new connection with the logged ones of other accounts (database query by exact modern-HWID match, any of the connection's HWIDs may match).
- The warning is appended to the same message when the account matches at least one other account and its overall playtime is under `discord.new_player_alt_playtime_minutes` (default 30, 0 disables). It lists up to five matched accounts, the playtime and the trust score.
- Limitation: only the first HWID of each past connection is stored, so a match through a different HWID of the same set is missed until the account reconnects.

## Adding a feed
Add a value to `DiscordFeed`, a webhook CVar, one `Register(...)` call in `Initialize`, then call `Send` from the event source.

# Setup (server operator)

## 1. Create the webhook in Discord
1. Open the target channel (e.g. `#admin-chat`) → **Edit Channel** (gear) → **Integrations** → **Webhooks** → **New Webhook**.
2. Name it, pick the channel, press **Copy Webhook URL**. It looks like `https://discord.com/api/webhooks/<id>/<token>`.
3. Make one webhook per feed (one for admin chat, a different channel for new players). Treat the URL as a password: anyone with it can post to the channel. If it leaks, delete the webhook and make a new one.
4. Admin chat is private information, so the channel must be visible to admins only.

## 2. Give the URL to the server
CVars (both `SERVERONLY | CONFIDENTIAL`, so they are never shown to clients or printed by `cvar` listings):

| CVar | Feed |
|---|---|
| `discord.admin_chat_webhook` | admin chat |
| `discord.new_player_webhook` | first-time players |
| `discord.new_player_alt_playtime_minutes` | playtime limit for the second-account warning (not a secret) |

Pick one way, the URL goes only on the server host, never into the repository:

- **`server_config.toml`** next to the server binary (the file is not tracked by git):
  ```toml
  [discord]
  admin_chat_webhook = "https://discord.com/api/webhooks/<id>/<token>"
  new_player_webhook = "https://discord.com/api/webhooks/<id>/<token>"
  ```
- **Environment variables** (handy for Docker/systemd; `__` becomes `.`):
  ```
  ROBUST_CVAR_discord__admin_chat_webhook=https://discord.com/api/webhooks/<id>/<token>
  ROBUST_CVAR_discord__new_player_webhook=https://discord.com/api/webhooks/<id>/<token>
  ```
- **Console, no restart**: `cvar discord.admin_chat_webhook "<url>"` (host only). It applies immediately. Add it to the toml too, otherwise it is lost on restart.

To disable a feed, set its CVar to an empty string.

## 3. Check
Join the server as an admin and write in admin chat (`]` prefix by default), or join with a never-seen account. The message should appear within a few seconds. If not, look at the server log for the `discord.feed` and `DISCORD` sawmills: "Could not resolve the Discord webhook" means a wrong or deleted URL; `Failed to Create message ... 404/401` also points to the URL.
