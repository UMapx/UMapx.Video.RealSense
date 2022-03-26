using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UMapx.Imaging;

namespace UMapx.Video.RealSense.Example
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : System.Windows.Window
    {
        #region Fields

        private readonly IVideoDepthSource _realSenseVideoSource;
        private static readonly object _locker = new object();
        private Bitmap _frame;
        private Bitmap _depth;

        #endregion

        #region Launcher

        /// <summary>
        /// Constructor.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            Closing += MainWindow_Closing;

            _realSenseVideoSource = new RealSenseVideoSource();
            _realSenseVideoSource.NewFrame += OnNewFrame;
            _realSenseVideoSource.NewDepth += OnNewDepth;
            _realSenseVideoSource.Start();
        }

        /// <summary>
        /// Windows closing.
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event args</param>
        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _realSenseVideoSource.SignalToStop();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Get frame and dispose previous.
        /// </summary>
        Bitmap Frame
        {
            get
            {
                if (_frame is null)
                    return null;

                Bitmap frame;

                lock (_locker)
                {
                    frame = (Bitmap)_frame.Clone();
                }

                return frame;
            }
            set
            {
                lock (_locker)
                {
                    if (_frame is object)
                    {
                        _frame.Dispose();
                        _frame = null;
                    }

                    _frame = value;
                }
            }
        }

        /// <summary>
        /// Gets depth and dispose previous.
        /// </summary>
        Bitmap Depth
        {
            get
            {
                if (_depth is null)
                    return null;

                Bitmap depth;

                lock (_locker)
                {
                    depth = (Bitmap)_depth.Clone();
                }

                return depth;
            }
            set
            {
                lock (_locker)
                {
                    if (_depth is object)
                    {
                        _depth.Dispose();
                        _depth = null;
                    }

                    _depth = value;
                }
            }
        }

        #endregion

        #region Handling events

        /// <summary>
        /// Frame handling on event call.
        /// </summary>
        /// <param name="sender">sender</param>
        /// <param name="eventArgs">event arguments</param>
        private void OnNewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            Frame = (Bitmap)eventArgs.Frame.Clone();
            InvokeDrawing();
        }

        /// <summary>
        /// Depth handling on event call.
        /// </summary>
        /// <param name="sender">sender</param>
        /// <param name="eventArgs">event arguments</param>
        private void OnNewDepth(object sender, NewDepthEventArgs eventArgs)
        {
            Depth = eventArgs.Depth.Equalize().FromDepth();
            InvokeDrawing();
        }

        #endregion

        #region Private voids

        /// <summary>
        /// Draw calculated <see cref="BitmapImage"/> based on <see cref="RealSenseVideoSource"/> bitmap converted frames
        /// in <see cref="Window"/> Image element
        /// </summary>
        private void InvokeDrawing()
        {
            try
            {
                // color drawing
                var printColor = Frame;

                if (printColor is object)
                {
                    var bitmapColor = ToBitmapImage(printColor);
                    bitmapColor.Freeze();
                    Dispatcher.BeginInvoke(new ThreadStart(delegate { imgColor.Source = bitmapColor; }));
                }

                // depth drawing
                var printDepth = Depth;
                
                if (printDepth is object)
                {
                    var bitmapDepth = ToBitmapImage(printDepth);
                    bitmapDepth.Freeze();
                    Dispatcher.BeginInvoke(new ThreadStart(delegate { imgDepth.Source = bitmapDepth; }));
                }
            }
            catch { }
        }

        /// <summary>
        /// Converts a <see cref="Bitmap"/> to <see cref="BitmapImage"/>.
        /// </summary>
        /// <param name="bitmap">Bitmap</param>
        /// <returns>BitmapImage</returns>
        private BitmapImage ToBitmapImage(Bitmap bitmap)
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Bmp);
            ms.Seek(0, SeekOrigin.Begin);
            bi.StreamSource = ms;
            bi.EndInit();
            return bi;
        }

        #endregion
    }
}
