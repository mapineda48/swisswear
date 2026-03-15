using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using StackExchange.Redis;
using SwissWear.Web.Components;
using SwissWear.Web.Data;
using SwissWear.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Localization
builder.Services.AddLocalization();

// Add MudBlazor services
builder.Services.AddMudServices();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Storage
builder.Services.AddSingleton<IStorageService, AzureBlobStorageService>();

// Cache
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(builder.Configuration["Valkey:ConnectionString"]
        ?? throw new InvalidOperationException("Valkey:ConnectionString is not configured.")));
builder.Services.AddSingleton<ICacheService, ValkeyCacheService>();

// HTTP context for capturing client info (IP, UserAgent)
builder.Services.AddHttpContextAccessor();

// Application services
builder.Services.AddScoped<PersonService>();
builder.Services.AddSingleton<IChatAuditService, ChatAuditService>();
builder.Services.AddSingleton<IChatService, ChatService>();

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
