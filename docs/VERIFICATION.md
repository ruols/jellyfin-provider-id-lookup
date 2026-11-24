# 接口与验证记录

日期：2026-11-24（Asia/Shanghai）。本记录列出实际完成的本地验证；GitHub Actions 是否运行成功以仓库 Actions 页面为准，不能用此记录代替。

## 版本证据

从 Docker Registry 按 digest 读取 `nyanmisaka/jellyfin:261006-amd64-testing`，只提取服务器层；未启动镜像，也未读取、修改生产数据库。

| 项目 | 实测 |
| --- | --- |
| 镜像 digest | `sha256:d2f219cd552aaf7df74846b80f99bdcd2e8f5679ead305ab06c920685a4193b8` |
| 服务器层 digest | `sha256:6fe37a28bc9207219f17635123602a9cff87fe6af885aadc526ec84a291bdc0c` |
| jellyfin / MediaBrowser.Controller / Jellyfin.Database.Implementations 程序集版本 | `12.2.0.0` |
| Jellyfin.Api 程序集及文件版本 | `1.0.0.0`；该库单独的版本不能用于判定服务器版本 |
| runtimeconfig | `net10.0`，.NET / ASP.NET Core `10.0.12` |
| EF Core / SQLite 托管依赖 | `10.0.11`（来自镜像 deps 文件，测试使用对应包） |

关键 DLL SHA-256：

```text
626f52a1a21faece89d6401b00367ff98b175ba4b4616023ce09b64d800fe8c8  MediaBrowser.Controller.dll
f31a79e6a5b3728cea5b503c6f0ad963049a909e6ee467e0e0e9167545652c4e  Jellyfin.Api.dll
4362c3f3296934824b67b1ddd54f6043370c0c0f938eeca095cde559258951e7  Jellyfin.Database.Implementations.dll
```

## 实际接口审查

直接审查目标程序集元数据、XML 及反编译结果，而非仅参考在线 master：

- `IPluginServiceRegistrator.RegisterServices(IServiceCollection, IServerApplicationHost)` 存在。
- 无配置的 `BasePlugin` 使用无参构造；没有旧示例假定的双参数构造。
- `ILibraryManager.GetItemIds(InternalItemsQuery)` 返回 `IReadOnlyList<Guid>`。
- `ILibraryManager.ConfigureUserAccess(InternalItemsQuery, User)` 存在。
- `InternalItemsQuery.HasAnyProviderId` 为 `Dictionary<string, string>`；`HasAnyProviderIds` 为 `Dictionary<string, string[]>`，后者支持同 provider 的多个值。
- `ItemsController.GetItems` 和 `GetItemsByUserIdLegacy` 均有名为 `ids`、类型为 `Guid[]` 的参数，支持全局 action filter。
- `RequestHelpers.GetUserId` 是 internal；插件使用原生 claims 扩展并复现该版本的短小用户选择规则（不同用户要求管理员）。
- 原生 `AddUserToQuery` 在已有 ParentId 或 ItemIds 等范围时会跳过补充可访问影库范围，因此插件必须先调用 `ConfigureUserAccess`，且不能把客户端 Ids 直接塞入候选查询。
- 原生空 `Ids` 表示不按 ID 限制；插件零候选时直接返回空结果。
- 原生 `SearchTerm` 与 Ids 是 UNION；插件拒绝组合请求。
- 原生控制器会清空 query.Parent；插件在第一阶段单独应用显式 ParentId 限制。

## 已执行的检查

- `scripts/fetch_refs.py`：在独立临时目录实际下载、校验并提取成功，415 个 DLL；生成来源及逐 DLL SHA-256 记录。
- Release 编译：成功，0 警告、0 错误。引用由上述脚本提取的目标 DLL。
- 自动测试：**35 通过，0 失败，0 跳过**，含真实 SQLite 原生 Provider ID 表达式测试。
- 本地执行环境：macOS arm64，.NET SDK `10.0.404`，测试运行时 `.NET 10.0.15`。这是跨平台托管代码编译及自动测试，并非 Ubuntu / .NET 10.0.12 完整服务端运行验收。
- Python 脚本语法检查：通过。
- ZIP 检查：仅包含插件 DLL，压缩包完整性正常；manifest GUID、四段版本、ABI、ZIP MD5 校验一致；根清单无可安装版本。

首次增加版本契约测试时，将 `Jellyfin.Api` 自身版本误当成服务端版本，测试如实失败；核实实际主程序及 Controller 版本后改为分别断言，不跳过失败测试。最终上述 35 项均通过。

## 尚未验证

未安装到用户 Jellyfin；未重启服务器；未验证生产影库访问控制、真实电影电视剧/季集数据、虚拟视图、EPlayerX 聚合、播放或性能。仓库保持私有，未激活匿名 Web 安装清单。没有停用或修改 Go 反代。

发布或切换入口前按 [ACCEPTANCE.md](ACCEPTANCE.md) 验收。构建成功与自动测试通过，不等于设备或服务器验收稳定。
