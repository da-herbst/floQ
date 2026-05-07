// WebAuthn-Helfer: Encoding zwischen base64url (Wire-Format) und ArrayBuffer (Browser-API).
// Keine Library-Abhängigkeit — Fido2NetLib spricht standard-konformes JSON.

(function (window) {
    'use strict';

    function b64urlToBytes(b64url) {
        const b64 = b64url.replace(/-/g, '+').replace(/_/g, '/');
        const padded = b64 + '==='.slice((b64.length + 3) % 4);
        const bin = atob(padded);
        const bytes = new Uint8Array(bin.length);
        for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
        return bytes.buffer;
    }

    function bytesToB64url(buffer) {
        const bytes = new Uint8Array(buffer);
        let bin = '';
        for (let i = 0; i < bytes.byteLength; i++) bin += String.fromCharCode(bytes[i]);
        return btoa(bin).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    }

    // Wandelt die Server-Optionen so um, dass die WebAuthn-API sie akzeptiert
    // (challenge/user.id/excludeCredentials[].id etc. müssen ArrayBuffer sein).
    function decodeCreationOptions(opts) {
        opts.challenge = b64urlToBytes(opts.challenge);
        opts.user.id = b64urlToBytes(opts.user.id);
        if (opts.excludeCredentials) {
            opts.excludeCredentials = opts.excludeCredentials.map(c => ({
                ...c,
                id: b64urlToBytes(c.id)
            }));
        }
        return opts;
    }

    function decodeRequestOptions(opts) {
        opts.challenge = b64urlToBytes(opts.challenge);
        if (opts.allowCredentials) {
            opts.allowCredentials = opts.allowCredentials.map(c => ({
                ...c,
                id: b64urlToBytes(c.id)
            }));
        }
        return opts;
    }

    function encodeAttestation(cred) {
        return {
            id: cred.id,
            rawId: bytesToB64url(cred.rawId),
            type: cred.type,
            response: {
                attestationObject: bytesToB64url(cred.response.attestationObject),
                clientDataJSON: bytesToB64url(cred.response.clientDataJSON)
            },
            extensions: cred.getClientExtensionResults ? cred.getClientExtensionResults() : {}
        };
    }

    function encodeAssertion(cred) {
        return {
            id: cred.id,
            rawId: bytesToB64url(cred.rawId),
            type: cred.type,
            response: {
                authenticatorData: bytesToB64url(cred.response.authenticatorData),
                clientDataJSON: bytesToB64url(cred.response.clientDataJSON),
                signature: bytesToB64url(cred.response.signature),
                userHandle: cred.response.userHandle ? bytesToB64url(cred.response.userHandle) : null
            },
            extensions: cred.getClientExtensionResults ? cred.getClientExtensionResults() : {}
        };
    }

    async function postJson(url, body) {
        const res = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        return res.json();
    }

    window.floqWebAuthn = {
        decodeCreationOptions,
        decodeRequestOptions,
        encodeAttestation,
        encodeAssertion,
        postJson
    };
})(window);
