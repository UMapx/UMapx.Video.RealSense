using Intel.RealSense;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UMapx.Imaging;

namespace RealSense.FaceID
{
    public static class Helpers
    {
        /// <summary>
        /// Unsafely converts a <see cref="VideoFrame"/> to <see cref="Bitmap"/>
        /// </summary>
        /// <param name="frame">Frame value</param>
        /// <param name="pixelFormat"></param>
        /// <returns></returns>
        public unsafe static Bitmap ToBitmap(VideoFrame frame, System.Windows.Media.PixelFormat pixelFormat)
        {
            var width = frame.Width;
            var height = frame.Height;

            var rectangle = new Rectangle(0, 0, width, height);
            var bitmap = new Bitmap(width, height);
            var bmData = bitmap.LockBits(rectangle, ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
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

    }
}
