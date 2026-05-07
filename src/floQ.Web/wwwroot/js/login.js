(function () {
    'use strict';
    const form = document.getElementById('login-form');
    const status = document.getElementById('status');
    const wa = window.floqWebAuthn;

    if (!window.PublicKeyCredential) {
        status.textContent = 'Dein Browser unterstützt keine Passkeys.';
        status.className = 'status error';
        form.querySelector('button').disabled = true;
        return;
    }

    form.addEventListener('submit', async (e) => {
        e.preventDefault();
        status.textContent = '';
        status.className = 'status';

        const email = form.email.value.trim();

        try {
            const begin = await wa.postJson('/auth/login?handler=Begin', { email });
            if (!begin.success) throw new Error(begin.errorMessage);

            const opts = wa.decodeRequestOptions(begin.data);
            const assertion = await navigator.credentials.get({ publicKey: opts });

            const complete = await wa.postJson('/auth/login?handler=Complete', {
                assertion: wa.encodeAssertion(assertion)
            });
            if (!complete.success) throw new Error(complete.errorMessage);

            window.location.href = complete.data.redirect || '/';
        } catch (err) {
            status.textContent = 'Fehler: ' + (err.message || err);
            status.className = 'status error';
        }
    });
})();
