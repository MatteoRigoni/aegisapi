using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Gateway.ControlPlane.Stores;

namespace Gateway.Security;

public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string HeaderName = "X-API-Key";
    private readonly IApiKeyStore _store;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiKeyStore store)
        : base(options, logger, encoder)
    {
        _store = store;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values))
            return Task.FromResult(AuthenticateResult.NoResult());

        var provided = values.FirstOrDefault();

        if (string.IsNullOrEmpty(provided))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API Key"));

        var providedBytes = SHA256.HashData(Encoding.UTF8.GetBytes(provided));

        foreach (var key in _store.GetAll())
        {
            if (string.IsNullOrEmpty(key.Hash))
                continue;

            var expectedBytes = Convert.FromHexString(key.Hash);
            if (CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
            {
                var claims = new[] { new Claim("ApiKey", "Valid") };
                var identity = new ClaimsIdentity(claims, Scheme.Name);
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, Scheme.Name);
                return Task.FromResult(AuthenticateResult.Success(ticket));
            }
        }

        return Task.FromResult(AuthenticateResult.Fail("Invalid API Key"));
    }
}
