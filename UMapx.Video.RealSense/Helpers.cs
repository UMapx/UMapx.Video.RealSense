using Intel.RealSense;
using System.Drawing;
using System.Drawing.Imaging;
using UMapx.Imaging;

namespace UMapx.Video.RealSense
{
    public static class Helpers
    {
        /// <summary>
        /// Unsafely converts a <see cref="VideoFrame"/> to <see cref="Bitmap"/>
        /// </summary>
        /// <param name="frame">Frame value</param>
        /// <returns></returns>
        public unsafe static Bitmap ToBitmap(this VideoFrame frame)
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
    }
}
