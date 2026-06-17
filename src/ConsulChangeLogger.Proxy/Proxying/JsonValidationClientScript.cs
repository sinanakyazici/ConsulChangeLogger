namespace ConsulChangeLogger.Proxy.Proxying;

internal static class JsonValidationClientScript
{
    public const string Path = "/_ccl/json-validation.js";

    public static string Content =>
        """
        (() => {
          const warningMessage =
            "Girilen deger JSON gibi gorunuyor ancak gecerli degil.\n\nYine de kaydetmek istiyor musunuz?";
          let redirectingToLogin = false;

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

            try {
              const resolved = new URL(url, window.location.origin);
              return resolved.pathname === "/v1/kv" || resolved.pathname.startsWith("/v1/kv/");
            } catch {
              return false;
            }
          }

          function confirmInvalidJson(body) {
            const inspection = inspectValue(body);
            if (!inspection.looksLikeJson || inspection.isValidJson !== false) {
              return true;
            }

            return window.confirm(warningMessage);
          }

          const originalFetch = window.fetch.bind(window);
          window.fetch = async function(input, init) {
            const url = typeof input === "string" ? input : input?.url;
            const method = init?.method || (typeof input !== "string" ? input?.method : "GET") || "GET";
            const body = init?.body;

            if (typeof body === "string" && shouldCheck(method, url) && !confirmInvalidJson(body)) {
              throw new DOMException("JSON validation cancelled by user.", "AbortError");
            }

            const response = await originalFetch(input, init);
            if (response.redirected && isLoginUrl(response.url)) {
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
              if (isLoginUrl(this.responseURL)) {
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

            return send.apply(this, arguments);
          };
        })();
        """;
}
