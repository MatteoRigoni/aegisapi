using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace Gateway.IntegrationTests;

public class GatewayFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((context, config) =>
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.Test.json");
            config.AddJsonFile(path, optional: true);
        });
    }
}
