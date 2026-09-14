using Chess.Client;
using Chess.Web;
using Chess.Web.Storage;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped(provider => new ChessClient(provider.GetRequiredService<HttpClient>(), "Chess Web"));
builder.Services.AddScoped<BrowserGameStore>();
await builder.Build().RunAsync();
