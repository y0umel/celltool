namespace CellTool.Services;

public sealed class CalProcState
{
    public const byte StableMark = 0x40;
    public const byte AbnormalMark = 0xD2;

    public CalProcState(int cellCount)
    {
        if (cellCount < 0)
            throw new ArgumentOutOfRangeException(nameof(cellCount));

        RdLogic = new byte[cellCount];
        Last = new byte[cellCount];
        CStats = new byte[cellCount];
        JCnt = new byte[cellCount];
        SJPos = new byte[cellCount];
        EJPos = new byte[cellCount];
        Stable = new byte[cellCount];
        ECnt = new byte[cellCount];
        UCnt = new byte[cellCount];
    }

    public byte[] RdLogic { get; }
    public byte[] Last { get; }
    public byte[] CStats { get; }
    public byte[] JCnt { get; }
    public byte[] SJPos { get; }
    public byte[] EJPos { get; }
    public byte[] Stable { get; }
    public byte[] ECnt { get; }
    public byte[] UCnt { get; }

    public void MarkStable(int cell)
    {
        CheckCell(cell);
        CStats[cell] = (byte)(StableMark + RdLogic[cell]);
        Last[cell] = RdLogic[cell];
    }

    public void StartWindow(int cell, int scanIndex)
    {
        CheckCell(cell);
        byte value = unchecked((byte)scanIndex);
        SJPos[cell] = value;
        EJPos[cell] = value;
        CStats[cell] = Last[cell];
        UCnt[cell] = 0;
        ECnt[cell] = 0;
        Stable[cell] = 1;
        Last[cell] = RdLogic[cell];
    }

    public void ExtendWindow(int cell, int scanIndex)
    {
        CheckCell(cell);
        if (SJPos[cell] == EJPos[cell])
            SJPos[cell] = unchecked((byte)(SJPos[cell] - 1));

        EJPos[cell] = unchecked((byte)scanIndex);
    }

    private void CheckCell(int cell)
    {
        if ((uint)cell >= (uint)RdLogic.Length)
            throw new IndexOutOfRangeException(cell.ToString());
    }
}
