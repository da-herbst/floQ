/* Tenant-SMTP-Konfiguration über /api/v1/mail-settings.
   Passwort ist write-only: GET liefert nur hasPassword, PUT schreibt nur
   wenn ein neuer Wert eingetragen wurde. */
(() => {
    const $ = id => document.getElementById(id);

    async function load() {
        const s = await floqApi.get('/api/v1/mail-settings');
        $('fHost').value = s.host;
        $('fPort').value = s.port;
        $('fUserName').value = s.userName;
        $('fSender').value = s.sender;
        $('fSenderDisplayName').value = s.senderDisplayName || '';
        $('pwHint').textContent = s.hasPassword ? '(gespeichert — nur zum Ändern ausfüllen)' : '(noch keines gespeichert)';
    }

    async function save() {
        await floqApi.put('/api/v1/mail-settings', {
            host: $('fHost').value.trim(),
            port: Number($('fPort').value) || 587,
            userName: $('fUserName').value.trim(),
            sender: $('fSender').value.trim(),
            senderDisplayName: $('fSenderDisplayName').value.trim() || null,
            password: $('fPassword').value || null,
        });
        $('fPassword').value = '';
    }

    $('btnSaveMail').addEventListener('click', async () => {
        try {
            await save();
            floqToast('E-Mail-Einstellungen gespeichert.');
            await load();
        } catch (e) { floqToast(e.message, true); }
    });

    $('btnTestMail').addEventListener('click', async () => {
        try {
            await save();
            const result = await floqApi.post('/api/v1/mail-settings/test');
            floqToast(`Test-Mail an ${result.sentTo} gesendet — bitte Posteingang prüfen.`);
            await load();
        } catch (e) { floqToast(e.message, true); }
    });

    load().catch(e => floqToast(e.message, true));
})();
