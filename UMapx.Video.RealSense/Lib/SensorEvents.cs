using System;
using System.Drawing;

namespace UMapx.Video.RealSense
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
        public NewSensorsEventArgs(Bitmap frame, ushort[,] depth)
        {
            this.Frame = frame;
            this.Depth = depth;
        }

        /// <summary>
        /// New frame from video source.
        /// </summary>
        /// 
        public Bitmap Frame { get; private set; }

        /// <summary>
        /// 
        /// </summary>
        public ushort[,] Depth { get; private set; }
    }
}
