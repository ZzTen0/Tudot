using System.IO;
using Tudot.Models;
using Microsoft.Data.Sqlite;

namespace Tudot.Services;

public class DatabaseService
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    public DatabaseService()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tudot");
        Directory.CreateDirectory(appData);
        _dbPath = Path.Combine(appData, "albums.db");

        // 从旧版 AlbumManager 目录迁移数据库
        var oldDb = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AlbumManager", "albums.db");
        if (!File.Exists(_dbPath) && File.Exists(oldDb))
        {
            try { File.Copy(oldDb, _dbPath); } catch { }
        }

        _connectionString = $"Data Source={_dbPath}";
    }

    public void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS Categories (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                Color TEXT DEFAULT '#999999',
                SortOrder INTEGER DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS Creators (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                FolderPath TEXT NOT NULL,
                CreatedDate TEXT DEFAULT CURRENT_TIMESTAMP,
                ThumbPath TEXT DEFAULT '',
                CategoryId INTEGER DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS Albums (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Path TEXT NOT NULL UNIQUE,
                CreatorId INTEGER DEFAULT 0,
                CoverPath TEXT,
                ImageCount INTEGER DEFAULT 0,
                CreatedDate TEXT DEFAULT CURRENT_TIMESTAMP,
                ModifiedDate TEXT DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (CreatorId) REFERENCES Creators(Id)
            );

            CREATE TABLE IF NOT EXISTS ImageFiles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AlbumId INTEGER NOT NULL,
                FileName TEXT NOT NULL,
                FilePath TEXT NOT NULL,
                FileType TEXT DEFAULT 'image',
                FileSize INTEGER DEFAULT 0,
                SortOrder INTEGER DEFAULT 0,
                FOREIGN KEY (AlbumId) REFERENCES Albums(Id)
            );

            CREATE TABLE IF NOT EXISTS Bookmarks (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                Url TEXT NOT NULL,
                Description TEXT,
                CreatedDate TEXT DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS Settings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
        ";
        command.ExecuteNonQuery();

        // 旧库迁移：Creators 增加 ThumbPath 列
        var columns = new List<string>();
        using (var pragma = new SqliteCommand("PRAGMA table_info(Creators)", connection))
        using (var reader = pragma.ExecuteReader())
        {
            while (reader.Read())
                columns.Add(reader.GetString(1));
        }
        if (!columns.Contains("ThumbPath"))
        {
            using var alter = new SqliteCommand(
                "ALTER TABLE Creators ADD COLUMN ThumbPath TEXT DEFAULT ''", connection);
            alter.ExecuteNonQuery();
        }
        if (!columns.Contains("CategoryId"))
        {
            using var alter = new SqliteCommand(
                "ALTER TABLE Creators ADD COLUMN CategoryId INTEGER DEFAULT 0", connection);
            alter.ExecuteNonQuery();
        }

        // 旧库迁移：Albums 增加 Favorite 列（收藏标记）
        var albumCols = new List<string>();
        using (var pragma = new SqliteCommand("PRAGMA table_info(Albums)", connection))
        using (var reader = pragma.ExecuteReader())
        {
            while (reader.Read())
                albumCols.Add(reader.GetString(1));
        }
        if (!albumCols.Contains("Favorite"))
        {
            using var alter = new SqliteCommand(
                "ALTER TABLE Albums ADD COLUMN Favorite INTEGER DEFAULT 0", connection);
            alter.ExecuteNonQuery();
        }
        if (!albumCols.Contains("AddOnly"))
        {
            using var alter = new SqliteCommand(
                "ALTER TABLE Albums ADD COLUMN AddOnly INTEGER DEFAULT 0", connection);
            alter.ExecuteNonQuery();
        }
    }

    public string GetSetting(string key, string defaultValue = "")
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = new SqliteCommand("SELECT Value FROM Settings WHERE Key = @key", connection);
        command.Parameters.AddWithValue("@key", key);
        return command.ExecuteScalar() as string ?? defaultValue;
    }

    public void SetSetting(string key, string value)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = new SqliteCommand(
            "INSERT INTO Settings (Key, Value) VALUES (@key, @value) ON CONFLICT(Key) DO UPDATE SET Value = @value",
            connection);
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@value", value);
        command.ExecuteNonQuery();
    }

    public List<Album> GetAlbums(int? creatorId = null, string searchText = "", string sortBy = "Date")
    {
        var albums = new List<Album>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var sql = @"
            SELECT a.*, COALESCE(c.Name, '未分类') as CreatorName
            FROM Albums a
            LEFT JOIN Creators c ON a.CreatorId = c.Id
            WHERE (@creatorId IS NULL OR a.CreatorId = @creatorId)
            AND (@searchText = '' OR a.Name LIKE @searchText)
        ";

        sql += sortBy switch
        {
            "Name" => " ORDER BY a.Name",
            "Count" => " ORDER BY a.ImageCount DESC",
            "Creator" => " ORDER BY c.Name, a.Name",
            _ => " ORDER BY a.ModifiedDate DESC"
        };

        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@creatorId", creatorId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@searchText", $"%{searchText}%");

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            albums.Add(new Album
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Path = reader.GetString(2),
                CreatorId = reader.GetInt32(3),
                CoverPath = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                ImageCount = reader.GetInt32(5),
                CreatedDate = DateTime.Parse(reader.GetString(6)),
                ModifiedDate = DateTime.Parse(reader.GetString(7)),
                IsFavorite = reader.GetInt32(8) == 1,
                IsAddOnly = reader.GetInt32(9) == 1,
                CreatorName = reader.GetString(10)
            });
        }

        return albums;
    }

    public Album? GetAlbum(int id)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = new SqliteCommand(
            @"SELECT a.*, COALESCE(c.Name, '未分类') as CreatorName
              FROM Albums a
              LEFT JOIN Creators c ON a.CreatorId = c.Id
              WHERE a.Id = @id", connection);
        command.Parameters.AddWithValue("@id", id);

        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            return new Album
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Path = reader.GetString(2),
                CreatorId = reader.GetInt32(3),
                CoverPath = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                ImageCount = reader.GetInt32(5),
                CreatedDate = DateTime.Parse(reader.GetString(6)),
                ModifiedDate = DateTime.Parse(reader.GetString(7)),
                IsFavorite = reader.GetInt32(8) == 1,
                IsAddOnly = reader.GetInt32(9) == 1,
                CreatorName = reader.GetString(10)
            };
        }
        return null;
    }

    public void SetAlbumAddOnly(int albumId, bool addOnly)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = new SqliteCommand(
            "UPDATE Albums SET AddOnly = @v WHERE Id = @id", connection);
        command.Parameters.AddWithValue("@v", addOnly ? 1 : 0);
        command.Parameters.AddWithValue("@id", albumId);
        command.ExecuteNonQuery();
    }

    public void UpdateAlbumPath(int albumId, string path)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = new SqliteCommand(
            "UPDATE Albums SET Path = @path, ModifiedDate = @modifiedDate WHERE Id = @id", connection);
        command.Parameters.AddWithValue("@path", path);
        command.Parameters.AddWithValue("@modifiedDate", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("@id", albumId);
        command.ExecuteNonQuery();
    }

    public void UpdateAlbumCreator(int albumId, int creatorId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = new SqliteCommand(
            "UPDATE Albums SET CreatorId = @creatorId, ModifiedDate = @modifiedDate WHERE Id = @id", connection);
        command.Parameters.AddWithValue("@creatorId", creatorId);
        command.Parameters.AddWithValue("@modifiedDate", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("@id", albumId);
        command.ExecuteNonQuery();
    }

    public void DeleteImageFilesByAlbum(int albumId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = new SqliteCommand("DELETE FROM ImageFiles WHERE AlbumId = @albumId", connection);
        command.Parameters.AddWithValue("@albumId", albumId);
        command.ExecuteNonQuery();
    }

    public void SetFavorite(int albumId, bool isFavorite)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = new SqliteCommand(
            "UPDATE Albums SET Favorite = @fav WHERE Id = @id", connection);
        command.Parameters.AddWithValue("@fav", isFavorite ? 1 : 0);
        command.Parameters.AddWithValue("@id", albumId);
        command.ExecuteNonQuery();
    }

    public int AddAlbum(Album album)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = new SqliteCommand(
            @"INSERT INTO Albums (Name, Path, CreatorId, CoverPath, ImageCount, CreatedDate, ModifiedDate, AddOnly)
              VALUES (@name, @path, @creatorId, @coverPath, @imageCount, @createdDate, @modifiedDate, @addOnly);
              SELECT last_insert_rowid();", connection);

        command.Parameters.AddWithValue("@name", album.Name);
        command.Parameters.AddWithValue("@path", album.Path);
        command.Parameters.AddWithValue("@creatorId", album.CreatorId);
        command.Parameters.AddWithValue("@coverPath", album.CoverPath ?? string.Empty);
        command.Parameters.AddWithValue("@imageCount", album.ImageCount);
        command.Parameters.AddWithValue("@createdDate", album.CreatedDate.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("@modifiedDate", album.ModifiedDate.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("@addOnly", album.IsAddOnly ? 1 : 0);

        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void UpdateAlbumCover(int albumId, string coverPath)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = new SqliteCommand(
            "UPDATE Albums SET CoverPath = @coverPath, ModifiedDate = @modifiedDate WHERE Id = @id", connection);
        command.Parameters.AddWithValue("@coverPath", coverPath);
        command.Parameters.AddWithValue("@modifiedDate", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("@id", albumId);
        command.ExecuteNonQuery();
    }

    public List<Creator> GetCreators()
    {
        var creators = new List<Creator>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = new SqliteCommand(
            @"SELECT c.*, COALESCE(cat.Name, '') as CategoryName,
                     COUNT(a.Id) as AlbumCount, COALESCE(SUM(a.ImageCount), 0) as TotalImages
              FROM Creators c
              LEFT JOIN Categories cat ON c.CategoryId = cat.Id
              LEFT JOIN Albums a ON c.Id = a.CreatorId
              GROUP BY c.Id
              ORDER BY c.Name", connection);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            creators.Add(new Creator
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                FolderPath = reader.GetString(2),
                ThumbPath = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                CategoryId = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                CategoryName = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                AlbumCount = reader.GetInt32(7),
                TotalImages = reader.GetInt32(8)
            });
        }

        return creators;
    }

    public int AddCreator(Creator creator)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = new SqliteCommand(
            "INSERT INTO Creators (Name, FolderPath, CategoryId) VALUES (@name, @folderPath, @categoryId); SELECT last_insert_rowid();", connection);
        command.Parameters.AddWithValue("@name", creator.Name);
        command.Parameters.AddWithValue("@folderPath", creator.FolderPath);
        command.Parameters.AddWithValue("@categoryId", creator.CategoryId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void UpdateCreator(int id, string name, string? thumbPath = null, int? categoryId = null)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = new SqliteCommand(
            "UPDATE Creators SET Name = @name, ThumbPath = @thumbPath, CategoryId = @categoryId WHERE Id = @id", connection);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@thumbPath", thumbPath ?? string.Empty);
        command.Parameters.AddWithValue("@categoryId", categoryId ?? 0);
        command.Parameters.AddWithValue("@id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>仅更新创作者的分类</summary>
    public void UpdateCreatorCategory(int creatorId, int categoryId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = new SqliteCommand(
            "UPDATE Creators SET CategoryId = @categoryId WHERE Id = @id", connection);
        command.Parameters.AddWithValue("@categoryId", categoryId);
        command.Parameters.AddWithValue("@id", creatorId);
        command.ExecuteNonQuery();
    }

    /// <summary>更新创作者文件夹路径，同时更新其名下所有相册的路径前缀</summary>
    public void UpdateCreatorFolderPath(int creatorId, string newFolderPath)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        // 先获取旧路径，用于替换相册路径前缀
        string? oldFolderPath = null;
        using (var sel = new SqliteCommand("SELECT FolderPath FROM Creators WHERE Id = @id", connection))
        {
            sel.Parameters.AddWithValue("@id", creatorId);
            oldFolderPath = sel.ExecuteScalar() as string;
        }

        using var updCreator = new SqliteCommand(
            "UPDATE Creators SET FolderPath = @folderPath WHERE Id = @id", connection);
        updCreator.Parameters.AddWithValue("@folderPath", newFolderPath);
        updCreator.Parameters.AddWithValue("@id", creatorId);
        updCreator.ExecuteNonQuery();

        // 更新该创作者名下所有相册的路径前缀
        if (!string.IsNullOrEmpty(oldFolderPath))
        {
            using var updAlbums = new SqliteCommand(
                "UPDATE Albums SET Path = REPLACE(Path, @old, @new) WHERE CreatorId = @id", connection);
            updAlbums.Parameters.AddWithValue("@old", oldFolderPath);
            updAlbums.Parameters.AddWithValue("@new", newFolderPath);
            updAlbums.Parameters.AddWithValue("@id", creatorId);
            updAlbums.ExecuteNonQuery();
        }
    }

    // ===== 分类 Categories =====

    public List<Category> GetCategories()
    {
        var categories = new List<Category>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = new SqliteCommand(
            @"SELECT cat.Id, cat.Name, cat.Color, cat.SortOrder,
                     COUNT(DISTINCT c.Id) as CreatorCount,
                     COUNT(a.Id) as AlbumCount
              FROM Categories cat
              LEFT JOIN Creators c ON c.CategoryId = cat.Id
              LEFT JOIN Albums a ON a.CreatorId = c.Id
              GROUP BY cat.Id
              ORDER BY cat.SortOrder, cat.Name", connection);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            categories.Add(new Category
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Color = reader.IsDBNull(2) ? "#999999" : reader.GetString(2),
                SortOrder = reader.GetInt32(3),
                CreatorCount = reader.GetInt32(4),
                AlbumCount = reader.GetInt32(5)
            });
        }

        return categories;
    }

    public int AddCategory(Category category)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = new SqliteCommand(
            "INSERT INTO Categories (Name, Color, SortOrder) VALUES (@name, @color, @sortOrder); SELECT last_insert_rowid();", connection);
        command.Parameters.AddWithValue("@name", category.Name);
        command.Parameters.AddWithValue("@color", category.Color);
        command.Parameters.AddWithValue("@sortOrder", category.SortOrder);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void UpdateCategory(int id, string name)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = new SqliteCommand(
            "UPDATE Categories SET Name = @name WHERE Id = @id", connection);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@id", id);
        command.ExecuteNonQuery();
    }

    public void DeleteCategory(int id)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        // 将该分类下的创作者改为无分类
        using var cmd1 = new SqliteCommand("UPDATE Creators SET CategoryId = 0 WHERE CategoryId = @id", connection);
        cmd1.Parameters.AddWithValue("@id", id);
        cmd1.ExecuteNonQuery();

        using var cmd2 = new SqliteCommand("DELETE FROM Categories WHERE Id = @id", connection);
        cmd2.Parameters.AddWithValue("@id", id);
        cmd2.ExecuteNonQuery();
    }

    public void DeleteCreator(int id, bool deleteFiles = false)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        if (deleteFiles)
        {
            // 删除该创作者名下所有相册记录及源文件
            var paths = new List<string>();
            using (var select = new SqliteCommand("SELECT Id, Path FROM Albums WHERE CreatorId = @id", connection))
            {
                select.Parameters.AddWithValue("@id", id);
                using var reader = select.ExecuteReader();
                while (reader.Read())
                    paths.Add(reader.GetString(1));
            }

            foreach (var path in paths)
            {
                try
                {
                    if (Directory.Exists(path))
                        Directory.Delete(path, true);
                }
                catch { /* 忽略删除失败的文件 */ }
            }

            using (var cmdImg = new SqliteCommand(
                "DELETE FROM ImageFiles WHERE AlbumId IN (SELECT Id FROM Albums WHERE CreatorId = @id)", connection))
            {
                cmdImg.Parameters.AddWithValue("@id", id);
                cmdImg.ExecuteNonQuery();
            }
            using (var cmdAlb = new SqliteCommand("DELETE FROM Albums WHERE CreatorId = @id", connection))
            {
                cmdAlb.Parameters.AddWithValue("@id", id);
                cmdAlb.ExecuteNonQuery();
            }
        }
        else
        {
            // 仅删除索引：将相册的创作者置为无
            using var cmd1 = new SqliteCommand("UPDATE Albums SET CreatorId = 0 WHERE CreatorId = @id", connection);
            cmd1.Parameters.AddWithValue("@id", id);
            cmd1.ExecuteNonQuery();
        }

        using (var cmd2 = new SqliteCommand("DELETE FROM Creators WHERE Id = @id", connection))
        {
            cmd2.Parameters.AddWithValue("@id", id);
            cmd2.ExecuteNonQuery();
        }
    }

    public void DeleteAlbum(int id, bool deleteFiles = false)
    {
        string? albumPath = null;
        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            using (var select = new SqliteCommand("SELECT Path FROM Albums WHERE Id = @id", connection))
            {
                select.Parameters.AddWithValue("@id", id);
                albumPath = select.ExecuteScalar() as string;
            }

            using (var cmdImg = new SqliteCommand("DELETE FROM ImageFiles WHERE AlbumId = @id", connection))
            {
                cmdImg.Parameters.AddWithValue("@id", id);
                cmdImg.ExecuteNonQuery();
            }
            using (var cmdAlb = new SqliteCommand("DELETE FROM Albums WHERE Id = @id", connection))
            {
                cmdAlb.Parameters.AddWithValue("@id", id);
                cmdAlb.ExecuteNonQuery();
            }
        }

        if (deleteFiles && !string.IsNullOrEmpty(albumPath))
        {
            try
            {
                if (Directory.Exists(albumPath))
                    Directory.Delete(albumPath, true);
            }
            catch { /* 忽略删除失败的文件 */ }
        }
    }

    public void DeleteBookmark(int id)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = new SqliteCommand("DELETE FROM Bookmarks WHERE Id = @id", connection);
        command.Parameters.AddWithValue("@id", id);
        command.ExecuteNonQuery();
    }

    public void UpdateAlbumInfo(int id, string name, int creatorId, string coverPath, string path)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = new SqliteCommand(
            @"UPDATE Albums SET Name = @name, CreatorId = @creatorId, CoverPath = @coverPath,
              Path = @path, ModifiedDate = @modifiedDate WHERE Id = @id", connection);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@creatorId", creatorId);
        command.Parameters.AddWithValue("@coverPath", coverPath);
        command.Parameters.AddWithValue("@path", path);
        command.Parameters.AddWithValue("@modifiedDate", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("@id", id);
        command.ExecuteNonQuery();
    }

    public void UpdateAlbumImagePaths(int albumId, string oldPathPrefix, string newPathPrefix)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = new SqliteCommand(
            "SELECT Id, FilePath FROM ImageFiles WHERE AlbumId = @albumId", connection);
        command.Parameters.AddWithValue("@albumId", albumId);

        var updates = new List<(int id, string path)>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var path = reader.GetString(1);
                if (path.StartsWith(oldPathPrefix, StringComparison.OrdinalIgnoreCase))
                    updates.Add((reader.GetInt32(0), newPathPrefix + path[oldPathPrefix.Length..]));
            }
        }

        foreach (var (id, path) in updates)
        {
            using var update = new SqliteCommand("UPDATE ImageFiles SET FilePath = @path WHERE Id = @id", connection);
            update.Parameters.AddWithValue("@path", path);
            update.Parameters.AddWithValue("@id", id);
            update.ExecuteNonQuery();
        }
    }

    public void DeleteImageFile(int id)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = new SqliteCommand("DELETE FROM ImageFiles WHERE Id = @id", connection);
        command.Parameters.AddWithValue("@id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>批量删除图片索引记录</summary>
    public void DeleteImageFiles(IEnumerable<int> ids)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return;

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = new SqliteCommand(
            $"DELETE FROM ImageFiles WHERE Id IN ({string.Join(",", idList)})", connection);
        command.ExecuteNonQuery();
    }

    public void RefreshAlbumImageCount(int albumId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = new SqliteCommand(
            @"UPDATE Albums SET ImageCount =
              (SELECT COUNT(1) FROM ImageFiles WHERE AlbumId = @albumId AND FileType = 'image')
              WHERE Id = @albumId", connection);
        command.Parameters.AddWithValue("@albumId", albumId);
        command.ExecuteNonQuery();
    }

    public bool AlbumExists(string path)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = new SqliteCommand("SELECT COUNT(1) FROM Albums WHERE Path = @path", connection);
        command.Parameters.AddWithValue("@path", path);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    public List<ImageFile> GetAlbumImages(int albumId)
    {
        var images = new List<ImageFile>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = new SqliteCommand(
            "SELECT * FROM ImageFiles WHERE AlbumId = @albumId ORDER BY SortOrder, FileName", connection);
        command.Parameters.AddWithValue("@albumId", albumId);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            images.Add(new ImageFile
            {
                Id = reader.GetInt32(0),
                AlbumId = reader.GetInt32(1),
                FileName = reader.GetString(2),
                FilePath = reader.GetString(3),
                FileType = reader.GetString(4),
                FileSize = reader.GetInt64(5),
                SortOrder = reader.GetInt32(6)
            });
        }

        return images;
    }

    public void AddImageFile(ImageFile file)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = new SqliteCommand(
            @"INSERT INTO ImageFiles (AlbumId, FileName, FilePath, FileType, FileSize, SortOrder)
              VALUES (@albumId, @fileName, @filePath, @fileType, @fileSize, @sortOrder)", connection);
        command.Parameters.AddWithValue("@albumId", file.AlbumId);
        command.Parameters.AddWithValue("@fileName", file.FileName);
        command.Parameters.AddWithValue("@filePath", file.FilePath);
        command.Parameters.AddWithValue("@fileType", file.FileType);
        command.Parameters.AddWithValue("@fileSize", file.FileSize);
        command.Parameters.AddWithValue("@sortOrder", file.SortOrder);
        command.ExecuteNonQuery();
    }

    public void UpdateImageFiles(List<ImageFile> files)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        foreach (var file in files)
        {
            using var command = new SqliteCommand(
                "UPDATE ImageFiles SET FileName = @fileName, FilePath = @filePath WHERE Id = @id", connection);
            command.Parameters.AddWithValue("@fileName", file.FileName);
            command.Parameters.AddWithValue("@filePath", file.FilePath);
            command.Parameters.AddWithValue("@id", file.Id);
            command.ExecuteNonQuery();
        }
    }

    public List<Bookmark> GetBookmarks()
    {
        var bookmarks = new List<Bookmark>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = new SqliteCommand(
            "SELECT * FROM Bookmarks ORDER BY CreatedDate DESC", connection);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            bookmarks.Add(new Bookmark
            {
                Id = reader.GetInt32(0),
                Title = reader.GetString(1),
                Url = reader.GetString(2),
                Description = reader.IsDBNull(3) ? null : reader.GetString(3),
                CreatedDate = DateTime.Parse(reader.GetString(4))
            });
        }

        return bookmarks;
    }

    public void AddBookmark(Bookmark bookmark)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = new SqliteCommand(
            "INSERT INTO Bookmarks (Title, Url, Description, CreatedDate) VALUES (@title, @url, @description, @createdDate)",
            connection);
        command.Parameters.AddWithValue("@title", bookmark.Title);
        command.Parameters.AddWithValue("@url", bookmark.Url);
        command.Parameters.AddWithValue("@description", bookmark.Description ?? string.Empty);
        command.Parameters.AddWithValue("@createdDate", bookmark.CreatedDate.ToString("yyyy-MM-dd HH:mm:ss"));
        command.ExecuteNonQuery();
    }
}
