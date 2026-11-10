// PrintPrice Worker: (1) encrypted History backups in KV, (2) cached Energi Data Service proxy for the website.
// Plain ES module Worker, no dependencies. Bindings (see wrangler.toml or the dashboard):
//   BACKUPS      KV namespace
//   PUT_LIMIT    rate limit binding (optional; skipped if missing)
//   READ_LIMIT   rate limit binding (optional; skipped if missing)
// The Worker never sees a backup code or plain data: only a 32-hex id and an encrypted "PPB1" blob (scheme.md v1).

const ALLOWED_ORIGINS = ["https://gettic.github.io"];  // add "http://localhost:5000" only while testing the website locally
const MAX_BLOB = 512 * 1024;                           // 512 KB
const MIN_BLOB = 68;                                   // "PPB1" + IV 16 + one AES block 16 + HMAC 32
const TTL_SECONDS = 400 * 24 * 3600;                   // 400 days, renewed on every PUT
const ID_RE = /^[0-9a-f]{32}$/;
const DAILY_UPLOADS = 5;                               // stored backups per IP per UTC day (GET and DELETE don't count)
const COUNTER_TTL_SECONDS = 2 * 24 * 3600;             // daily counters vanish after 2 days
const POWER_TTL_MS = 5 * 60 * 1000;                    // prices change every 15 min; 5 min cache is plenty
const powerMemo = {};                                  // per-isolate memory cache: { DK1: {t, body}, DK2: ... }

export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url);
    const origin = request.headers.get("Origin");
    const cors = corsHeaders(origin);

    // A browser from another site gets no CORS headers and no service. Non-browser clients (the desktop app)
    // send no Origin and are allowed; CORS is not a security wall, the encryption is.
    if (origin && !cors) return text(403, "origin not allowed", null);
    if (request.method === "OPTIONS") return new Response(null, { status: 204, headers: cors || {} });

    try {
      const m = url.pathname.match(/^\/b\/([^\/]+)$/);
      if (m) return await backup(request, env, m[1], cors);
      if (url.pathname === "/power" && request.method === "GET") return await power(url, env, ctx, cors);
      return text(404, "not found", cors);
    } catch (e) {
      return text(500, "server error", cors);   // no details, no data echoed
    }
  }
};

async function backup(request, env, id, cors) {
  if (!ID_RE.test(id)) return text(400, "bad id", cors);
  const ip = request.headers.get("CF-Connecting-IP") || "?";

  if (request.method === "GET") {
    if (!(await allowed(env.READ_LIMIT, "r:" + ip))) return minuteLimit(cors);
    let data;
    try { data = await env.BACKUPS.get(id, "arrayBuffer"); }
    catch (e) { return busy(cors); }
    if (data === null) return text(404, "no backup", cors);
    return new Response(data, { status: 200, headers: merge(cors, {
      "Content-Type": "application/octet-stream", "Cache-Control": "no-store" }) });
  }

  if (request.method === "PUT") {
    // Daily cap first (its 429 carries the reset time), then the short per-minute limiter.
    const day = await dailyCounter(env, ip);
    if (day.count >= DAILY_UPLOADS) {
      return text(429, "daily upload limit reached", cors, {
        "Retry-After": String(day.retryAfter), "X-Uploads-Left": "0", "X-Uploads-Reset": day.resetIso });
    }
    if (!(await allowed(env.PUT_LIMIT, "w:" + ip))) return minuteLimit(cors);
    const declared = parseInt(request.headers.get("Content-Length") || "0", 10);
    if (declared > MAX_BLOB) return text(413, "too big", cors);
    const buf = await request.arrayBuffer();
    if (buf.byteLength > MAX_BLOB) return text(413, "too big", cors);
    if (!looksLikeBlob(new Uint8Array(buf))) return text(400, "not a PrintPrice backup", cors);
    try { await env.BACKUPS.put(id, buf, { expirationTtl: TTL_SECONDS }); }   // overwrite = renew the 400 days
    catch (e) { return busy(cors); }                                         // e.g. the free plan's 1,000 writes/day
    const used = day.count + 1;
    try { await env.BACKUPS.put(day.key, String(used), { expirationTtl: COUNTER_TTL_SECONDS }); }
    catch (e) { }                                                            // the backup is stored; a missed count is harmless
    return new Response(null, { status: 204, headers: merge(cors, { "Cache-Control": "no-store",
      "X-Uploads-Left": String(Math.max(0, DAILY_UPLOADS - used)), "X-Uploads-Reset": day.resetIso }) });
  }

  if (request.method === "DELETE") {
    if (!(await allowed(env.PUT_LIMIT, "w:" + ip))) return minuteLimit(cors);
    try { await env.BACKUPS.delete(id); }
    catch (e) { return busy(cors); }
    return new Response(null, { status: 204, headers: cors || {} });
  }

  return text(405, "method not allowed", cors);
}

// Uploads today from this IP. The counter key holds no IP: "rl:<UTC day>:<first 16 bytes of SHA-256(label|salt|day|ip)>",
// kept 2 days. IP_SALT is an optional Worker secret (wrangler secret put IP_SALT) that makes the hash impossible to
// reverse by trying all IPv4 addresses. Costs one KV read per PUT and one extra KV write per stored backup.
async function dailyCounter(env, ip) {
  const now = new Date();
  const dayStr = now.toISOString().slice(0, 10);
  const reset = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() + 1));
  const data = new TextEncoder().encode("PrintPrice rl v1|" + (env.IP_SALT || "") + "|" + dayStr + "|" + ip);
  const hash = new Uint8Array(await crypto.subtle.digest("SHA-256", data));
  let hex = "";
  for (let i = 0; i < 16; i++) hex += hash[i].toString(16).padStart(2, "0");
  const key = "rl:" + dayStr + ":" + hex;
  let count = 0;
  try { count = parseInt((await env.BACKUPS.get(key)) || "0", 10) || 0; }
  catch (e) { }                                                              // can't read: don't block the upload
  return { key: key, count: count, resetIso: reset.toISOString().replace(/\.\d{3}Z$/, "Z"),
           retryAfter: Math.max(1, Math.ceil((reset.getTime() - now.getTime()) / 1000)) };
}

function minuteLimit(cors) { return text(429, "too many requests", cors, { "Retry-After": "60" }); }

// Shape check only (the Worker can't and shouldn't verify the HMAC).
function looksLikeBlob(b) {
  if (b.length < MIN_BLOB) return false;
  if (b[0] !== 0x50 || b[1] !== 0x50 || b[2] !== 0x42 || b[3] !== 0x31) return false;   // "PPB1"
  return (b.length - 4 - 16 - 32) % 16 === 0;                                            // whole AES blocks
}

// Same query as src\PowerPrice.cs, so the website can reuse PickCurrent.
function powerUrl(area) {
  return "https://api.energidataservice.dk/dataset/DayAheadPrices" +
    "?start=now-PT3H&end=now%2BPT1H" +
    "&filter=" + encodeURIComponent('{"PriceArea":["' + area + '"]}') +
    "&columns=TimeUTC,DayAheadPriceDKK&sort=TimeUTC%20asc&limit=1000";
}

async function power(url, env, ctx, cors) {
  const area = url.searchParams.get("area") === "DK2" ? "DK2" : "DK1";   // nothing else is passed upstream
  const now = Date.now();
  const memo = powerMemo[area];
  if (memo && now - memo.t < POWER_TTL_MS) return json(200, memo.body, cors, "HIT");

  const upstream = powerUrl(area);

  // Edge cache too (works on custom domains; may be a no-op on workers.dev, then the memory cache carries it).
  const cacheKey = new Request("https://cache.printprice/power/" + area);
  const cache = caches.default;
  const hit = await cache.match(cacheKey);
  if (hit) {
    const body = await hit.text();
    powerMemo[area] = { t: now, body: body };
    return json(200, body, cors, "HIT");
  }

  let r = null;
  try { r = await fetch(upstream, { headers: { "User-Agent": "PrintPrice-Worker", "Accept": "application/json" } }); }
  catch (e) { r = null; }
  if (!r || !r.ok) {
    // Energi Data Service busy (429, it often limits Cloudflare's shared addresses) or down: serve the last good copy,
    // from this isolate's memory, else from KV, with X-Price-Stale = when that copy was fetched.
    if (memo) return json(200, memo.body, cors, "STALE", { "X-Price-Stale": iso(memo.t) });
    const kept = await keptPrice(env, area);
    if (kept) {
      powerMemo[area] = { t: kept.t, body: kept.body };
      return json(200, kept.body, cors, "STALE", { "X-Price-Stale": iso(kept.t) });
    }
    return r && r.status === 429 ? text(429, "price service busy", cors, { "Retry-After": "60" })
                                 : text(502, "price service unavailable", cors);
  }
  const body = await r.text();
  powerMemo[area] = { t: now, body: body };
  ctx.waitUntil(cache.put(cacheKey, new Response(body, {
    headers: { "Content-Type": "application/json", "Cache-Control": "max-age=300" } })));
  ctx.waitUntil(keepPrice(env, area, body, now));
  return json(200, body, cors, "MISS");
}

// The last good /power answer per area in KV ("power:DK1"; metadata t = fetch time, ms). Written only when the answer
// changed and the kept copy is at least 10 minutes old: about one write per 15-minute price slot per area.
const PRICE_KEEP_MS = 10 * 60 * 1000;
async function keptPrice(env, area) {
  try {
    const v = await env.BACKUPS.getWithMetadata("power:" + area);
    if (v && v.value !== null && v.metadata && v.metadata.t) return { body: v.value, t: v.metadata.t };
  } catch (e) { }
  return null;
}
async function keepPrice(env, area, body, now) {
  const kept = await keptPrice(env, area);
  if (kept && (kept.body === body || now - kept.t < PRICE_KEEP_MS)) return;
  try { await env.BACKUPS.put("power:" + area, body, { metadata: { t: now } }); } catch (e) { }
}
function iso(ms) { return new Date(ms).toISOString().replace(/\.\d{3}Z$/, "Z"); }

async function allowed(limiter, key) {
  if (!limiter) return true;                     // binding not set up: rely on KV's daily write cap
  const r = await limiter.limit({ key: key });
  return r.success;
}

function corsHeaders(origin) {
  if (!origin || ALLOWED_ORIGINS.indexOf(origin) < 0) return null;
  return {
    "Access-Control-Allow-Origin": origin,
    "Access-Control-Allow-Methods": "GET, PUT, DELETE, OPTIONS",
    "Access-Control-Allow-Headers": "Content-Type",
    "Access-Control-Expose-Headers": "X-Uploads-Left, X-Uploads-Reset, Retry-After, X-Price-Stale, X-Cache",
    "Access-Control-Max-Age": "86400",
    "Vary": "Origin"
  };
}

// A KV call failed (daily free-plan limit or a storage hiccup): the app shows "busy, try later".
function busy(cors) { return text(503, "storage busy", cors); }
function merge(a, b) { return Object.assign({}, a || {}, b); }
function text(status, msg, cors, extra) {
  return new Response(msg, { status: status, headers: merge(merge(cors, extra), { "Content-Type": "text/plain; charset=utf-8", "Cache-Control": "no-store" }) });
}
function json(status, body, cors, cacheState, extra) {
  return new Response(body, { status: status, headers: merge(merge(cors, extra), {
    "Content-Type": "application/json", "Cache-Control": "public, max-age=60", "X-Cache": cacheState }) });
}
