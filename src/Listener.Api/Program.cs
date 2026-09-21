using Listener.Api.Diagnostics;
using Listener.Api.Data;
using Listener.Api.Simulation;
using Listener.Api.Email;
using Listener.Api.Panel;
using Listener.Api.Automatic;
using Microsoft.EntityFrameworkCore;

// One-off local check: no HTTP endpoint and no persisted mailbox credentials.
if (args.Contains("--test-gmail", StringComparer.Ordinal)
    || args.Contains("--read-gmail-bcp", StringComparer.Ordinal))
{
    if (args.Length != 1)
    {
        Console.Error.WriteLine("Usa solamente --test-gmail o --read-gmail-bcp.");
        Environment.ExitCode = 2;
        return;
    }
    Environment.ExitCode = await GmailConnectionTest.RunAsync(args[0] == "--read-gmail-bcp");
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

var builder = WebApplication.CreateBuilder(args);

var listenerConnection = builder.Configuration.GetConnectionString("ListenerDb")
    ?? throw new InvalidOperationException(
        "Configura ConnectionStrings:ListenerDb mediante User Secrets en desarrollo o el almacen de secretos del despliegue.");
builder.Services.AddDbContext<ListenerDbContext>(options =>
    options.UseSqlServer(listenerConnection, sql => sql.EnableRetryOnFailure()));

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddRazorPages();
builder.Services.AddAntiforgery(options => options.Cookie.SameSite = SameSiteMode.Strict);
builder.Services.AddSingleton<AutomaticState>();
builder.Services.AddSingleton<IAutomaticMailbox, AutomaticMailbox>();
builder.Services.AddSingleton<NotificationStore>();
builder.Services.AddScoped<NotificationQueries>();
builder.Services.AddHostedService<AutomaticReaderWorker>();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// This first administration view is local-only. Do not expose it until real
// administrator authentication and authorization have been implemented.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/panel")
        || context.Request.Path.StartsWithSegments("/api/notificaciones")
        || context.Request.Path.StartsWithSegments("/api/lector"))
    {
        if (!app.Environment.IsDevelopment())
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        if (!LocalPanelAccess.IsAllowed(context.Connection.RemoteIpAddress, context.Request.Host.Host))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; frame-ancestors 'none'; form-action 'self'; base-uri 'none'";
    }
    await next(context);
});
app.UseStaticFiles();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapRazorPages();
    app.MapGet("/", () => Results.Redirect("/panel")).ExcludeFromDescription();
    app.MapGet("/api/lector", (AutomaticState state) => state.Status);
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
