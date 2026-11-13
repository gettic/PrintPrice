// History page (HistoryPage.razor): app.css shows the phone cards at max-width 720px and the table above it, so C#
// builds only the list that is shown. Printing builds both (there the paper's width decides). The calls are
// synchronous, so the list is in place before the browser paints or prints. One watcher per page instance (key).
const watchers = new Map();

export function watchNarrow(dotnet, key) {
    stopNarrow(key);
    const mq = window.matchMedia('(max-width: 720px)');
    const w = {
        mq: mq,
        change: function () { try { dotnet.invokeMethod('NarrowChanged', mq.matches); } catch (e) { } },
        before: function () { try { dotnet.invokeMethod('PrintMode', true); } catch (e) { } },
        after: function () { try { dotnet.invokeMethod('PrintMode', false); } catch (e) { } }
    };
    mq.addEventListener('change', w.change);
    window.addEventListener('beforeprint', w.before);
    window.addEventListener('afterprint', w.after);
    watchers.set(key, w);
    return mq.matches;
}

// ---- idea 22: the quotes' pictures. C# renders <img data-pic="<record id>"> without src; when one comes near the
// screen, its "pic:<id>" data URL is read from IndexedDB here (window.printPrice.idbGetMany, a batch per transaction),
// made into a Blob object URL and set as src. The bytes never go through C#. URLs are kept while the History page is
// open and revoked when it closes.
const urls = new Map();     // record id -> object URL
const pend = new Set();
let io = null, mo = null, timer = 0, gen = 0, users = 0;

export function watchPictures() {
    users++;
    if (mo) { scan(document); return; }
    io = 'IntersectionObserver' in window ? new IntersectionObserver(seen, { rootMargin: '400px 0px' }) : null;
    mo = new MutationObserver(changed);
    mo.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['data-pic'] });
    scan(document);
}

export function stopPictures() {
    if (--users > 0) return;
    users = 0;
    gen++;
    if (io) io.disconnect();
    if (mo) mo.disconnect();
    io = mo = null;
    clearTimeout(timer); timer = 0;
    pend.clear();
    urls.forEach(function (u) { URL.revokeObjectURL(u); });
    urls.clear();
}

function scan(node) {
    if (node.matches && node.matches('img[data-pic]')) add(node);
    if (node.querySelectorAll) node.querySelectorAll('img[data-pic]').forEach(add);
}

function add(img) {
    const id = img.getAttribute('data-pic');
    if (!id) return;
    const u = urls.get(id);
    if (u) { if (img.getAttribute('src') !== u) img.setAttribute('src', u); return; }
    if (img.hasAttribute('src')) img.removeAttribute('src');   // the element now shows another quote
    if (io) io.observe(img); else want(id);
}

function changed(list) {
    list.forEach(function (m) {
        if (m.type === 'attributes') add(m.target);
        else m.addedNodes.forEach(function (n) { if (n.nodeType === 1) scan(n); });
    });
}

function seen(entries) {
    entries.forEach(function (e) {
        if (!e.isIntersecting) return;
        if (io) io.unobserve(e.target);
        want(e.target.getAttribute('data-pic'));
    });
}

function want(id) {
    if (!id || urls.has(id)) return;
    pend.add(id);
    if (!timer) timer = setTimeout(load, 20);   // gather what scrolled in together into one read
}

async function load() {
    timer = 0;
    const g = gen, ids = Array.from(pend);
    pend.clear();
    const pp = window.printPrice;
    if (!pp || !pp.idbGetMany || ids.length === 0) return;
    for (let i = 0; i < ids.length; i += 50) {
        const part = ids.slice(i, i + 50);
        let a = null;
        try { a = await pp.idbGetMany(part.map(function (id) { return 'pic:' + id; })); } catch (e) { a = null; }
        if (g !== gen) return;   // the page closed meanwhile
        if (!a || !a.ok || !a.values) continue;
        part.forEach(function (id, k) {
            if (urls.has(id)) return;
            const b = toBlob(a.values[k]);
            if (b) urls.set(id, URL.createObjectURL(b));
        });
    }
    document.querySelectorAll('img[data-pic]').forEach(function (img) {
        const u = urls.get(img.getAttribute('data-pic'));
        if (u && img.getAttribute('src') !== u) img.setAttribute('src', u);
    });
}

function toBlob(url) {
    if (typeof url !== 'string' || url.indexOf('data:image/') !== 0) return null;
    const c = url.indexOf(',');
    if (c < 0) return null;
    const head = url.substring(5, c);
    if (!/;base64$/.test(head)) return null;
    try {
        const bin = atob(url.substring(c + 1));
        const bytes = new Uint8Array(bin.length);
        for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
        return new Blob([bytes], { type: head.substring(0, head.length - 7) });
    } catch (e) { return null; }
}

// ---- idea 22 (qol W2): the Delete / Clear all ask. Focus goes to its Cancel button ([data-cancel]); Tab and Shift+Tab
// stay inside it; Esc anywhere on the page = Cancel. releaseDialog puts the focus back where it was.
let held = null;

export function holdDialog(el) {
    releaseDialog();
    if (!el) return;
    const before = document.activeElement;
    const items = function () {
        return Array.from(el.querySelectorAll('button:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])'))
            .filter(function (x) { return x.offsetParent !== null; });
    };
    const key = function (e) {
        if (!document.body.contains(el)) { releaseDialog(); return; }
        if (e.key === 'Escape') {
            e.preventDefault(); e.stopPropagation();
            const c = el.querySelector('[data-cancel]');
            if (c) c.click();
        } else if (e.key === 'Tab') {
            const l = items();
            if (l.length === 0) return;
            const i = l.indexOf(document.activeElement);
            let n = i < 0 ? 0 : (e.shiftKey ? i - 1 : i + 1);
            if (n < 0) n = l.length - 1;
            if (n >= l.length) n = 0;
            e.preventDefault();
            l[n].focus();
        }
    };
    document.addEventListener('keydown', key, true);
    held = { key: key, before: before };
    const c = el.querySelector('[data-cancel]');
    if (c) c.focus();
}

export function releaseDialog() {
    if (!held) return;
    document.removeEventListener('keydown', held.key, true);
    const b = held.before;
    held = null;
    try { if (b && document.body.contains(b) && b.focus) b.focus(); } catch (e) { }
}

export function stopNarrow(key) {
    const w = watchers.get(key);
    if (!w) return;
    w.mq.removeEventListener('change', w.change);
    window.removeEventListener('beforeprint', w.before);
    window.removeEventListener('afterprint', w.after);
    watchers.delete(key);
}
