namespace Hiram.Api.Authentication;

public sealed class ApiKeyMiddleware
{
    private const string HeaderName = "X-Api-Key";
    private const string ProtectedPrefix = "/v1";

    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly IProblemDetailsService _problemDetails;
    private readonly ILogger<ApiKeyMiddleware> _logger;

    public ApiKeyMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        IProblemDetailsService problemDetails,
        ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _configuration = configuration;
        _problemDetails = problemDetails;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(ProtectedPrefix))
        {
            await _next(context);
            return;
        }

        var configuredKey = _configuration["Auth:DevApiKey"];
        if (string.IsNullOrEmpty(configuredKey))
        {
            _logger.LogError("Auth:DevApiKey is not configured; rejecting request to {Path}", context.Request.Path);
            await WriteUnauthorized(context, "API key authentication is not configured.");
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var provided)
            || !FixedTimeEquals(provided.ToString(), configuredKey))
        {
            await WriteUnauthorized(context, "A valid X-Api-Key header is required.");
            return;
        }

        await _next(context);
    }

    private async Task WriteUnauthorized(HttpContext context, string detail)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await _problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails =
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Detail = detail
            }
        });
    }

    private static bool FixedTimeEquals(string provided, string expected)
    {
        // Compare without an early exit on first mismatch so a wrong key cannot be inferred by timing.
        if (provided.Length != expected.Length)
            return false;

        var difference = 0;
        for (var i = 0; i < provided.Length; i++)
            difference |= provided[i] ^ expected[i];

        return difference == 0;
    }
}
