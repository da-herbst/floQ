// E-Mail-Einmalcode als Passkey-Fallback: Schritt 1 fordert den Code an
// (E-Mail aus dem Login-Formular), Schritt 2 meldet mit dem Code an.
(function () {
    'use strict';
    const loginForm = document.getElementById('login-form');
    const codeForm = document.getElementById('code-form');
    const toggle = document.getElementById('code-toggle');
    const status = document.getElementById('status');
    const postJson = window.floqWebAuthn.postJson;

    function setStatus(text, isError) {
        status.textContent = text;
        status.className = 'auth-status' + (isError ? ' error' : '');
    }

    toggle.addEventListener('click', async (e) => {
        e.preventDefault();

        const email = loginForm.email.value.trim();
        if (!email) {
            setStatus('Bitte zuerst die E-Mail-Adresse eingeben.', true);
            loginForm.email.focus();
            return;
        }

        setStatus('Code wird angefordert …', false);
        try {
            const res = await postJson('/auth/login?handler=CodeBegin', { email });
            if (!res.success) throw new Error(res.errorMessage);

            codeForm.hidden = false;
            codeForm.code.focus();
            setStatus(res.data.message, false);
        } catch (err) {
            setStatus('Fehler: ' + (err.message || err), true);
        }
    });

    codeForm.addEventListener('submit', async (e) => {
        e.preventDefault();

        const email = loginForm.email.value.trim();
        const code = codeForm.code.value.trim();
        if (!/^[0-9]{6}$/.test(code)) {
            setStatus('Bitte den 6-stelligen Code eingeben.', true);
            return;
        }

        try {
            const res = await postJson('/auth/login?handler=CodeComplete', { email, code });
            if (!res.success) throw new Error(res.errorMessage);

            window.location.href = res.data.redirect || '/';
        } catch (err) {
            setStatus('Fehler: ' + (err.message || err), true);
        }
    });
})();
