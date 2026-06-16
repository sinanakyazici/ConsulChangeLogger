using System.Net;

namespace ConsulChangeLogger.Proxy.Authentication;

internal static class LoginPage
{
    public static async Task WriteAsync(HttpContext context, string csrfToken, string? error = null)
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        var errorHtml = string.IsNullOrWhiteSpace(error)
            ? string.Empty
            : $"""<div class="error">{WebUtility.HtmlEncode(error)}</div>""";

        await context.Response.WriteAsync($$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Consul Change Logger</title>
  <style>
    body { margin: 0; font-family: Arial, sans-serif; background: #f5f7fb; color: #172033; }
    main { min-height: 100vh; display: grid; place-items: center; padding: 24px; }
    form { width: min(380px, 100%); background: #fff; border: 1px solid #d8deea; border-radius: 8px; padding: 28px; box-shadow: 0 12px 36px rgba(20, 31, 54, .08); }
    h1 { font-size: 22px; margin: 0 0 20px; }
    label { display: block; font-size: 13px; font-weight: 700; margin: 16px 0 6px; }
    input { width: 100%; box-sizing: border-box; border: 1px solid #c5cede; border-radius: 6px; padding: 11px 12px; font-size: 14px; }
    button { width: 100%; margin-top: 22px; border: 0; border-radius: 6px; background: #174ea6; color: #fff; padding: 12px; font-weight: 700; cursor: pointer; }
    .error { background: #fff1f0; border: 1px solid #ffccc7; color: #a8071a; border-radius: 6px; padding: 10px 12px; margin-bottom: 14px; font-size: 13px; }
  </style>
</head>
<body>
  <main>
    <form method="post" action="/login">
      <h1>Consul Change Logger</h1>
      {{errorHtml}}
      <input name="csrf_token" type="hidden" value="{{WebUtility.HtmlEncode(csrfToken)}}">
      <label for="username">Username</label>
      <input id="username" name="username" type="text" autocomplete="username" required autofocus>
      <label for="password">Password</label>
      <input id="password" name="password" type="password" autocomplete="current-password" required>
      <button type="submit">Sign in</button>
    </form>
  </main>
</body>
</html>
""");
    }
}
