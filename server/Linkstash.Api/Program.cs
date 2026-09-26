using Linkstash.Api.Auth;
using Linkstash.Api.Collect;
using Linkstash.Api.Fetch;
using Linkstash.Api.Store;
using Linkstash.Api.Translate;
using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.FileProviders;
using Refit;

var builder = WebApplication.CreateBuilder(args);

var auth = builder.Configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
builder.Services.AddSingleton(auth);

var conn = builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=linkstash.db";
var store = new SqliteCollectionStore(conn);
await store.InitAsync();
builder.Services.AddSingleton<ICollectionStore>(store);

var proxy = builder.Configuration.GetSection("Proxy").Get<ProxyOptions>() ?? new ProxyOptions();
var proxyStr = string.IsNullOrWhiteSpace(proxy.Url) ? Environment.GetEnvironmentVariable("HTTPS_PROXY") ?? Environment.GetEnvironmentVariable("https_proxy") ?? "" : proxy.Url;
HttpClientHandler? MakeHandler() => string.IsNullOrWhiteSpace(proxyStr)
    ? null
    : new HttpClientHandler { Proxy = new WebProxy(proxyStr), UseProxy = true };

builder.Services.AddHttpClient<PageFetcher>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(15);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (linkstash/1.0)");
}).ConfigurePrimaryHttpMessageHandler(() => MakeHandler() as HttpMessageHandler ?? new HttpClientHandler());

builder.Services.AddHttpClient<IMyMemoryApi>(c => c.BaseAddress = new Uri("https://api.mymemory.translated.net"))
    .AddTypedClient(c => RestService.For<IMyMemoryApi>(c))
    .ConfigurePrimaryHttpMessageHandler(() => MakeHandler() as HttpMessageHandler ?? new HttpClientHandler());
var translateOptions = builder.Configuration.GetSection("Translate").Get<TranslateOptions>() ?? new TranslateOptions();
builder.Services.AddSingleton(translateOptions);
builder.Services.AddScoped<TranslateService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "linkstash_auth";
        o.LoginPath = "/api/auth/login";
        o.Cookie.SecurePolicy = auth.CookieSecure ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        o.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
        p.WithOrigins("http://localhost:5173")
         .AllowAnyHeader()
         .AllowAnyMethod()
         .AllowCredentials()));
}

var app = builder.Build();

if (builder.Environment.IsDevelopment())
{
    app.UseCors();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints(auth);
app.MapCollectEndpoints();

var wwwroot = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
if (Directory.Exists(wwwroot))
{
    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(wwwroot)
    });
    app.MapFallbackToFile("index.html");
}

app.Run();
