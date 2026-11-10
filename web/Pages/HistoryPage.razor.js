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

export function stopNarrow(key) {
    const w = watchers.get(key);
    if (!w) return;
    w.mq.removeEventListener('change', w.change);
    window.removeEventListener('beforeprint', w.before);
    window.removeEventListener('afterprint', w.after);
    watchers.delete(key);
}
