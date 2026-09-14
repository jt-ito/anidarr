using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Sonarr.Http.Middleware
{
    public class SecurityHeadersMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<SecurityHeadersMiddleware> _logger;

        public SecurityHeadersMiddleware(RequestDelegate next, ILogger<SecurityHeadersMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var headers = context.Response.Headers;

            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["X-XSS-Protection"] = "0"; // Tells modern browsers to use built-in protection
            headers["Content-Security-Policy"] =
                "default-src 'self'; " +
                "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://static.cloudflareinsights.com; " +  // unsafe-inline and unsafe-eval needed for React in dev
                "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
                "font-src 'self' https://fonts.gstatic.com; " +
                "connect-src 'self' wss: https://sentry.sonarr.tv https://cloudflareinsights.com; " +
                "img-src 'self' data: https:; " +
                "media-src 'none'; " +
                "object-src 'none'; " +
                "frame-ancestors 'none';";

            if (context.Request.IsHttps)
            {
                headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
            }

            context.Response.OnStarting(() =>
            {
                // Ensure empty/error responses (e.g. 401, 403, 404, 500) have an explicit Content-Type
                // so WebKit/Safari never misinterprets them as an unrecognized binary file download.
                if (string.IsNullOrEmpty(context.Response.ContentType) && context.Response.StatusCode >= 400)
                {
                    context.Response.ContentType = "text/plain; charset=utf-8";
                }

                return Task.CompletedTask;
            });

            await _next(context);
        }
    }
}
