# Tu. — 本地相册管理器

一款纯本地的 Windows 相册管理软件，按「创作者 / 相册」两级结构整理图片与视频。

## 功能特性

- **相册网格主页**：按网格展示所有相册，支持按创作者筛选、按日期 / 名称 / 数量 / 创作者排序
- **导入整理**：批量导入文件夹，自动按 `库路径/创作者/相册` 结构整理；支持指定创作者批量归类，也支持「仅添加不移动文件」模式（文件保留原位置，仅入库）
- **创作者管理**：自由编辑创作者信息、自定义方形缩略图头像，点击创作者查看其全部相册
- **相册详情**：
  - 紧凑模式（按原图比例显示、自动补空档）/ 网格模式（固定比例整齐排列）一键切换
  - 缩略图真实磁盘缓存 + 降采样解码，大图秒开
  - 视频缩略图（优先 FFmpeg，无则回退 Windows Shell 缩略图）
  - 右键菜单：打开 / 重命名 / 删除 / 属性
  - 相册编辑：修改名称、切换创作者（可同步移动文件夹）、指定封面、添加图片
- **应用内图片查看器**：大图 + 翻页（方向键 / 点击两侧 / 滚轮），Ctrl+滚轮或 +/- 缩放；也可在设置中切换为系统默认查看器
- **批量重命名**：相册内图片按命名规则批量编号
- **网址收藏**：记录常用图片网址
- **个性化设置**：相册卡片 / 创作者 / 图片缩略图大小滑块实时调整；库路径与缩略图缓存路径可自定义
- **安全删除**：所有删除操作均有确认提示，可选「仅删除程序内索引」或「同时删除源文件」

## 技术栈

- WPF + .NET 8（仅 Windows）
- [HandyControl](https://github.com/HandyOrg/HandyControl) 3.5.1 UI 组件
- Microsoft.Data.Sqlite 本地数据库（`%LocalAppData%\AlbumManager\albums.db`）
- 无边框自定义窗口 + 现代化对话框 UI

## 构建与运行

```bash
cd AlbumManager
dotnet run
```

## 发布打包

```bash
dotnet publish AlbumManager/AlbumManager.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -o publish/AlbumManager
```

生成单文件自包含 exe（无需安装 .NET 运行时），解压即用。

## 目录结构

```
AlbumManager/
├── Models/         # 实体（Album / Creator / ImageFile / Bookmark）
├── ViewModels/     # MainViewModel（数据加载、导入、设置持久化）
├── Views/          # 页面与对话框（主页/创作者/收藏/相册详情/查看器等）
├── Services/       # DatabaseService / FileService / 缩略图缓存 / 视频缩略图
├── Converters/     # XAML 值转换器（含异步缩略图转换器）
└── Assets/         # 应用图标
```

## 许可证

[MIT License](LICENSE)
