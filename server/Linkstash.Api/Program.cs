using System.Net;
using Linkstash.Api.Auth;
using Linkstash.Api.Collect;
using Linkstash.Api.Fetch;
using Linkstash.Api.Import;
using Linkstash.Api.Resolve;
using Linkstash.Api.Store;
using Linkstash.Api.Translate;
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

// 抓取与翻译使用不同的代理配置：
// - Proxy:FetchUrl  仅作用于抓取页面（部分站点在境内直连会挂起/超时）
// - 翻译（MyMemory）实测直连可用，不走代理，也不读取 HTTPS_PROXY 环境变量，
//   避免系统代理设置意外改变翻译链路行为。
var proxy = builder.Configuration.GetSection("Proxy").Get<ProxyOptions>() ?? new ProxyOptions();
var fetchProxyStr = string.IsNullOrWhiteSpace(proxy.FetchUrl) ? "" : proxy.FetchUrl;
HttpMessageHandler FetchHandler() =>
    string.IsNullOrWhiteSpace(fetchProxyStr)
        ? new HttpClientHandler()
        : new HttpClientHandler { Proxy = new WebProxy(fetchProxyStr), UseProxy = true };

builder
    .Services.AddHttpClient<PageFetcher>(c =>
    {
        c.Timeout = TimeSpan.FromSeconds(30);
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (linkstash/1.0)");
    })
    .ConfigurePrimaryHttpMessageHandler(FetchHandler);

builder
    .Services.AddHttpClient<IMyMemoryApi>(c =>
        c.BaseAddress = new Uri("https://api.mymemory.translated.net")
    )
    .AddTypedClient(c => RestService.For<IMyMemoryApi>(c));

// slug 与检索解析：不套用 Proxy:FetchUrl——抓取代理指向收藏目标站点，
// 与检索出口无关，避免境外检索 API 被强制走同一个代理而失败。
builder.Services.AddHttpClient<SlugResolver>(c =>
    {
        c.BaseAddress = new Uri("https://api.github.com");
        c.Timeout = TimeSpan.FromSeconds(10);
        c.DefaultRequestHeaders.UserAgent.ParseAdd("linkstash/1.0");
    }
)
.ConfigurePrimaryHttpMessageHandler(FetchHandler);

builder.Services.AddHttpClient<SearchResolver>(c =>
    {
        c.Timeout = TimeSpan.FromSeconds(10);
        c.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 "
                + "(KHTML, like Gecko) Chrome/130.0 Safari/537.36"
        );
    }
)
.ConfigurePrimaryHttpMessageHandler(FetchHandler);
var translateOptions =
    builder.Configuration.GetSection("Translate").Get<TranslateOptions>() ?? new TranslateOptions();
builder.Services.AddSingleton(translateOptions);
builder.Services.AddScoped<TranslateService>();

builder
    .Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "linkstash_auth";
        o.LoginPath = "/api/auth/login";
        o.Cookie.SecurePolicy = auth.CookieSecure
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
        o.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(o =>
        o.AddDefaultPolicy(p =>
            p.WithOrigins("http://localhost:5173")
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()
        )
    );
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
app.MapImportEndpoints();

var wwwroot = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
if (Directory.Exists(wwwroot))
{
    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(wwwroot) });
    app.MapFallbackToFile("index.html");
}

app.Run();
