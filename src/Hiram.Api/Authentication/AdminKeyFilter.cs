using System.Security.Cryptography;
using System.Text;

namespace Hiram.Api.Authentication;

// Shared X-Admin-Key gate for the provisional operator surface and the dev-only demo bootstrap. Both
// guard on a single configured key rather than tenant API keys, compared in fixed time.
internal static class AdminKeyFilter
{
    private const string HeaderName = "X-Admin-Key";

    public static async ValueTask<object?> Require(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var configured = configuration["Hiram:AdminKey"];
        var presented = context.HttpContext.Request.Headers[HeaderName].ToString();

        if (string.IsNullOrEmpty(configured) || !Matches(presented, configured))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "A valid X-Admin-Key header is required.");
        }

        return await next(context);
    }

    private static bool Matches(string presented, string configured)
    {
        if (string.IsNullOrEmpty(presented))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presented),
            Encoding.UTF8.GetBytes(configured));
    }
}
