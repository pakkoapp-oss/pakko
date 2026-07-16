using FluentAssertions;

namespace Archiver.App.Core.Tests;

public sealed class DeferredActionGateTests
{
    [Fact]
    public void Open_FlushesQueuedActionsInFifoOrder()
    {
        var gate = new DeferredActionGate();
        var order = new List<int>();

        gate.RunOrDefer(() => order.Add(1));
        gate.RunOrDefer(() => order.Add(2));
        gate.RunOrDefer(() => order.Add(3));
        order.Should().BeEmpty();

        gate.Open();

        order.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void RunOrDefer_AfterOpen_RunsImmediately()
    {
        var gate = new DeferredActionGate();
        gate.Open();
        var ran = false;

        gate.RunOrDefer(() => ran = true);

        ran.Should().BeTrue();
    }

    [Fact]
    public void Open_CalledTwice_DoesNotRerunAlreadyFlushedActions()
    {
        var gate = new DeferredActionGate();
        var runCount = 0;
        gate.RunOrDefer(() => runCount++);

        gate.Open();
        gate.Open();

        runCount.Should().Be(1);
    }

    [Fact]
    public void Open_ActionEnqueuedDuringFlush_RunsExactlyOnce()
    {
        var gate = new DeferredActionGate();
        var order = new List<string>();
        gate.RunOrDefer(() =>
        {
            order.Add("first");
            gate.RunOrDefer(() => order.Add("nested"));
        });

        gate.Open();

        order.Should().Equal("first", "nested");
    }

    [Fact]
    public void Cancel_BeforeOpen_DiscardsQueuedActionsWithoutRunningThem()
    {
        var gate = new DeferredActionGate();
        var ran = false;
        gate.RunOrDefer(() => ran = true);

        gate.Cancel();
        gate.Open();

        ran.Should().BeFalse();
    }
}
