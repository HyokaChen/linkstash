using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Linkstash.Api.Auth;

public record LoginRequest(string Password);

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app, AuthOptions options)
    {
        app.MapPost("/api/auth/login", async (HttpContext ctx, LoginRequest req) =>
        {
            if (string.IsNullOrEmpty(req.Password) || string.IsNullOrEmpty(options.Password) ||
                !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(req.Password),
                    Encoding.UTF8.GetBytes(options.Password)))
            {
                return Results.Json(new { error = "invalid password" }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var claims = new[] { new Claim(ClaimTypes.Name, "linkstash") };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await ctx.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
                });
            return Results.Json(new { ok = true });
        }).AllowAnonymous();

        app.MapPost("/api/auth/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Json(new { ok = true });
        }).AllowAnonymous();
    }
}
