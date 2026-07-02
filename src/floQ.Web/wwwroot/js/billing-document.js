/* Beleg-Workbench — Editor, PDF-Vorschau, Lebenszyklus, Zahlungen.
   Konzept nach batOS-V2-Billing-Workbench, floQ-schlank: Freitext-Positionen,
   direkter Empfänger (autarker Beleg), alle Flows über /api/v1 (API-First).

   Aufruf: ?id={belegId} (bestehender Beleg) oder ?new={typ} (Gutschrift/
   Storno/Mahnung: zuerst Originalrechnung wählen, dann Folgebeleg erzeugen). */
(() => {
    const TYPE_LABEL = { 1: 'Angebot', 2: 'Rechnung', 3: 'Gutschrift', 4: 'Stornorechnung', 5: 'Mahnung' };
    const STATUS_LABEL = { 0: 'Entwurf', 1: 'Abgeschlossen', 2: 'Versendet', 3: 'Gesehen', 4: 'Storniert' };
    const STATUS_TONE = { 0: 'warn', 1: 'ok', 2: 'info', 3: 'neutral', 4: 'danger' };

    const params = new URLSearchParams(window.location.search);
    let docId = Number(params.get('id')) || 0;
    const newType = Number(params.get('new')) || 0;

    let detail = null;     // Kopf/Status (immer geladen)
    let draft = null;      // Editierzustand (nur solange Entwurf)
    let entries = [];      // Positionen als flaches Array (Voll-Array-Reihenfolge)

    const $ = id => document.getElementById(id);
    const isDraft = () => detail && detail.status === 0;
    const val = id => $(id).value.trim() || null;
    const num = id => { const v = $(id).value; return v === '' ? null : Number(v); };

    // ── Tabs ────────────────────────────────────────────────────────────
    document.querySelectorAll('[data-tab]').forEach(tab => tab.addEventListener('click', async () => {
        if (tab.dataset.tab === 'vorschau' && isDraft()) {
            // Vorschau zeigt den gespeicherten Stand — vorher speichern.
            const ok = await save(true);
            if (!ok) return;
        }
        document.querySelectorAll('[data-tab]').forEach(t => t.classList.toggle('is-active', t === tab));
        document.querySelectorAll('[data-tabpanel]').forEach(p => p.hidden = p.dataset.tabpanel !== tab.dataset.tab);
        if (tab.dataset.tab === 'vorschau') refreshPreview();
    }));

    function switchTab(key) {
        const tab = document.querySelector(`[data-tab="${key}"]`);
        if (tab) tab.click();
    }

    // ── Modals (generisch) ──────────────────────────────────────────────
    document.querySelectorAll('.modal-scrim').forEach(scrim => {
        scrim.addEventListener('click', e => { if (e.target === scrim) scrim.hidden = true; });
        scrim.querySelectorAll('[data-modal-close]').forEach(b => b.addEventListener('click', () => { scrim.hidden = true; }));
    });

    // ── Positionen-Editor ───────────────────────────────────────────────
    function entryInput(row, key, opts = {}) {
        const input = h('input', {
            value: row[key] ?? '',
            type: opts.type || 'text',
            step: opts.step, min: opts.min, placeholder: opts.placeholder,
            class: opts.num ? 'num' : null,
        });
        input.addEventListener('input', () => {
            row[key] = opts.type === 'number' ? (input.value === '' ? 0 : Number(input.value)) : input.value;
            renderTotals();
            const netCell = input.closest('tr')?.querySelector('[data-net]');
            if (netCell) netCell.textContent = floqFmt.money(row.quantity * row.unitPrice);
        });
        return input;
    }

    function renderEntries() {
        const body = $('entriesBody');
        const rows = entries.map((row, i) => {
            const tr = h('tr', { class: row.isDiscount ? 'row-discount' : '' }, [
                h('td', {}, entryInput(row, 'description', { placeholder: row.isDiscount ? 'Rabatt-Bezeichnung' : 'Leistung / Beschreibung' })),
                h('td', {}, entryInput(row, 'quantity', { type: 'number', step: '0.01', num: true })),
                h('td', {}, entryInput(row, 'unit', { placeholder: 'Std.' })),
                h('td', {}, entryInput(row, 'unitPrice', { type: 'number', step: '0.01', num: true })),
                h('td', {}, entryInput(row, 'vatRate', { type: 'number', step: '0.5', min: '0', num: true })),
                h('td', { class: 'num', 'data-net': '1', style: 'padding-top:13px' }, floqFmt.money(row.quantity * row.unitPrice)),
                h('td', {}, h('div', { class: 'hstack', style: 'gap:2px' }, [
                    !row.isDiscount ? h('button', {
                        class: 'entry-del', type: 'button', title: 'Rabattzeile hinzufügen',
                        onclick: () => {
                            entries.splice(i + 1, 0, { description: 'Rabatt', quantity: 1, unit: 'pauschal', unitPrice: 0, vatRate: row.vatRate, isDiscount: true });
                            renderEntries();
                        },
                    }, '%') : null,
                    h('button', {
                        class: 'entry-del', type: 'button', title: 'Zeile entfernen',
                        onclick: () => {
                            // Hauptposition löschen entfernt auch ihre Rabattzeilen.
                            let end = i + 1;
                            if (!row.isDiscount) while (end < entries.length && entries[end].isDiscount) end++;
                            entries.splice(i, end - i);
                            renderEntries();
                        },
                    }, '×'),
                ])),
            ]);
            return tr;
        });
        hFill(body, rows);
        renderTotals();
    }

    function renderTotals() {
        const rcMode = Number($('fReverseChargeMode').value);
        const net = entries.reduce((s, e) => s + e.quantity * e.unitPrice, 0);
        const rows = [h('div', { class: 'totals-row' }, [h('span', {}, 'Gesamtbetrag netto'), h('span', { class: 'num' }, floqFmt.money(net))])];
        let vatTotal = 0;
        if (rcMode === 0) {
            const groups = {};
            entries.forEach(e => { if (e.vatRate > 0) groups[e.vatRate] = (groups[e.vatRate] || 0) + e.quantity * e.unitPrice * e.vatRate / 100; });
            Object.keys(groups).sort((a, b) => b - a).forEach(rate => {
                vatTotal += groups[rate];
                rows.push(h('div', { class: 'totals-row' }, [h('span', {}, `zzgl. Umsatzsteuer ${rate} %`), h('span', { class: 'num' }, floqFmt.money(groups[rate]))]));
            });
        } else {
            rows.push(h('div', { class: 'totals-row' }, [h('span', {}, 'zzgl. Umsatzsteuer 0 %'), h('span', { class: 'num' }, floqFmt.money(0))]));
        }
        rows.push(h('div', { class: 'totals-row gross' }, [h('span', {}, 'Gesamtbetrag brutto'), h('span', { class: 'num' }, floqFmt.money(net + vatTotal))]));
        hFill($('totalsBox'), rows);
    }

    $('btnAddEntry').addEventListener('click', () => {
        entries.push({ description: '', quantity: 1, unit: 'Std.', unitPrice: 0, vatRate: 20, isDiscount: false });
        renderEntries();
    });
    $('fReverseChargeMode').addEventListener('change', renderTotals);

    // ── Laden ───────────────────────────────────────────────────────────
    async function load() {
        detail = await floqApi.get(`/api/v1/documents/${docId}`);
        $('docTypeName').textContent = detail.typeName;
        $('docNumber').textContent = detail.number || '';
        $('asideNumber').textContent = detail.number || '–';
        $('asideGross').textContent = floqFmt.money(detail.gross);
        hFill($('asideStatus'), h('span', { class: `badge badge-${STATUS_TONE[detail.status]}` }, [h('span', { class: 'dot' }), STATUS_LABEL[detail.status]]));

        // Typ-abhängige Felder ein-/ausblenden (data-only="1,2").
        document.querySelectorAll('[data-only]').forEach(el => {
            el.hidden = !el.dataset.only.split(',').map(Number).includes(detail.type);
        });
        $('tabRechnungen').hidden = detail.type !== 5;
        $('tabPositionen').hidden = detail.type === 5;

        if (isDraft()) {
            draft = await floqApi.get(`/api/v1/documents/${docId}/draft`);
            fillForm();
            try {
                const peek = await floqApi.get(`/api/v1/documents/peek-number/${detail.type}`);
                $('docSub').textContent = `Entwurf — Nummer beim Abschluss: ${peek.number}`;
            } catch { $('docSub').textContent = 'Entwurf'; }
        } else {
            $('docSub').textContent = `${detail.typeName} vom ${floqFmt.date(detail.dateVienna)}`;
            if (draft === null) {
                // Abgeschlossen: Formulare sperren, Vorschau als Start-Tab.
                document.querySelectorAll('input, select, textarea').forEach(el => {
                    if (!el.closest('.modal')) el.disabled = true;
                });
                $('btnAddEntry').disabled = true;
            }
        }

        // Aktionen je Zustand
        $('btnSave').hidden = !isDraft();
        $('btnFinalize').hidden = !isDraft();
        $('btnDelete').hidden = !isDraft();
        $('btnUnlock').hidden = isDraft();
        $('btnProcess').hidden = isDraft();
        $('btnSend').hidden = isDraft() || detail.status === 4;
        $('btnDownload').hidden = isDraft();
        $('btnDownload').href = `/api/v1/documents/${docId}/pdf?download=true`;

        // Zahlungen: nur abgeschlossene Rechnungen.
        const showPayments = detail.type === 2 && !isDraft() && detail.status !== 4;
        $('paymentsCard').hidden = !showPayments;
        if (showPayments) await loadPayments();

        // Versand-Historie: alle abgeschlossenen Belege.
        $('distributionsCard').hidden = isDraft();
        if (!isDraft()) await loadDistributions();

        if (!isDraft()) switchTab('vorschau');
        if (isDraft() && detail.type === 5) await loadReminderInvoices();
    }

    function fillForm() {
        $('fRecipientName').value = draft.recipientName || '';
        $('fRecipientAddress').value = draft.recipientAddress || '';
        $('fRecipientZip').value = draft.recipientZip || '';
        $('fRecipientCity').value = draft.recipientCity || '';
        $('fRecipientCountry').value = draft.recipientCountry || '';
        $('fRecipientUid').value = draft.recipientUid || '';
        $('fRecipientEmail').value = draft.recipientEmail || '';

        $('fDate').value = floqFmt.dateInput(draft.documentDateVienna);
        $('fServiceDate').value = floqFmt.dateInput(draft.serviceDateVienna);
        $('fPeriodStart').value = floqFmt.dateInput(draft.servicePeriodStartVienna);
        $('fPeriodEnd').value = floqFmt.dateInput(draft.servicePeriodEndVienna);
        $('fValidUntil').value = floqFmt.dateInput(draft.validUntilVienna);
        $('fExternalReference').value = draft.externalReference || '';
        $('fPaymentTermDays').value = draft.paymentTermDays ?? '';
        $('fPaymentTermDiscountDays').value = draft.paymentTermDiscountDays ?? '';
        $('fDiscountRate').value = draft.discountRate ?? '';
        $('fReverseChargeMode').value = String(draft.reverseChargeMode);
        $('fReverseChargeNote').value = draft.reverseChargeNote || '';
        $('fConditionNotes').value = draft.conditionNotes || '';
        $('fNote').value = draft.note || '';

        $('fReminderLevel').value = String(draft.reminderLevel);
        $('fReminderDueDate').value = floqFmt.dateInput(draft.reminderDueDateVienna);
        $('fReminderFee').value = draft.reminderFee ?? 0;
        $('fInterestAmount').value = draft.interestAmount ?? '';

        entries = draft.entries.map(e => ({
            description: e.description, quantity: e.quantity, unit: e.unit,
            unitPrice: e.unitPrice, vatRate: e.vatRate, isDiscount: e.parentEntryIndex !== null,
        }));
        renderEntries();
    }

    function buildDraftPayload() {
        // ParentEntryIndex = Voll-Array-Index der letzten Hauptposition davor.
        let lastMain = -1;
        const entryDtos = entries.map((e, i) => {
            if (!e.isDiscount) lastMain = i;
            return {
                description: e.description, quantity: e.quantity, unitPrice: e.unitPrice,
                vatRate: e.vatRate, unit: e.unit,
                parentEntryIndex: e.isDiscount ? lastMain : null,
                discountPercent: null,
            };
        });

        return {
            id: docId,
            type: detail.type,
            documentDateVienna: $('fDate').value || new Date().toISOString().substring(0, 10),
            customerId: draft.customerId,
            note: val('fNote'),
            paymentTermDays: num('fPaymentTermDays'),
            paymentTermDiscountDays: num('fPaymentTermDiscountDays'),
            discountRate: num('fDiscountRate'),
            reverseChargeMode: Number($('fReverseChargeMode').value),
            reverseChargeNote: val('fReverseChargeNote'),
            recipientName: val('fRecipientName'),
            recipientAddress: val('fRecipientAddress'),
            recipientZip: val('fRecipientZip'),
            recipientCity: val('fRecipientCity'),
            recipientCountry: val('fRecipientCountry'),
            recipientUid: val('fRecipientUid'),
            recipientEmail: val('fRecipientEmail'),
            serviceDateVienna: $('fServiceDate').value || null,
            servicePeriodStartVienna: $('fPeriodStart').value || null,
            servicePeriodEndVienna: $('fPeriodEnd').value || null,
            validUntilVienna: $('fValidUntil').value || null,
            externalReference: val('fExternalReference'),
            conditionNotes: val('fConditionNotes'),
            originalInvoiceId: draft.originalInvoiceId,
            grossOverride: null,
            reminderLevel: Number($('fReminderLevel').value),
            reminderDueDateVienna: $('fReminderDueDate').value || null,
            reminderFee: num('fReminderFee') ?? 0,
            interestRate: draft.interestRate,
            interestAmount: num('fInterestAmount'),
            reminderInvoices: draft.reminderInvoices,
            entries: entryDtos,
        };
    }

    async function save(silent = false) {
        try {
            await floqApi.put(`/api/v1/documents/${docId}/draft`, buildDraftPayload());
            if (!silent) floqToast('Gespeichert.');
            detail = await floqApi.get(`/api/v1/documents/${docId}`);
            $('asideGross').textContent = floqFmt.money(detail.gross);
            return true;
        } catch (e) {
            floqToast(e.message, true);
            return false;
        }
    }

    // ── Vorschau ────────────────────────────────────────────────────────
    function refreshPreview() {
        $('previewLoading').style.display = 'flex';
        const frame = $('previewFrame');
        frame.onload = () => { $('previewLoading').style.display = 'none'; };
        frame.src = `/api/v1/documents/${docId}/pdf?ts=${Date.now()}#toolbar=0`;
    }

    // ── Lebenszyklus ────────────────────────────────────────────────────
    $('btnSave').addEventListener('click', () => save());

    $('btnFinalize').addEventListener('click', async () => {
        if (!await save(true)) return;
        if (!confirm('Beleg abschließen? Die Belegnummer wird gezogen; danach ist der Beleg nicht mehr frei editierbar.')) return;
        try {
            await floqApi.post(`/api/v1/documents/${docId}/finalize`);
            floqToast('Beleg abgeschlossen.');
            window.location.reload();
        } catch (e) { floqToast(e.message, true); }
    });

    $('btnUnlock').addEventListener('click', async () => {
        if (!confirm('Beleg entsperren? Das persistierte PDF wird verworfen; der Beleg wird wieder zum Entwurf.')) return;
        try {
            await floqApi.post(`/api/v1/documents/${docId}/unlock`);
            window.location.reload();
        } catch (e) { floqToast(e.message, true); }
    });

    $('btnDelete').addEventListener('click', async () => {
        if (!confirm('Entwurf unwiderruflich verwerfen?')) return;
        try {
            await floqApi.del(`/api/v1/documents/${docId}`);
            window.location.href = '/Billing/Documents';
        } catch (e) { floqToast(e.message, true); }
    });

    // Weiterverarbeiten (abgeschlossene Belege → Folgebeleg).
    $('btnProcess').addEventListener('click', () => { $('processModal').hidden = false; });
    document.querySelectorAll('[data-process-target]').forEach(btn => btn.addEventListener('click', async () => {
        try {
            const { id } = await floqApi.post(`/api/v1/documents/${docId}/process`,
                { targetType: Number(btn.dataset.processTarget), reminderLevel: 0 });
            window.location.href = `/Billing/Document?id=${id}`;
        } catch (e) { floqToast(e.message, true); }
    }));

    // ── Versand ─────────────────────────────────────────────────────────
    $('btnSend').addEventListener('click', () => {
        $('sRecipient').value = detail.recipientEmail || '';
        $('sendModal').hidden = false;
    });

    $('btnConfirmSend').addEventListener('click', async () => {
        const btn = $('btnConfirmSend');
        btn.disabled = true;
        btn.textContent = 'Sendet …';
        try {
            await floqApi.post(`/api/v1/documents/${docId}/send`, {
                recipientEmail: $('sRecipient').value.trim(),
                message: $('sMessage').value.trim() || null,
                attachPdf: $('sAttachPdf').checked,
                sendCopyToSelf: $('sCopyToSelf').checked,
            });
            $('sendModal').hidden = true;
            floqToast('Beleg versendet.');
            detail = await floqApi.get(`/api/v1/documents/${docId}`);
            hFill($('asideStatus'), h('span', { class: `badge badge-${STATUS_TONE[detail.status]}` }, [h('span', { class: 'dot' }), STATUS_LABEL[detail.status]]));
            await loadDistributions();
        } catch (e) {
            floqToast(e.message, true);
        } finally {
            btn.disabled = false;
            btn.textContent = 'Senden';
        }
    });

    async function loadDistributions() {
        const dists = await floqApi.get(`/api/v1/documents/${docId}/distributions`);
        const rows = dists.map(d => {
            const trail = [];
            if (d.sentAtVienna) trail.push(`gesendet ${floqFmt.date(d.sentAtVienna)}`);
            trail.push(d.attachPdf ? 'PDF-Anhang' : 'Link');
            if (d.openCount > 0) trail.push(`geöffnet ×${d.openCount}`);
            if (d.downloadCount > 0) trail.push(`geladen ×${d.downloadCount}`);
            return h('div', { style: 'padding:4px 0' }, [
                h('div', { class: 't-sm', style: 'font-weight:600' }, d.recipientEmail),
                h('div', { class: 't-xs t-muted' }, trail.join(' · ')),
            ]);
        });
        hFill($('distributionsList'), rows.length ? rows : h('div', { class: 't-xs t-muted' }, 'Noch nicht versendet.'));
    }

    // ── Zahlungen ───────────────────────────────────────────────────────
    async function loadPayments() {
        const payments = await floqApi.get(`/api/v1/documents/${docId}/payments`);
        const rows = payments.map(p =>
            h('div', { class: 'meta-row' }, [
                h('span', { class: 'meta-label' }, `${floqFmt.date(p.paidDateVienna)} · ${p.methodLabel}`),
                h('span', { class: 'meta-value num hstack', style: 'gap:6px;justify-content:flex-end' }, [
                    floqFmt.money(p.amount),
                    h('button', {
                        class: 'entry-del', type: 'button', title: 'Zahlung löschen', style: 'padding:0 2px',
                        onclick: async () => {
                            if (!confirm('Zahlung löschen?')) return;
                            try { await floqApi.del(`/api/v1/payments/${p.id}`); await loadPayments(); }
                            catch (e) { floqToast(e.message, true); }
                        },
                    }, '×'),
                ]),
            ]));
        hFill($('paymentsList'), rows.length ? rows : h('div', { class: 't-xs t-muted' }, 'Noch keine Zahlungen.'));
    }

    $('btnAddPayment').addEventListener('click', () => {
        $('pDate').value = new Date().toISOString().substring(0, 10);
        $('paymentModal').hidden = false;
    });

    $('btnSavePayment').addEventListener('click', async () => {
        try {
            await floqApi.post(`/api/v1/documents/${docId}/payments`, {
                amount: Number($('pAmount').value),
                paidDate: $('pDate').value,
                method: Number($('pMethod').value),
                reference: val('pReference'),
                note: val('pNote'),
            });
            $('paymentModal').hidden = true;
            $('pAmount').value = ''; $('pReference').value = ''; $('pNote').value = '';
            floqToast('Zahlung erfasst.');
            await loadPayments();
        } catch (e) { floqToast(e.message, true); }
    });

    // ── Mahnung: gemahnte Rechnungen (read-only Liste) ──────────────────
    async function loadReminderInvoices() {
        const list = draft.reminderInvoices.map(ri =>
            h('div', { class: 'meta-row' }, [
                h('span', { class: 'meta-label' }, `Rechnung #${ri.invoiceId}`),
                h('span', { class: 'meta-value num' }, floqFmt.money(ri.outstandingAmount)),
            ]));
        hFill($('reminderInvoiceList'), list.length ? list : h('div', { class: 'ent-empty' }, 'Keine Rechnung verknüpft — Mahnungen entstehen aus einer Rechnung („Weiterverarbeiten").'));
    }

    // ── Einstieg: Folgebeleg-Flow (?new=3|4|5) ──────────────────────────
    async function startFromInvoice(targetType) {
        const eyebrow = { 3: 'Gutschrift', 4: 'Stornorechnung', 5: 'Mahnung' }[targetType];
        $('pickInvoiceEyebrow').textContent = eyebrow;
        const ctx = await floqApi.get('/api/v1/editor-context');
        const items = ctx.invoices.map(inv =>
            h('button', {
                class: 'pick-item', type: 'button',
                onclick: async () => {
                    try {
                        const { id } = await floqApi.post(`/api/v1/documents/${inv.id}/process`,
                            { targetType, reminderLevel: 0 });
                        window.location.replace(`/Billing/Document?id=${id}`);
                    } catch (e) { floqToast(e.message, true); }
                },
            }, inv.label));
        hFill($('pickInvoiceList'), items.length ? items : h('div', { class: 'ent-empty' }, 'Keine abgeschlossene Rechnung vorhanden.'));
        $('pickInvoiceModal').hidden = false;
    }

    (async () => {
        try {
            if (newType) {
                $('docTypeName').textContent = TYPE_LABEL[newType] || 'Beleg';
                $('docSub').textContent = 'Originalrechnung wählen';
                await startFromInvoice(newType);
                return;
            }
            await load();
        } catch (e) {
            floqToast(e.message, true);
        }
    })();
})();
