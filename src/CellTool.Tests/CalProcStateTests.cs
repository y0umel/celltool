using CellTool.Services;
using Xunit;

namespace CellTool.Tests;

public class CalProcStateTests
{
    [Fact]
    public void MarkStable_SetsCStatsAndLast()
    {
        var state = new CalProcState(4);
        state.RdLogic[2] = 5;

        state.MarkStable(2);

        Assert.Equal(0x45, state.CStats[2]);
        Assert.Equal(5, state.Last[2]);
    }

    [Fact]
    public void StartWindow_UsesScanIndexAndPreviousLast()
    {
        var state = new CalProcState(4);
        state.Last[1] = 3;
        state.RdLogic[1] = 6;
        state.UCnt[1] = 9;
        state.ECnt[1] = 8;

        state.StartWindow(1, 5);

        Assert.Equal(5, state.SJPos[1]);
        Assert.Equal(5, state.EJPos[1]);
        Assert.Equal(3, state.CStats[1]);
        Assert.Equal(0, state.UCnt[1]);
        Assert.Equal(0, state.ECnt[1]);
        Assert.Equal(1, state.Stable[1]);
        Assert.Equal(6, state.Last[1]);
    }

    [Fact]
    public void ExtendWindow_MovesEqualStartBackOne()
    {
        var state = new CalProcState(4);
        state.SJPos[3] = 5;
        state.EJPos[3] = 5;

        state.ExtendWindow(3, 7);

        Assert.Equal(4, state.SJPos[3]);
        Assert.Equal(7, state.EJPos[3]);
    }
}
