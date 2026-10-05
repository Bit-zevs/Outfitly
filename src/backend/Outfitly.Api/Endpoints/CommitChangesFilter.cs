using Outfitly.Application;

namespace Outfitly.Api.Endpoints;

// Applied only to command endpoints. Failed commands never reach the commit.
public sealed class CommitChangesFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);
        if (result is IStatusCodeHttpResult { StatusCode: >= 400 })
            return result;
        await context.HttpContext.RequestServices.GetRequiredService<IUnitOfWork>()
            .SaveChangesAsync(context.HttpContext.RequestAborted);
        return result;
    }
}
