using Microsoft.Extensions.Configuration;
using Outfitly.Application;
namespace Outfitly.Infrastructure;
public sealed class ConfiguredSupportAdmins(IConfiguration configuration) : ISupportAdminAccess
{
    public bool IsAdmin(Guid actor) => actor != Guid.Empty && configuration.GetSection("Support:AdminUserIds").GetChildren()
        .Any(x => Guid.TryParse(x.Value, out var id) && id == actor);
}
