using UMapx.Video;
using static RealSense.FaceID.SensorEvents;

namespace RealSense.FaceID
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
