# PrintPrice Worker

A small Cloudflare Worker (free plan) at `https://printprice.gtd-relay.workers.dev`. It does two jobs:

| Route | Used by | What it does |
|---|---|---|
| `PUT /b/<id>` | desktop app, website | Stores an encrypted backup (`PPB1` blob, at most 512 KB) in KV for 400 days. Every upload restarts the 400 days. |
| `GET /b/<id>` | desktop app, website | Returns the stored blob, or 404. |
| `DELETE /b/<id>` | desktop app, website | Deletes the blob. |
| `GET /power?area=DK1\|DK2` | website | Gets the current Energi Data Service day-ahead prices, cached for 5 minutes. The last good answer per area is kept in KV (`power:DK1`, `power:DK2`, see below). |

- `<id>` is 32 lower-case hex characters worked out from the backup code (`Backup.StorageId` in `src\Backup.cs`). The Worker never gets the code, so it can't read a backup. The scheme is described in `src\Backup.cs`.
- CORS answers only `https://gettic.github.io`. A request from another website gets 403. The desktop app sends no Origin.
- Daily cap: **5 uploads (PUT) per IP per UTC day**. GET and DELETE don't count.
  - Every stored upload answers `X-Uploads-Left: <n>` and `X-Uploads-Reset: <next UTC midnight, ISO>`.
  - Over the cap, it answers 429 with `Retry-After: <seconds>` and the same reset header, and stores nothing.
  - Any other 429 is the short per-minute limit (5 writes and 30 reads a minute, `Retry-After: 60`).
- A failed KV call answers 503 ("busy").
- Power price fallback: Energi Data Service often answers 429 to Cloudflare's shared addresses.
  - When it does, or when it is down, `/power` answers 200 with the last good copy (from memory, else from KV) and the headers `X-Cache: STALE` and `X-Price-Stale: <ISO UTC time the copy was fetched>`.
  - A fresh answer has `X-Cache: MISS` or `HIT` and no `X-Price-Stale`.
  - With no copy at all, it answers 429 (`Retry-After: 60`) or 502.
  - The KV copy is rewritten only when the answer changed and the copy is at least 10 minutes old: about one write per 15-minute price slot per area, so up to about 200 writes a day.
  - Browsers can read `X-Price-Stale`, `X-Cache`, `X-Uploads-Left`, `X-Uploads-Reset` and `Retry-After` (CORS expose).
- Privacy: the daily counter is stored in KV for 2 days under `rl:<day>:<hash>`.
  - The hash is the first 16 bytes of SHA-256 over a label, the day, the optional `IP_SALT` Worker secret, and the IP. The IP itself is never stored.
  - `IP_SALT` is a random Worker secret (`npx --yes wrangler@4 secret put IP_SALT`), so the hash can't be reversed by trying every IPv4 address. It's not stored anywhere else.
- `IP_SALT` is set (2026-11-10).
- Costs: each stored upload uses 2 KV writes (the backup and the counter). With the price copies (up to about 200 writes a day), the free plan's 1,000 writes a day leave room for about 400 backups a day across all users.

## Deploy

Needs Node.js and a wrangler login to the account (`npx --yes wrangler@4 login`, once per PC). From this folder:

```
npx --yes wrangler@4 deploy
```

Deploy only with the owner's OK. There are no secrets in this folder: never put a token or account id in it.
