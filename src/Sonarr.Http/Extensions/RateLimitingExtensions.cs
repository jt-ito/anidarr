using System;
using System.Linq;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Sonarr.Http.Extensions
{
    public static class RateLimitingExtensions
    {
        public const string ApiGeneralPolicy = "api-general";
        public const string ApiSearchPolicy = "api-search";
        public const string AuthLoginPolicy = "auth-login";

        /// <summary>
        /// Registers Anidarr rate limiting policies using the .NET 8+ built-in RateLimiter.
        /// api-general: 100 req/60s per IP (all /api/* routes)
        /// api-search:  20 req/60s per IP (series lookup endpoints)
        /// auth-login:  10 req/60s per IP (login page, brute-force protection)
        /// </summary>
        public static IServiceCollection AddAnidarrRateLimiting(this IServiceCollection services)
        {
            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = 429;

                options.AddPolicy(ApiGeneralPolicy, context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: GetClientIp(context),
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 100,
                            Window = TimeSpan.FromSeconds(60),
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            QueueLimit = 0
                        }));

                options.AddPolicy(ApiSearchPolicy, context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: GetClientIp(context),
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 20,
                            Window = TimeSpan.FromSeconds(60),
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            QueueLimit = 0
                        }));

                options.AddPolicy(AuthLoginPolicy, context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: GetClientIp(context),
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 10,
                            Window = TimeSpan.FromSeconds(60),
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            QueueLimit = 0
                        }));

                options.OnRejected = async (context, cancellationToken) =>
                {
                    context.HttpContext.Response.StatusCode = 429;
                    context.HttpContext.Response.Headers["Retry-After"] = "60";
                    context.HttpContext.Response.ContentType = "application/json";
                    await context.HttpContext.Response.WriteAsync(
                        "{\"message\":\"Rate limit exceeded. Please slow down your requests.\"}",
                        cancellationToken);
                };
            });

            return services;
        }

        private static string GetClientIp(Microsoft.AspNetCore.Http.HttpContext context)
        {
            // Respect X-Forwarded-For if behind a reverse proxy
            var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwardedFor))
            {
                return forwardedFor.Split(',')[0].Trim();
            }

            return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }
    }
}
