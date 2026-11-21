// The quote / invoice window (QuoteView): Esc anywhere on the page = Close, as QuoteForm's CancelButton on the desktop.
// Not while a dialog is open on top (AskDialog, a History confirm: they handle their own Esc), and in the name box the
// window decides (WebQuote.EscCloses: a changed name isn't lost by one Esc). One listener per open window (key).
const held = new Map();

export function holdEsc(key, view, dotnet) {
    releaseEsc(key);
    if (!view || !dotnet) return;
    const listener = function (e) {
        if (e.key !== 'Escape' || e.defaultPrevented || e.isComposing) return;
        if (!document.body.contains(view)) { releaseEsc(key); return; }
        // a dialog on top of the window: its own Esc closes it, not this window
        for (const o of document.querySelectorAll('.overlay')) if (!o.contains(view) && o.getClientRects().length) return;
        e.preventDefault();
        const a = document.activeElement;
        dotnet.invokeMethodAsync('Esc', !!(a && a.id === 'qv-name')).catch(function () { });
    };
    // capture: seen before the page's own handlers can re-render (and remove) a dialog that is on top
    document.addEventListener('keydown', listener, true);
    held.set(key, listener);
}

export function releaseEsc(key) {
    const l = held.get(key);
    if (l) { document.removeEventListener('keydown', l, true); held.delete(key); }
}
