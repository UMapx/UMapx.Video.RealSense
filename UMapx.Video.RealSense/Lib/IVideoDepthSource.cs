using static UMapx.Video.RealSense.DepthEvent;

namespace UMapx.Video.RealSense
{
    /// <summary>
    /// Extension interface for handling Intel RealSense depth events.
    /// </summary>
    public interface IVideoDepthSource : IVideoSource
    {
        /// <summary>
        /// Handler of received frames
        /// </summary>
        public event NewDepthEventHandler NewDepth;
    }
}
