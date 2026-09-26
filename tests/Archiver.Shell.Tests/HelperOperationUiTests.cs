using System.Globalization;
using Archiver.Core.Models;
using Archiver.OperationUi.Protocol;
using Archiver.Shell;
using FluentAssertions;

namespace Archiver.Shell.Tests;

// T-F268 step 4: Shell's side of the operation window helper, against a fake helper on real
// anonymous pipes. The fallback is FakeOperationUi, so no test ever opens a native window.
public sealed class HelperOperationUiTests : IDisposable
{
    private static readonly OperationMessage Warning = new("Extracting", MessageSeverity.Warning, "old.zip: damaged");
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    // A session disposed at the end of a test waits this long for a WindowClosed the fake never sends.
    private static readonly TimeSpan ShortClose = TimeSpan.FromMilliseconds(300);

    private readonly FakeHelper _helper = new();
    private readonly FakeOperationUi _fallback = new();
    private readonly List<ConflictInfo> _win32Conflicts = [];
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

    public HelperOperationUiTests() => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("uk-UA");

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _originalUiCulture;
        _helper.Dispose();
    }

    private HelperOperationUi CreateUi(TimeSpan? readyTimeout = null, TimeSpan? closeTimeout = null) =>
        new(_helper, _fallback,
            info =>
            {
                _win32Conflicts.Add(info);
                return Task.FromResult(new ConflictDecision { Resolution = ConflictResolution.Rename });
            },
            (_, _) => Task.FromResult(new PasswordDecision { Password = "win32" }))
        {
            ReadyTimeout = readyTimeout ?? WaitLimit,
            CloseTimeout = closeTimeout ?? ShortClose,
            ProgressInterval = TimeSpan.Zero,
        };

    private async Task<IOperationSession> BeginReadyAsync(
        string title = "Extracting: a.zip", ProgressStyle style = ProgressStyle.Bytes, TimeSpan? closeTimeout = null)
    {
        IOperationSession session = CreateUi(closeTimeout: closeTimeout).Begin(title, style);
        await _helper.ReadUntilAsync<Begin>();
        await _helper.SendReadyAsync();
        return session;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var limit = DateTime.UtcNow + WaitLimit;
        while (!condition())
        {
            if (DateTime.UtcNow > limit)
                throw new TimeoutException("The condition never became true.");
            await Task.Delay(20);
        }
    }

    // --- Happy path ---

    [Fact]
    public async Task Begin_SendsHelloThenBegin()
    {
        using var session = CreateUi().Begin("Розпакування: звіт.zip", ProgressStyle.Percent);

        var hello = (Hello)(await _helper.ReadAsync())!;
        var begin = (Begin)(await _helper.ReadAsync())!;

        hello.ProtocolVersion.Should().Be(FrameCodec.ProtocolVersion);
        hello.Culture.Should().Be("uk-UA");
        hello.RightToLeft.Should().BeFalse();
        hello.Strings.Should().ContainKeys(WindowStrings.Cancel, WindowStrings.CancelAll, WindowStrings.Close, WindowStrings.ItemOfCount);
        begin.Should().Be(new Begin("Розпакування: звіт.zip", ProgressKind.Percent));
        _fallback.Sessions.Should().BeEmpty();
    }

    [Fact]
    public async Task Progress_ReachesTheHelperWithItsStatusLine()
    {
        using var session = await BeginReadyAsync(style: ProgressStyle.Percent);

        session.Progress!.Report(new ProgressReport { Percent = 40, CurrentFile = @"папка\a.txt" });

        (await _helper.ReadUntilAsync<Progress>()).Should().Be(new Progress(40, @"папка\a.txt", "40%"));
    }

    [Fact]
    public async Task BeginItem_NamesTheArchive()
    {
        using var session = await BeginReadyAsync();

        session.BeginItem("b.zip", 2, 3);

        (await _helper.ReadUntilAsync<Item>()).Should().Be(new Item("b.zip", 2, 3));
    }

    [Fact]
    public async Task CleanComplete_ReturnsOnceTheWindowClosed()
    {
        using var session = await BeginReadyAsync(closeTimeout: WaitLimit);

        var complete = Task.Run(() => session.Complete(null));
        (await _helper.ReadUntilAsync<Complete>()).Result.Should().BeNull();
        await Task.Delay(100);
        complete.IsCompleted.Should().BeFalse("it waits for the helper to confirm the window closed");
        await _helper.SendAsync(new WindowClosed());

        await complete.WaitAsync(WaitLimit);
        _fallback.Sessions.Should().BeEmpty();
        _fallback.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Result_WaitsUntilTheUserClosesIt()
    {
        using var session = await BeginReadyAsync();

        var complete = Task.Run(() => session.Complete(Warning));
        (await _helper.ReadUntilAsync<Complete>()).Result.Should().Be(new ResultText(ResultSeverity.Warning, "Extracting", "old.zip: damaged"));
        await Task.Delay(ShortClose * 2);
        complete.IsCompleted.Should().BeFalse("the result stays on screen until the user closes it, however long");

        await _helper.SendAsync(new WindowClosed());

        await complete.WaitAsync(WaitLimit);
        _fallback.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task CancelInTheWindow_CancelsTheOperationWithoutFailover()
    {
        using var session = await BeginReadyAsync();

        await _helper.SendAsync(new CancelRequested());
        await _helper.SendAsync(new WindowClosed());
        _helper.Crash();

        await WaitUntilAsync(() => session.Cancellation.IsCancellationRequested);
        await Task.Delay(200);
        _fallback.Sessions.Should().BeEmpty();
    }

    [Fact]
    public async Task PromptsBeforeFailover_UseTheWin32Prompts()
    {
        using var session = await BeginReadyAsync();

        var decision = await session.AskConflictAsync(new ConflictInfo { ExistingPath = @"C:\a.txt" });

        decision.Resolution.Should().Be(ConflictResolution.Rename);
        _win32Conflicts.Should().ContainSingle();
    }

    // --- Security & boundary ---

    [Fact]
    public async Task ProgressFlood_NeverBlocksTheOperationAndTheLatestReportArrives()
    {
        using var session = CreateUi().Begin("t", ProgressStyle.Percent);

        // The helper reads nothing yet: a synchronous write would fill the pipe and block here.
        var reporting = Task.Run(() =>
        {
            for (int i = 0; i < 20_000; i++)
                session.Progress!.Report(new ProgressReport { Percent = i % 101, CurrentFile = i.ToString(CultureInfo.InvariantCulture) });
        });
        await reporting.WaitAsync(WaitLimit);

        int received = 0;
        Progress last;
        do
        {
            last = await _helper.ReadUntilAsync<Progress>();
            received++;
        }
        while (last.CurrentFile != "19999");
        received.Should().BeLessThan(20_000);
    }

    [Fact]
    public async Task ProgressAfterComplete_IsNotSent()
    {
        using var session = await BeginReadyAsync();

        var complete = Task.Run(() => session.Complete(null));
        await _helper.ReadUntilAsync<Complete>();
        session.Progress!.Report(new ProgressReport { Percent = 99 });

        var next = _helper.ReadAsync();
        (await Task.WhenAny(next, Task.Delay(300))).Should().NotBe(next);
        await _helper.SendAsync(new WindowClosed());
        await complete.WaitAsync(WaitLimit);
    }

    // --- Misuse ---

    [Fact]
    public async Task UserClosedAsTheOperationFinished_ResultGoesToTheFallback()
    {
        using var session = await BeginReadyAsync();
        await _helper.SendAsync(new CancelRequested());
        await _helper.SendAsync(new WindowClosed());
        await WaitUntilAsync(() => session.Cancellation.IsCancellationRequested);

        session.Complete(Warning);

        _fallback.Messages.Should().Equal(Warning);
        _fallback.Sessions.Should().BeEmpty();
    }

    [Fact]
    public async Task HelperOfAnotherProtocolVersion_FailsOver()
    {
        // Far longer than the wait below: only the version check can fail over in time.
        using var session = CreateUi(readyTimeout: TimeSpan.FromMinutes(5)).Begin("t", ProgressStyle.Bytes);
        await _helper.ReadUntilAsync<Begin>();

        await _helper.SendAsync(new HelperReady(FrameCodec.ProtocolVersion + 1));

        await WaitUntilAsync(() => _fallback.Sessions.Count == 1);
        _helper.Killed.Should().BeTrue();
    }

    // --- Error path ---

    [Fact]
    public void HelperCannotStart_TheWholeOperationUsesTheFallback()
    {
        _helper.FailToLaunch = true;

        using var session = CreateUi().Begin("Extracting: a.zip", ProgressStyle.Bytes);

        session.Should().BeOfType<FakeOperationSession>();
        _fallback.Sessions.Should().ContainSingle().Which.Title.Should().Be("Extracting: a.zip");
    }

    [Fact]
    public async Task HelperNotReadyInTime_FailsOverAndIsEnded()
    {
        using var session = CreateUi(readyTimeout: TimeSpan.FromMilliseconds(200)).Begin("t", ProgressStyle.Bytes);

        await WaitUntilAsync(() => _fallback.Sessions.Count == 1);

        _helper.Killed.Should().BeTrue();
        session.Progress!.Report(new ProgressReport { Percent = 5 });
        _fallback.Sessions[0].ProgressReports.Should().Be(1);
    }

    [Fact]
    public async Task HelperCrashMidOperation_TheFallbackTakesOverTheRest()
    {
        using var session = await BeginReadyAsync("Extracting 3 archives");
        session.BeginItem("b.zip", 2, 3);
        await _helper.ReadUntilAsync<Item>();

        _helper.Crash();
        await WaitUntilAsync(() => _fallback.Sessions.Count == 1);

        var takeover = _fallback.Sessions[0];
        takeover.Title.Should().Be("Extracting 3 archives");
        takeover.Items.Should().Equal(("b.zip", 2, 3));

        session.Progress!.Report(new ProgressReport { Percent = 50 });
        takeover.ProgressReports.Should().Be(1);

        (await session.AskConflictAsync(new ConflictInfo { ExistingPath = @"C:\a.txt" })).Resolution
            .Should().Be(ConflictResolution.Skip, "the fallback session answers now");
        _win32Conflicts.Should().BeEmpty();

        takeover.Cancel();
        session.Cancellation.IsCancellationRequested.Should().BeTrue();

        session.Complete(Warning);
        takeover.Completed.Should().BeTrue();
        _fallback.Messages.Should().Equal(Warning);
    }

    [Fact]
    public async Task HelperCrashWhileShowingTheResult_ShowsItThroughTheFallback()
    {
        using var session = await BeginReadyAsync();

        var complete = Task.Run(() => session.Complete(Warning));
        await _helper.ReadUntilAsync<Complete>();
        _helper.Crash();

        await complete.WaitAsync(WaitLimit);
        _fallback.Messages.Should().Equal(Warning);
        _fallback.Sessions.Should().BeEmpty("no progress window flashes just to show a result");
    }

    [Fact]
    public async Task DisposeWithoutComplete_ClosesTheWindow()
    {
        var session = await BeginReadyAsync(closeTimeout: WaitLimit);

        var dispose = Task.Run(session.Dispose);
        (await _helper.ReadUntilAsync<Complete>()).Result.Should().BeNull();
        await _helper.SendAsync(new WindowClosed());

        await dispose.WaitAsync(WaitLimit);
        _fallback.Sessions.Should().BeEmpty();
    }

    [Fact]
    public async Task DisposeWithAnUnresponsiveHelper_EndsItAfterTheTimeout()
    {
        var session = CreateUi(closeTimeout: TimeSpan.FromMilliseconds(200)).Begin("t", ProgressStyle.Bytes);
        await _helper.ReadUntilAsync<Begin>();
        await _helper.SendReadyAsync();

        await Task.Run(session.Dispose).WaitAsync(WaitLimit);

        _helper.Killed.Should().BeTrue();
        _fallback.Sessions.Should().BeEmpty();
    }
}
