using Archiver.Core.Services.Sandbox;
using FluentAssertions;

namespace Archiver.Core.Tests.Services.Sandbox;

// T-F561: the rule a tar.exe run's writes are held to, without a process.
public sealed class JobWriteLimitTests
{
    private const long Slack = JobWriteLimit.PipeSlackBytes;

    [Theory]
    [InlineData(0, 0, 0, false)]
    [InlineData(Slack, 0, 0, false)]
    [InlineData(Slack + 1, 0, 0, true)]
    [InlineData(1000 + Slack, 0, 1000, false)]
    [InlineData(1000 + Slack + 1, 0, 1000, true)]
    [InlineData(1000 + Slack + 500, 500, 1000, false)]
    [InlineData(1000 + Slack + 501, 500, 1000, true)]
    public void WroteMoreThanDeclared_PastTheDeclaredBytesAndTheSlack_LessTheStdErrRead(
        long written, long stdErrRead, long declared, bool over) =>
        JobWriteLimit.WroteMoreThanDeclared(written, stdErrRead, declared).Should().Be(over);

    [Fact]
    public void WroteMoreThanDeclared_DeclaredNearTheLargestLong_DoesNotWrap()
    {
        JobWriteLimit.WroteMoreThanDeclared(long.MaxValue, 0, long.MaxValue - 1).Should().BeFalse();
        JobWriteLimit.WroteMoreThanDeclared(long.MaxValue, 0, long.MaxValue).Should().BeFalse();
    }

    [Fact]
    public void Finish_JobThatWroteNothing_IsWithinItsLimit()
    {
        using var job = SandboxJobObject.Create(1L << 30, TimeSpan.FromMinutes(1));
        using var limit = JobWriteLimit.Start(job, 0, new ByteCounter());

        job.WrittenBytes().Should().Be(0);
        limit.Finish().Should().BeFalse();
    }
}
