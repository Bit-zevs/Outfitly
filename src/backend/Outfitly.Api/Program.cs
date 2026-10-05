using Outfitly.Application;
using Outfitly.Infrastructure;
using Outfitly.Api.Authentication;
using Outfitly.Api.Endpoints;
using Outfitly.Api.Errors;
using Microsoft.AspNetCore.Authentication;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Outfitly")
    ?? throw new InvalidOperationException("Configure ConnectionStrings:Outfitly for PostgreSQL.");
builder.Services.AddOutfitlyInfrastructure(connectionString);
builder.Services.AddScoped<WardrobeService>();
builder.Services.AddAuthentication(PendingAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, PendingAuthenticationHandler>(PendingAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(options => options.AddPolicy(CurrentActor.PolicyName,
    policy => policy.RequireAuthenticatedUser().RequireAssertion(context => CurrentActor.HasValidId(context.User))));
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapWardrobeEndpoints();

app.Run();

public partial class Program;
