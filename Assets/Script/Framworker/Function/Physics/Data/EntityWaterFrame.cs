namespace PhyData
{
    /// <summary>
    /// 职能：相位 2 发布的只读区域水事实，不持有资源或运动状态。
    /// 上缘入水决定淹没；整框跨水面另报告半露出。两类候选各核对同源许可，不由按键改写。
    /// </summary>
    public readonly struct EntityWaterFrame
    {
        public readonly bool submerged;
        public readonly bool swimmingAvailable;
        public readonly bool partlyAboveSurface; // 上缘露出、下缘仍低于同一水面
        public readonly bool surfaceSwimmingAvailable; // 同一半露出水源允许游泳
        public readonly float surfaceY; // 有效水平水面的世界高度；只在半露出事实成立时读取

        public EntityWaterFrame(bool submerged, bool swimmingAvailable, bool partlyAboveSurface = false,
            bool surfaceSwimmingAvailable = false, float surfaceY = 0f)
        {
            this.submerged = submerged;
            this.swimmingAvailable = swimmingAvailable;
            this.partlyAboveSurface = partlyAboveSurface;
            this.surfaceSwimmingAvailable = surfaceSwimmingAvailable;
            this.surfaceY = surfaceY;
        }
    }
}
