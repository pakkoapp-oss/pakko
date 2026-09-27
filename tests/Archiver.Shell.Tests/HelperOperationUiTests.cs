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
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("PakkoHelperUiTests");
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

    public HelperOperationUiTests() => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("uk-UA");

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _originalUiCulture;
        _helper.Dispose();
        _temp.Delete(recursive: true);
    }

    private HelperOperationUi CreateUi(TimeSpan? readyTimeout = null, TimeSpan? closeTimeout = null) =>
        new(_helper, _fallback)
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

    // The fallback session is listed by FakeOperationUi as soon as it is created, but used only once
    // HelperOperationUi has finished setting it up; ending the helper comes after that.
    private Task WaitUntilTakenOverAsync() => WaitUntilAsync(() => _fallback.Sessions.Count == 1 && _helper.Killed);

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
    public async Task BeginItem_DropsThePreviousArchivesSpeed()
    {
        using var session = await BeginReadyAsync();
        session.Progress!.Report(new ProgressReport { Percent = 0, BytesTransferred = 0, TotalBytes = 100_000_000 });
        await Task.Delay(TimeSpan.FromMilliseconds(400));
        var first = new ProgressReport { Percent = 90, BytesTransferred = 90_000_000, TotalBytes = 100_000_000 };
        session.Progress.Report(first);
        string? firstStatus = (await ReadProgressAsync(90)).Status;
        firstStatus.Should().StartWith(ProgressText.FormatStatus(first, null)).And.NotBe(ProgressText.FormatStatus(first, null));

        session.BeginItem("b.zip", 2, 2);
        var next = new ProgressReport { Percent = 1, BytesTransferred = 1_000, TotalBytes = 100_000_000 };
        session.Progress.Report(next);

        (await ReadProgressAsync(1)).Status.Should().Be(ProgressText.FormatStatus(next, null));
    }

    private async Task<Progress> ReadProgressAsync(int percent)
    {
        Progress progress;
        do
            progress = await _helper.ReadUntilAsync<Progress>();
        while (progress.Percent != percent);
        return progress;
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

    // Until step 5 moves prompts into the window, a Win32 prompt next to the helper window lost the
    // foreground to it when the window showed (found on device): a prompt hands the operation over.
    // T-F268 step 5: prompts are asked inside the window, not handed to the Win32 dialogs.
    [Fact]
    public async Task AConflict_IsAskedInTheWindowWithBothFilesDetails()
    {
        string existing = Path.Combine(_temp.FullName, "звіт.pdf");
        File.WriteAllBytes(existing, new byte[2048]);
        File.SetLastWriteTimeUtc(existing, new DateTime(2026, 9, 12, 11, 3, 0, DateTimeKind.Utc));
        using var session = await BeginReadyAsync();

        var asking = session.AskConflictAsync(new ConflictInfo
        {
            ExistingPath = existing,
            IncomingSize = 3 * 1_048_576,
            IncomingModified = new DateTimeOffset(2026, 9, 20, 6, 41, 0, TimeSpan.Zero),
        });
        var ask = await _helper.ReadUntilAsync<AskConflict>();

        ask.ExistingPath.Should().Be(existing);
        ask.ExistingDetails.Should().StartWith(ProgressText.FormatBytes(2048));
        ask.IncomingDetails.Should().StartWith(ProgressText.FormatBytes(3 * 1_048_576));
        ask.IncomingIsNewer.Should().BeTrue();
        asking.IsCompleted.Should().BeFalse();

        await _helper.SendAsync(new ConflictAnswer(ask.RequestId, ConflictChoice.Overwrite, ApplyToAll: true));

        var decision = await asking.WaitAsync(WaitLimit);
        decision.Resolution.Should().Be(ConflictResolution.Overwrite);
        decision.ApplyToAll.Should().BeTrue();
        _fallback.Sessions.Should().BeEmpty();
        _fallback.ConflictPrompts.Should().BeEmpty();
        _helper.Killed.Should().BeFalse();
    }

    [Fact]
    public async Task APassword_IsAskedInTheWindow()
    {
        using var session = await BeginReadyAsync();

        var asking = session.AskPasswordAsync(
            new PasswordPromptInfo { ArchiveName = "secret.zip", Purpose = PasswordPurpose.Decrypt, AttemptNumber = 2, PreviousAttemptWasWrong = true },
            canApplyToRemaining: true);
        var ask = await _helper.ReadUntilAsync<AskPassword>();
        ask.Should().Be(new AskPassword(ask.RequestId, "secret.zip", 2, PreviousAttemptWasWrong: true, CanApplyToRemaining: true));

        await _helper.SendAsync(new PasswordAnswer(ask.RequestId, "пароль", ApplyToRemaining: true));

        var decision = await asking.WaitAsync(WaitLimit);
        decision.Password.Should().Be("пароль");
        decision.ApplyToRemaining.Should().BeTrue();
        _fallback.PasswordPrompts.Should().BeEmpty();
    }

    [Fact]
    public async Task ExistingFileMissing_HasNoExistingDetails()
    {
        using var session = await BeginReadyAsync();

        _ = session.AskConflictAsync(new ConflictInfo { ExistingPath = Path.Combine(_temp.FullName, "not-there.txt") });

        var ask = await _helper.ReadUntilAsync<AskConflict>();
        ask.ExistingDetails.Should().BeNull();
        ask.IncomingDetails.Should().BeNull();
        ask.IncomingIsNewer.Should().BeFalse();
    }

    [Fact]
    public async Task CancelDuringAPrompt_AnswersItSafelyWithoutAsking()
    {
        using var session = await BeginReadyAsync();
        var conflict = session.AskConflictAsync(new ConflictInfo { ExistingPath = @"C:\a.txt" });
        var password = session.AskPasswordAsync(new PasswordPromptInfo { ArchiveName = "s.zip", Purpose = PasswordPurpose.Decrypt }, canApplyToRemaining: true);
        await _helper.ReadUntilAsync<AskPassword>();

        await _helper.SendAsync(new CancelRequested());

        // Answered on the cancel itself, before the window reports it closed.
        (await conflict.WaitAsync(WaitLimit)).Should().Be(new ConflictDecision { Resolution = ConflictResolution.Skip, ApplyToAll = false });
        (await password.WaitAsync(WaitLimit)).Should().Be(new PasswordDecision { Password = null, ApplyToRemaining = false });
        (await session.AskConflictAsync(new ConflictInfo { ExistingPath = @"C:.txt" }).WaitAsync(WaitLimit)).Resolution
            .Should().Be(ConflictResolution.Skip);
        await _helper.SendAsync(new WindowClosed());
        session.Cancellation.IsCancellationRequested.Should().BeTrue();
        _fallback.Sessions.Should().BeEmpty();
        _fallback.ConflictPrompts.Should().BeEmpty();
        _fallback.PasswordPrompts.Should().BeEmpty();
    }

    // Step 4 asked a Win32 dialog here; the operation is being cancelled, so nobody is asked now.
    [Fact]
    public async Task APromptAfterTheUserClosedTheWindow_IsAnsweredSafelyWithoutAsking()
    {
        using var session = await BeginReadyAsync();
        await _helper.SendAsync(new CancelRequested());
        await _helper.SendAsync(new WindowClosed());
        await WaitUntilAsync(() => session.Cancellation.IsCancellationRequested);

        var decision = await session.AskConflictAsync(new ConflictInfo { ExistingPath = @"C:\a.txt" }).WaitAsync(WaitLimit);

        decision.Resolution.Should().Be(ConflictResolution.Skip);
        decision.ApplyToAll.Should().BeFalse();
        _fallback.Sessions.Should().BeEmpty();
        _fallback.ConflictPrompts.Should().BeEmpty();
    }

    // The plan's "prompt open at crash -> re-asked via Win32": never decided for the user.
    [Fact]
    public async Task HelperCrashWhileAPromptIsOpen_ReasksItThroughTheFallback()
    {
        _fallback.ConflictAnswer = _ => new ConflictDecision { Resolution = ConflictResolution.Rename };
        using var session = await BeginReadyAsync();
        var asking = session.AskConflictAsync(new ConflictInfo { ExistingPath = @"C:\a.txt" });
        await _helper.ReadUntilAsync<AskConflict>();

        _helper.Crash();

        (await asking.WaitAsync(WaitLimit)).Resolution.Should().Be(ConflictResolution.Rename);
        _fallback.ConflictPrompts.Should().ContainSingle().Which.ExistingPath.Should().Be(@"C:\a.txt");
    }

    [Fact]
    public async Task APromptBeforeTheHelperIsReady_WaitsAndIsAskedInTheWindow()
    {
        using var session = CreateUi().Begin("t", ProgressStyle.Bytes);
        var asking = session.AskPasswordAsync(new PasswordPromptInfo { ArchiveName = "s.zip", Purpose = PasswordPurpose.Decrypt }, canApplyToRemaining: false);

        var ask = await _helper.ReadUntilAsync<AskPassword>();
        await _helper.SendReadyAsync();
        await _helper.SendAsync(new PasswordAnswer(ask.RequestId, "p", ApplyToRemaining: false));

        (await asking.WaitAsync(WaitLimit)).Password.Should().Be("p");
    }

    [Fact]
    public async Task APromptWhileTheHelperNeverBecomesReady_IsAskedThroughTheFallback()
    {
        using var session = CreateUi(readyTimeout: TimeSpan.FromMilliseconds(200)).Begin("t", ProgressStyle.Bytes);

        var decision = await session.AskPasswordAsync(new PasswordPromptInfo { ArchiveName = "s.zip", Purpose = PasswordPurpose.Decrypt }, canApplyToRemaining: false).WaitAsync(WaitLimit);

        decision.Password.Should().BeNull("the fallback's preset answer");
        _fallback.PasswordPrompts.Should().ContainSingle();
    }

    [Fact]
    public async Task DisposeWithAPromptOpen_AnswersIt()
    {
        var session = await BeginReadyAsync();
        var asking = session.AskConflictAsync(new ConflictInfo { ExistingPath = @"C:\a.txt" });
        await _helper.ReadUntilAsync<AskConflict>();

        session.Dispose();

        (await asking.WaitAsync(WaitLimit)).Resolution.Should().Be(ConflictResolution.Skip);
    }

    // --- Security & boundary ---

    [Fact]
    public async Task ApplyToRemaining_IsDroppedWhenItWasNotOffered()
    {
        using var session = await BeginReadyAsync();
        var asking = session.AskPasswordAsync(new PasswordPromptInfo { ArchiveName = "s.zip", Purpose = PasswordPurpose.Decrypt }, canApplyToRemaining: false);
        var ask = await _helper.ReadUntilAsync<AskPassword>();

        await _helper.SendAsync(new PasswordAnswer(ask.RequestId, "p", ApplyToRemaining: true));

        (await asking.WaitAsync(WaitLimit)).ApplyToRemaining.Should().BeFalse();
    }

    [Fact]
    public async Task AnswersWithAnUnknownIdOrOfTheWrongKind_AreIgnored()
    {
        using var session = await BeginReadyAsync();
        var asking = session.AskConflictAsync(new ConflictInfo { ExistingPath = @"C:\a.txt" });
        var ask = await _helper.ReadUntilAsync<AskConflict>();

        await _helper.SendAsync(new ConflictAnswer(ask.RequestId + 100, ConflictChoice.Overwrite, ApplyToAll: true));
        await _helper.SendAsync(new PasswordAnswer(ask.RequestId, "p", ApplyToRemaining: true));
        await Task.Delay(200);
        asking.IsCompleted.Should().BeFalse();

        await _helper.SendAsync(new ConflictAnswer(ask.RequestId, ConflictChoice.Rename, ApplyToAll: false));
        (await asking.WaitAsync(WaitLimit)).Resolution.Should().Be(ConflictResolution.Rename);
        _helper.Killed.Should().BeFalse();
    }

    // A helper that reports its window closed without a cancel (a bug) must not leave the
    // operation waiting on a prompt nobody can see.
    [Fact]
    public async Task WindowClosedWithoutACancel_AnswersOpenAndLaterPromptsSafely()
    {
        using var session = await BeginReadyAsync();
        var asking = session.AskConflictAsync(new ConflictInfo { ExistingPath = @"C:.txt" });
        await _helper.ReadUntilAsync<AskConflict>();

        await _helper.SendAsync(new WindowClosed());

        (await asking.WaitAsync(WaitLimit)).Resolution.Should().Be(ConflictResolution.Skip);
        (await session.AskPasswordAsync(new PasswordPromptInfo { ArchiveName = "s.zip", Purpose = PasswordPurpose.Decrypt }, canApplyToRemaining: false)
            .WaitAsync(WaitLimit)).Password.Should().BeNull();
        _fallback.PasswordPrompts.Should().BeEmpty();
    }

    [Fact]
    public async Task AnUndefinedConflictChoice_IsSkipNeverOverwrite()
    {
        using var session = await BeginReadyAsync();
        var asking = session.AskConflictAsync(new ConflictInfo { ExistingPath = @"C:\a.txt" });
        var ask = await _helper.ReadUntilAsync<AskConflict>();

        await _helper.SendAsync(new ConflictAnswer(ask.RequestId, (ConflictChoice)99, ApplyToAll: false));

        (await asking.WaitAsync(WaitLimit)).Resolution.Should().Be(ConflictResolution.Skip);
    }

    [Fact]
    public async Task ASecondAnswerToTheSamePrompt_IsIgnored()
    {
        using var session = await BeginReadyAsync();
        var asking = session.AskConflictAsync(new ConflictInfo { ExistingPath = @"C:\a.txt" });
        var ask = await _helper.ReadUntilAsync<AskConflict>();
        await _helper.SendAsync(new ConflictAnswer(ask.RequestId, ConflictChoice.Rename, ApplyToAll: false));
        (await asking.WaitAsync(WaitLimit)).Resolution.Should().Be(ConflictResolution.Rename);

        await _helper.SendAsync(new ConflictAnswer(ask.RequestId, ConflictChoice.Overwrite, ApplyToAll: true));
        var next = session.AskConflictAsync(new ConflictInfo { ExistingPath = @"C:\b.txt" });
        var nextAsk = await _helper.ReadUntilAsync<AskConflict>();

        nextAsk.RequestId.Should().NotBe(ask.RequestId);
        next.IsCompleted.Should().BeFalse();
    }

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

        await WaitUntilTakenOverAsync();
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

        await WaitUntilTakenOverAsync();

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
        await WaitUntilTakenOverAsync();

        var takeover = _fallback.Sessions[0];
        takeover.Title.Should().Be("Extracting 3 archives");
        takeover.Items.Should().Equal(("b.zip", 2, 3));

        session.Progress!.Report(new ProgressReport { Percent = 50 });
        takeover.ProgressReports.Should().Be(1);

        (await session.AskConflictAsync(new ConflictInfo { ExistingPath = @"C:\a.txt" })).Resolution
            .Should().Be(ConflictResolution.Skip, "the fallback session answers now");
        _fallback.ConflictPrompts.Should().ContainSingle();

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
