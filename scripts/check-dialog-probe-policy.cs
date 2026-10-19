// dotnet run --file scripts/check-dialog-probe-policy.cs
#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:property SelfContained=true
#:property RuntimeIdentifier=win-x64
#:project ../Quickstart/Quickstart.csproj

using Quickstart.Core;

void Check(bool value, string message) { if (!value) throw new Exception(message); }
foreach (var caption in new[] { "WPS Office", "正在启动", "七部2026年开标计划 - WPS Office", "打开文件说明.docx - WPS Office", "" })
    Check(!FileDialogNavigator.IsWpsPickerCaption(caption), "Must not scan startup/document: " + caption);
foreach (var caption in new[] { "打开", "另存为", "选择文件", "Open", "Save As" })
    Check(FileDialogNavigator.IsWpsPickerCaption(caption), "Picker must remain automatic: " + caption);
var policy = new DialogProbeBackoff();
Check(FileDialogNavigator.IsWpsPickerCandidate("", "Qt5QWindowIcon"), "WPS picker with painted-only title");
Check(!FileDialogNavigator.IsWpsPickerCandidate("", "OpusApp"), "Blank non-Qt window excluded");
Check(!FileDialogNavigator.IsWpsPickerCandidate("WPS Office", "Qt5QWindowIcon"), "Named startup window excluded");
Check(!FileDialogNavigator.IsWpsPickerCandidate("文件.docx - WPS Office", "Qt5QWindowIcon"), "Named document window excluded");
var pickerClasses = new[] { "KcfdFileDialogContentWidget", "KcfdFilterWidget", "KcfdTitleLabel" };
Check(FileDialogNavigator.IsWpsCustomPickerStructure(pickerClasses, "打开"), "Kcfd open picker");
Check(FileDialogNavigator.IsWpsCustomPickerStructure(pickerClasses, "另存为"), "Kcfd save picker");
Check(!FileDialogNavigator.IsWpsCustomPickerStructure(pickerClasses.Take(2), "打开"), "Incomplete picker rejected");
Check(!FileDialogNavigator.IsWpsCustomPickerStructure(pickerClasses, "WPS Office"), "Non-picker title rejected");
var window = new IntPtr(123);
Check(policy.CanProbe(window, 10, 0), "New picker immediate");
policy.Failed(window, 10, 0);
for (var eventTime = 1; eventTime < 500; eventTime++)
    Check(!policy.CanProbe(window, 10, eventTime), "Event storm must not reset cooldown");
Check(policy.CanProbe(window, 10, 500), "Delayed controls retry");
policy.Failed(window, 10, 500, providerFailure: true);
Check(!policy.CanProbe(window, 10, 60_499), "Timeout cooldown");
Check(policy.CanProbe(window, 10, 60_500), "Timeout can recover later");
Check(policy.CanProbe(window, 11, 501), "Reused HWND in new process");
Check(policy.CanProbe(new IntPtr(124), 10, 501), "Another picker immediate");
policy.RemoveClosed((_, _) => false);
Check(policy.CanProbe(window, 10, 501), "Closed/recreated picker reset");
Console.WriteLine("PASS: WPS startup/document exclusion, picker captions, event storm, delayed controls, timeout cooldown, independent/recreated windows.");
