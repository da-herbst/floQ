/* Beleg-Übersicht — Liste + clientseitige Filter-Pills (Konzept batOS-V2
   entitylist/acc-Filter, floQ-schlank). Datenquelle /api/v1/documents. */
(async () => {
    const TYPE_CODE = { 1: 'AN', 2: 'RE', 3: 'GS', 4: 'SR', 5: 'MA' };
    const TYPE_LABEL = { 1: 'Angebot', 2: 'Rechnung', 3: 'Gutschrift', 4: 'Stornorechnung', 5: 'Mahnung' };
    const STATUS_LABEL = { 0: 'Entwurf', 1: 'Abgeschlossen', 2: 'Versendet', 3: 'Gesehen', 4: 'Storniert' };

    let rows = [];
    const activeTypes = new Set();
    const activeStatuses = new Set();

    async function load() {
        try {
            rows = await floqApi.get('/api/v1/documents');
        } catch (e) {
            floqToast(e.message, true);
            return;
        }

        // Vorbelegung aus der URL (?type=2) — Deep-Link vom Dashboard.
        const urlType = new URLSearchParams(window.location.search).get('type');
        [...new Set(rows.map(r => r.type))].forEach(t => {
            if (!urlType || String(t) === urlType) activeTypes.add(t);
        });
        [...new Set(rows.map(r => r.status))].forEach(s => activeStatuses.add(s));

        renderPills();
        renderList();
    }

    function metaLine(r) {
        const typeText = r.type === 5 && r.reminderLevel !== null
            ? (r.reminderLevel === 0 ? 'Zahlungserinnerung' : `${r.reminderLevel}. Mahnung`)
            : TYPE_LABEL[r.type];
        const parts = [typeText, floqFmt.date(r.dateVienna)];
        if (r.servicePeriod) parts.push(r.servicePeriod);
        return parts.join(' · ');
    }

    function renderList() {
        const filtered = rows.filter(r => activeTypes.has(r.type) && activeStatuses.has(r.status));

        const list = filtered.map(r =>
            h('div', { class: 'ent-row', onclick: () => window.location.href = `/Billing/Document?id=${r.id}` }, [
                h('span', { class: 'ent-code', title: TYPE_LABEL[r.type] }, TYPE_CODE[r.type]),
                h('div', { class: 'ent-main' }, [
                    h('div', { class: 'ent-number' }, r.number || 'Entwurf'),
                    h('div', { class: 'ent-meta' }, metaLine(r)),
                ]),
                h('div', { class: 'ent-customer' }, r.customerName && r.customerName !== '–' ? r.customerName : ''),
                floqStatusEl(r.status),
                h('span', { class: `ent-amount num${r.gross === 0 ? ' is-zero' : ''}` }, floqFmt.money(r.gross)),
            ]));

        hFill(document.getElementById('docList'),
            list.length ? list : h('div', { class: 'ent-empty' }, 'Keine Belege gefunden'));

        document.getElementById('countShown').textContent = String(filtered.length);
        document.getElementById('countTotal').textContent = String(rows.length);
        document.getElementById('sumCount').textContent = String(filtered.length);
        document.getElementById('sumDrafts').textContent = String(filtered.filter(r => r.status === 0).length);
        document.getElementById('sumGross').textContent = floqFmt.money(filtered.reduce((s, r) => s + r.gross, 0));
    }

    function filterItem(label, count, set, key) {
        const el = h('button', {
            class: 'filter-item' + (set.has(key) ? ' is-active' : ''),
            type: 'button',
            onclick: () => {
                if (set.has(key)) set.delete(key); else set.add(key);
                el.classList.toggle('is-active');
                renderList();
            },
        }, [h('span', { class: 'fi-label' }, label), h('span', {}, String(count))]);
        return el;
    }

    function renderPills() {
        const types = [...new Set(rows.map(r => r.type))].sort((a, b) => a - b);
        const statuses = [...new Set(rows.map(r => r.status))].sort((a, b) => a - b);
        hFill(document.getElementById('typePills'),
            types.map(t => filterItem(`${TYPE_CODE[t]} ${TYPE_LABEL[t].toUpperCase()}`, rows.filter(r => r.type === t).length, activeTypes, t)));
        hFill(document.getElementById('statusPills'),
            statuses.map(s => filterItem(`${FLOQ_STATUS_SIGN[s]} ${STATUS_LABEL[s].toUpperCase()}`, rows.filter(r => r.status === s).length, activeStatuses, s)));
    }

    // Neuer Beleg: Typ-Auswahl-Modal → Draft anlegen → Workbench.
    const modal = document.getElementById('newDocModal');
    document.getElementById('btnNewDocument').addEventListener('click', () => { modal.hidden = false; });
    modal.querySelectorAll('[data-modal-close]').forEach(b => b.addEventListener('click', () => { modal.hidden = true; }));
    modal.addEventListener('click', e => { if (e.target === modal) modal.hidden = true; });
    modal.querySelectorAll('[data-new-type]').forEach(btn => btn.addEventListener('click', async () => {
        const type = Number(btn.dataset.newType);
        // Gutschrift/Storno/Mahnung entstehen aus einer Rechnung — die Workbench
        // zeigt zuerst den Original-Picker (kein leerer Draft vorab).
        if (type >= 3) {
            window.location.href = `/Billing/Document?new=${type}`;
            return;
        }
        try {
            const { id } = await floqApi.post('/api/v1/documents', { type });
            window.location.href = `/Billing/Document?id=${id}`;
        } catch (e) { floqToast(e.message, true); }
    }));

    await load();
})();
