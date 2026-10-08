/// <summary>只记当前关序号，以及刚载入的场景是不是教学关。不做探索、模拟或逃生切换。</summary>
public class LevelStageMgr : BaseMgr<LevelStageMgr>
{
    public int LevelIndex { get; private set; }
    public bool Teaching { get; private set; }

    public void Note(int index, bool teaching)
    {
        LevelIndex = index;
        Teaching = teaching;
    }
}
