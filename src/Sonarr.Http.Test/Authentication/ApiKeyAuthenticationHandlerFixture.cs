using System.Text.Encodings.Web;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using Sonarr.Http.Authentication;

namespace Sonarr.Http.Test.Authentication
{
    [TestFixture]
    public class ApiKeyAuthenticationHandlerFixture
    {
        private Mock<IConfigFileProvider> _configMock;
        private Mock<IOptionsMonitor<ApiKeyAuthenticationOptions>> _optionsMock;
        private Mock<ILoggerFactory> _loggerFactoryMock;
        private ApiKeyAuthenticationHandler _handler;
        private DefaultHttpContext _httpContext;

        [SetUp]
        public async Task SetUp()
        {
            _configMock = new Mock<IConfigFileProvider>();
            _configMock.Setup(c => c.ApiKey).Returns("valid-api-key");

            _optionsMock = new Mock<IOptionsMonitor<ApiKeyAuthenticationOptions>>();
            _optionsMock.Setup(o => o.Get(It.IsAny<string>())).Returns(new ApiKeyAuthenticationOptions());

            _loggerFactoryMock = new Mock<ILoggerFactory>();
            _loggerFactoryMock.Setup(l => l.CreateLogger(It.IsAny<string>()))
                .Returns(new Mock<ILogger>().Object);

            _handler = new ApiKeyAuthenticationHandler(
                _optionsMock.Object,
                _loggerFactoryMock.Object,
                UrlEncoder.Default,
                _configMock.Object);

            _httpContext = new DefaultHttpContext();
            var scheme = new AuthenticationScheme("API", "API Key", typeof(ApiKeyAuthenticationHandler));
            await _handler.InitializeAsync(scheme, _httpContext);
        }

        [Test]
        public async Task should_not_overwrite_302_redirect_on_challenge()
        {
            _httpContext.Response.StatusCode = 302;
            _httpContext.Response.Headers.Location = "/login?returnUrl=%2F";

            await _handler.ChallengeAsync(new AuthenticationProperties());

            _httpContext.Response.StatusCode.Should().Be(302);
            _httpContext.Response.Headers.Location.ToString().Should().Be("/login?returnUrl=%2F");
        }

        [Test]
        public async Task should_set_401_on_challenge_when_not_redirecting()
        {
            _httpContext.Response.StatusCode = 200;

            await _handler.ChallengeAsync(new AuthenticationProperties());

            _httpContext.Response.StatusCode.Should().Be(401);
        }

        [Test]
        public async Task should_not_overwrite_302_redirect_on_forbidden()
        {
            _httpContext.Response.StatusCode = 302;
            _httpContext.Response.Headers.Location = "/login?loginFailed=true";

            await _handler.ForbidAsync(new AuthenticationProperties());

            _httpContext.Response.StatusCode.Should().Be(302);
        }

        [Test]
        public async Task should_set_403_on_forbidden_when_not_redirecting()
        {
            _httpContext.Response.StatusCode = 200;

            await _handler.ForbidAsync(new AuthenticationProperties());

            _httpContext.Response.StatusCode.Should().Be(403);
        }
    }
}
