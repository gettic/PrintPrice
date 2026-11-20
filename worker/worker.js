// PrintPrice Worker: (1) encrypted History backups in KV, (2) cached day-ahead power prices for the website
// (Energi Data Service, else elprisenligenu.dk, else the last good copy).
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
    const buf = await readCapped(request, MAX_BLOB);   // null = over the cap (also without a Content-Length)
    if (!buf) return text(413, "too big", cors);
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

// The request body as an ArrayBuffer, read piece by piece; stops and returns null as soon as it passes max bytes.
async function readCapped(request, max) {
  if (!request.body) return new ArrayBuffer(0);
  const reader = request.body.getReader();
  const parts = [];
  let size = 0;
  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    size += value.byteLength;
    if (size > max) { try { await reader.cancel(); } catch (e) { } return null; }
    parts.push(value);
  }
  const out = new Uint8Array(size);
  let at = 0;
  for (const p of parts) { out.set(p, at); at += p.byteLength; }
  return out.buffer;
}

function minuteLimit(cors) { return text(429, "too many requests", cors, { "Retry-After": "60" }); }

// Shape check only (the Worker can't and shouldn't verify the HMAC).
function looksLikeBlob(b) {
  if (b.length < MIN_BLOB) return false;
  if (b[0] !== 0x50 || b[1] !== 0x50 || b[2] !== 0x42 || b[3] !== 0x31) return false;   // "PPB1"
  return (b.length - 4 - 16 - 32) % 16 === 0;                                            // whole AES blocks
}

// /power answers {"records":[{"TimeUTC":"2026-11-20T13:00:00","DayAheadPriceDKK":1881.0}, ...]} (DKK per MWh, UTC
// without "Z", oldest first), the shape of Energi Data Service's DayAheadPrices, so the website's PickCurrent (the
// latest slot that has started, at most 2 h old) works on it unchanged. It covers a whole day: from the start of the
// UTC hour 3 h ago to 36 h later, so a kept copy still has the current slot for hours (and tomorrow once published).
// Sources in order: Energi Data Service (15-min slots), then elprisenligenu.dk (free open API, hourly, DKK/kWh,
// converted), then the last good copy in KV. Energi Data Service often answers 429 to Cloudflare's shared addresses.
const WINDOW_BACK_H = 3, WINDOW_AHEAD_H = 36;
const UPSTREAM_MS = 8000;                              // each upstream request: 8 s at most
const KEPT_FRESH_MS = 60 * 60 * 1000;                  // a KV copy younger than this that still covers the next hour is served without asking upstream
const SOURCE_EDS = "Energi Data Service", SOURCE_ELPRIS = "elprisenligenu.dk";

// The window [start, end) for "now": start = the start of the UTC hour WINDOW_BACK_H hours ago, so the query (and the
// answer) only changes once an hour, which keeps KV writes low.
function priceWindow(now) {
  const start = Math.floor((now - WINDOW_BACK_H * 3600000) / 3600000) * 3600000;
  return { start: start, end: start + (WINDOW_BACK_H + WINDOW_AHEAD_H) * 3600000 };
}

// "2026-11-20T13:00" (UTC, minutes) for Energi Data Service's start/end.
function edsTime(ms) { return new Date(ms).toISOString().slice(0, 16); }
// "2026-11-20T13:00:00" (UTC, no zone) as in Energi Data Service's TimeUTC.
function recTime(ms) { return new Date(ms).toISOString().slice(0, 19); }

function edsUrl(area, win) {
  return "https://api.energidataservice.dk/dataset/DayAheadPrices" +
    "?start=" + edsTime(win.start) + "&end=" + edsTime(win.end) +
    "&filter=" + encodeURIComponent('{"PriceArea":["' + area + '"]}') +
    "&columns=TimeUTC,DayAheadPriceDKK&sort=TimeUTC%20asc&limit=1000";
}

// fetch with a timeout; null on a network error / timeout. Logs the answer (status or error) for `wrangler tail`.
async function upstream(label, url) {
  try {
    const r = await fetch(url, { headers: { "User-Agent": "PrintPrice-Worker", "Accept": "application/json" },
                                 signal: AbortSignal.timeout(UPSTREAM_MS) });
    console.log("power upstream " + label + ": " + r.status);
    if (label.indexOf("EDS") === 0) lastEdsStatus = r.status;
    return r;
  } catch (e) {
    console.log("power upstream " + label + ": error " + (e && e.name ? e.name : "") + " " + (e && e.message ? e.message : ""));
    return null;
  }
}

// The records of a JSON body (array of {TimeUTC, DayAheadPriceDKK} with a numeric price), or null.
function recordsOf(body) {
  try {
    const d = JSON.parse(body);
    if (!d || !Array.isArray(d.records)) return null;
    return d.records.filter(x => x && typeof x.TimeUTC === "string" && typeof x.DayAheadPriceDKK === "number" && isFinite(x.DayAheadPriceDKK));
  } catch (e) { return null; }
}

// Energi Data Service: the window's records, or null (busy / down / bad answer / none).
async function fromEds(area, win) {
  const r = await upstream("EDS " + area, edsUrl(area, win));
  if (!r || !r.ok) return null;
  let body;
  try { body = await r.text(); } catch (e) { console.log("power upstream EDS " + area + ": body cut off"); return null; }
  const recs = recordsOf(body);
  if (!recs || recs.length === 0) { console.log("power upstream EDS " + area + ": no records"); return null; }
  return recs.map(x => ({ TimeUTC: x.TimeUTC, DayAheadPriceDKK: x.DayAheadPriceDKK }));
}

// The Copenhagen calendar date of a time: "2026/11-20" as in elprisenligenu.dk's path.
function cphDay(ms) {
  const p = new Intl.DateTimeFormat("en-CA", { timeZone: "Europe/Copenhagen", year: "numeric", month: "2-digit", day: "2-digit" })
    .formatToParts(new Date(ms));
  const get = t => (p.find(x => x.type === t) || {}).value;
  return get("year") + "/" + get("month") + "-" + get("day");
}

// elprisenligenu.dk: one file per Danish day (/api/v1/prices/2026/11-20_DK1.json, entries {DKK_per_kWh, time_start
// with offset}), excl. VAT like Energi Data Service. Fetches the Danish days the window touches (tomorrow's file
// is 404 until about 13:00, which is fine), converts to records in the window, or null when none.
async function fromElpris(area, win) {
  const days = [];
  for (let t = win.start; t < win.end + 3600000; t += 6 * 3600000) {
    const d = cphDay(Math.min(t, win.end));
    if (days.indexOf(d) < 0) days.push(d);
  }
  const recs = [];
  for (const d of days) {
    const r = await upstream("elpris " + d + "_" + area, "https://www.elprisenligenu.dk/api/v1/prices/" + d + "_" + area + ".json");
    if (!r || !r.ok) continue;
    let list;
    try { list = JSON.parse(await r.text()); } catch (e) { continue; }
    if (!Array.isArray(list)) continue;
    for (const x of list) {
      if (!x || typeof x.DKK_per_kWh !== "number" || !isFinite(x.DKK_per_kWh) || typeof x.time_start !== "string") continue;
      const t = Date.parse(x.time_start);
      if (isNaN(t) || t < win.start || t >= win.end) continue;
      recs.push({ TimeUTC: recTime(t), DayAheadPriceDKK: Math.round(x.DKK_per_kWh * 1000 * 1000) / 1000 });   // DKK/kWh -> DKK/MWh
    }
  }
  if (recs.length === 0) return null;
  recs.sort((a, b) => a.TimeUTC < b.TimeUTC ? -1 : a.TimeUTC > b.TimeUTC ? 1 : 0);
  return recs;
}

// true when the records hold a slot that has started at most 2 h before `now` (what PickCurrent accepts).
function hasCurrent(recs, now) {
  return !!recs && recs.some(x => { const t = Date.parse(x.TimeUTC + "Z"); return t <= now && now - t <= 2 * 3600000; });
}
// true when the records already reach at least `ms` (a slot starting at or after it).
function reaches(recs, ms) {
  return !!recs && recs.some(x => Date.parse(x.TimeUTC + "Z") >= ms);
}

async function power(url, env, ctx, cors) {
  const area = url.searchParams.get("area") === "DK2" ? "DK2" : "DK1";   // nothing else is passed upstream
  const now = Date.now();
  const memo = powerMemo[area];
  // memory: asked upstream (or KV) less than 5 min ago and it still has the current slot
  if (memo && now - memo.checked < POWER_TTL_MS && hasCurrent(memo.recs, now)) return priceAnswer(memo, cors, "HIT", now);

  // KV: a copy fetched within the hour that still covers the next hour is served without asking upstream
  const kept = await keptPrice(env, area);
  if (kept && now - kept.t < KEPT_FRESH_MS && hasCurrent(kept.recs, now) && reaches(kept.recs, now + 3600000)) {
    const entry = { checked: now, fetched: kept.t, source: kept.source, body: kept.body, recs: kept.recs };
    powerMemo[area] = entry;
    return priceAnswer(entry, cors, "KEPT", now);
  }

  // upstream: Energi Data Service, then elprisenligenu.dk (each 8 s at most)
  const win = priceWindow(now);
  lastEdsStatus = 0;
  let recs = await fromEds(area, win), source = SOURCE_EDS;
  if (!hasCurrent(recs, now)) {
    const alt = await fromElpris(area, win);
    if (hasCurrent(alt, now)) { recs = alt; source = SOURCE_ELPRIS; }
    else recs = null;
  }
  if (recs) {
    const body = JSON.stringify({ records: recs });
    const entry = { checked: now, fetched: now, source: source, body: body, recs: recs };
    powerMemo[area] = entry;
    ctx.waitUntil(keepPrice(env, area, body, now, source, kept));
    return priceAnswer(entry, cors, "MISS", now);
  }

  // both failed: the last good copy (memory or KV, the newer one), marked stale; not asked upstream again for 5 min
  console.log("power " + area + ": no source answered, serving the kept copy if any");
  let old = kept && kept.recs && kept.recs.length ? { fetched: kept.t, source: kept.source, body: kept.body, recs: kept.recs } : null;
  if (memo && memo.recs && memo.recs.length && (!old || memo.fetched > old.fetched)) old = memo;
  if (old) {
    const entry = { checked: now, fetched: old.fetched, source: old.source, body: old.body, recs: old.recs };
    powerMemo[area] = entry;
    return priceAnswer(entry, cors, "STALE", now);
  }
  return lastEdsStatus === 429 ? text(429, "price service busy", cors, { "Retry-After": "60" })
                               : text(502, "price service unavailable", cors);
}

// The /power answer: the records body plus X-Cache, X-Price-Source (which service the prices came from),
// X-Price-Fetched (ISO UTC time they were fetched) and, when the copy is old or no source answered, X-Price-Stale (same
// time; kept for the website version that reads it).
let lastEdsStatus = 0;
function priceAnswer(entry, cors, cacheState, now) {
  const extra = { "X-Price-Source": entry.source || SOURCE_EDS, "X-Price-Fetched": iso(entry.fetched) };
  if (cacheState === "STALE") extra["X-Price-Stale"] = iso(entry.fetched);
  return json(200, entry.body, cors, cacheState, extra);
}

// The last good /power answer per area in KV ("power:DK1"; metadata t = fetch time in ms, s = source). Old copies
// (raw Energi Data Service bodies, no s) still read. Written only when the answer changed and the kept copy is at
// least 10 minutes old; the window only moves once an hour, so about 1-2 writes per hour per area.
const PRICE_KEEP_MS = 10 * 60 * 1000;
async function keptPrice(env, area) {
  try {
    const v = await env.BACKUPS.getWithMetadata("power:" + area);
    if (v && v.value !== null && v.metadata && v.metadata.t) {
      const recs = recordsOf(v.value);
      return { body: v.value, t: v.metadata.t, source: v.metadata.s || SOURCE_EDS, recs: recs || [] };
    }
  } catch (e) { }
  return null;
}
async function keepPrice(env, area, body, now, source, kept) {
  if (kept && (kept.body === body || now - kept.t < PRICE_KEEP_MS)) return;
  try { await env.BACKUPS.put("power:" + area, body, { metadata: { t: now, s: source } }); } catch (e) { }
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
    "Access-Control-Expose-Headers": "X-Uploads-Left, X-Uploads-Reset, Retry-After, X-Price-Stale, X-Price-Fetched, X-Price-Source, X-Cache",
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
