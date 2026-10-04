using Outfitly.Application;
using Outfitly.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IWardrobeRepository, InMemoryWardrobeRepository>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/wardrobe", (IWardrobeRepository repository) => repository.GetAll());

app.Run();
