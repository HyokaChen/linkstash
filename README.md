# linkstash

粘贴 URL → 抓取页面标题 → 翻译成中文 → 以 markdown 卡片收藏。

```
[Example Domain](https://example.com) => 示例域
[Hacker News](https://news.ycombinator.com) => 黑客新闻
```

自托管的单人书签收集服务，手机浏览器打开就能用，适合收藏各类 H5 页面。

## 功能

- **粘贴即收藏** — 输入 URL 自动抓取 `<title>`，翻译为中文，生成 markdown 卡片，支持一键复制
- **聚合页批量提取** — 勾选开关后从列表页提取全部外链，逐条抓标题并翻译，预览勾选后批量入库
- **收藏库** — SQLite 存储，支持搜索、分页、删除
- **翻译零密钥** — 使用 MyMemory 公开 API，无需注册；中文标题自动跳过，不消耗额度
- **密码保护** — 单密码 + Cookie 会话，公网部署只需反代加 TLS

## 快速开始

```bash
# 后端
cd server/Linkstash.Api
dotnet run

# 前端（另开终端）
cd web
npm install
npm run dev
```

开发期访问 <http://localhost:5173>，Vite 会把 `/api` 代理到后端 5000 端口。默认密码 `changeme`。

## 生产部署

```bash
cd web && npm run build              # 前端产物输出到 server/Linkstash.Api/wwwroot
cd ../server/Linkstash.Api
dotnet publish -c Release -o ../../publish
```

运行：

```bash
ASPNETCORE_URLS=http://127.0.0.1:5000 \
Auth__Password='你的强密码' \
Auth__CookieSecure=true \
ConnectionStrings__Sqlite="Data Source=/data/linkstash.db" \
./publish/Linkstash.Api
```

公网访问用 Nginx / Caddy 反代到 `127.0.0.1:5000` 并配置 TLS。

## 配置项

均可用环境变量覆盖，格式为 `Section__Key`。

| 配置项 | 默认值 | 说明 |
|---|---|---|
| `Auth:Password` | `changeme` | 登录密码，**生产必须覆盖** |
| `Auth:CookieSecure` | `false` | 生产设为 `true`（要求 HTTPS） |
| `ConnectionStrings:Sqlite` | `Data Source=linkstash.db` | SQLite 文件路径 |
| `Proxy:FetchUrl` | 空 | 抓取页面用的代理，**仅影响抓取标题，不影响翻译** |
| `Translate:ContactEmail` | 空 | MyMemory 联系邮箱，配置后每日额度 5,000 → 50,000 字符 |

翻译链路（MyMemory）实测直连可用，**无需配置代理**。仅当收藏的境外站点直连打不开时才需要 `Proxy:FetchUrl`。

## 技术栈

- **后端** .NET 10 / ASP.NET Core Minimal API、AngleSharp、SQLite、Refit
- **前端** Vue 3 / Vite / TypeScript / shadcn-vue (reka-nova) / vue-sonner

## 翻译说明

`TranslateService` 处理了 MyMemory 的三个实测边界：

1. **单条 500 字符上限** — 超限返回 403，标题按 500 字符截断
2. **不支持 `auto` 源语言** — `auto|zh-CN` 返回 403，改为本地语种识别（假名→ja、汉字→zh、西里尔→ru、其余→en）
3. **额度耗尽返回 `quotaFinished: true`** — 此时标记 `(翻译失败)`，避免"未翻译"与"已翻译"无法区分

中文标题直接返回原文，不请求 API、不消耗额度。

## 测试

```bash
dotnet test
```

覆盖：响应解析、语种识别、500 字符截断、额度耗尽、瞬时网络故障重试、邮箱参数传递、中文跳过。

## 已知限制

- **SPA 页面**：用 HttpClient + AngleSharp 静态抓取，不执行 JS。取不到标题时用 URL 末段兜底，卡片会标记"标题兜底"
- **单用户**：无多用户隔离，密码为单值
- **额度限制**：MyMemory 每日 50,000 字符（已配置邮箱），仅翻译非中文标题
