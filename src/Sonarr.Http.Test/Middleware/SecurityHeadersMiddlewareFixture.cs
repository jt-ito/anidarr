using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Sonarr.Http.Middleware;

namespace Sonarr.Http.Test.Middleware
{
    [TestFixture]
    public class SecurityHeadersMiddlewareFixture
    {
        private Mock<ILogger<SecurityHeadersMiddleware>> _loggerMock;

        [SetUp]
        public void SetUp()
        {
            _loggerMock = new Mock<ILogger<SecurityHeadersMiddleware>>();
        }

        [Test]
        public async Task should_set_security_headers_on_response()
        {
            var middleware = new SecurityHeadersMiddleware(
                context =>
                {
                    context.Response.StatusCode = 401;
                    return Task.CompletedTask;
                },
                _loggerMock.Object);

            var context = new DefaultHttpContext();
            await middleware.InvokeAsync(context);

            context.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
            context.Response.Headers["X-Frame-Options"].ToString().Should().Be("DENY");
        }

        [Test]
        public async Task should_preserve_existing_content_type_on_200()
        {
            var middleware = new SecurityHeadersMiddleware(
                context =>
                {
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "text/html; charset=utf-8";
                    return Task.CompletedTask;
                },
                _loggerMock.Object);

            var context = new DefaultHttpContext();
            await middleware.InvokeAsync(context);

            context.Response.ContentType.Should().Be("text/html; charset=utf-8");
        }
    }
}
