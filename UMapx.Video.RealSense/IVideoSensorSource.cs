using UMapx.Video;
using static UMapx.Video.RealSense.SensorEvents;

namespace UMapx.Video.RealSense
{
    /// <summary>
    /// Extension interface for handling Intel RealSense sensor events
    /// </summary>
    public interface IVideoSensorSource : IVideoSource
    {
        /// <summary>
        /// Handler of received frames
        /// </summary>
        public event NewSensorsEventHandler NewSensorsFrames;
    }
}
