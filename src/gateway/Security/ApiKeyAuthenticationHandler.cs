using Microsoft.AspNetCore.Authentication;
using Gateway.ControlPlane.Stores;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Linq;

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
        : base(options, logger, encoder) => _store = store;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values))
            return Task.FromResult(AuthenticateResult.NoResult());

        var provided = values.FirstOrDefault();
        if (string.IsNullOrEmpty(provided))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API Key"));

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(provided)));
        var record = _store.GetAll().FirstOrDefault(k =>
        {
            var expectedBytes = Convert.FromHexString(k.Hash);
            var providedBytes = Convert.FromHexString(hash);
            return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
        });
        if (record is null)
            return Task.FromResult(AuthenticateResult.Fail("Invalid API Key"));

        var claims = new[] { new Claim("ApiKey", record.Id), new Claim("plan", record.Plan) };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
