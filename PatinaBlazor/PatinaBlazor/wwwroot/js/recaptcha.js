// reCAPTCHA v3 integration - follows this app's existing JS-interop convention (see
// wwwroot/js/imageCompression.js, wwwroot/js/paypalCardFields.js): a single window.xxx
// object with plain functions, invoked via JSRuntime.InvokeAsync.
//
// v3 is score-based and invisible (no click-a-challenge widget, unlike v2) - the page just
// calls execute() right before an existing form submits, and gets back an opaque token
// string to send to the server for real verification (see Services/RecaptchaService.cs;
// the client-side score is never trusted on its own).
window.recaptchaHelper = {
    _loadedSiteKey: null,
    _loadPromise: null,

    // Loads Google's reCAPTCHA script for the given site key (once per site key - this app
    // only ever uses one at a time, but guards against a redundant reload the same way
    // paypalCardFields.js's _ensureSdkLoaded does) and waits for grecaptcha.ready().
    _ensureLoaded: function (siteKey) {
        if (this._loadedSiteKey === siteKey && window.grecaptcha) {
            return Promise.resolve();
        }
        if (this._loadPromise) {
            return this._loadPromise;
        }

        const self = this;
        this._loadPromise = new Promise((resolve, reject) => {
            const script = document.createElement("script");
            script.src = "https://www.google.com/recaptcha/api.js?render=" + encodeURIComponent(siteKey);
            script.async = true;
            script.onload = () => {
                window.grecaptcha.ready(() => {
                    self._loadedSiteKey = siteKey;
                    self._loadPromise = null;
                    resolve();
                });
            };
            script.onerror = () => {
                self._loadPromise = null;
                reject(new Error("Failed to load reCAPTCHA."));
            };
            document.head.appendChild(script);
        });
        return this._loadPromise;
    },

    // Loads the script if needed, then runs a fresh invisible check for this specific form
    // submission and returns the resulting token - a real string to hand to
    // IRecaptchaService.VerifyAsync, or null if anything went wrong (network failure loading
    // the script, etc.) so the caller can decide how to handle that rather than this throwing
    // past JSRuntime and surfacing as an unhandled exception.
    execute: async function (siteKey, action) {
        try {
            await this._ensureLoaded(siteKey);
            return await window.grecaptcha.execute(siteKey, { action: action });
        } catch (error) {
            console.error("reCAPTCHA execute() failed", error);
            return null;
        }
    }
};
