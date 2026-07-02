(function () {
    'use strict';
    const form = document.getElementById('register-form');
    const status = document.getElementById('status');
    const wa = window.floqWebAuthn;

    if (!window.PublicKeyCredential) {
        status.textContent = 'Dein Browser unterstützt keine Passkeys.';
        status.className = 'auth-status error';
        form.querySelector('button').disabled = true;
        return;
    }

    form.addEventListener('submit', async (e) => {
        e.preventDefault();
        status.textContent = '';
        status.className = 'auth-status';

        const email = form.email.value.trim();
        const displayName = form.displayName.value.trim();
        const credentialName = form.credentialName.value.trim();

        try {
            const begin = await wa.postJson('/auth/register?handler=Begin', { email, displayName });
            if (!begin.success) throw new Error(begin.errorMessage);

            const opts = wa.decodeCreationOptions(begin.data);
            const cred = await navigator.credentials.create({ publicKey: opts });

            const complete = await wa.postJson('/auth/register?handler=Complete', {
                attestation: wa.encodeAttestation(cred),
                credentialName
            });
            if (!complete.success) throw new Error(complete.errorMessage);

            window.location.href = complete.data.redirect || '/';
        } catch (err) {
            status.textContent = 'Fehler: ' + (err.message || err);
            status.className = 'auth-status error';
        }
    });
})();
