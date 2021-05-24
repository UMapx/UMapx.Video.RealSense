using Intel.RealSense;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Media.Imaging;
using UMapx.Core;
using UMapx.Imaging;

namespace RealSense.FaceID
{
    public static class Helpers
    {
        /// <summary>
        /// Unsafely converts a <see cref="VideoFrame"/> to <see cref="Bitmap"/>
        /// </summary>
        /// <param name="frame">Frame value</param>
        /// <returns></returns>
        public unsafe static Bitmap ToBitmap(VideoFrame frame)
        {
            var width = frame.Width;
            var height = frame.Height;

            var rectangle = new Rectangle(0, 0, width, height);
            var bitmap = new Bitmap(width, height);
            var bmData = bitmap.LockBits(rectangle, ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            var dst = (byte*)bmData.Scan0.ToPointer();
            var src = (byte*)frame.Data.ToPointer();

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++, dst += 3, src += 3)
                {
                    dst[0] = src[2];
                    dst[1] = src[1];
                    dst[2] = src[0];
                }
            }

            bitmap.Unlock(bmData);
            return bitmap;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="frame"></param>
        /// <returns></returns>
        public unsafe static float[,] ToArray(VideoFrame frame)
        {
            var width = frame.Width;
            var height = frame.Height;
            var stride = frame.Stride;

            var rectangle = new Rectangle(0, 0, width, height);
            var src = (byte*)frame.Data.ToPointer();
            var dst = new float[height, width];

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    var k = x * 2 + stride * y;
                    //var bytes = new byte[] { src[k + 1], src[k + 0] };
                    //var val = src[k];
                    dst[y, x] = ((uint)src[k + 1] << 8) + src[k];
                }
            }

            return Normalize(dst);
        }

        /// <summary>
        /// Converts a <see cref="Bitmap"/> to <see cref="BitmapImage"/> 
        /// </summary>
        /// <param name="bitmap"></param>
        /// <returns></returns>
        public static BitmapImage ToBitmapImage(Bitmap bitmap)
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            var ms = new System.IO.MemoryStream();
            bitmap.Save(ms, ImageFormat.Bmp);
            ms.Seek(0, System.IO.SeekOrigin.Begin);
            bi.StreamSource = ms;
            bi.EndInit();
            return bi;
        }

        public static float[,] Normalize(float[,] array)
        {
            var max = array.Max().Max();
            var min = array.Min().Min();

            return array.Sub(min).Div(max - min);
        }
    }
}
