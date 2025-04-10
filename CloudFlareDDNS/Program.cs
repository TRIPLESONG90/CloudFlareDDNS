using CloudFlareDDNS.Components;
using CloudFlareDDNS.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Trace);
builder.Services.AddSingleton<WorkerManager>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<WorkerManager>());


builder.Services.AddHttpClient("CloudFlareClient", client =>
{
    client.BaseAddress = new Uri("https://api.cloudflare.com/client/v4/");
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
