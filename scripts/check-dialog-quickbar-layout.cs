// Run: dotnet run --file scripts/check-dialog-quickbar-layout.cs
#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:property SelfContained=true
#:property RuntimeIdentifier=win-x64
#:project ../Quickstart/Quickstart.csproj

using System.Drawing;
using Quickstart.UI;

var calculate = typeof(DialogQuickBar).Assembly.GetType("Quickstart.UI.DialogQuickBarLayout")!.GetMethod("Calculate")!;
(Rectangle bounds, int offset, int listTop, int listHeight) Layout(Rectangle dialog, Rectangle work, int dpi, bool expanded)
{
    var result = calculate.Invoke(null, [dialog, work, dpi, expanded])!;
    T Get<T>(string name) => (T)result.GetType().GetProperty(name)!.GetValue(result)!;
    return (Get<Rectangle>("Bounds"), Get<int>("InputOffset"), Get<int>("ResultsTop"), Get<int>("ResultsHeight"));
}
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
var count = 0;
foreach (var dpi in new[] { 96, 120, 144, 192 })
foreach (var work in new[] { new Rectangle(0, 0, 1920, 1040), new Rectangle(-1920, 40, 1920, 1040), new Rectangle(200, -1080, 1280, 1040), new Rectangle(0, 0, 800, 560) })
foreach (var left in new[] { work.Left - 200, work.Left + 10, work.Right - 120 })
foreach (var bottom in new[] { work.Top + 100, work.Top + work.Height / 2, work.Bottom - 140, work.Bottom - 10, work.Bottom + 100 })
{
    var dialog = new Rectangle(left, bottom - 600, 900, 600);
    var compact = Layout(dialog, work, dpi, false);
    var expanded = Layout(dialog, work, dpi, true);
    Check(compact.bounds.Top + compact.offset == expanded.bounds.Top + expanded.offset, "Input moved when expanding");
    Check(compact.bounds.Left == expanded.bounds.Left && compact.bounds.Width == expanded.bounds.Width, "Horizontal anchor changed");
    Check(work.Contains(compact.bounds) && work.Contains(expanded.bounds), "Off-screen bounds");
    Check(expanded.listHeight > 0, "No usable list space");
    int S(int value) => (int)Math.Round(value * dpi / 96d);
    if (expanded.offset > 0)
        Check(expanded.listTop + expanded.listHeight < expanded.offset + S(6), "List overlaps input when growing upward");
    else
        Check(expanded.listTop >= S(70) && expanded.listTop + expanded.listHeight <= expanded.bounds.Height - S(22), "Downward list overlaps controls/status");
    count++;
}
var lower = Layout(new Rectangle(10, 100, 1400, 800), new Rectangle(0, 0, 1920, 1040), 96, true);
Check(lower.offset > 0 && lower.bounds.Top > 600, "Bottom dialog jumped to screen top");
var upper = Layout(new Rectangle(10, 40, 900, 400), new Rectangle(0, 0, 1920, 1040), 96, true);
Check(upper.offset == 0 && upper.bounds.Top == 444, "Enough space should expand downward");
Console.WriteLine($"PASS {count} placements: stable input, upward/downward results, screen edges, multi-monitor origins, 100–200% DPI.");
