using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Stainer.Tests;

public sealed class WebHostIntegrationTests
{
    [Fact]
    public async Task Web_host_serves_pages_static_assets_health_api_and_fallback()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        var info = await client.GetFromJsonAsync<SystemInfoResponse>("/api/system/info");
        Assert.NotNull(info);
        Assert.False(info!.PythonRuntimeRequired);
        Assert.Equal("ASP.NET Core", info.UiHost);

        foreach (var route in new[] { "/", "/dashboard", "/samples", "/reagents", "/run", "/alerts", "/history", "/configure", "/engineer", "/admin" })
        {
            var html = await client.GetStringAsync(route);
            Assert.Contains("app.css", html);
            Assert.DoesNotContain("{%", html);
            Assert.DoesNotContain("{{", html);
        }

        var dashboard = await client.GetStringAsync("/dashboard");
        Assert.Contains("app-shell", dashboard);
        Assert.Contains("drawerBoard", dashboard);

        var css = await client.GetAsync("/static/css/app.css");
        Assert.Equal(HttpStatusCode.OK, css.StatusCode);
        Assert.Contains("text/css", css.Content.Headers.ContentType?.MediaType);

        var js = await client.GetAsync("/static/js/api.js");
        Assert.Equal(HttpStatusCode.OK, js.StatusCode);

        var fallback = await client.GetStringAsync("/kiosk/unknown");
        Assert.Contains("drawerBoard", fallback);
    }

    [Fact]
    public async Task Web_host_mock_api_supports_login_initialize_and_state()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/login", new { username = "operator", password = "123456", role = "operator" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var initialize = await client.PostAsync("/api/system/initialize", null);
        Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);

        var samples = await client.PostAsync("/api/samples/scan?count=4", null);
        Assert.Equal(HttpStatusCode.OK, samples.StatusCode);

        var state = await client.GetFromJsonAsync<RuntimeStateResponse>("/api/state");
        Assert.NotNull(state);
        Assert.True(state!.Initialized);
        Assert.Equal("ready", state.Status);
        Assert.Equal(4, state.Channels.SelectMany(x => x.Slides).Count());
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "stainer-web-host-tests", Guid.NewGuid().ToString("N"), "stainer.db");
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:StainerDatabase"] = $"Data Source={databasePath}"
                    });
                });
            });
    }

    private sealed record SystemInfoResponse(bool PythonRuntimeRequired, string UiHost);

    private sealed record RuntimeStateResponse(bool Initialized, string Status, RuntimeChannel[] Channels);

    private sealed record RuntimeChannel(RuntimeSlide[] Slides);

    private sealed record RuntimeSlide(string Id);
}
