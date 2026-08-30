// Colour-mode handling for the Admin UI. Runs before first paint (referenced from the
// document <head>) so there is no light/dark flash, then exposes window.ctmsTheme for the
// in-app toggle. Preference is one of "system" | "light" | "dark"; "system" follows
// prefers-color-scheme and is represented by the ABSENCE of a localStorage entry.
(function () {
    "use strict";

    var KEY = "ctms-theme";

    function systemTheme() {
        return window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches
            ? "dark"
            : "light";
    }

    function resolve(pref) {
        return pref === "dark" || pref === "light" ? pref : systemTheme();
    }

    function readPref() {
        try {
            return window.localStorage.getItem(KEY) || "system";
        } catch (e) {
            return "system";
        }
    }

    function apply(pref) {
        document.documentElement.setAttribute("data-bs-theme", resolve(pref));
    }

    window.ctmsTheme = {
        get: readPref,
        resolved: function () { return resolve(readPref()); },
        set: function (pref) {
            try {
                if (pref === "light" || pref === "dark") {
                    window.localStorage.setItem(KEY, pref);
                } else {
                    window.localStorage.removeItem(KEY);
                }
            } catch (e) { /* private mode / storage disabled — still apply for this page */ }
            apply(pref);
            return resolve(pref);
        }
    };

    apply(readPref());

    if (window.matchMedia) {
        window.matchMedia("(prefers-color-scheme: dark)").addEventListener("change", function () {
            if (readPref() === "system") {
                apply("system");
            }
        });
    }
})();
