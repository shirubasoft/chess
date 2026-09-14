using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.WebHost.UseStaticWebAssets();
var serverAddress = builder.Configuration["Chess:ServerUrl"]
    ?? builder.Configuration["services:server:http:0"]
    ?? builder.Configuration["services:server:https:0"]
    ?? throw new InvalidOperationException("Configure Chess:ServerUrl or an Aspire reference to the server resource.");
if (!Uri.TryCreate(serverAddress, UriKind.Absolute, out var serverUri) ||
    serverUri.Scheme is not ("http" or "https"))
    throw new InvalidOperationException("Chess:ServerUrl must be an absolute HTTP or HTTPS address.");

builder.Services.AddReverseProxy().LoadFromMemory(
    [new RouteConfig { RouteId = "chess-api", ClusterId = "server", Match = new RouteMatch { Path = "/api/{**path}" } }],
    [new ClusterConfig
    {
        ClusterId = "server",
        Destinations = new Dictionary<string, DestinationConfig>
        {
            ["server"] = new() { Address = serverUri.AbsoluteUri }
        },
        HttpRequest = new() { ActivityTimeout = TimeSpan.FromMinutes(2) }
    }]);
var app = builder.Build();
app.UseStaticFiles();
app.MapStaticAssets();
app.MapDefaultEndpoints();
app.MapReverseProxy();
app.MapFallbackToFile("index.html");
app.Run();
