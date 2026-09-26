using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using Archiver.OperationUi.Protocol;
using FluentAssertions;

namespace Archiver.OperationUi.Tests;

// T-F268 step 3: the frame format between Archiver.Shell and the operation window helper.
public sealed class FrameCodecTests
{
    private const int MaxPathChars = 32_767;

    public static TheoryData<ProtocolMessage> EveryMessage() => new()
    {
        new Hello(FrameCodec.ProtocolVersion, "uk-UA", RightToLeft: false,
            new Dictionary<string, string> { ["Cancel"] = "Скасувати", ["Close"] = "Закрити" }),
        new Begin("Розпакування 5 архівів", ProgressKind.Bytes),
        new Item("photos-2026.zip", 2, 5),
        new Progress(34, @"Відпустка\2026-07\IMG_0412.jpg", "34% · 1,2 ГБ з 3,5 ГБ"),
        new Progress(0, null, null),
        new AskConflict(7, @"D:\Проєкти\звіт.pdf", 1_234_567, new DateTimeOffset(2026, 9, 12, 14, 3, 0, TimeSpan.FromHours(3)), 1_400_000, null),
        new AskPassword(8, "secret.zip", 2, PreviousAttemptWasWrong: true, CanApplyToRemaining: true),
        new Complete(new ResultText(ResultSeverity.Warning, "Розпакування", "old.zip: пошкоджено")),
        new Complete(null),
        new HelperReady(FrameCodec.ProtocolVersion),
        new CancelRequested(),
        new ConflictAnswer(7, ConflictChoice.Rename, ApplyToAll: true),
        new PasswordAnswer(8, "пароль 🔑", ApplyToRemaining: false),
        new PasswordAnswer(8, null, ApplyToRemaining: false),
        new WindowClosed(),
    };

    // --- Happy path ---

    [Theory]
    [MemberData(nameof(EveryMessage))]
    public async Task EveryMessage_RoundTrips(ProtocolMessage message)
    {
        var decoded = await RoundTripAsync(message);

        decoded.Should().BeOfType(message.GetType());
        // Records compare by value, but a dictionary member compares by reference.
        if (message is Hello hello)
            ((Hello)decoded).Strings.Should().Equal(hello.Strings);
        else
            decoded.Should().Be(message);
    }

    [Fact]
    public async Task SeveralFrames_AreReadInOrderThenCleanEndOfStream()
    {
        using var stream = new MemoryStream();
        foreach (ProtocolMessage m in new ProtocolMessage[] { new Item("a.zip", 1, 2), new Item("b.zip", 2, 2), new WindowClosed() })
            stream.Write(FrameCodec.Encode(m));
        stream.Position = 0;

        (await FrameCodec.ReadAsync(stream, CancellationToken.None)).Should().Be(new Item("a.zip", 1, 2));
        (await FrameCodec.ReadAsync(stream, CancellationToken.None)).Should().Be(new Item("b.zip", 2, 2));
        (await FrameCodec.ReadAsync(stream, CancellationToken.None)).Should().Be(new WindowClosed());
        (await FrameCodec.ReadAsync(stream, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public void Cyrillic_IsWrittenAsUtf8NotEscaped()
    {
        byte[] frame = FrameCodec.Encode(new Item("звіт.zip", 1, 1));

        Encoding.UTF8.GetString(frame, 4, frame.Length - 4).Should().Contain("звіт.zip").And.Contain("\"type\":\"item\"");
    }

    [Fact]
    public async Task RealAnonymousPipe_CarriesFramesBothWays()
    {
        using var server = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
        using var client = new AnonymousPipeClientStream(PipeDirection.In, server.ClientSafePipeHandle);
        using var writer = new MessageWriter(server);

        var read = Task.Run(() => FrameCodec.ReadAsync(client, CancellationToken.None));
        await writer.WriteAsync(new AskPassword(1, "секрет.zip", 1, false, false), CancellationToken.None);

        (await read).Should().Be(new AskPassword(1, "секрет.zip", 1, false, false));
    }

    // --- Security & boundary ---

    [Fact]
    public async Task LongestCyrillicPath_InAConflict_FitsAndRoundTrips()
    {
        var message = new AskConflict(1, @"D:\" + new string('ї', MaxPathChars - 3), null, null, null, null);

        var decoded = await RoundTripAsync(message);

        decoded.Should().Be(message);
    }

    [Fact]
    public async Task ResultListingTenLongestPaths_FitsAndRoundTrips()
    {
        string line = new string('є', MaxPathChars) + ": доступ заборонено";
        var message = new Complete(new ResultText(ResultSeverity.Error, "Розпакування", string.Join('\n', Enumerable.Repeat(line, 10))));

        var decoded = await RoundTripAsync(message);

        decoded.Should().Be(message);
    }

    [Fact]
    public void MessageOverTheLimit_IsRejectedBeforeWriting()
    {
        var message = new Complete(new ResultText(ResultSeverity.Error, "x", new string('a', FrameCodec.MaxFrameBytes)));

        FluentActions.Invoking(() => FrameCodec.Encode(message)).Should().Throw<ProtocolException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(FrameCodec.MaxFrameBytes + 1)]
    [InlineData(int.MaxValue)]
    public async Task InvalidLengthField_IsRejected(int length)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, length);

        await FluentActions.Awaiting(() => FrameCodec.ReadAsync(new MemoryStream(header), CancellationToken.None))
            .Should().ThrowAsync<ProtocolException>();
    }

    [Fact]
    public void PasswordAnswer_ToString_HidesThePassword()
    {
        string text = new PasswordAnswer(3, "correct horse", ApplyToRemaining: true).ToString();

        text.Should().NotContain("correct horse").And.Contain("***").And.Contain("RequestId = 3");
    }

    [Fact]
    public void MalformedFrameHoldingAPassword_ErrorDoesNotQuoteIt()
    {
        byte[] payload = Encoding.UTF8.GetBytes("{\"type\":\"passwordAnswer\",\"requestId\":1,\"password\":\"correct horse\",\"applyToRemaining\":\"oops\"}");

        var error = FluentActions.Invoking(() => FrameCodec.Decode(payload)).Should().Throw<ProtocolException>().Which;

        error.ToString().Should().NotContain("correct horse");
        error.InnerException.Should().BeNull();
    }

    // --- Misuse & fool ---

    [Theory]
    [InlineData("{\"type\":\"format-disk\"}")]
    [InlineData("{\"requestId\":1}")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{\"type\":\"item\",\"name\":\"a.zip\",\"index\":1}")]
    public void InvalidPayload_IsAProtocolException(string json)
    {
        FluentActions.Invoking(() => FrameCodec.Decode(Encoding.UTF8.GetBytes(json))).Should().Throw<ProtocolException>();
    }

    [Fact]
    public void NullWhereTheRecordSaysNonNull_IsAProtocolException()
    {
        byte[] json = Encoding.UTF8.GetBytes("{\"type\":\"item\",\"name\":null,\"index\":1,\"count\":1}");

        FluentActions.Invoking(() => FrameCodec.Decode(json)).Should().Throw<ProtocolException>();
    }

    // --- Error path ---

    [Fact]
    public async Task StreamEndingInsideTheHeader_IsAProtocolException()
    {
        await FluentActions.Awaiting(() => FrameCodec.ReadAsync(new MemoryStream([5, 0]), CancellationToken.None))
            .Should().ThrowAsync<ProtocolException>();
    }

    [Fact]
    public async Task StreamEndingInsideThePayload_IsAProtocolException()
    {
        byte[] frame = FrameCodec.Encode(new Item("a.zip", 1, 1));

        await FluentActions.Awaiting(() => FrameCodec.ReadAsync(new MemoryStream(frame[..^3]), CancellationToken.None))
            .Should().ThrowAsync<ProtocolException>();
    }

    [Fact]
    public async Task EmptyStream_IsACleanEnd()
    {
        (await FrameCodec.ReadAsync(new MemoryStream(), CancellationToken.None)).Should().BeNull();
    }

    // --- Concurrency ---

    [Fact]
    public async Task ConcurrentWrites_NeverInterleaveFrames()
    {
        using var stream = new MemoryStream();
        using var writer = new MessageWriter(stream);

        await Task.WhenAll(Enumerable.Range(1, 200).Select(i =>
            Task.Run(() => writer.WriteAsync(new Progress(i % 100, new string('ф', i * 10), $"{i}"), CancellationToken.None))));

        stream.Position = 0;
        var seen = new List<string?>();
        while (await FrameCodec.ReadAsync(stream, CancellationToken.None) is Progress p)
            seen.Add(p.Status);
        seen.Should().HaveCount(200).And.OnlyHaveUniqueItems();
    }

    private static async Task<ProtocolMessage> RoundTripAsync(ProtocolMessage message)
    {
        using var stream = new MemoryStream(FrameCodec.Encode(message));
        return (await FrameCodec.ReadAsync(stream, CancellationToken.None))!;
    }
}
