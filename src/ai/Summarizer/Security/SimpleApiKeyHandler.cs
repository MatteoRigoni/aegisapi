using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Summarizer.Security;

public sealed class SimpleApiKeyHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string HeaderName = "X-Internal-Key";
    private readonly IConfiguration _config;

    public SimpleApiKeyHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration config) : base(options, logger, encoder)
    {
        _config = config;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values))
            return Task.FromResult(AuthenticateResult.Fail("Missing"));

        var expected = _config["Summarizer:InternalKey"] ?? "dev";
        if (values.FirstOrDefault() == expected)
        {
            var claims = new[] { new Claim("ApiKey", "Valid") };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
        return Task.FromResult(AuthenticateResult.Fail("Invalid"));
    }
}
