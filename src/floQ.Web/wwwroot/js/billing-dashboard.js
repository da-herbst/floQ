/* Dashboard Verrechnung — KPIs und Kurzlisten aus /api/v1/documents
   (Konzept nach batOS-V2-Billing-Dashboard, floQ: Owner-only, kein Job-Bezug).
   Rendering XSS-sicher via h()-DOM-Helper (api.js). */
(async () => {
    document.getElementById('dashDate').textContent =
        new Date().toLocaleDateString('de-AT', { weekday: 'long', day: '2-digit', month: 'long', year: 'numeric' });

    document.getElementById('btnNewInvoice').addEventListener('click', async () => {
        try {
            const { id } = await floqApi.post('/api/v1/documents', { type: 2 /* Invoice */ });
            window.location.href = `/Billing/Document?id=${id}`;
        } catch (e) { floqToast(e.message, true); }
    });

    let rows;
    try {
        rows = await floqApi.get('/api/v1/documents');
    } catch (e) {
        floqToast(e.message, true);
        return;
    }

    const today = new Date(); today.setHours(0, 0, 0, 0);
    const isInvoice = r => r.type === 2;
    const drafts = rows.filter(r => r.status === 0);
    const openInvoices = rows.filter(r => isInvoice(r) && r.status !== 0 && r.status !== 4 && r.remaining > 0);
    const overdue = openInvoices.filter(r => r.dueDateVienna && new Date(r.dueDateVienna) < today);
    const year = new Date().getFullYear();
    const yearRevenue = rows
        .filter(r => isInvoice(r) && r.status !== 0 && r.status !== 4 && new Date(r.dateVienna).getFullYear() === year)
        .reduce((s, r) => s + r.gross, 0);

    // Keine Statusfarben: Überfällig wird durch Unterstreichung/Gewicht betont.
    const kpis = [
        { label: 'Entwürfe', value: String(drafts.length), sub: 'in Bearbeitung', href: '/Billing/Documents' },
        { label: 'Offene Forderungen', value: floqFmt.money(openInvoices.reduce((s, r) => s + r.remaining, 0)), sub: `${openInvoices.length} Rechnungen`, href: '/Billing/Documents?type=2' },
        { label: 'Überfällig', value: String(overdue.length), sub: floqFmt.money(overdue.reduce((s, r) => s + r.remaining, 0)), over: overdue.length > 0, href: '/Billing/Documents?type=2' },
        { label: `Umsatz ${year}`, value: floqFmt.money(yearRevenue), sub: 'abgeschlossene Rechnungen' },
    ];

    hFill(document.getElementById('kpiGrid'), kpis.map(k =>
        h(k.href ? 'a' : 'div', { class: 'kpi' + (k.over ? ' is-over' : ''), href: k.href }, [
            h('div', { class: 'kpi-label' }, k.label),
            h('div', { class: 'kpi-value' }, k.value),
            h('div', { class: 'kpi-sub' }, k.sub),
        ])));

    const typeCode = t => ({ 1: 'AN', 2: 'RE', 3: 'GS', 4: 'SR', 5: 'MA' }[t] || '–');
    const customerPart = r => (r.customerName && r.customerName !== '–' ? r.customerName + ' · ' : '');
    const open = r => window.location.href = `/Billing/Document?id=${r.id}`;

    // Überfällige: Nummer + „Kunde · fällig …" + offener Betrag.
    const overdueRow = r =>
        h('div', { class: 'ent-row compact', onclick: () => open(r) }, [
            h('span', { class: 'ent-code' }, typeCode(r.type)),
            h('span', { class: 'num', style: 'font-size:14px' }, r.number || 'Entwurf'),
            h('span', { class: 'ent-inline' }, `${customerPart(r)}fällig ${floqFmt.date(r.dueDateVienna)}`),
            h('span', { class: 'spacer' }),
            h('span', { class: 'ent-amount num', style: 'font-weight:500' }, floqFmt.money(r.remaining)),
        ]);

    // Zuletzt bearbeitet: Nummer/Meta + Status-Zeichen (ohne Label).
    const recentRow = r =>
        h('div', { class: 'ent-row', onclick: () => open(r) }, [
            h('span', { class: 'ent-code' }, typeCode(r.type)),
            h('div', { class: 'ent-main' }, [
                h('div', { class: 'ent-number' }, r.number || 'Entwurf'),
                h('div', { class: 'ent-meta' }, `${customerPart(r)}${floqFmt.date(r.dateVienna)}`),
            ]),
            h('span', { class: 'spacer' }),
            floqStatusEl(r.status, false),
        ]);

    const overdueSorted = [...overdue].sort((a, b) => new Date(a.dueDateVienna) - new Date(b.dueDateVienna)).slice(0, 8);
    hFill(document.getElementById('overdueList'), overdueSorted.length
        ? overdueSorted.map(overdueRow)
        : h('div', { class: 'ent-empty' }, '— sonst nichts überfällig, sehr fesch.'));

    const recent = rows.slice(0, 8);
    hFill(document.getElementById('recentList'), recent.length
        ? recent.map(recentRow)
        : h('div', { class: 'ent-empty' }, 'Noch keine Belege. Leg mit „Neue Rechnung" los.'));
})();
