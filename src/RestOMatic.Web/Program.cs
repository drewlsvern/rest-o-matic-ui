using RestOMatic.Web.Features.Health;
using RestOMatic.Web.Infrastructure.Persistence;
using RestOMatic.Web.Infrastructure.Storage;
using RestOMatic.Web.Shell;

// Composition only. Each slice and each piece of infrastructure registers
// itself; nothing here knows what a feature does.
var builder = WebApplication.CreateBuilder(args);

builder.AddStorage();
builder.AddPersistence();
builder.AddShell();
builder.AddHealth();

var app = builder.Build();

app.InitialiseStorage();
app.MigrateDatabase();

app.UseShell();
app.MapHealth();

app.Run();
