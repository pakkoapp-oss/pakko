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
    private TaskCompletionSource _readySeen = new();
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("PakkoHelperUiTests");
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

    public HelperOperationUiTests() => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("uk-UA");

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _originalUiCulture;
        _helper.Dispose();
        _temp.Delete(recursive: true);
    }

    private HelperOperationUi CreateUi(TimeSpan? readyTimeout = null, TimeSpan? closeTimeout = null)
    {
        var readySeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _readySeen = readySeen;
        return new(_helper, _fallback)
        {
            ReadyTimeout = readyTimeout ?? WaitLimit,
            CloseTimeout = closeTimeout ?? ShortClose,
            ProgressInterval = TimeSpan.Zero,
            // These tests are about a started helper; when it starts is DeferredOperationSessionTests'.
            StartDelay = TimeSpan.Zero,
            HelperBecameReady = () => readySeen.TrySetResult(),
        };
    }

    private async Task<IOperationSession> BeginReadyAsync(
        string title = "Extracting: a.zip", ProgressStyle style = ProgressStyle.Bytes, TimeSpan? closeTimeout = null)
    {
        IOperationSession session = CreateUi(closeTimeout: closeTimeout).Begin(title, style);
        await _helper.ReadUntilAsync<Begin>();
        await _helper.SendReadyAsync();
        // T-F351: a clean end before HelperReady was taken in ends the helper instead of closing its window.
        await _readySeen.Task.WaitAsync(WaitLimit);
        return session;
    }

    // The fallback session is listed by FakeOperationUi as soon as it is created, but used only once
    // HelperOperationUi has finished setting it up; ending the helper comes after that.
    private Task WaitUntilTakenOverAsync() => WaitUntilAsync(() => _fallback.Sessions.Count == 1 && _helper.Killed);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime limit = DateTime.UtcNow + WaitLimit;
        while (!condition())
        {
            if (DateTime.UtcNow > limit)
                throw new TimeoutException("The condition never became true.");
            await Task.Delay(20);
        }
    }

    // --- T-F356: the helper starts only when the operation is not fast ---

    private HelperOperationUi CreateDeferredUi(TimeSpan startDelay) => new(_helper, _fallback)
    {
        ReadyTimeout = WaitLimit,
        CloseTimeout = ShortClose,
        ProgressInterval = TimeSpan.Zero,
        StartDelay = startDelay,
    };

    [Fact]
    public void FastCleanOperation_NeverStartsTheHelper()
    {
        using (IOperationSession session = CreateDeferredUi(TimeSpan.FromMinutes(10)).Begin("Extracting: a.zip", ProgressStyle.Bytes))
        {
            session.BeginItem("a.zip", 1, 1);
            session.Progress!.Report(new ProgressReport { Percent = 100 });
            session.Complete(null);
        }

        _helper.Launches.Should().Be(0);
        _fallback.Sessions.Should().BeEmpty();
    }

    [Fact]
    public async Task OperationLongerThanTheDelay_StartsTheHelperAndTellsItHowLongTheOperationHasRun()
    {
        using IOperationSession session = CreateDeferredUi(TimeSpan.FromMilliseconds(200)).Begin("Extracting: a.zip", ProgressStyle.Bytes);
        session.BeginItem("a.zip", 1, 1);
        await WaitUntilAsync(() => _helper.Launches == 1);

        Begin begin = await _helper.ReadUntilAsync<Begin>();
        Item item = await _helper.ReadUntilAsync<Item>();

        begin.Title.Should().Be("Extracting: a.zip");
        begin.ElapsedMs.Should().BeInRange(150, 5000);
        item.Should().Be(new Item("a.zip", 1, 1));
    }

    [Fact]
    public async Task PromptBeforeTheDelay_StartsTheHelperAtOnceAndItsAnswerComesBack()
    {
        using IOperationSession session = CreateDeferredUi(TimeSpan.FromMinutes(10)).Begin("Extracting: a.zip", ProgressStyle.Bytes);

        Task<bool> answer = session.ConfirmAsync(new ConfirmPrompt("t", "m", "yes", "no"));
        await _helper.SendReadyAsync();
        AskConfirm ask = await _helper.ReadUntilAsync<AskConfirm>();
        await _helper.SendAsync(new ConfirmAnswer(ask.RequestId, true));

        (await answer.WaitAsync(WaitLimit)).Should().BeTrue();
        _helper.Launches.Should().Be(1);
    }

    [Fact]
    public void OperationThatEndsWithAResult_StartsTheHelperAtOnce()
    {
        using IOperationSession session = CreateDeferredUi(TimeSpan.FromMinutes(10))
            .Begin("Testing: a.zip", ProgressStyle.Bytes, endsWithResult: true);

        _helper.Launches.Should().Be(1);
    }

    [Fact]
    public void HelperCannotStartWhenItIsNeeded_TheResultIsShownByTheFallback()
    {
        _helper.FailToLaunch = true;
        using IOperationSession session = CreateDeferredUi(TimeSpan.FromMinutes(10)).Begin("Extracting: a.zip", ProgressStyle.Bytes);

        session.Complete(Warning);

        _fallback.Sessions.Should().ContainSingle();
        _fallback.Messages.Should().Equal(Warning);
    }

    // --- Happy path ---

    [Fact]
    public async Task Begin_SendsHelloThenBegin()
    {
        using IOperationSession session = CreateUi().Begin("Розпакування: звіт.zip", ProgressStyle.Percent);

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
        using IOperationSession session = await BeginReadyAsync(style: ProgressStyle.Percent);

        session.Progress!.Report(new ProgressReport { Percent = 40, CurrentFile = @"папка\a.txt" });

        (await _helper.ReadUntilAsync<Progress>()).Should().Be(new Progress(40, @"папка\a.txt", "40%"));
    }

    [Fact]
    public async Task BeginItem_NamesTheArchive()
    {
        using IOperationSession session = await BeginReadyAsync();

        session.BeginItem("b.zip", 2, 3);

        (await _helper.ReadUntilAsync<Item>()).Should().Be(new Item("b.zip", 2, 3));
    }

    [Fact]
    public async Task BeginItem_DropsThePreviousArchivesSpeed()
    {
        using IOperationSession session = await BeginReadyAsync();
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
        using IOperationSession session = await BeginReadyAsync(closeTimeout: WaitLimit);

        var complete = Task.Run(() => session.Complete(null));
        (await _helper.ReadUntilAsync<Complete>()).Result.Should().BeNull();
        await Task.Delay(100);
        complete.IsCompleted.Should().BeFalse("it waits for the helper to confirm the window closed");
        await _helper.SendAsync(new WindowClosed());

        await complete.WaitAsync(WaitLimit);
        _fallback.Sessions.Should().BeEmpty();
        _fallback.Messages.Should().BeEmpty();
    }

    // T-F351: before HelperReady nothing is on screen, so a clean end has no window to wait for.
    [Fact]
    public async Task CleanCompleteBeforeTheHelperIsReady_EndsItWithoutWaiting()
    {
        using IOperationSession session = CreateUi(readyTimeout: WaitLimit * 3, closeTimeout: WaitLimit * 3).Begin("t", ProgressStyle.Bytes);
        await _helper.ReadUntilAsync<Begin>();

        await Task.Run(() => session.Complete(null)).WaitAsync(WaitLimit);

        _helper.Killed.Should().BeTrue();
        _fallback.Sessions.Should().BeEmpty();
        _fallback.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task DisposeWithoutCompleteBeforeTheHelperIsReady_EndsItWithoutWaiting()
    {
        IOperationSession session = CreateUi(readyTimeout: WaitLimit * 3, closeTimeout: WaitLimit * 3).Begin("t", ProgressStyle.Bytes);
        await _helper.ReadUntilAsync<Begin>();

        await Task.Run(session.Dispose).WaitAsync(WaitLimit);

        _helper.Killed.Should().BeTrue();
        _fallback.Sessions.Should().BeEmpty();
    }

    [Fact]
    public async Task ResultBeforeTheHelperIsReady_StillWaitsForTheWindow()
    {
        using IOperationSession session = CreateUi(closeTimeout: WaitLimit).Begin("t", ProgressStyle.Bytes);
        await _helper.ReadUntilAsync<Begin>();

        var complete = Task.Run(() => session.Complete(Warning));
        (await _helper.ReadUntilAsync<Complete>()).Result.Should().NotBeNull();
        await Task.Delay(100);
        complete.IsCompleted.Should().BeFalse("the result is shown once the helper is up");
        _helper.Killed.Should().BeFalse();
        await _helper.SendReadyAsync();
        await _helper.SendAsync(new WindowClosed());

        await complete.WaitAsync(WaitLimit);
        _fallback.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task PreformattedResult_ReachesTheWindowPreformatted()
    {
        using IOperationSession session = await BeginReadyAsync();

        _ = Task.Run(() => session.Complete(new OperationMessage("SHA-256", MessageSeverity.Information, "a.txt: 00", Preformatted: true)));

        (await _helper.ReadUntilAsync<Complete>()).Result.Should().Be(
            new ResultText(ResultSeverity.Information, "SHA-256", "a.txt: 00", Preformatted: true));
    }

    [Fact]
    public async Task Result_WaitsUntilTheUserClosesIt()
    {
        using IOperationSession session = await BeginReadyAsync();

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
        using IOperationSession session = await BeginReadyAsync();

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
    // T-F217
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AQuestion_IsAskedInTheWindowAndAnswered(bool confirmed)
    {
        using IOperationSession session = await BeginReadyAsync();

        Task<bool> asking = session.ConfirmAsync(new ConfirmPrompt("Підозрілий архів", "bomb.zip", "Видобути", "Пропустити"));
        AskConfirm ask = await _helper.ReadUntilAsync<AskConfirm>();
        ask.Message.Should().Be("bomb.zip");
        asking.IsCompleted.Should().BeFalse();

        await _helper.SendAsync(new ConfirmAnswer(ask.RequestId, confirmed));

        (await asking.WaitAsync(WaitLimit)).Should().Be(confirmed);
    }

    [Fact]
    public async Task AQuestion_WhenTheWindowCloses_IsANo()
    {
        using IOperationSession session = await BeginReadyAsync();
        Task<bool> asking = session.ConfirmAsync(new ConfirmPrompt("T", "M", "Yes", "No"));
        await _helper.ReadUntilAsync<AskConfirm>();

        await _helper.SendAsync(new WindowClosed());

        (await asking.WaitAsync(WaitLimit)).Should().BeFalse();
    }

    [Fact]
    public async Task AConflict_IsAskedInTheWindowWithBothFilesDetails()
    {
        string existing = Path.Combine(_temp.FullName, "звіт.pdf");
        File.WriteAllBytes(existing, new byte[2048]);
        File.SetLastWriteTimeUtc(existing, new DateTime(2026, 9, 12, 11, 3, 0, DateTimeKind.Utc));
        using IOperationSession session = await BeginReadyAsync();

        Task<ConflictDecision> asking = session.AskConflictAsync(new ConflictInfo
        {
            ExistingPath = existing,
            IncomingSize = 3 * 1_048_576,
            IncomingModified = new DateTimeOffset(2026, 9, 20, 6, 41, 0, TimeSpan.Zero),
        });
        AskConflict ask = await _helper.ReadUntilAsync<AskConflict>();

        ask.ExistingPath.Should().Be(existing);
        ask.ExistingDetails.Should().StartWith(ProgressText.FormatBytes(2048));
        ask.IncomingDetails.Should().StartWith(ProgressText.FormatBytes(3 * 1_048_576));
        ask.IncomingIsNewer.Should().BeTrue();
        asking.IsCompleted.Should().BeFalse();

        await _helper.SendAsync(new ConflictAnswer(ask.RequestId, ConflictChoice.Overwrite, ApplyToAll: true));

        ConflictDecision decision = await asking.WaitAsync(WaitLimit);
        decision.Resolution.Should().Be(ConflictResolution.Overwrite);
        decision.ApplyToAll.Should().BeTrue();
        _fallback.Sessions.Should().BeEmpty();
        _fallback.ConflictPrompts.Should().BeEmpty();
        _helper.Killed.Should().BeFalse();
    }

    [Fact]
    public async Task APassword_IsAskedInTheWindow()
    {
        using IOperationSession session = await BeginReadyAsync();

        Task<PasswordDecision> asking = session.AskPasswordAsync(
            new PasswordPromptInfo { ArchiveName = "secret.zip", Purpose = PasswordPurpose.Decrypt, AttemptNumber = 2, PreviousAttemptWasWrong = true },
            canApplyToRemaining: true);
        AskPassword ask = await _helper.ReadUntilAsync<AskPassword>();
        ask.Should().Be(new AskPassword(ask.RequestId, "secret.zip", 2, PreviousAttemptWasWrong: true, CanApplyToRemaining: true));

        await _helper.SendAsync(new PasswordAnswer(ask.RequestId, "пароль", ApplyToRemaining: true));

        PasswordDecision decision = await asking.WaitAsync(WaitLimit);
        decision.Password.Should().Be("пароль");
        decision.ApplyToRemaining.Should().BeTrue();
        _fallback.PasswordPrompts.Should().BeEmpty();
    }

    [Fact]
    public async Task ExistingFileMissing_HasNoExistingDetails()
    {
        using IOperationSession session = await BeginReadyAsync();

        _ = session.AskConflictAsync(new ConflictInfo { ExistingPath = Path.Combine(_temp.FullName, "not-there.txt") });

        AskConflict ask = await _helper.ReadUntilAsync<AskConflict>();
        ask.ExistingDetails.Should().BeNull();
        ask.IncomingDetails.Should().BeNull();
        ask.IncomingIsNewer.Should().BeFalse();
    }

    [Fact]
    public async Task CancelDuringAPrompt_AnswersItSafelyWithoutAsking()
    {
        using IOperationSession session = await BeginReadyAsync();
        Task<ConflictDecision> conflict = session.AskConflictAsync(new ConflictInfo { ExistingPath = @"C:\a.txt" });
        Task<PasswordDecision> password = session.AskPasswordAsync(new PasswordPromptInfo { ArchiveName = "s.zip", Purpose = PasswordPurpose.Decrypt }, canApplyToRemaining: true);
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
        using IOperationSession session = await BeginReadyAsync();
        await _helper.SendAsync(new CancelRequested());
        await _helper.SendAsync(new WindowClosed());
        await WaitUntilAsync(() => session.Cancellation.IsCancellationRequested);

        ConflictDecision decision = await session.AskConflictAsync(new ConflictInfo { ExistingPath = @"C:\a.txt" }).WaitAsync(WaitLimit);

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
        using IOperationSession session = await BeginReadyAsync();
        Task<ConflictDecision> asking = session.AskConflictAsync(new ConflictInfo { ExistingPath = @"C:\a.txt" });
        await _helper.ReadUntilAsync<AskConflict>();

        _helper.Crash();

        (await asking.WaitAsync(WaitLimit)).Resolution.Should().Be(ConflictResolution.Rename);
        _fallback.ConflictPrompts.Should().ContainSingle().Which.ExistingPath.Should().Be(@"C:\a.txt");
    }

    [Fact]
    public async Task APromptBeforeTheHelperIsReady_WaitsAndIsAskedInTheWindow()
    {
        using IOperationSession session = CreateUi().Begin("t", ProgressStyle.Bytes);
        Task<PasswordDecision> asking = session.AskPasswordAsync(new PasswordPromptInfo { ArchiveName = "s.zip", Purpose = PasswordPurpose.Decrypt }, canApplyToRemaining: false);

        AskPassword ask = await _helper.ReadUntilAsync<AskPassword>();
        await _helper.SendReadyAsync();
        await _helper.SendAsync(new PasswordAnswer(ask.RequestId, "p", ApplyToRemaining: false));

        (await asking.WaitAsync(WaitLimit)).Password.Should().Be("p");
    }

    [Fact]
    public async Task APromptWhileTheHelperNeverBecomesReady_IsAskedThroughTheFallback()
    {
        using IOperationSession session = CreateUi(readyTimeout: TimeSpan.FromMilliseconds(200)).Begin("t", ProgressStyle.Bytes);

        PasswordDecision decision = await session.AskPasswordAsync(new PasswordPromptInfo { ArchiveName = "s.zip", Purpose = PasswordPurpose.Decrypt }, canApplyToRemaining: false).WaitAsync(WaitLimit);

        decision.Password.Should().BeNull("the fallback's preset answer");
        _fallback.PasswordPrompts.Should().ContainSingle();
    }

    [Fact]
    public async Task DisposeWithAPromptOpen_AnswersIt()
    {
        IOperationSession session = await BeginReadyAsync();
        Task<ConflictDecision> asking = session.AskConflictAsync(new ConflictInfo { ExistingPath = @"C:\a.txt" });
        await _helper.ReadUntilAsync<AskConflict>();

        session.Dispose();

        (await asking.WaitAsync(WaitLimit)).Resolution.Should().Be(ConflictResolution.Skip);
    }

    // --- Security & boundary ---

    [Fact]
    public async Task ApplyToRemaining_IsDroppedWhenItWasNotOffered()
    {
        using IOperationSession session = await BeginReadyAsync();
        Task<PasswordDecision> asking = session.AskPasswordAsync(new PasswordPromptInfo { ArchiveName = "s.zip", Purpose = PasswordPurpose.Decrypt }, canApplyToRemaining: false);
        AskPassword ask = await _helper.ReadUntilAsync<AskPassword>();

        await _helper.SendAsync(new PasswordAnswer(ask.RequestId, "p", ApplyToRemaining: true));

        (await asking.WaitAsync(WaitLimit)).ApplyToRemaining.Should().BeFalse();
    }

    [Fact]
    public async Task AnswersWithAnUnknownIdOrOfTheWrongKind_AreIgnored()
    {
        using IOperationSession session = await BeginReadyAsync();
        Task<ConflictDecision> asking = session.AskConflictAsync(new ConflictInfo { ExistingPath = @"C:\a.txt" });
        AskConflict ask = await _helper.ReadUntilAsync<AskConflict>();

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
        using IOperationSession session = await BeginReadyAsync();
        Task<ConflictDecision> asking = session.AskConflictAsync(new ConflictInfo { ExistingPath = @"C:.txt" });
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
        using IOperationSession session = await BeginReadyAsync();
        Task<ConflictDecision> asking = session.AskConflictAsync(new ConflictInfo { ExistingPath = @"C:\a.txt" });
        AskConflict ask = await _helper.ReadUntilAsync<AskConflict>();

        await _helper.SendAsync(new ConflictAnswer(ask.RequestId, (ConflictChoice)99, ApplyToAll: false));

        (await asking.WaitAsync(WaitLimit)).Resolution.Should().Be(ConflictResolution.Skip);
    }

    [Fact]
    public async Task ASecondAnswerToTheSamePrompt_IsIgnored()
    {
        using IOperationSession session = await BeginReadyAsync();
        Task<ConflictDecision> asking = session.AskConflictAsync(new ConflictInfo { ExistingPath = @"C:\a.txt" });
        AskConflict ask = await _helper.ReadUntilAsync<AskConflict>();
        await _helper.SendAsync(new ConflictAnswer(ask.RequestId, ConflictChoice.Rename, ApplyToAll: false));
        (await asking.WaitAsync(WaitLimit)).Resolution.Should().Be(ConflictResolution.Rename);

        await _helper.SendAsync(new ConflictAnswer(ask.RequestId, ConflictChoice.Overwrite, ApplyToAll: true));
        Task<ConflictDecision> next = session.AskConflictAsync(new ConflictInfo { ExistingPath = @"C:\b.txt" });
        AskConflict nextAsk = await _helper.ReadUntilAsync<AskConflict>();

        nextAsk.RequestId.Should().NotBe(ask.RequestId);
        next.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task ProgressFlood_NeverBlocksTheOperationAndTheLatestReportArrives()
    {
        using IOperationSession session = CreateUi().Begin("t", ProgressStyle.Percent);

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
        using IOperationSession session = await BeginReadyAsync();

        var complete = Task.Run(() => session.Complete(null));
        await _helper.ReadUntilAsync<Complete>();
        session.Progress!.Report(new ProgressReport { Percent = 99 });

        Task<ProtocolMessage?> next = _helper.ReadAsync();
        (await Task.WhenAny(next, Task.Delay(300))).Should().NotBe(next);
        await _helper.SendAsync(new WindowClosed());
        await complete.WaitAsync(WaitLimit);
    }

    // --- Misuse ---

    [Fact]
    public async Task UserClosedAsTheOperationFinished_ResultGoesToTheFallback()
    {
        using IOperationSession session = await BeginReadyAsync();
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
        using IOperationSession session = CreateUi(readyTimeout: TimeSpan.FromMinutes(5)).Begin("t", ProgressStyle.Bytes);
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

        using IOperationSession session = CreateUi().Begin("Extracting: a.zip", ProgressStyle.Bytes);

        session.Should().BeOfType<FakeOperationSession>();
        _fallback.Sessions.Should().ContainSingle().Which.Title.Should().Be("Extracting: a.zip");
    }

    [Fact]
    public async Task HelperNotReadyInTime_FailsOverAndIsEnded()
    {
        using IOperationSession session = CreateUi(readyTimeout: TimeSpan.FromMilliseconds(200)).Begin("t", ProgressStyle.Bytes);

        await WaitUntilTakenOverAsync();

        _helper.Killed.Should().BeTrue();
        session.Progress!.Report(new ProgressReport { Percent = 5 });
        _fallback.Sessions[0].ProgressReports.Should().Be(1);
    }

    [Fact]
    public async Task HelperCrashMidOperation_TheFallbackTakesOverTheRest()
    {
        using IOperationSession session = await BeginReadyAsync("Extracting 3 archives");
        session.BeginItem("b.zip", 2, 3);
        await _helper.ReadUntilAsync<Item>();

        _helper.Crash();
        await WaitUntilTakenOverAsync();

        FakeOperationSession takeover = _fallback.Sessions[0];
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
        using IOperationSession session = await BeginReadyAsync();

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
        IOperationSession session = await BeginReadyAsync(closeTimeout: WaitLimit);

        var dispose = Task.Run(session.Dispose);
        (await _helper.ReadUntilAsync<Complete>()).Result.Should().BeNull();
        await _helper.SendAsync(new WindowClosed());

        await dispose.WaitAsync(WaitLimit);
        _fallback.Sessions.Should().BeEmpty();
    }

    [Fact]
    public async Task DisposeWithAnUnresponsiveHelper_EndsItAfterTheTimeout()
    {
        IOperationSession session = CreateUi(closeTimeout: TimeSpan.FromMilliseconds(200)).Begin("t", ProgressStyle.Bytes);
        await _helper.ReadUntilAsync<Begin>();
        await _helper.SendReadyAsync();

        await Task.Run(session.Dispose).WaitAsync(WaitLimit);

        _helper.Killed.Should().BeTrue();
        _fallback.Sessions.Should().BeEmpty();
    }
}
