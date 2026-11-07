// Run: dotnet run --file scripts/check-local-folder-browser.cs
#:property PublishAot=false
#:property PublishTrimmed=false
#:project ../Quickstart.Core/Quickstart.Core.csproj

using Quickstart.Core;

void Check(bool value, string message) { if (!value) throw new Exception(message); }
var root = Path.GetFullPath(Path.Combine("artifacts/debug/checks/local-folder-browser", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
var nested = Directory.CreateDirectory(Path.Combine(root, "Z 中文子目录")).FullName;
var empty = Directory.CreateDirectory(Path.Combine(root, "A 空目录")).FullName;
File.WriteAllText(Path.Combine(nested, "不应递归列出.txt"), "nested");
var small = Path.Combine(root, "alpha 中文 [v1] #.txt");
var large = Path.Combine(root, "BETA.txt");
var hidden = Path.Combine(root, "hidden.txt");
File.WriteAllText(small, "a");
File.WriteAllText(large, new string('b', 128));
File.WriteAllText(hidden, "hidden");
if (OperatingSystem.IsWindows()) File.SetAttributes(hidden, FileAttributes.Hidden | FileAttributes.System);
File.SetLastWriteTime(small, new DateTime(2025, 1, 2, 12, 0, 0));
File.SetLastWriteTime(large, new DateTime(2025, 2, 3, 12, 0, 0));
File.SetLastWriteTime(hidden, new DateTime(2025, 1, 1, 12, 0, 0));

Task<FileSearchPage> Read(string path, string filter = "", int kind = 0, uint offset = 0,
    uint sort = 1, CancellationToken token = default, string? selectedPath = null)
    => LocalFolderBrowser.ReadAsync(path, filter, kind, offset, sort, token, selectedPath);

var all = await Read(root);
Check(all.Total == 5 && all.Items.Count == 5, "Direct listing omitted a fresh or hidden entry, or recursed into descendants");
Check(all.Items.Take(2).All(item => item.IsFolder) && all.Items.Skip(2).All(item => !item.IsFolder), "Directories must precede files");
Check(all.Items[0].FullPath == empty && all.Items[1].FullPath == nested, "Name sorting is not stable");
Check(all.Items.Single(item => item.FullPath == large).Size == 128, "File metadata missing");
Check(all.Items.Where(item => item.IsFolder).All(item => item.Size == null), "Directory size must remain unknown");
Check((await Read(root, "ALPHA 中文 [V1] #")).Items.Single().FullPath == small, "Case-insensitive literal filtering lost Unicode or punctuation");
Check((await Read(root, "*")).Total == 0, "Folder filtering unexpectedly interpreted wildcards");
Check((await Read(root, kind: 1)).Total == 3 && (await Read(root, kind: 2)).Total == 2, "File/folder filters failed");
Check((await Read(root, "中文", kind: 1)).Items.Single().FullPath == small, "Name and kind filters did not compose");
var byDate = await Read(root, sort: 14);
Check(byDate.Items.Take(2).All(item => item.IsFolder) && byDate.Items[2].FullPath == large, "Date sorting or folder precedence failed");
var bySize = await Read(root, sort: 6);
Check(bySize.Items.Take(2).All(item => item.IsFolder) && bySize.Items[2].FullPath == large, "Size sorting or folder precedence failed");
var emptyPage = await Read(empty, offset: 100);
Check(emptyPage.Total == 0 && emptyPage.Offset == 0 && emptyPage.Items.Count == 0, "Empty folder paging failed");
Check((await Read(root, "no such name", offset: 100)).Offset == 0, "Empty filter did not reset stale offset");

var pagesRoot = Directory.CreateDirectory(Path.Combine(root, "分页")).FullName;
for (var i = 0; i < 205; i++) File.WriteAllText(Path.Combine(pagesRoot, $"item-{i:D3}.txt"), "page");
var first = await Read(pagesRoot);
var second = await Read(pagesRoot, offset: 100);
var last = await Read(pagesRoot, offset: uint.MaxValue);
Check(first.Total == 205 && first.Items.Count == 100 && first.Offset == 0, "First page is wrong");
Check(second.Items.Count == 100 && second.Offset == 100 && second.Items[0].Name == "item-100.txt", "Second page is wrong");
Check(last.Items.Count == 5 && last.Offset == 200 && last.Items[0].Name == "item-200.txt", "Stale offset did not clamp to last page");
Check(first.Items.Concat(second.Items).Concat(last.Items).Select(item => item.FullPath).Distinct().Count() == 205, "Pagination duplicated or omitted entries");
Check((await Read(pagesRoot, "item-00", offset: 200)).Offset == 0, "Filtering did not clamp stale offset");

var folderPages = Directory.CreateDirectory(Path.Combine(nested, "分页目录")).FullName;
for (var i = 0; i < 205; i++) Directory.CreateDirectory(Path.Combine(folderPages, $"Folder-{i:D3} 中文"));
var selectedFolder = Path.Combine(folderPages, "Folder-150 中文");
var selectedPage = await Read(folderPages, selectedPath: selectedFolder.ToUpperInvariant());
Check(selectedPage.Total == 205 && selectedPage.Offset == 100 && selectedPage.Items[50].FullPath == selectedFolder,
    "Returning to a child beyond the first 100 folders did not load its page case-insensitively");
Check((await Read(folderPages, selectedPath: Path.Combine(folderPages, "Folder-204 中文"))).Offset == 200,
    "Selected folder on the final page was not found");
Check((await Read(folderPages, offset: 200, selectedPath: Path.Combine(folderPages, "Folder-000 中文"))).Offset == 0,
    "Selection did not override the old page offset");
Check((await Read(folderPages, offset: 100, selectedPath: selectedFolder + " extra")).Offset == 100,
    "A missing/partial selection changed an otherwise valid page");
Check((await Read(folderPages, "Folder-00", offset: 200, selectedPath: selectedFolder)).Offset == 0,
    "A filtered-out selection broke normal stale-offset clamping");
var movedFile = Path.Combine(pagesRoot, "item-150.txt");
File.SetLastWriteTime(movedFile, new DateTime(2100, 1, 1));
var movedPage = await Read(pagesRoot, offset: 100, sort: 14, selectedPath: movedFile);
Check(movedPage.Offset == 0 && movedPage.Items[0].FullPath == movedFile,
    "Refresh did not follow a selected file after its sort order moved to another page");

try { await Read(Path.Combine(root, "missing")); throw new Exception("Missing root was silently treated as empty"); }
catch (DirectoryNotFoundException) { }
try { await Read(small); throw new Exception("File path was silently treated as an empty folder"); }
catch (IOException) { }
using var cancel = new CancellationTokenSource();
cancel.Cancel();
try { await Read(root, token: cancel.Token); throw new Exception("Cancelled read returned data"); }
catch (OperationCanceledException) { }
Check((await Read(root)).Total == 6, "A cancelled read blocked subsequent listing");
Console.WriteLine("PASS direct filesystem children, fresh/hidden files, Unicode/literal filtering, kinds, folders first, sorts, pagination/clamping, selected-child page restoration, sort-position changes, empty/missing roots and cancellation.");
