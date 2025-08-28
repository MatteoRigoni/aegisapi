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
        services.AddOptions<ResilienceSettings>().BindConfiguration("Resilience");
        services.AddMemoryCache();

        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "Control Plane", Version = "v1" });
        });

        services.AddSingleton<IRouteStore>(sp => {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var routesSection = cfg.GetSection("ReverseProxy:Routes");
            var clustersSection = cfg.GetSection("ReverseProxy:Clusters");
            var routeList = new List<Gateway.ControlPlane.Models.RouteConfig>();
            foreach (var routeChild in routesSection.GetChildren())
            {
                var routeId = routeChild.Key;
                var path = routeChild.GetSection("Match:Path").Value ?? routeChild.GetSection("Match").GetValue<string>("Path") ?? "/";
                var clusterId = routeChild["ClusterId"] ?? routeId;
                var clusterSection = clustersSection.GetSection(clusterId);
                var destination = clusterSection.GetSection("Destinations:d1:Address").Value
                    ?? clusterSection.GetSection("Destinations").GetChildren().FirstOrDefault()?.GetValue<string>("Address")
                    ?? "";
                TimeSpan? activityTimeout = null;
                var timeoutStr = clusterSection.GetSection("HttpRequest")["ActivityTimeout"];
                if (TimeSpan.TryParse(timeoutStr, out var parsed))
                    activityTimeout = parsed;
                string? authPolicy = routeChild["AuthorizationPolicy"];
                string? pathRemovePrefix = null;
                foreach (var t in routeChild.GetSection("Transforms").GetChildren())
                {
                    pathRemovePrefix ??= t["PathRemovePrefix"];
                }
                routeList.Add(new Gateway.ControlPlane.Models.RouteConfig {
                    Id = routeId,
                    Path = path,
                    Destination = destination,
                    AuthorizationPolicy = authPolicy,
                    PathRemovePrefix = pathRemovePrefix,
                    ActivityTimeout = activityTimeout
                });
            }
            return new InMemoryRouteStore(routeList);
        });
        services.AddSingleton<IRateLimitPlanStore>(sp =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var store = new InMemoryRateLimitStore();
            var defaultRpm = cfg.GetValue<int?>("RateLimiting:DefaultRpm");
            if (defaultRpm.HasValue)
                store.Add(new RateLimitPlan { Plan = IRateLimitPlanStore.DefaultPlan, Rpm = defaultRpm.Value });
            var plans = cfg.GetSection("RateLimiting:Plans").GetChildren();
            foreach (var p in plans)
            {
                if (int.TryParse(p.Value, out var rpm))
                    store.Add(new RateLimitPlan { Plan = p.Key, Rpm = rpm });
            }
            return store;
        });
        services.AddSingleton<IWafToggleStore>(sp =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var store = new InMemoryWafStore();
            var wafSection = cfg.GetSection("Waf");
            foreach (var child in wafSection.GetChildren())
            {
                if (bool.TryParse(child.Value, out var enabled))
                    store.Add(new WafToggle { Rule = child.Key, Enabled = enabled });
            }
            return store;
        });
        services.AddSingleton<IAuditLog, InMemoryAuditLog>();
        // NOTE: Register YARP core first, then override its default IProxyConfigProvider with our dynamic provider

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
            var jwtKey = string.IsNullOrWhiteSpace(configuration["Auth:JwtKey"]) ? "dev-secret" : configuration["Auth:JwtKey"];
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
        })
        .Services.AddSingleton<IApiKeyStore>(sp =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var store = new InMemoryApiKeyStore();
            var hash = cfg["Auth:ApiKeyHash"];
            if (!string.IsNullOrEmpty(hash))
                store.Add(new ApiKeyRecord { Id = Guid.NewGuid().ToString(), Hash = hash, Plan = string.Empty });
            return store;
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
        // Override YARP's default IProxyConfigProvider after AddReverseProxy so our dynamic provider is used
        services.AddSingleton<DynamicProxyConfigProvider>();
        services.AddSingleton<IProxyConfigProvider>(sp => sp.GetRequiredService<DynamicProxyConfigProvider>());

        services.AddOptions<AnomalyDetectionSettings>().BindConfiguration("AnomalyDetection");
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
     // Use a safe default if configuration value is missing or empty
     var baseUrl = configuration["Summarizer:BaseUrl"];
     if (string.IsNullOrWhiteSpace(baseUrl))
     {
         baseUrl = "http://localhost:5290";
     }
     http.BaseAddress = new Uri(baseUrl, UriKind.Absolute);

     var internalKey = configuration["Summarizer:InternalKey"];
     if (string.IsNullOrWhiteSpace(internalKey))
     {
         internalKey = "dev";
     }
     http.DefaultRequestHeaders.Add("X-Internal-Key", internalKey);
 });
 services.AddHostedService<FeatureConsumerService>();

 return services;
    }
}
