using Listener.Api.Configuration;
using Listener.Api.Data;
using Listener.Api.Simulation;
using Listener.Api.Email;
using Listener.Api.Security;
using Listener.Api.Yape;
using Listener.Api.Bbva;
using Listener.Api.Devices;
using Listener.Api.History;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

LocalEnvironment.Load();

// Retired commands must exit instead of starting an obsolete mailbox connection.
if (args.Contains("--test-gmail", StringComparer.Ordinal)
    || args.Contains("--read-gmail-bcp", StringComparer.Ordinal))
{
    Console.Error.WriteLine("El lector de correo fue retirado. Usa el historial web y la recepción de BBVA Empresas desde Android.");
    Environment.ExitCode = 2;
    return;
}

if (args.Contains("--inspect-bcp-eml", StringComparer.Ordinal))
{
    Environment.ExitCode = await BcpEmailCommand.RunAsync(args);
    return;
}

if (args.Contains("--inspect-bcp-emls", StringComparer.Ordinal))
{
    Environment.ExitCode = await BcpEmailCommand.RunBatchAsync(args);
    return;
}

if (args.Contains("--simulate-payment", StringComparer.Ordinal)
    || args.Contains("--simulation-result", StringComparer.Ordinal))
{
    Environment.ExitCode = await SimulationCommand.RunAsync(args);
    return;
}

var builder = WebApplication.CreateBuilder(args.Where(x => x is not "--configure-admin" and not "--migrate-devices").ToArray());

if (args.Contains("--configure-admin", StringComparer.Ordinal))
{
    Environment.ExitCode = AdminSetupCommand.Run(builder.Environment.ContentRootPath);
    return;
}

var listenerConnection = builder.Configuration.GetConnectionString("ListenerDb")
    ?? throw new InvalidOperationException(
        "Configura ConnectionStrings:ListenerDb mediante User Secrets en desarrollo o el almacen de secretos del despliegue.");
builder.Services.AddDbContext<ListenerDbContext>(options =>
    options.UseSqlServer(listenerConnection, sql => sql.EnableRetryOnFailure()));
builder.Services.AddDbContext<PairingDbContext>(options => options.UseSqlServer(listenerConnection,
    sql => { sql.EnableRetryOnFailure(); sql.MigrationsHistoryTable("__PairingMigrationsHistory"); }));

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddRazorPages();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AdminCredentials>();
builder.Services.AddScoped<DevicePairingService>();
builder.Services.AddScoped<NotificationHistory>();
builder.Services.AddDataProtection().SetApplicationName("TonysGo.Listener.Admin")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "data-protection")));
builder.Services.AddAuthentication(AdminCredentials.Scheme).AddCookie(AdminCredentials.Scheme, options =>
{
    options.LoginPath = "/acceso";
    options.Cookie.Name = "Listener.Admin";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = true;
    options.Events.OnValidatePrincipal = context =>
    {
        var credentials = context.HttpContext.RequestServices.GetRequiredService<AdminCredentials>();
        if (credentials.Version is not { } version || context.Principal?.FindFirst("admin-version")?.Value != version)
            context.RejectPrincipal();
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api")) context.Response.StatusCode = 401;
        else context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization(options => options.AddPolicy(AdminCredentials.Scheme,
    policy => policy.AddAuthenticationSchemes(AdminCredentials.Scheme).RequireAuthenticatedUser().RequireRole("Administrator")));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    foreach (var (name, limit) in new[] { ("admin-login", 10), ("admin-actions", 20), ("device-pairing", 10) })
        options.AddPolicy(name, context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddScoped<YapeVerificationService>();
builder.Services.AddScoped<BbvaNotificationService>();
builder.Services.AddSingleton<BbvaAbonoSender>();
builder.Services.AddSingleton<BbvaAbonoDispatcher>();
builder.Services.AddSingleton<ApiKeyValidator>();
builder.Services.AddSingleton<TonysGoCallbackDispatcher>();
builder.Services.AddHttpClient("TonysGoCallback", client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHostedService<TonysGoCallbackWorker>();
builder.Services.AddHostedService<BbvaAbonoWorker>();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    // Lets Swagger UI send X-Listener-Api-Key through its "Authorize" button.
    document.Components ??= new OpenApiComponents();
    document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
    document.Components.SecuritySchemes["ApiKey"] = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = ApiKeyValidator.HeaderName
    };
    document.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("ApiKey", document)] = [] }];
    return Task.CompletedTask;
}));

var app = builder.Build();

if (args.Contains("--migrate-devices", StringComparer.Ordinal))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<PairingDbContext>().Database.MigrateAsync();
    return;
}

// Administrative pages and APIs share the authenticated session and privacy headers.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/acceso") || context.Request.Path.StartsWithSegments("/dispositivos")
        || context.Request.Path.StartsWithSegments("/historial")
        || context.Request.Path.StartsWithSegments("/reportes")
        || context.Request.Path.StartsWithSegments("/api/v1/admin") || context.Request.Path.StartsWithSegments("/api/v1/devices"))
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; frame-ancestors 'none'; form-action 'self'; base-uri 'none'";
    }
    await next(context);
});
app.UseStaticFiles();

// Configure the HTTP request pipeline.
// Outside Development the API docs stay off unless OpenApi:Habilitado is set.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("OpenApi:Habilitado"))
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Listener API v1"));
}

app.MapRazorPages();
app.MapGet("/", () => Results.Redirect("/historial")).ExcludeFromDescription();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program { }
