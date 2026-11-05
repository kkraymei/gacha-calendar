# Gacha Calendar · 0.1.0

追踪抽卡游戏卡池、活动与版本公告的桌面日历。

A desktop calendar for gacha banners, events and updates.

## 功能 / Features

- 多游戏、多区服公告聚合与重复条目合并 / Multi-game, multi-region announcements with deduplication.
- 时间线与列表视图，按类型筛选和搜索 / Timeline and list views with filters and search.
- 收藏、完成与隐藏标记 / Star, complete and hide entries.
- 自定义来源、自动刷新和本地缓存 / Custom sources, automatic refresh and local caching.
- ICS 日历导出，浅色与深色主题 / ICS export with light and dark themes.

基于 Avalonia 和 .NET 10；当前发布 Windows x64 安装包与便携包，其他平台需自行构建验证。

Built with Avalonia and .NET 10. This release provides Windows x64 setup and portable packages; other platforms require building and validation.

## 下载与安装 / Download and install

从 [Releases](https://github.com/kkraymei/gacha-calendar/releases) 下载 `GachaCalendar-0.1.0-win-x64-Setup.exe`，或解压便携包运行 `GachaCalendar.exe`。无需单独安装 .NET。

Download the setup executable from Releases, or extract the portable ZIP and run `GachaCalendar.exe`. No separate .NET installation is required.

## 构建 / Build

需要 .NET 10 SDK；生成安装程序还需要 Inno Setup 6。

Requires the .NET 10 SDK and, for the installer, Inno Setup 6.

```powershell
dotnet build src/GachaCalendar.App -c Release
./script/package_windows.ps1 -IsccPath 'C:/Program Files (x86)/Inno Setup 6/ISCC.exe'
```

干净发布目录为 `.release/0.1.0/`；打包脚本仅使用 `dotnet publish` 的新输出，不读取已有 `dist/`。

Release output goes to `.release/0.1.0/`. Packaging uses fresh `dotnet publish` output and never reads existing `dist/` contents.

## 数据与隐私 / Data and privacy

设置、个人标记与缓存保存在系统应用数据目录下的 `GachaCalendar` 文件夹中。源码与发布包不包含这些数据；`dist/`、本地构建结果和凭据文件均被 Git 忽略。刷新时会访问所配置的公告来源。

Settings, personal marks and caches are stored in the system application-data folder under `GachaCalendar`. They are excluded from source and release packages. Git ignores `dist/`, local build output and credential files. Refreshing contacts the configured announcement sources.

## 许可证 / License

[MIT](LICENSE)
