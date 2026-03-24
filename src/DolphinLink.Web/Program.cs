using System.Runtime.Versioning;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using DolphinLink.Web;
using DolphinLink.Web.Services;

[assembly: SupportedOSPlatform("browser")]

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Shared RPC console — singleton so the log persists across page navigation.
builder.Services.AddSingleton<RpcConsoleService>();

// Shared Flipper RPC client — singleton so all pages see the same connection.
builder.Services.AddSingleton<ClientService>();

// Markdown documentation renderer — singleton so docs are parsed once and cached.
builder.Services.AddSingleton<MarkdownService>();

// Theme manager — singleton so the light/dark preference is shared across pages.
builder.Services.AddSingleton<ThemeService>();

await builder.Build().RunAsync();
