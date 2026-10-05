using RestOMatic.Web.Features.Accounts;
using RestOMatic.Web.Features.Health;
using RestOMatic.Web.Infrastructure.Hosting;
using RestOMatic.Web.Infrastructure.Persistence;
using RestOMatic.Web.Infrastructure.Storage;
using RestOMatic.Web.Shell;

// Composition only. Each slice and each piece of infrastructure registers
// itself; nothing here knows what a feature does.
var builder = WebApplication.CreateBuilder(args);

builder.AddReverseProxy();
builder.AddStorage();
builder.AddPersistence();
builder.AddShell();
builder.AddHealth();
builder.AddAccounts();

var app = builder.Build();

app.InitialiseStorage();
app.MigrateDatabase();

// A command such as `reset-password alice` runs instead of the web server.
if (await app.RunAccountsCommandAsync(args))
{
    return;
}

app.UseReverseProxy();
app.UseShell();
app.UseAccounts();
app.MapShell();

app.MapHealth();
app.MapAccounts();

app.Run();
