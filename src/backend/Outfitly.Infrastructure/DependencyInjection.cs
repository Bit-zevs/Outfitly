using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Outfitly.Application;
using Outfitly.Infrastructure.Persistence;
using Outfitly.Application.Authentication;
using Outfitly.Infrastructure.Authentication;

namespace Outfitly.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOutfitlyInfrastructure(this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddDbContext<OutfitlyDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IWardrobeRepository, EfWardrobeRepository>();
        services.AddScoped<IAccountStore, EfAccountStore>();
        services.AddSingleton<IAccountPasswordHasher, AccountPasswordHasher>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<OutfitlyDbContext>());
        return services;
    }
}
