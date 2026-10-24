using Outfitly.Application;
using Outfitly.Infrastructure;
using Outfitly.Api.Authentication;
using Outfitly.Api.Endpoints;
using Outfitly.Api.Errors;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using System.Threading.RateLimiting;
using Outfitly.Application.Authentication;
using System.Text.Json.Serialization;
using Outfitly.Api.Contracts;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Outfitly")
    ?? throw new InvalidOperationException("Configure ConnectionStrings:Outfitly for PostgreSQL.");
builder.Services.AddOutfitlyInfrastructure(connectionString);
builder.Services.AddScoped<WardrobeService>();
builder.Services.AddScoped<PhotoChanges>();
builder.Services.AddScoped<PhotoService>();
builder.Services.AddScoped<ItemCatalog>();
var photoRoot = Path.GetFullPath(builder.Configuration["Photos:RootPath"] ?? "App_Data/photos", builder.Environment.ContentRootPath);
var webRoot = builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
var photoRelativePath = Path.GetRelativePath(webRoot, photoRoot);
if (photoRelativePath == "." || (!Path.IsPathRooted(photoRelativePath) && photoRelativePath != ".." &&
    !photoRelativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
    throw new InvalidOperationException("Photos:RootPath must be outside the public web root.");
builder.Services.AddSingleton<IPhotoStore>(provider => new Outfitly.Infrastructure.Photos.LocalPhotoStore(
    photoRoot, provider.GetRequiredService<ILogger<Outfitly.Infrastructure.Photos.LocalPhotoStore>>()));
builder.Services.AddScoped<AccountService>();
var emailOptions = new Outfitly.Infrastructure.Authentication.SmtpConfirmationOptions();
builder.Configuration.GetSection("EmailConfirmation").Bind(emailOptions);
builder.Services.AddSingleton(emailOptions);
builder.Services.AddScoped<IConfirmationEmailSender, Outfitly.Infrastructure.Authentication.SmtpConfirmationEmailSender>();
builder.Services.AddScoped<SessionCookieEvents>();
builder.Services.AddDataProtection().SetApplicationName("Outfitly");
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "__Host-Outfitly.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.Path = "/";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;
    options.EventsType = typeof(SessionCookieEvents);
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-Outfitly.Csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.Path = "/";
});
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("account-access", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
        }));
    options.OnRejected = async (context, _) =>
        await Results.Problem(statusCode: 429, title: "Too many account requests").ExecuteAsync(context.HttpContext);
});
builder.Services.AddAuthorization(options => options.AddPolicy(CurrentActor.PolicyName,
    policy => policy.RequireAuthenticatedUser().RequireAssertion(context => CurrentActor.HasValidId(context.User))));
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new ClothingCategoryJsonConverter());
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseMiddleware<CsrfMiddleware>();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapWardrobeEndpoints();
app.MapPhotoEndpoints();
app.MapAuthEndpoints();

app.Run();

public partial class Program;
