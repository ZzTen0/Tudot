using System.IO;
using AlbumManager.Models;

namespace AlbumManager.Services;

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
