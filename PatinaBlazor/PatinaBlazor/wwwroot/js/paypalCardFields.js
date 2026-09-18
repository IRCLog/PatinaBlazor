// PayPal Card Fields (v6 Web SDK) integration - lets a customer save a card directly, no
// PayPal account required. Card number/expiry/CVV are collected inside PayPal-hosted
// iframes (never touch this app's server), and the SDK exchanges them for a reusable vault
// token our own .NET side already knows how to finalize/charge via IStoragePaymentService.
//
// Follows this app's existing JS-interop convention (see wwwroot/js/imageCompression.js):
// a single window.xxx object with plain functions, invoked via JSRuntime.InvokeAsync, with
// a DotNetObjectReference passed in for JS -> .NET callbacks.
//
// Every failure callback (OnCardFieldsError/OnCardSubmitFailed) takes TWO strings, not one:
// a short, human-readable message for the customer, and the full raw error detail for
// server-side logging - PayPal's real GraphQL card-verification errors are talked to
// directly from the browser (see submit() below), so without this the server would never
// find out a card failed at all, let alone why. Real example diagnosed this way: a
// "declined" card during testing turned out to carry the actual explanation nested three
// levels deep (errors[0].data.details, itself a JSON-encoded string) rather than in the
// top-level message PayPal's SDK surfaces by default - describeError() below digs for it.
window.paypalCardFields = {
    _sdkInstance: null,
    _cardSession: null,
    _dotNetRef: null,
    _loadedScriptUrl: null,

    // Loads the SDK script (once per script URL - sandbox vs live use different hosts, and
    // v5/v6 must never coexist on one page), initializes a Card Fields save-payment session,
    // and mounts the three field components into their container elements. Calls back into
    // .NET via OnCardFieldsReady() or OnCardFieldsIneligible()/OnCardFieldsError(message, raw).
    init: async function (clientToken, sdkScriptUrl, dotNetRef) {
        this._dotNetRef = dotNetRef;

        try {
            await this._ensureSdkLoaded(sdkScriptUrl);

            this._sdkInstance = await window.paypal.createInstance({
                clientToken: clientToken,
                components: ["card-fields"]
            });

            const methods = await this._sdkInstance.findEligibleMethods();
            if (!methods.isEligible("advanced_cards")) {
                await dotNetRef.invokeMethodAsync("OnCardFieldsIneligible");
                return;
            }

            this._cardSession = this._sdkInstance.createCardFieldsSavePaymentSession();

            const numberField = this._cardSession.createCardFieldsComponent({ type: "number", placeholder: "Card Number" });
            const expiryField = this._cardSession.createCardFieldsComponent({ type: "expiry", placeholder: "MM/YY" });
            const cvvField = this._cardSession.createCardFieldsComponent({ type: "cvv", placeholder: "CVV" });

            document.querySelector("#paypal-card-number").appendChild(numberField);
            document.querySelector("#paypal-card-expiry").appendChild(expiryField);
            document.querySelector("#paypal-card-cvv").appendChild(cvvField);

            await dotNetRef.invokeMethodAsync("OnCardFieldsReady");
        } catch (error) {
            console.error("PayPal Card Fields init failed", error);
            const { friendly, raw } = this._describeError(error);
            await dotNetRef.invokeMethodAsync("OnCardFieldsError", friendly || "Failed to load card entry. Please try again.", raw);
        }
    },

    // Submits the entered card against the given setup token id. Calls back into .NET via
    // OnCardSubmitSucceeded(), OnCardSubmitCanceled(), or OnCardSubmitFailed(message, raw).
    submit: async function (setupTokenId) {
        if (!this._cardSession) {
            await this._dotNetRef.invokeMethodAsync("OnCardSubmitFailed", "Card entry was not ready. Please try again.", "Card entry was not ready (no active session).");
            return;
        }

        try {
            const { data, state } = await this._cardSession.submit(setupTokenId);

            switch (state) {
                case "succeeded":
                    await this._dotNetRef.invokeMethodAsync("OnCardSubmitSucceeded");
                    break;
                case "canceled":
                    await this._dotNetRef.invokeMethodAsync("OnCardSubmitCanceled");
                    break;
                default: {
                    // submit() resolves (rather than throws) for card-declined/validation-
                    // type outcomes - the real explanation lives in `data`, in the same
                    // nested shape describeError() already knows how to dig through.
                    const { friendly, raw } = this._describeError(data);
                    await this._dotNetRef.invokeMethodAsync("OnCardSubmitFailed", friendly || "We couldn't save your card. Please check your details and try again.", raw);
                    break;
                }
            }
        } catch (error) {
            console.error("PayPal Card Fields submit failed", error);
            const { friendly, raw } = this._describeError(error);
            await this._dotNetRef.invokeMethodAsync("OnCardSubmitFailed", friendly || "We couldn't save your card. Please check your details and try again.", raw);
        }
    },

    // Drops references so a later init() on the same page (e.g. switching payment methods
    // back and forth) starts clean - does not remove the loaded SDK script itself, since v6
    // explicitly does not support being loaded twice on one page.
    teardown: function () {
        this._cardSession = null;
        this._sdkInstance = null;
        this._dotNetRef = null;
    },

    _ensureSdkLoaded: function (sdkScriptUrl) {
        if (this._loadedScriptUrl === sdkScriptUrl && window.paypal) {
            return Promise.resolve();
        }

        return new Promise((resolve, reject) => {
            const script = document.createElement("script");
            script.src = sdkScriptUrl;
            script.async = true;
            script.onload = () => {
                this._loadedScriptUrl = sdkScriptUrl;
                resolve();
            };
            script.onerror = () => reject(new Error("Failed to load the PayPal SDK script."));
            document.head.appendChild(script);
        });
    },

    // Extracts the best available explanation from a PayPal error/data object, which can
    // take several different real shapes depending on where the failure happened:
    //   - a thrown DevError (init() failures, network-level submit() failures)
    //   - the `data` half of a resolved {data, state:"failed"} pair (validation/decline
    //     outcomes, e.g. errors[0].data.details - itself a JSON-*encoded string*, not a
    //     real nested object, containing the actual field-level description)
    // Returns { friendly, raw }: `friendly` is the best short human-readable line found (or
    // null if nothing usable was found - the caller supplies its own generic fallback text),
    // `raw` is a full JSON dump of the source for server-side logging, captured via
    // Object.getOwnPropertyNames so a real Error's non-enumerable `message`/`stack` aren't
    // silently dropped the way a plain JSON.stringify(error) would drop them.
    _describeError: function (source) {
        if (!source) {
            return { friendly: null, raw: null };
        }

        let raw;
        try {
            raw = JSON.stringify(source, Object.getOwnPropertyNames(source));
        } catch (e) {
            raw = String(source);
        }

        let friendly = null;
        try {
            const detailsSource = (source.details) || (source.data && source.data.details);
            if (typeof detailsSource === "string") {
                const parsed = JSON.parse(detailsSource);
                if (Array.isArray(parsed) && parsed.length > 0) {
                    friendly = parsed
                        .map(d => d && d.description)
                        .filter(Boolean)
                        .join(" ");
                }
            }
        } catch (e) {
            // Not JSON, or not the expected shape - fall through to the plainer fields below.
        }

        if (!friendly) {
            friendly = (source.data && source.data.message) || source.message || null;
        }

        // Some PayPal SDK error paths set `.message` to a JSON-encoded dump of the error
        // itself rather than real prose - confirmed for real via the app's own Logs table:
        // a DevError whose own .message was literally
        // '{"name":"DevError","code":"ERR_DEV_RECEIVED_GRAPHQL_ERROR","isRecoverable":false}'
        // with no `.details` to fall back to. Showing that to a customer would be exactly
        // the unhelpful raw-JSON text this whole change exists to avoid - treat it as no
        // usable message at all, so the caller's own generic fallback text is shown instead
        // (the full raw dump is still captured above regardless, for server-side logging).
        if (friendly && /^[\[{]/.test(friendly.trim())) {
            friendly = null;
        }

        return { friendly: friendly || null, raw };
    }
};
