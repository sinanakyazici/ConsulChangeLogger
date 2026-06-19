namespace ConsulChangeLogger.Proxy.Proxying;

internal static class JsonValidationClientScript
{
    public const string Path = "/ui/_ccl/json-validation.js";

    public static string Content =>
        """
        (() => {
          const warningMessage =
            "Girilen deger JSON gibi gorunuyor ancak gecerli degil.\n\nYine de kaydetmek istiyor musunuz?";
          let redirectingToLogin = false;
          let allowNextKvPutUntil = 0;
          const uiRequestHeaderName = "X-Consul-Change-Logger-UI";
          const uiRequestHeaderValue = "true";

          function isLoginUrl(url) {
            try {
              const resolved = new URL(url, window.location.origin);
              return resolved.pathname === "/login";
            } catch {
              return false;
            }
          }

          function redirectToLogin() {
            if (redirectingToLogin || window.location.pathname === "/login") {
              return;
            }

            redirectingToLogin = true;
            window.location.assign("/login");
          }

          function injectLogoutButton() {
            if (document.getElementById("ccl-logout-button")) {
              return;
            }

            const button = document.createElement("button");
            button.id = "ccl-logout-button";
            button.type = "button";
            button.textContent = "Logout";
            button.setAttribute("aria-label", "Logout");
            button.style.position = "fixed";
            button.style.top = "12px";
            button.style.right = "16px";
            button.style.zIndex = "2147483647";
            button.style.height = "32px";
            button.style.padding = "0 12px";
            button.style.border = "1px solid #b8c4d8";
            button.style.borderRadius = "4px";
            button.style.background = "#ffffff";
            button.style.color = "#172033";
            button.style.font = "600 13px system-ui, -apple-system, Segoe UI, sans-serif";
            button.style.boxShadow = "0 2px 8px rgba(15, 23, 42, 0.16)";
            button.style.cursor = "pointer";

            button.addEventListener("click", async () => {
              button.disabled = true;
              try {
                await fetch("/logout", { method: "POST", credentials: "same-origin" });
              } finally {
                window.location.assign("/login");
              }
            });

            document.body.appendChild(button);
          }

          function allowNextKvPut() {
            allowNextKvPutUntil = Date.now() + 1500;
          }

          function consumeAllowedKvPut() {
            if (Date.now() <= allowNextKvPutUntil) {
              allowNextKvPutUntil = 0;
              return true;
            }

            return false;
          }

          function inspectValue(value) {
            if (typeof value !== "string") {
              return { looksLikeJson: false, isValidJson: null };
            }

            const trimmed = value.trimStart();
            if (!trimmed.startsWith("{") && !trimmed.startsWith("[")) {
              return { looksLikeJson: false, isValidJson: null };
            }

            try {
              JSON.parse(value);
              return { looksLikeJson: true, isValidJson: true };
            } catch {
              return { looksLikeJson: true, isValidJson: false };
            }
          }

          function shouldCheck(method, url) {
            if ((method || "").toUpperCase() !== "PUT") {
              return false;
            }

            return isConsulApiUrl(url, "/v1/kv");
          }

          function isConsulApiUrl(url, prefix = "/v1") {
            try {
              const resolved = new URL(url, window.location.origin);
              return resolved.pathname === prefix || resolved.pathname.startsWith(prefix + "/");
            } catch {
              return false;
            }
          }

          function withUiRequestHeader(headers) {
            const updated = new Headers(headers || {});
            updated.set(uiRequestHeaderName, uiRequestHeaderValue);
            return updated;
          }

          function readCandidateValue(node) {
            if (!node) {
              return "";
            }

            if (typeof node.value === "string") {
              return node.value;
            }

            if (node.matches?.(".cm-content")) {
              return node.textContent || "";
            }

            if (node.isContentEditable) {
              return node.innerText || node.textContent || "";
            }

            return node.textContent || "";
          }

          function findEditorValue(startNode) {
            const roots = [
              startNode?.closest?.("form"),
              startNode?.closest?.("[data-test-kv-editor]"),
              startNode?.closest?.("[data-test-view='kv/edit']"),
              document
            ].filter(Boolean);

            const selectors = [
              "textarea",
              "input[type='text']",
              "input:not([type])",
              "[contenteditable='true']",
              ".cm-content",
              ".CodeMirror textarea",
              ".CodeMirror-code"
            ];

            for (const root of roots) {
              const values = selectors
                .flatMap(selector => Array.from(root.querySelectorAll(selector)))
                .map(readCandidateValue)
                .filter(value => typeof value === "string" && value.trim().length > 0)
                .sort((left, right) => right.length - left.length);

              if (values.length > 0) {
                return values[0];
              }
            }

            return "";
          }

          function isSaveActionElement(target) {
            const action = target?.closest?.("button, [role='button'], input[type='submit'], input[type='button']");
            if (!action) {
              return false;
            }

            const label = (action.value || action.textContent || "").trim().toLowerCase();
            return label === "save";
          }

          function handleUiSaveAttempt(target, event) {
            if (!isSaveActionElement(target)) {
              return;
            }

            const editorValue = findEditorValue(target);
            const inspection = inspectValue(editorValue);
            if (!inspection.looksLikeJson || inspection.isValidJson !== false) {
              allowNextKvPut();
              return;
            }

            if (!window.confirm(warningMessage)) {
              event.preventDefault();
              event.stopImmediatePropagation();
              target?.focus?.();
              return;
            }

            allowNextKvPut();
          }

          function confirmInvalidJson(body) {
            if (consumeAllowedKvPut()) {
              return true;
            }

            const inspection = inspectValue(body);
            if (!inspection.looksLikeJson || inspection.isValidJson !== false) {
              return true;
            }

            return window.confirm(warningMessage);
          }

          document.addEventListener("click", event => {
            handleUiSaveAttempt(event.target, event);
          }, true);

          document.addEventListener("submit", event => {
            handleUiSaveAttempt(event.target, event);
          }, true);

          if (document.readyState === "loading") {
            document.addEventListener("DOMContentLoaded", injectLogoutButton, { once: true });
          } else {
            injectLogoutButton();
          }

          const originalFetch = window.fetch.bind(window);
          window.fetch = async function(input, init) {
            const url = typeof input === "string" ? input : input?.url;
            const method = init?.method || (typeof input !== "string" ? input?.method : "GET") || "GET";
            const body = init?.body;
            const isUiApiRequest = isConsulApiUrl(url);

            if (typeof body === "string" && shouldCheck(method, url) && !confirmInvalidJson(body)) {
              throw new DOMException("JSON validation cancelled by user.", "AbortError");
            }

            let nextInput = input;
            let nextInit = init;
            if (isUiApiRequest) {
              if (typeof input === "string" || input instanceof URL) {
                nextInit = { ...(init || {}), headers: withUiRequestHeader(init?.headers) };
              } else {
                nextInput = new Request(input, { ...(init || {}), headers: withUiRequestHeader(init?.headers || input.headers) });
                nextInit = undefined;
              }
            }

            const response = await originalFetch(nextInput, nextInit);
            if ((isUiApiRequest && response.status === 401) || (response.redirected && isLoginUrl(response.url))) {
              redirectToLogin();
            }

            return response;
          };

          const open = XMLHttpRequest.prototype.open;
          const send = XMLHttpRequest.prototype.send;

          XMLHttpRequest.prototype.open = function(method, url) {
            this.__cclMethod = method;
            this.__cclUrl = url;

             this.addEventListener("load", function() {
              if ((isConsulApiUrl(this.__cclUrl) && this.status === 401) || isLoginUrl(this.responseURL)) {
                redirectToLogin();
              }
            });

            return open.apply(this, arguments);
          };

          XMLHttpRequest.prototype.send = function(body) {
            if (typeof body === "string" && shouldCheck(this.__cclMethod, this.__cclUrl) && !confirmInvalidJson(body)) {
              this.abort();
              return;
            }

            if (isConsulApiUrl(this.__cclUrl)) {
              this.setRequestHeader(uiRequestHeaderName, uiRequestHeaderValue);
            }

            return send.apply(this, arguments);
          };
        })();
        """;
}
