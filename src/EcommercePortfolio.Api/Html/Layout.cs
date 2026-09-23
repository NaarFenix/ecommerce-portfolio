using System.Net;

namespace EcommercePortfolio.Api.Html;

public static class Layout
{
    public static string Wrap(string title, string body, string? bodyClass = null, string? csrfToken = null) => $@"<!doctype html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
{(string.IsNullOrEmpty(csrfToken) ? "" : $"<meta name=\"csrf-token\" content=\"{H(csrfToken)}\">")}
<title>{H(title)}</title>
<link rel=""stylesheet"" href=""/css/site.css"">
<script src=""/js/htmx.min.js""></script>
<script>
document.addEventListener('htmx:configRequest', function (e) {{
  var m = document.querySelector('meta[name=""csrf-token""]');
  if (m) e.detail.headers['RequestVerificationToken'] = m.getAttribute('content');
}});
document.addEventListener('DOMContentLoaded', function () {{
  var m = document.querySelector('meta[name=""csrf-token""]');
  if (!m) return;
  document.querySelectorAll('form').forEach(function (f) {{
    if (f.querySelector('input[name=""__RequestVerificationToken""]')) return;
    var i = document.createElement('input');
    i.type = 'hidden';
    i.name = '__RequestVerificationToken';
    i.value = m.getAttribute('content');
    f.appendChild(i);
  }});
}});
</script>
</head>
<body{(string.IsNullOrEmpty(bodyClass) ? "" : $" class=\"{H(bodyClass)}\"")}>
{body}
</body>
</html>";

    public static string H(string? s) => WebUtility.HtmlEncode(s ?? "");
}