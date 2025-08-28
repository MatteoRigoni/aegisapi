using Gateway.Features;
using Gateway.Observability;
using Gateway.RateLimiting;
using Gateway.Resilience;
using Gateway.Security;
using Gateway.Validation;
using Gateway.ControlPlane.Stores;
using Gateway.ControlPlane;
using Gateway.ControlPlane.Models;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Text;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;
using Gateway.Settings;

namespace Gateway;

public static class ServiceConfigurationExtensions
{
    public static IServiceCollection AddGatewayServices(this IServiceCollection services, IConfiguration configuration, ILoggingBuilder loggingBuilder)
    {
        // Resilience configuration
        services.Configure<ResilienceSettings>(configuration.GetSection("Resilience"));
        services.AddMemoryCache();

        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "Control Plane", Version = "v1" });
        });

        services.AddSingleton<IRouteStore>(sp => {
            var routesSection = configuration.GetSection("ReverseProxy:Routes");
            var clustersSection = configuration.GetSection("ReverseProxy:Clusters");
            var routeList = new List<Gateway.ControlPlane.Models.RouteConfig>();
            foreach (var routeChild in routesSection.GetChildren())
            {
                var routeId = routeChild.Key;
                var path = routeChild.GetSection("Match:Path").Value ?? routeChild.GetSection("Match").GetValue<string>("Path") ?? "/";
                var clusterId = routeChild["ClusterId"] ?? routeId;
                var destination = clustersSection.GetSection(clusterId).GetSection("Destinations:d1:Address").Value
                    ?? clustersSection.GetSection(clusterId).GetSection("Destinations").GetChildren().FirstOrDefault()?.GetValue<string>("Address")
                    ?? "";
                routeList.Add(new Gateway.ControlPlane.Models.RouteConfig {
                    Id = routeId,
                    Path = path,
                    Destination = destination
                });
            }
            return new InMemoryRouteStore(routeList);
        });
        services.AddSingleton<IRateLimitPlanStore>(sp =>
        {
            var store = new InMemoryRateLimitStore();
            var plans = configuration.GetSection("RateLimiting:Plans").GetChildren();
            foreach (var p in plans)
            {
                if (int.TryParse(p.Value, out var rpm))
                    store.Add(new RateLimitPlan { Plan = p.Key, Rpm = rpm });
            }
            return store;
        });
        services.AddSingleton<IWafToggleStore>(sp =>
        {
            var store = new InMemoryWafStore();
            var wafSection = configuration.GetSection("Waf");
            foreach (var child in wafSection.GetChildren())
            {
                if (bool.TryParse(child.Value, out var enabled))
                    store.Add(new WafToggle { Rule = child.Key, Enabled = enabled });
            }
            return store;
        });
        services.AddSingleton<IApiKeyStore>(sp =>
        {
            var store = new InMemoryApiKeyStore();
            var hash = configuration["Auth:ApiKeyHash"];
            if (!string.IsNullOrEmpty(hash))
                store.Add(new ApiKeyRecord { Id = Guid.NewGuid().ToString(), Hash = hash, Plan = string.Empty });
            return store;
        });
        services.AddSingleton<IAuditLog, InMemoryAuditLog>();
        services.AddSingleton<DynamicProxyConfigProvider>();
        services.AddSingleton<IProxyConfigProvider>(sp => sp.GetRequiredService<DynamicProxyConfigProvider>());

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
                    ctx.User.HasClaim(c => c.Type == "ApiKey"));
            });
        });

        // YARP resilience
        services.AddSingleton<Yarp.ReverseProxy.Forwarder.IForwarderHttpClientFactory, ResilienceForwarderHttpClientFactory>();
        services.AddReverseProxy();

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

        services.AddHttpClient<Gateway.AI.ISummarizerClient, Gateway.AI.SummarizerHttpClient>(http =>
        {
            http.BaseAddress = new Uri(configuration["Summarizer:BaseUrl"] ?? "http://localhost:5290");
            http.DefaultRequestHeaders.Add("X-Internal-Key", configuration["Summarizer:InternalKey"] ?? "dev");
        });
        services.AddHostedService<FeatureConsumerService>();

        return services;
    }
}
