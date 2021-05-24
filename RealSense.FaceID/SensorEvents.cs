using System;
using System.Drawing;

namespace RealSense.FaceID
{
    /// <summary>
    /// Sensor event handler class
    /// </summary>
    public class SensorEvents
    {
        public delegate void NewSensorsEventHandler(object sender, NewSensorsEventArgs eventArgs);
    }

    /// <summary>
    /// Arguments for new frame event from video source.
    /// </summary>
    public class NewSensorsEventArgs : EventArgs
    {

        /// <summary>
        /// Initializes a new instance of the <see cref="NewSensorsEventArgs"/> class.
        /// </summary>
        /// 
        /// <param name="frames">New frame.</param>
        /// 
        public NewSensorsEventArgs(params Bitmap[] frames)
        {
            this.Frames = frames;
        }

        /// <summary>
        /// New frame from video source.
        /// </summary>
        /// 
        public Bitmap[] Frames { get; private set; }
    }
}
