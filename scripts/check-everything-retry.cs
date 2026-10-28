// Run: dotnet run --file scripts/check-everything-retry.cs
#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:property SelfContained=true
#:property RuntimeIdentifier=win-x64
#:project ../Quickstart/Quickstart.csproj
using System.Reflection;
using Quickstart.Core;

var method = typeof(EverythingSearchClient).GetMethod("ExecuteWithRetryAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
Task<FileSearchPage> Run(Func<CancellationToken, Task<FileSearchPage>> search, CancellationToken token = default, Action<int>? progress = null, int delay = 0)
    => (Task<FileSearchPage>)method.Invoke(null, [search, token, progress, TimeSpan.FromMilliseconds(delay)])!;
void Check(bool value, string message) { if (!value) throw new Exception(message); }
var expected = new FileSearchPage(1, 100, []);
int attempts = 0;
var progress = new List<int>();
var result = await Run(_ => ++attempts < 3 ? Task.FromException<FileSearchPage>(new TimeoutException()) : Task.FromResult(expected), progress: progress.Add);
Check(ReferenceEquals(result, expected) && attempts == 3 && progress.SequenceEqual(new[]{1,2}), "Retry recovery/pagination failed");
attempts = 0;
try { await Run(_ => { attempts++; throw new TimeoutException(); }); throw new Exception("Expected final timeout"); }
catch (TimeoutException ex) { Check(attempts == 3 && ex.Message.Contains("2 次") && !ex.Message.Contains("索引"), "Retry limit or message incorrect"); }
attempts = 0;
try { await Run(_ => { attempts++; throw new InvalidOperationException("permission"); }); throw new Exception("Expected permanent failure"); }
catch (InvalidOperationException ex) { Check(attempts == 1 && ex.Message == "permission", "Permanent error retried"); }
using var cancel = new CancellationTokenSource();
attempts = 0;
try { await Run(_ => { attempts++; throw new TimeoutException(); }, cancel.Token, _ => cancel.Cancel(), 5000); throw new Exception("Expected cancellation"); }
catch (OperationCanceledException) { Check(attempts == 1, "Cancelled retry was sent"); }
attempts = 0;
try { await Run(_ => { attempts++; return Task.FromResult(expected); }, cancel.Token); throw new Exception("Expected pre-cancellation"); }
catch (OperationCanceledException) { Check(attempts == 0, "Pre-cancelled request sent"); }
using var stale = new CancellationTokenSource();
try { await Run(_ => { stale.Cancel(); return Task.FromResult(expected); }, stale.Token); throw new Exception("Stale result returned"); }
catch (OperationCanceledException) { }
Check(ReferenceEquals(await Run(_ => Task.FromResult(expected)), expected), "New query blocked after cancelled request");
Console.WriteLine("PASS: recovery, retry limit, accurate error, non-timeout errors, cancellation during backoff, pre-cancellation, stale results and subsequent queries.");
