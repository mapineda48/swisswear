using MudBlazor.Services;
using SwissWear.Infrastructure;
using SwissWear.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Localization
builder.Services.AddLocalization();

// Add MudBlazor services
builder.Services.AddMudServices();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Infrastructure (Database, Storage, Cache, PubSub, Application services)
builder.Services.AddSwissWearInfrastructure(builder.Configuration);

// HTTP context for capturing client info (IP, UserAgent)
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Localization middleware — detects language from Accept-Language header
var supportedCultures = new[] { "en", "es" };
app.UseRequestLocalization(options =>
{
    options.SetDefaultCulture("en");
    options.AddSupportedCultures(supportedCultures);
    options.AddSupportedUICultures(supportedCultures);
    options.ApplyCurrentCultureToResponseHeaders = true;
});

app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
