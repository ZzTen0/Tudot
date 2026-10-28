using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Tudot.Models;
using Tudot.Services;

// 使用独立临时库，绝不实例化默认 DatabaseService 或访问真实媒体文件。
var scratch = Path.Combine(Path.GetTempPath(), "TudotPerformanceChecks", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
var databasePath = Path.Combine(scratch, "checks.db");
var database = new DatabaseService(databasePath);
database.Initialize();
using var connection = new SqliteConnection($"Data Source={databasePath}");
connection.Open();
var creatorId = database.AddCreator(new Creator { Name = "Test creator", FolderPath = scratch });

void Assert(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    Console.WriteLine($"PASS: {description}");
}

void Execute(string sql)
{
    using var command = new SqliteCommand(sql, connection);
    command.ExecuteNonQuery();
}

int NewAlbum(string name) => database.AddAlbum(new Album
{
    Name = name, Path = Path.Combine(scratch, name), CreatorId = creatorId,
    CreatedDate = DateTime.Now, ModifiedDate = DateTime.Now
});

List<ImageFile> Files(int albumId, int count) => Enumerable.Range(0, count).Select(i => new ImageFile
{
    AlbumId = albumId, FileName = $"image-{i}.jpg", FilePath = Path.Combine(scratch, $"image-{i}.jpg"),
    FileType = i % 2 == 0 ? "image" : "video", FileSize = 1000, SortOrder = i
}).ToList();

var albumId = NewAlbum("batch");
var initial = Files(albumId, 300);
database.WriteAlbumImages(albumId, initial);
database.WriteAlbumImages(albumId, initial);
Assert(database.GetAlbumImages(albumId).Count == 300, "批量追加和重复插入保持唯一索引");
var album = database.GetAlbum(albumId)!;
Assert(album.ImageCount == 150 && album.VideoCount == 150, "图片/视频计数与批量写入一致");

// 在删除旧索引、写入第一条新记录后强制第二条失败，验证事务完整回滚。
Execute("CREATE TRIGGER fail_insert BEFORE INSERT ON ImageFiles WHEN NEW.FileName = 'reject.jpg' BEGIN SELECT RAISE(ABORT, 'injected failure'); END;");
var replacement = Files(albumId, 2);
replacement[0].FileName = "replacement.jpg";
replacement[1].FileName = "reject.jpg";
var failed = false;
try { database.WriteAlbumImages(albumId, replacement, replaceExisting: true); }
catch (SqliteException) { failed = true; }
Assert(failed, "故障注入确实触发数据库异常");
var restored = database.GetAlbumImages(albumId);
Assert(restored.Count == 300 && restored.All(f => f.FileName.StartsWith("image-")), "替换失败恢复完整旧索引且不残留部分新记录");
album = database.GetAlbum(albumId)!;
Assert(album.ImageCount == 150 && album.VideoCount == 150, "失败后计数保持不变");
Execute("DROP TRIGGER fail_insert;");

database.WriteAlbumImages(albumId, Files(albumId, 10), replaceExisting: true);
Assert(database.GetAlbumImages(albumId).Count == 10, "成功替换移除旧记录");
database.Initialize();
Assert(database.GetAlbumImages(albumId).Count == 10, "重复初始化保留现有记录");
using (var command = new SqliteCommand("SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name IN ('IX_Albums_Creator_Modified', 'IX_Albums_Modified', 'IX_Creators_Category')", connection))
    Assert(Convert.ToInt32(command.ExecuteScalar()) == 3, "查询索引存在");
database.WriteAlbumImages(albumId, Array.Empty<ImageFile>(), replaceExisting: true);
album = database.GetAlbum(albumId)!;
Assert(database.GetAlbumImages(albumId).Count == 0 && album.ImageCount == 0 && album.VideoCount == 0, "空扫描结果清除记录并同步归零计数");

// 同一临时库、相同数据量下比较原有逐条 API 与批量 API，不把计时作为脆弱断言。
var baselineId = NewAlbum("baseline");
var baseline = Files(baselineId, 1000);
var timer = Stopwatch.StartNew();
foreach (var file in baseline) database.AddImageFile(file);
timer.Stop();
var baselineMs = timer.Elapsed.TotalMilliseconds;
var batchId = NewAlbum("timed-batch");
var batch = Files(batchId, 1000);
timer.Restart();
database.WriteAlbumImages(batchId, batch);
timer.Stop();
Assert(database.GetAlbumImages(baselineId).Count == database.GetAlbumImages(batchId).Count, "基准与批量两条路径写入相同数量");
Console.WriteLine($"1000 rows: per-row={baselineMs:F1} ms; batch={timer.Elapsed.TotalMilliseconds:F1} ms");
Console.WriteLine($"Temporary test database: {databasePath}");

// 隔离验证四种排序字段的双向结果、同值稳定顺序和筛选边界；不接触真实图库。
var sortCreators = new[] { "Z-sort", "A-sort", "M-sort" }
    .Select(name => database.AddCreator(new Creator { Name = name, FolderPath = scratch })).ToArray();
var sortIds = Enumerable.Range(0, 3).Select(i => database.AddAlbum(new Album
{
    Name = "direction-check-" + (char)('A' + i), CreatorId = sortCreators[i],
    Path = Path.Combine(scratch, "direction-check-" + i),
    ModifiedDate = new DateTime(2026, 1, new[] { 2, 3, 1 }[i]),
    CreatedDate = new DateTime(2026, 1, 1), ImageCount = new[] { 3, 1, 2 }[i]
})).ToArray();
var expectedOrders = new Dictionary<string, int[]>
{
    ["Name"] = [sortIds[0], sortIds[1], sortIds[2]],
    ["Date"] = [sortIds[2], sortIds[0], sortIds[1]],
    ["Count"] = [sortIds[1], sortIds[2], sortIds[0]],
    ["Creator"] = [sortIds[1], sortIds[2], sortIds[0]]
};
foreach (var (sort, expected) in expectedOrders)
{
    Assert(database.GetAlbums(null, "direction-check-", sort, false).Select(a => a.Id).SequenceEqual(expected), sort + " 正序覆盖筛选全集");
    Assert(database.GetAlbums(null, "direction-check-", sort, true).Select(a => a.Id).SequenceEqual(expected.Reverse()), sort + " 倒序覆盖筛选全集");
}
Assert(database.GetAlbums(sortCreators[0], "direction-check-", "Name", true).Single().Id == sortIds[0], "方向切换保留创作者筛选");
Assert(database.GetAlbums(null, "direction-check-", "Date").Select(a => a.Id).SequenceEqual(expectedOrders["Date"].Reverse()), "旧调用保留默认日期倒序");
Execute("UPDATE Albums SET ImageCount = 0 WHERE Name LIKE 'direction-check-%'");
Assert(database.GetAlbums(null, "direction-check-", "Count", false).Select(a => a.Id).SequenceEqual(sortIds), "排序值相同时按 Id 稳定排序");
