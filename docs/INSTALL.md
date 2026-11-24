# 安装 0.1.0.0 测试版

适用于锁定的 `nyanmisaka/jellyfin:261006-amd64-testing`、Jellyfin 12.2.0 / net10.0。属于实验版，真实服务器权限及客户端兼容验收仍未完成。

1. 登录有权限的 GitHub 账号，在本私有仓库 Releases 中下载 `ProviderIdLookup-0.1.0.0.zip`。不要将整个源码 ZIP 当成插件。
2. 解压后应只有 `Jellyfin.Plugin.ProviderIdLookup.dll`。使用同一 Release 的 `SHA256SUMS` 校验 ZIP。
3. 将 DLL 复制到现有 Jellyfin 持久化配置下的 `/config/plugins/ProviderIdLookup/`。这里是容器内路径，应映射到实际配置卷。不要复制 `refs/`，不要覆盖其他插件目录。
4. 在维护窗口重启 Jellyfin，检查日志和控制台确认 **Provider ID Lookup 0.1.0.0** 正常加载。
5. 先从原生 Jellyfin 测试入口检查存在及不存在的电影 ID，再测电视剧、季集、受限用户和 EPlayerX。按 [ACCEPTANCE.md](ACCEPTANCE.md) 逐项验收。

原对话的入口约定为 8096 → Go，8097 → 原生 Jellyfin，安装前以实际映射为准。**保留 Go 容器、8096/8097 端口配置和现有网络，不执行入口切换。**

若插件导致启动异常：停止 Jellyfin，将 `ProviderIdLookup` 整个目录移出 `/config/plugins/`，再启动服务。不要删除配置卷、数据库或其他插件。

这是下载 ZIP 后手动安装的版本。私有 GitHub Release 不能直接作为匿名 Web 插件目录；若要 Web 安装，可以保持源码私有，另设公开的 manifest + ZIP 分发位置，或另行选择公开整个仓库。
