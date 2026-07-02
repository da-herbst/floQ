/* Firmenprofil — Stammdaten + Briefpapier-Upload über /api/v1/company-profile. */
(() => {
    const $ = id => document.getElementById(id);
    const val = id => $(id).value.trim() || null;

    const FIELDS = ['LegalName', 'Street', 'ZipCode', 'City', 'CountryCode', 'VatId',
        'Email', 'Phone', 'Website', 'Iban', 'Bic', 'BankName', 'TaxExemptionText'];

    async function load() {
        const p = await floqApi.get('/api/v1/company-profile/');
        for (const f of FIELDS) $('f' + f).value = p[f.charAt(0).toLowerCase() + f.slice(1)] || '';
        $('fIsSmallBusiness').checked = p.isSmallBusiness;
        renderLetterhead(p.hasLetterhead);
    }

    function renderLetterhead(has) {
        $('letterheadStatus').textContent = has ? '● HINTERLEGT' : '○ KEINES';
        $('btnDeleteLetterhead').hidden = !has;
    }

    $('btnSaveProfile').addEventListener('click', async () => {
        try {
            await floqApi.put('/api/v1/company-profile/', {
                legalName: val('fLegalName') || '',
                street: val('fStreet') || '',
                zipCode: val('fZipCode') || '',
                city: val('fCity') || '',
                countryCode: val('fCountryCode'),
                vatId: val('fVatId'),
                email: val('fEmail'),
                phone: val('fPhone'),
                website: val('fWebsite'),
                iban: val('fIban'),
                bic: val('fBic'),
                bankName: val('fBankName'),
                isSmallBusiness: $('fIsSmallBusiness').checked,
                taxExemptionText: val('fTaxExemptionText'),
            });
            floqToast('Firmenprofil gespeichert.');
        } catch (e) { floqToast(e.message, true); }
    });

    $('btnUploadLetterhead').addEventListener('click', () => $('letterheadFile').click());
    $('letterheadFile').addEventListener('change', async () => {
        const file = $('letterheadFile').files[0];
        if (!file) return;
        const form = new FormData();
        form.append('file', file);
        try {
            const resp = await fetch('/api/v1/company-profile/letterhead', { method: 'POST', body: form });
            const envelope = await resp.json();
            if (!envelope.success) throw new Error(envelope.errorMessage || 'Upload fehlgeschlagen.');
            floqToast('Briefpapier hochgeladen.');
            renderLetterhead(true);
        } catch (e) { floqToast(e.message, true); }
        $('letterheadFile').value = '';
    });

    $('btnDeleteLetterhead').addEventListener('click', async () => {
        if (!await floqConfirm({
            eyebrow: 'Briefpapier', title: 'Briefpapier entfernen?', confirm: 'Entfernen',
            text: 'Belege werden künftig ohne Hintergrund gerendert.',
        })) return;
        try {
            await floqApi.del('/api/v1/company-profile/letterhead');
            renderLetterhead(false);
        } catch (e) { floqToast(e.message, true); }
    });

    load().catch(e => floqToast(e.message, true));
})();
