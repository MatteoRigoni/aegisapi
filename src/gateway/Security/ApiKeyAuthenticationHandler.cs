using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

namespace Gateway.Security;

public sealed class ApiKeyValidationOptions
{
    public string Hash { get; }
    public ApiKeyValidationOptions(string hash) => Hash = hash;
}

public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string HeaderName = "X-API-Key";
    private readonly ApiKeyValidationOptions _options;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock,
        ApiKeyValidationOptions validationOptions)
        : base(options, logger, encoder, clock)
    {
        _options = validationOptions;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values))
            return Task.FromResult(AuthenticateResult.NoResult());

        var provided = values.FirstOrDefault();
        if (string.IsNullOrEmpty(provided) || string.IsNullOrEmpty(_options.Hash))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API Key"));

        var providedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(provided)));
        var providedBytes = Convert.FromHexString(providedHash);
        var expectedBytes = Convert.FromHexString(_options.Hash);

        if (CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
        {
            var claims = new[] { new Claim("ApiKey", "Valid") };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        return Task.FromResult(AuthenticateResult.Fail("Invalid API Key"));
    }
}
