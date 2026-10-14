using System.Security.Cryptography;
using System.Text;

public partial class Program
{
    private static void ConfigureWebAccess(WebApplication app)
    {
        string? username = app.Configuration["WebAccess:Username"];
        string? password = app.Configuration["WebAccess:Password"];
        bool configured = !string.IsNullOrWhiteSpace(username) &&
            !string.IsNullOrWhiteSpace(password);

        if (!configured)
        {
            if (!string.IsNullOrEmpty(username) ||
                !string.IsNullOrEmpty(password) ||
                app.Configuration.GetValue<bool>("RENDER"))
            {
                throw new InvalidOperationException(
                    "Set both WebAccess__Username and WebAccess__Password for hosted access.");
            }

            app.Logger.LogWarning("Web authentication is disabled for local use.");
            return;
        }

        byte[] expected = SHA256.HashData(
            Encoding.UTF8.GetBytes("Basic " +
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"))));

        app.Use(async (context, next) =>
        {
            if (context.Request.Path == "/health")
            {
                await next(context);
                return;
            }

            byte[] supplied = SHA256.HashData(
                Encoding.UTF8.GetBytes(context.Request.Headers.Authorization.ToString()));

            if (!CryptographicOperations.FixedTimeEquals(expected, supplied))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = "Basic realm=\"Football AI\", charset=\"UTF-8\"";
                context.Response.Headers.CacheControl = "no-store";
                await context.Response.WriteAsync("Authentication required.");
                return;
            }

            context.Response.Headers.CacheControl = "no-store";
            await next(context);
        });
    }
}
