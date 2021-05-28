namespace UMapx.Video.RealSense
{
    /// <summary>
    /// Sensor event handler class
    /// </summary>
    public class DepthEvent
    {
        /// <summary>
        /// New depth event handler.
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="eventArgs">Event arguments</param>
        public delegate void NewDepthEventHandler(object sender, NewDepthEventArgs eventArgs);
    }
}
