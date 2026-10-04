using System.IO;
using System.Runtime.InteropServices;
using Tudot.Models;

namespace Tudot.Services;

/// <summary>文件名自然排序比较器（img2 排在 img10 之前），与 Windows 资源管理器一致</summary>
public class NaturalStringComparer : IComparer<string>
{
    public static readonly NaturalStringComparer Instance = new();

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string psz1, string psz2);

    public int Compare(string? x, string? y) => StrCmpLogicalW(x ?? string.Empty, y ?? string.Empty);
}

public class FileService
{
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff" };
    private static readonly string[] VideoExtensions = { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm" };

    public List<ImageFile> ScanFolder(string folderPath)
    {
        var files = new List<ImageFile>();
        var directoryInfo = new DirectoryInfo(folderPath);

        if (!directoryInfo.Exists) return files;

        var allFiles = directoryInfo.GetFiles("*", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f.Name)
            .ToList();

        int sortOrder = 0;
        foreach (var file in allFiles)
        {
            var ext = file.Extension.ToLowerInvariant();
            var fileType = "other";

            if (ImageExtensions.Contains(ext)) fileType = "image";
            else if (VideoExtensions.Contains(ext)) fileType = "video";

            files.Add(new ImageFile
            {
                FileName = file.Name,
                FilePath = file.FullName,
                FileType = fileType,
                FileSize = file.Length,
                SortOrder = sortOrder++
            });
        }

        return files;
    }

    public List<ImageFile> BatchRename(List<ImageFile> files, string pattern)
    {
        var renamed = new List<ImageFile>();
        int counter = 1;

        foreach (var file in files.OrderBy(f => f.SortOrder))
        {
            var directory = Path.GetDirectoryName(file.FilePath) ?? string.Empty;
            var extension = Path.GetExtension(file.FileName);

            // 替换命名模式中的占位符
            var newName = pattern
                .Replace("####", counter.ToString("D4"))
                .Replace("###", counter.ToString("D3"))
                .Replace("##", counter.ToString("D2"))
                .Replace("#", counter.ToString())
                .Replace("{name}", Path.GetFileNameWithoutExtension(file.FileName))
                .Replace("{type}", file.FileType)
                .Replace("{ext}", extension.TrimStart('.'));

            if (!newName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                newName += extension;

            var newPath = Path.Combine(directory, newName);

            // 如果文件名冲突，添加序号
            int conflict = 1;
            while (File.Exists(newPath) && newPath != file.FilePath)
            {
                newName = $"{Path.GetFileNameWithoutExtension(newName)}_{conflict}{extension}";
                newPath = Path.Combine(directory, newName);
                conflict++;
            }

            // 执行重命名
            if (newPath != file.FilePath)
            {
                File.Move(file.FilePath, newPath);
                file.FileName = newName;
                file.FilePath = newPath;
            }

            renamed.Add(file);
            counter++;
        }

        return renamed;
    }

    public string GetThumbnailPath(string imagePath, int width = 300, int height = 300)
    {
        // 使用 Windows 缩略图缓存或生成缩略图
        // 这里简化处理，实际可以使用 ImageSharp 生成缩略图
        return imagePath;
    }

    /// <summary>递归扫描文件夹（含所有子目录），仅返回图片和视频文件</summary>
    public List<ImageFile> ScanMediaRecursive(string folderPath)
    {
        var files = new List<ImageFile>();
        var directoryInfo = new DirectoryInfo(folderPath);

        if (!directoryInfo.Exists) return files;

        var allFiles = directoryInfo.GetFiles("*", SearchOption.AllDirectories)
            .Where(f =>
            {
                var ext = f.Extension.ToLowerInvariant();
                return ImageExtensions.Contains(ext) || VideoExtensions.Contains(ext);
            })
            .OrderBy(f => f.FullName)
            .ToList();

        int sortOrder = 0;
        foreach (var file in allFiles)
        {
            var ext = file.Extension.ToLowerInvariant();
            files.Add(new ImageFile
            {
                FileName = file.Name,
                FilePath = file.FullName,
                FileType = ImageExtensions.Contains(ext) ? "image" : "video",
                FileSize = file.Length,
                SortOrder = sortOrder++
            });
        }

        return files;
    }

    /// <summary>
    /// 递归整理相册到 库路径/创作者/相册名：
    /// 图片和视频平铺提取到相册目录根部，其余文件移入二级目录「非视图文件」，
    /// 源目录清空后删除。返回整理后的路径；失败返回 null。
    /// </summary>
    public string? OrganizeAlbumRecursive(string sourcePath, string creatorName, string albumName, string libraryPath)
    {
        try
        {
            var targetBase = Path.Combine(libraryPath, creatorName);
            Directory.CreateDirectory(targetBase);

            var targetPath = Path.Combine(targetBase, albumName);

            // 已在目标位置则无需移动
            if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
                return targetPath;

            // 目标已存在则追加序号
            int conflict = 1;
            while (Directory.Exists(targetPath))
            {
                targetPath = Path.Combine(targetBase, $"{albumName}_{conflict}");
                conflict++;
            }
            Directory.CreateDirectory(targetPath);

            var othersDir = Path.Combine(targetPath, "非视图文件");
            var othersCreated = false;

            // 源目录下有多个二级目录时，提取的文件名加上二级目录名前缀，避免来源混淆
            var subDirs = Directory.GetDirectories(sourcePath);
            var prefixSubDir = subDirs.Length > 1;

            foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                var isMedia = ImageExtensions.Contains(ext) || VideoExtensions.Contains(ext);

                if (!isMedia && !othersCreated)
                {
                    Directory.CreateDirectory(othersDir);
                    othersCreated = true;
                }

                var destDir = isMedia ? targetPath : othersDir;
                var fileName = Path.GetFileName(file);

                // 文件位于二级目录（或更深层）时加前缀：二级目录名_原文件名
                if (prefixSubDir)
                {
                    var relative = Path.GetRelativePath(sourcePath, file);
                    var segments = relative.Split(Path.DirectorySeparatorChar);
                    if (segments.Length > 1)
                        fileName = $"{segments[0]}_{fileName}";
                }

                var destPath = Path.Combine(destDir, fileName);

                // 同名冲突追加序号
                int n = 1;
                while (File.Exists(destPath))
                {
                    destPath = Path.Combine(destDir,
                        $"{Path.GetFileNameWithoutExtension(fileName)}_{n}{Path.GetExtension(fileName)}");
                    n++;
                }

                File.Move(file, destPath);
            }

            // 删除已清空的子目录（深度优先）
            foreach (var dir in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length))
            {
                if (Directory.GetFiles(dir).Length == 0 && Directory.GetDirectories(dir).Length == 0)
                    Directory.Delete(dir);
            }

            // 源目录已空则删除
            if (Directory.Exists(sourcePath)
                && Directory.GetFiles(sourcePath).Length == 0
                && Directory.GetDirectories(sourcePath).Length == 0)
            {
                Directory.Delete(sourcePath);
            }

            return targetPath;
        }
        catch
        {
            return null;
        }
    }

    public string? OrganizeAlbums(string sourcePath, string creatorName, string libraryPath)
    {
        // 将相册整理到 库路径/创作者/相册xx 结构，返回整理后的路径；失败返回 null
        try
        {
            var targetBase = Path.Combine(libraryPath, creatorName);
            Directory.CreateDirectory(targetBase);

            var folderName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar));
            var targetPath = Path.Combine(targetBase, folderName);

            // 已在目标位置则无需移动
            if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
                return targetPath;

            // 目标已存在则追加序号
            int conflict = 1;
            while (Directory.Exists(targetPath))
            {
                targetPath = Path.Combine(targetBase, $"{folderName}_{conflict}");
                conflict++;
            }

            Directory.Move(sourcePath, targetPath);
            return targetPath;
        }
        catch
        {
            return null;
        }
    }
}
