/* floQ API-Client — alle UI-Flows laufen über /api/v1 (API-First).
   Antwortformat: { success, data, errorMessage }. */
const floqApi = (() => {
    async function request(method, url, body) {
        const opts = { method, headers: {} };
        if (body !== undefined) {
            opts.headers['Content-Type'] = 'application/json';
            opts.body = JSON.stringify(body);
        }
        const resp = await fetch(url, opts);
        if (resp.status === 401) {
            window.location.href = '/auth/login?ReturnUrl=' + encodeURIComponent(window.location.pathname + window.location.search);
            throw new Error('Nicht angemeldet.');
        }
        let envelope;
        try {
            envelope = await resp.json();
        } catch {
            throw new Error(`Serverfehler (${resp.status}).`);
        }
        if (!envelope.success) throw new Error(envelope.errorMessage || `Fehler (${resp.status}).`);
        return envelope.data;
    }

    return {
        get: (url) => request('GET', url),
        post: (url, body) => request('POST', url, body),
        put: (url, body) => request('PUT', url, body),
        del: (url) => request('DELETE', url),
    };
})();

/* Formatierung: Anzeige immer de-AT (Beträge, Datum). */
const floqFmt = {
    money: (v) => new Intl.NumberFormat('de-AT', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(v ?? 0) + ' €',
    date: (iso) => {
        if (!iso) return '–';
        const d = new Date(iso);
        return d.toLocaleDateString('de-AT', { day: '2-digit', month: '2-digit', year: 'numeric' });
    },
    /* ISO-Datum (yyyy-MM-dd) für <input type="date"> aus einem API-DateTime. */
    dateInput: (iso) => (iso ? iso.substring(0, 10) : ''),
};

/* DOM-Helper: h('div', {class:'x', onclick:fn}, [child, 'text']) — XSS-sicher,
   weil Texte immer als TextNodes landen (nie HTML-Parsing von Daten). */
function h(tag, attrs = {}, children = []) {
    const el = document.createElement(tag);
    for (const [k, v] of Object.entries(attrs)) {
        if (v === null || v === undefined || v === false) continue;
        if (k.startsWith('on') && typeof v === 'function') el.addEventListener(k.substring(2), v);
        else if (k === 'class') el.className = v;
        else el.setAttribute(k, v);
    }
    for (const c of [].concat(children)) {
        if (c === null || c === undefined) continue;
        el.append(c instanceof Node ? c : document.createTextNode(String(c)));
    }
    return el;
}

/* Container leeren und mit neuen Kindern füllen. */
function hFill(container, children) {
    container.replaceChildren(...[].concat(children).filter(Boolean));
}

/* Beleg-Status als Zeichen, nicht als Farbe (Mono, Versal). Storniert
   durchgestrichen. withLabel=false → nur das Glyph (kompakte Listen). */
const FLOQ_STATUS_SIGN = { 0: '○', 1: '●', 2: '➔', 3: '◐', 4: '✕' };
const FLOQ_STATUS_LABEL = { 0: 'Entwurf', 1: 'Abgeschlossen', 2: 'Versendet', 3: 'Gesehen', 4: 'Storniert' };
function floqStatusEl(status, withLabel = true) {
    const sign = FLOQ_STATUS_SIGN[status] ?? '○';
    const cls = 'status-sign'
        + (withLabel ? '' : ' glyph-only')
        + (status === 4 ? ' is-storniert' : '');
    const text = withLabel ? `${sign} ${(FLOQ_STATUS_LABEL[status] || '').toUpperCase()}` : sign;
    return h('span', { class: cls }, text);
}

/* Bestätigungs-Modal (ersetzt natives confirm()). Promise<bool>. */
function floqConfirm({ eyebrow = 'Bestätigen', title, text = '', confirm = 'Bestätigen' }) {
    return new Promise(resolve => {
        const done = (ok) => { scrim.remove(); resolve(ok); };
        const scrim = h('div', { class: 'modal-scrim', onclick: e => { if (e.target === scrim) done(false); } }, [
            h('div', { class: 'modal narrow' }, [
                h('div', { class: 'modal-header' }, [
                    h('div', { class: 'modal-eyebrow' }, eyebrow),
                    h('div', { class: 'modal-title sm' }, title),
                    text ? h('div', { class: 'modal-text' }, text) : null,
                ]),
                h('div', { class: 'modal-footer' }, [
                    h('button', { class: 'btn-text', type: 'button', onclick: () => done(false) }, 'Abbrechen'),
                    h('button', { class: 'btn btn-primary', type: 'button', onclick: () => done(true) }, confirm),
                ]),
            ]),
        ]);
        document.body.appendChild(scrim);
    });
}

/* Toast-Meldung — verschwindet nach 4s. Fehler: gleicher Look + „! " (kein Rot). */
function floqToast(message, isError = false) {
    let host = document.getElementById('floqToastHost');
    if (!host) {
        host = document.createElement('div');
        host.id = 'floqToastHost';
        host.className = 'toast-host';
        document.body.appendChild(host);
    }
    const el = document.createElement('div');
    el.className = 'toast';
    el.textContent = (isError ? '! ' : '') + message;
    host.appendChild(el);
    setTimeout(() => el.remove(), 4000);
}
