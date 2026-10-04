using Outfitly.Application;
using Outfitly.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IWardrobeRepository, InMemoryWardrobeRepository>();
builder.Services.AddSingleton<WardrobeService>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/wardrobe", (WardrobeService service) => service.GetPublicItems());

app.Run();
