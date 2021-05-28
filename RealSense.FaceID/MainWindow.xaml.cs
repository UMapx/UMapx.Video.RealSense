using FaceONNX;
using FaceONNX.Core;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using UMapx.Imaging;
using UMapx.Video;
using UMapx.Video.RealSense;

namespace RealSense.FaceID
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Fields
        private readonly IVideoDepthSource _realSenseVideoSource;
        private readonly FaceDetectorLight _faceDetectorLight = new FaceDetectorLight();
        private readonly Painter _painter = new Painter();
        private static object _locker = new object();
        private Bitmap _frame;
        private ushort[,] _depthFrame;
        private Thread _procTask = null;
        private System.Drawing.Rectangle _rectangle;

        #endregion

        #region Launcher

        public MainWindow()
        {
            InitializeComponent();

            var config = File.ReadAllText("Configurations/HighResHighAccuracyPreset.json");
            _realSenseVideoSource = new RealSenseVideoSource();
            _realSenseVideoSource.NewFrame += _realSenseVideoSource_NewFrame;
            _realSenseVideoSource.NewDepth += _realSenseVideoSource_NewDepth;
            _realSenseVideoSource.Start();
        }

        #endregion

        #region Processing Properties

        /// <summary>
        /// Get Color Bitmap and dispose previous
        /// </summary>
        Bitmap ColorBitmap
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
        /// Get Depth Bitmap and dispose previous
        /// </summary>
        ushort[,] DepthBitmap
        {
            get
            {
                if (_depthFrame is null)
                    return null;

                ushort[,] depthFrame;

                lock (_locker)
                {
                    depthFrame = (ushort[,])_depthFrame.Clone();
                }

                return depthFrame;
            }
            set
            {
                lock (_locker)
                {
                    if (_depthFrame is object)
                    {
                        _depthFrame = null;
                    }

                    _depthFrame = value;
                }
            }
        }

        public System.Drawing.Rectangle Rectangle
        {
            get
            {
                lock (_locker)
                    return _rectangle;
            }
            set
            {
                lock (_locker)
                    _rectangle = value;
            }
        }

        #endregion

        #region Frame Handling Methods

        /// <summary>
        /// Frame handling on event call.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="eventArgs"></param>
        private void _realSenseVideoSource_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            ColorBitmap = (Bitmap)eventArgs.Frame.Clone();
            InvokeDrawing();

            // do job
            if (_procTask == null && DepthBitmap is object)
            {
                // main process
                _procTask = new Thread(() => ProcessFrame(ColorBitmap, DepthBitmap))
                {
                    IsBackground = true,
                    Priority = ThreadPriority.Lowest
                };
                _procTask.Start();
            }
        }

        private void _realSenseVideoSource_NewDepth(object sender, NewDepthEventArgs eventArgs)
        {
            DepthBitmap = (ushort[,])eventArgs.Depth.Clone();
        }

        /// <summary>
        /// Frame processing for face recognition
        /// </summary>
        /// <param name="imageFrame"></param>
        private void ProcessFrame(Bitmap imageFrame, ushort[,] depthFrame)
        {
            // color
            Rectangle = _faceDetectorLight.Forward(imageFrame).FirstOrDefault();

            // drawing face rect if the face area is not empty (?)
            if (!Rectangle.IsEmpty)
            {
                InvokeDrawing();
            }

            // dispose
            imageFrame.Dispose();
            _procTask = null;
        }

        /// <summary>
        /// Draw calculated <see cref="BitmapImage"/> based on <see cref="RealSenseVideoSource"/> bitmap converted frames
        /// in <see cref="Window"/> Image element
        /// </summary>
        private void InvokeDrawing()
        {
            try
            {
                var paintData = new PaintData()
                {
                    Rectangle = this.Rectangle,
                    Title = string.Empty
                };

                // color drawing
                var printColor = ColorBitmap;

                if (printColor is object)
                {
                    lock (_locker) _painter.Draw(printColor, paintData);

                    var bitmapColor = ToBitmapImage(printColor);
                    bitmapColor.Freeze();
                    Dispatcher.BeginInvoke(new ThreadStart(delegate { imgColor.Source = bitmapColor; }));
                }

                // depth drawing
                var depth = DepthBitmap;

                if (depth is object)
                {
                    var depthBitmap = depth.Equalize().ToBitmap();
                    //var cropped = depth.Crop(_rectangle).Equalize().ToBitmap();

                    //BitmapTransform.Merge(depthBitmap, cropped, _rectangle);

                    var printDepth = depthBitmap;

                    if (printDepth is object)
                    {
                        lock (_locker) _painter.Draw(printDepth, paintData);

                        var bitmapDepth = ToBitmapImage(printDepth);
                        bitmapDepth.Freeze();
                        Dispatcher.BeginInvoke(new ThreadStart(delegate { imgDepth.Source = bitmapDepth; }));
                    }
                }
            }
            catch { }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Converts a <see cref="Bitmap"/> to <see cref="BitmapImage"/> 
        /// </summary>
        /// <param name="bitmap"></param>
        /// <returns></returns>
        public static BitmapImage ToBitmapImage(Bitmap bitmap)
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
