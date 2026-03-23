using System.Runtime.Versioning;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using FlipperZero.Web;
using FlipperZero.Web.Services;

[assembly: SupportedOSPlatform("browser")]

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Shared RPC console — singleton so the log persists across page navigation.
builder.Services.AddSingleton<RpcConsoleService>();

// Shared Flipper RPC client — singleton so all pages see the same connection.
builder.Services.AddSingleton<FlipperClientService>();

await builder.Build().RunAsync();
