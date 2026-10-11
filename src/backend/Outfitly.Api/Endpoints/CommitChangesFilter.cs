using Outfitly.Application;

namespace Outfitly.Api.Endpoints;

// Applied only to command endpoints. Failed commands never reach the commit.
public sealed class CommitChangesFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var photos = services.GetRequiredService<PhotoChanges>();
        var committed = false;
        try
        {
            var result = await next(context);
            if (result is IStatusCodeHttpResult { StatusCode: >= 400 }) return result;
            await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(context.HttpContext.RequestAborted);
            committed = true;
            return result;
        }
        finally { await photos.CompleteAsync(committed); }
    }
}
