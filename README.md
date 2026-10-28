# Tudot — 本地相册管理器

当前版本：**v1.2.2**。发布记录见 [CHANGELOG.md](CHANGELOG.md)。完整程序下载：[GitHub Release](https://github.com/ZzTen0/Tudot/releases/tag/v1.2.2) / [Gitee Release](https://gitee.com/zzten00/tudot/releases/tag/v1.2.2)。

一款纯本地的 Windows 相册管理软件，按「分类 / 创作者 / 相册」三级结构整理图片与视频，数据与文件完全由本机掌控。

## 功能特性

### 组织与浏览
- **三级结构**：分类 → 创作者 → 相册；未分类创作者自动归入「无分类」节点，分类默认展开
- **左侧目录树**：可折叠，节点支持拖拽调整归类（普通拖放仅改变逻辑关系，不动磁盘文件）；侧边栏宽度可拖动调整
- **主页**：面包屑导航；相册网格 / 创作者卡片两种内容模式；支持分页或全部浏览，每页相册数可配置（8–96）
- **相册计数**：角标显示 `xxP+xxV`（图片数 + 视频数），缺省项自动隐藏
- **收藏**：相册收藏页 + 网址收藏页
- **排序**：按名称（自然序）/ 日期 / 大小

### 导入整理
- **拖放导入**：点击导入弹出拖放窗口，可直接从资源管理器拖入文件夹，或点击区域走系统文件夹选择
- 批量导入文件夹：递归扫描源目录，图片 / 视频平铺提取到相册根目录，其余文件移入「非视图文件」二级目录；支持导入时重命名相册（非空、非法字符、重名校验）、随时取消
- **批量统一命名**：多文件夹导入时自动提取相似元素为标签（括号配对整体识别），以前缀 / 分隔符 / 原编号 / 后缀 token 自由组合（可拖拽排序、标签包裹形态切换），实时模板预览；支持同名自动编号、保留原编号、全部保留原命名
- **同步重命名相册内文件**：整理入库时可按 token 组合（相册后缀 + 分文件夹 + 分隔符 + 编号）同步重命名内含数字编号的媒体文件，编号位数可切换，保持原排序
- **整理到库**：物理移动到 `库路径/分类/创作者/相册` 结构
- **仅添加**：文件保留原位置，仅建立索引；外部相册为只读模式（蓝色「仅添加」标识），后续可一键「整理入库」

### 相册详情
- 紧凑模式（原图比例）/ 网格模式（固定比例）切换，选择持久化
- **虚拟化网格**：仅可见项创建容器并加载缩略图，千级大相册流畅滚动
- 头部工具栏：排序、打开文件夹、刷新索引、多选
- 多选模式：全选、批量重命名（`IMG_####` 等占位符）、批量删除（可选同时删除源文件）
- 封面：优先第一张图片，无图片时用视频（带 ▶ 播放标记）；支持手动指定视频封面

### 图片查看器
- 应用内大图查看：方向键 / 点击两侧 / 滚轮翻页，Ctrl+滚轮、+/− 缩放，0 键恢复 100%
- 控件静止 2 秒自动隐藏，移动鼠标重新显示
- 可在设置中切换为系统默认查看器

### 缩略图
- 真实磁盘缓存（路径可自定义），详情页虚拟化加载
- 视频缩略图：优先 FFmpeg，缺失时回退 Windows Shell
- 设置中一键清除缓存（保留创作者封面缓存）

### 其他
- 主界面排序支持日期、名称、图片数量、创作者，并可独立选择正序或倒序（初始日期倒序）。切换字段时保留所选方向，筛选及分页沿用同一顺序。
- 首页多选：收藏所选 / 批量删除所选；支持 iOS 式划选
- 创作者管理：编辑信息、方形头像缩略图
- 个性化设置：三类缩略图大小滑块（相册卡片 / 创作者 / 图片）、每页数量、库存储路径、缓存路径
- 安全删除：所有删除均有确认对话框，可选「仅删除索引」或「同时删除源文件」；文件操作失败弹窗提示
- 全局异常捕获，写入应用目录 `tudot_error.log`

## 软件更新与发布

**v1.2.2 是新更新协议的起点，本次两个仓库只发布完整 Windows x64 程序包。旧版用户下载 `Tudot-1.2.2-win-x64.zip`，解压到独立目录后运行 `Tudot.exe`；后续版本才发布离线更新包和在线清单。** 原数据库仍保存在同一用户目录，勿将旧程序目录或媒体文件夹作为解压目标。

默认从官方 [GitHub Release](https://github.com/ZzTen0/Tudot/releases) 获取更新，可在设置切换到本地发布目录；也可手动安装下载的更新包。不需要自行部署服务器，不会在日常启动时编译源码。

- 日常安装：`%LocalAppData%\Programs\Tudot\Tudot.exe`，桌面快捷方式直接运行该 exe。
- 默认更新源：`ZzTen0/Tudot` 的最新正式 GitHub Release，每 15 分钟后台检查；预发布和缺少兼容资产的旧 Release 不用于更新。本地来源每分钟检查一次，可选择含有 `latest.json` 的目录（开发时默认生成在 `publish/local-updates/`）。网络出错不自动切换来源。
- 设置 → 软件更新可选择「有更新时自动更新」「有更新时提醒用户更新」（默认）或「不更新」，点击「确定」保存生效。自动模式准备并校验新包后重启；打开子窗口时延后处理。提醒模式由用户确认重启，同一构建本次运行只提醒一次；不更新模式停止检查和准备更新包。
- 设置中显示当前构建、更新来源，并提供「检查更新」；手动检查只显示结果，保存并关闭设置后按所选模式处理。更新偏好保存在 `%LocalAppData%\Tudot\update-preferences.json`，更新或回滚安装包时保留。
- 「安装本地更新包…」选择从 Release 下载的 `.tudotupdate` 文件，验证大小、SHA-256 和构建号后，再确认重启。这是主动操作，即使选择「不更新」也可使用；不改变自动检查模式或来源。普通完整程序 ZIP 不能当作离线更新包导入。
- 确认后由独立更新器等待程序退出，再备份旧程序、替换安装目录、重启并等待首屏确认。新程序无法启动时自动恢复旧程序。
- 数据库仍在 `%LocalAppData%\Tudot\albums.db`，图片/视频仍留在现有位置，不随程序替换。默认 `Thumbs` 缓存随更新保留。

首次安装（已有日常安装时不要重复使用 `-Install`）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Publish-LocalUpdate.ps1 -Install
```

以后修改代码并发布更新包：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Publish-LocalUpdate.ps1 -Notes "本次改动说明"
```

启用更新时，应用首次渲染后后台检查，此后按来源间隔检查。默认提醒模式下，选择取消后仍可继续使用当前构建，下次启动再提醒，也可在设置中检查后点击「确定」重新处理。本地目录优先使用用户设置，旧安装的 `update-source.json` 仅作兼容默认值。

**升级兼容边界：只有加入本次功能后的版本支持 GitHub 更新和 `.tudotupdate` 导入。旧版本先通过原本地更新源升级一次，或下载完整程序包后运行。旧偏好的更新模式保留，缺失的来源字段默认 GitHub。**

在线检查通过 HTTPS 获取官方仓库的 Release 清单，下载只允许 GitHub 官方资产域名，并校验内层程序包 SHA-256。离线包没有发布者数字签名，只应安装官方或你信任的文件。旧安装备份和更新缓存暂不自动删除；更改数据库结构的未来更新需另行设计兼容迁移和数据备份。

### GitHub Release 发布流程

发布具体版本前，在 `CHANGELOG.md` 按项目规则追加版本、日期、上一版本和主要改动，并同步本 README。提交代码后，创建并推送 `v主版本.次版本.补丁` 标签到 `github` 远程；`.github/workflows/release.yml` 会测试和构建，先创建草稿并上传全部资产，成功后公开正式 Release：

| Release 资产 | 用途 |
| --- | --- |
| `Tudot-构建号-win-x64.zip` | 自包含完整 Windows x64 程序，首次使用时解压到独立目录后运行 `Tudot.exe` |
| `Tudot-构建号-update.tudotupdate` | 完整离线更新包，供新版本设置页导入或在线更新下载 |
| `latest.json` | 在线清单，含程序包大小、SHA-256 和构建号 |

两个程序资产是同一构建；离线包内含 `manifest.json` 和完整 `payload.zip`，不是差分补丁。正式程序版本由发布标签传给构建，更新先后仍按单调递增的构建号比较。CI 不会改写本机桌面快捷方式。本地生成上述三个资产可执行原发布脚本；要指定正式版本，传入 `-Version <版本号>`，CI 另加 `-SkipShortcut`。

流程文件和本地产物生成不等于已经发布到 GitHub；需要提交并推送符合要求的版本标签，且仓库 Actions 具有发布权限。`v1.2.2` 为本次引导版本，明确排除自动流程，手动向两个仓库发布同一完整程序包；后续标签按上述流程生成完整资产。

验证命令：

```powershell
dotnet run --project Tests/UpdateChecks/UpdateChecks.csproj
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Test-LocalUpdate.ps1
```

## 技术栈

- WPF + .NET 8（仅 Windows）
- [HandyControl](https://github.com/HandyOrg/HandyControl) 3.5.1 UI 组件
- [VirtualizingWrapPanel](https://github.com/sbaeumlisberger/virtualizing-wrappanel) 2.5.4 虚拟化面板
- Microsoft.Data.Sqlite 本地数据库（`%LocalAppData%\Tudot\albums.db`）
- 无边框自定义窗口 + 现代化对话框 UI

## 构建与运行

```bash
dotnet run --project Tudot
```

## 发布打包

```bash
dotnet publish Tudot/Tudot.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true \
  -o publish
```

生成单文件自包含 exe（无需安装 .NET 运行时），解压即用。

## 目录结构

```
Tudot/
├── Models/         # 实体（Album / Creator / Category / ImageFile / Bookmark / TreeNode）
├── ViewModels/     # MainViewModel（数据加载、导入、拖放、设置持久化）
├── Views/          # 页面与对话框（主页/创作者/收藏/相册详情/查看器/导入/设置等）
├── Services/       # DatabaseService / FileService / ThumbnailCache / VideoThumbnailService
├── Converters/     # XAML 值转换器（含异步缩略图转换器）
└── Assets/         # 应用图标
```

## 许可证

[MIT License](LICENSE)
