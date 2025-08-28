using Gateway.Features;
using Gateway.Observability;
using Gateway.RateLimiting;
using Gateway.Resilience;
using Gateway.Security;
using Gateway.Settings;
using Gateway.Validation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Text;

namespace Gateway;

public static class ServiceConfigurationExtensions
{
    public static IServiceCollection AddGatewayServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Resilience configuration
        services.Configure<ResilienceSettings>(configuration.GetSection("Resilience"));
        services.Configure<RateLimitingSettings>(configuration.GetSection("RateLimiting"));
        services.Configure<WafSettings>(configuration.GetSection("Waf"));
        services.AddMemoryCache();

        const string ApiKeyScheme = "ApiKey";
        const string BearerOrApiKeyScheme = "BearerOrApiKey";

        // Authentication
        services.AddAuthentication(options =>
        {
            options.DefaultScheme = BearerOrApiKeyScheme;
            options.DefaultChallengeScheme = BearerOrApiKeyScheme;
        })
        .AddPolicyScheme(BearerOrApiKeyScheme, JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.ForwardDefaultSelector = ctx =>
            {
                if (ctx.Request.Headers.ContainsKey("Authorization"))
                    return JwtBearerDefaults.AuthenticationScheme;
                if (ctx.Request.Headers.ContainsKey("X-API-Key"))
                    return ApiKeyScheme;
                return JwtBearerDefaults.AuthenticationScheme;
            };
        })
        .AddJwtBearer(options =>
        {
            var jwtKey = configuration["Auth:JwtKey"] ?? "dev-secret";
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
            };
        })
        .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyScheme, options =>
        {
            options.ClaimsIssuer = ApiKeyScheme;
        });

        // Authorization
        services.AddAuthorization(options =>
        {
            options.AddPolicy("ApiReadOrKey", policy =>
            {
                policy.RequireAssertion(ctx =>
                    ctx.User.HasClaim("scope", "api.read") ||
                    ctx.User.HasClaim("ApiKey", "Valid"));
            });
        });

        // ApiKey configuration
        services
            .AddOptions<ApiKeyValidationOptions>()
            .Configure<IConfiguration>((opt, cfg) => opt.Hash = cfg["Auth:ApiKeyHash"] ?? "");

        // YARP resilience
        services.AddSingleton<Yarp.ReverseProxy.Forwarder.IForwarderHttpClientFactory, ResilienceForwarderHttpClientFactory>();
        services.AddReverseProxy().LoadFromConfig(configuration.GetSection("ReverseProxy"));

        services.Configure<AnomalyDetectionSettings>(configuration.GetSection("AnomalyDetection"));
        services.AddSingleton<IRequestFeatureQueue, RequestFeatureQueue>();
        services.AddSingleton<IFeatureSource>(sp => sp.GetRequiredService<IRequestFeatureQueue>());
        services.AddSingleton<RollingThresholdDetector>();
        services.AddSingleton<MlAnomalyDetector>();
        services.AddSingleton<IAnomalyDetector>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<AnomalyDetectionSettings>>().Value;
            return settings.Mode switch
            {
                DetectionMode.Rules => sp.GetRequiredService<RollingThresholdDetector>(),
                DetectionMode.Ml => sp.GetRequiredService<MlAnomalyDetector>(),
                DetectionMode.Hybrid => new HybridDetector(
                    sp.GetRequiredService<RollingThresholdDetector>(),
                    sp.GetRequiredService<MlAnomalyDetector>()),
                _ => sp.GetRequiredService<RollingThresholdDetector>()
            };
        });
        services.AddSingleton<AnomalyDetectionService>();
        services.AddHostedService(sp => sp.GetRequiredService<AnomalyDetectionService>());

        services.AddHttpClient<Gateway.AI.ISummarizerClient, Gateway.AI.SummarizerHttpClient>(http =>
        {
            http.BaseAddress = new Uri(configuration["Summarizer:BaseUrl"] ?? "http://localhost:5290");
            http.DefaultRequestHeaders.Add("X-Internal-Key", configuration["Summarizer:InternalKey"] ?? "dev");
        });
        services.AddHostedService<FeatureConsumerService>();

        services.AddOpenTelemetry()
            .ConfigureResource(rb => rb.AddService("gateway"))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter())
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation();
                metrics.AddHttpClientInstrumentation();
                metrics.AddRuntimeInstrumentation();
                metrics.AddProcessInstrumentation();
                metrics.AddMeter(GatewayDiagnostics.MeterName);
                metrics.AddPrometheusExporter();
                metrics.AddOtlpExporter();
            });

        var builder = WebApplication.CreateBuilder();
        builder.Logging.AddOpenTelemetry(options =>
        {
            options.IncludeScopes = true;
            options.IncludeFormattedMessage = true;
            options.ParseStateValues = true;
            options.AddOtlpExporter();
        });

        return services;
    }
}
