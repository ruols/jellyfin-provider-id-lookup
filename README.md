# Provider ID Lookup

为 **Jellyfin 12.2.0 测试版 / .NET 10** 提供 Emby `AnyProviderIdEquals` 精确查询兼容。安装包见 [GitHub Releases](https://github.com/ruols/jellyfin-provider-id-lookup/releases)。

## 目标环境

- GitHub 账号及仓库：`ruols/jellyfin-provider-id-lookup`。
- 镜像：`nyanmisaka/jellyfin:261006-amd64-testing`，linux/amd64。
- 固定镜像 digest：`sha256:d2f219cd552aaf7df74846b80f99bdcd2e8f5679ead305ab06c920685a4193b8`。
- 主程序及 MediaBrowser.Controller 程序集版本：`12.2.0.0`（Jellyfin.Api 自身为 `1.0.0.0`，不能据此判断服务端版本）；运行配置：`net10.0`、Microsoft.NETCore.App / Microsoft.AspNetCore.App `10.0.12`。
- 编译时直接引用固定镜像中的 DLL，不用其他版本的 NuGet Jellyfin SDK 替代。锁定信息见 [image.lock.json](image.lock.json)。升级镜像后须重新核实接口并验证。

## 查询方式

支持原生 `ItemsController.GetItems` 和旧版 `GetItemsByUserIdLegacy`，包括 Jellyfin 原生路由提供的前缀别名：

```http
GET /Items?AnyProviderIdEquals=Tmdb.575265&Recursive=true&IncludeItemTypes=Movie
GET /Users/{userId}/Items?AnyProviderIdEquals=Tmdb.575265,Imdb.tt1630029&Recursive=true
GET /Items?AnyProviderIdEquals=Tmdb.1,Tmdb.2&IncludeItemTypes=Series
```

认证仍使用 Jellyfin 原生会话或 API Key。以上示例未包含令牌，不应把令牌写进仓库。

- 多个 `Provider.Id` 按 **OR** 匹配；同一 provider 可以给多个 ID，重复查询参数也支持。TMDB、IMDb、TVDB 名称忽略大小写并归一化；其他 provider 名称按原样传给原生查询。
- 以第一个 `.` 分割供应商名称与 ID；空 ID、无效格式、超过 16 对或 2048 字符返回 400。
- 原生 `InternalItemsQuery.HasAnyProviderIds` → `ILibraryManager.GetItemIds`，只查询 ID；不建独立索引、映射缓存或数据库，不扫描影库，不请求 TMDB，不监听 WebSocket，不定时刷新。
- 用户查询先通过 `ConfigureUserAccess` 配置原生影库权限，再添加父级限制。用户不能指定其他用户（原生管理员规则除外）；无用户上下文的原生 API Key 保持全库权限，因此不能拿 API Key 代替受限用户做权限验收。
- 结果与原有 `Ids` 取交集，再交还原生控制器处理类型、其他筛选、分页、排序及 DTO。未匹配直接返回空 `QueryResult`，避免空 `Ids` 变成全库查询。
- 显式 `ParentId` 也限制第一阶段查询：`Recursive=false` 查直接子项，否则用原生祖先 ID 查询。虚拟视图、合集和播放列表的成员语义仍需真实服务器测试，未宣称完整支持。
- 对电影、电视剧、季、单集按各条目**自身保存的外部 ID** 精确匹配，不把电视剧 TMDB ID 推断为季/集 ID；季/集通常通过原生 ParentId 浏览。不同媒体类型可能有相同数字 ID，客户端应给 `IncludeItemTypes`。
- 单次最多接受 4096 个候选条目，超过返回 422，不静默截断或给出错误总数。
- 本目标原生 `SearchTerm` 会将搜索命中与 `Ids` 合并。因此 `SearchTerm` 与本参数同时使用返回 400。普通不带本参数的搜索完全走原生逻辑。
- 最终响应若包含候选集合外的 ID，则阻断并返回 502；内部查询异常不会降级为全库查询。

插件仅参与上述 Items 查询，不代理视频、图片、认证或播放请求。没有零开销、性能提升或完整 Emby API 兼容的承诺。

## 编译与自动测试

需要 .NET 10 SDK、Python 3.9+ 和网络连接。官方镜像只下载服务器文件层，不启动镜像，不访问现有 Jellyfin 数据卷。

```sh
python3 scripts/fetch_refs.py
dotnet test tests/ProviderIdLookup.Tests -c Release --logger trx --results-directory TestResults
python3 scripts/package.py
```

`fetch_refs.py` 校验镜像 manifest 和服务器层 SHA-256，并将 DLL 校验记录写入 `refs/provenance.json`。`refs/`、构建输出和测试结果均不入 Git。`refs/` 非空时停止，避免覆盖用户提取的真实容器引用。

若需从现有容器提取依赖，在 **Ubuntu 主机**的本项目目录操作；仅复制 DLL 和运行配置，不修改容器：

```sh
mkdir refs
docker cp Jellyfin:/jellyfin/. /tmp/jellyfin-provider-id-server-copy
find /tmp/jellyfin-provider-id-server-copy -maxdepth 1 -type f \( -name '*.dll' -o -name 'jellyfin.runtimeconfig.json' \) -exec cp {} refs/ \;
```

与锁定镜像不一致的引用不能继承本项目已有的验证结论。不要把 `refs/` 的原生 Jellyfin DLL 安装进插件目录。

自动测试包含解析、双 Items 入口、原有 Ids 交集、空匹配、认证和用户规则、原生权限配置顺序、候选上限及响应范围检查；另以临时 SQLite 数据库执行镜像中真实 `WhereHasAnyProviderIds` 查询，检验 OR、精确匹配和元数据更新后立即查询。模拟服务测试不能证明生产服务器权限行为，详见 [验收清单](docs/ACCEPTANCE.md)。

GitHub Actions 的 **Build and test pinned Jellyfin target** 在 main push、PR 和手动运行时执行提取、编译、测试、打包，上传候选 ZIP、清单、校验值及测试结果。不会自动发布 Release。

## 安装与现有 Go 反代

**保留 Go 容器、当前端口和网络配置。** 首次只在原生 Jellyfin 测试入口验证（原对话约定：8096 为 Go 反代，8097 为 Jellyfin；请以实际映射为准）。本项目没有自动部署、重启或切换端口脚本。

构建产物：`artifacts/ProviderIdLookup-0.1.0.0.zip`，内部仅有 `Jellyfin.Plugin.ProviderIdLookup.dll`。手动测试时将 DLL 放入持久化配置的 `/config/plugins/ProviderIdLookup/`，维护窗口内重启 Jellyfin并检查加载日志。安装前应确认没有同 GUID 旧副本；不要让多个目录加载同一插件。

若出现启动异常，停止 Jellyfin，将该插件目录整体移出 `/config/plugins/`，再启动。保留现有 Go 反代可以继续回到原入口；不要删除数据库、配置卷或其他插件。

只有 [验收清单](docs/ACCEPTANCE.md) 中的权限、电影、电视剧及客户端测试均完成，才另行决定是否停用 Go 反代并切换端口。自动测试通过不等同于用户验收稳定。


## Jellyfin 网页插件源

在 Jellyfin 控制台的「插件 → 存储库」添加以下固定地址，之后可从插件目录选择版本：

```text
https://raw.githubusercontent.com/ruols/jellyfin-provider-id-lookup/main/manifest.json
```

根目录清单收录此公开仓库已发布且具备可安装 ZIP 的版本，按版本号从新到旧排列。建议安装最新版。ABI 是最低版本门槛，不能替代对具体 Jellyfin 镜像的兼容验证。

发布新的 Release 后，运行 `python3 scripts/sync_catalog.py`，检查生成的根清单并提交到 `main`。脚本核对每个发布 ZIP 的地址、内容和 MD5。Release 附带的单版本清单供打包与校验使用，客户端始终添加上面的固定地址。

## 参考与边界

- [Jellyfin 官方插件说明](https://jellyfin.org/docs/general/server/plugins/)
- [官方插件模板](https://github.com/jellyfin/jellyfin-plugin-template)
- [原生 ItemsController](https://github.com/jellyfin/jellyfin/blob/master/Jellyfin.Api/Controllers/ItemsController.cs)
- [原生查询结构](https://github.com/jellyfin/jellyfin/blob/master/MediaBrowser.Controller/Entities/InternalItemsQuery.cs)

在线 master 会变化；本项目接口核实以锁定镜像实际程序集及其 XML 为准，见 [接口与验证记录](docs/VERIFICATION.md)。
