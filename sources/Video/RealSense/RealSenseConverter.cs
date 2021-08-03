using Intel.RealSense;
using System.Drawing;
using System.Drawing.Imaging;
using UMapx.Imaging;

namespace UMapx.Video.RealSense
{
    /// <summary>
    /// Internal class to convert RealSense frames into bitmaps.
    /// </summary>
    internal static class RealSenseConverter
    {
        /// <summary>
        /// Unsafely converts a <see cref="VideoFrame"/> to <see cref="Bitmap"/>.
        /// </summary>
        /// <param name="frame">Frame</param>
        /// <returns>Bitmap</returns>
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
                    // switch rgb to bgr
                    dst[0] = src[2];
                    dst[1] = src[1];
                    dst[2] = src[0];
                }
            }

            bitmap.Unlock(bmData);
            return bitmap;
        }

        /// <summary>
        /// Unsafely converts a <see cref="DepthFrame"/> to <see cref="Bitmap"/>.
        /// </summary>
        /// <param name="depth">Depth</param>
        /// <returns>Bitmap</returns>
        public static Bitmap ToBitmap(this DepthFrame depth)
        {
            return DepthMatrix.FromDepth(depth.ToArray());
        }

        /// <summary>
        /// Unsafely converts a <see cref="DepthFrame"/> to <see cref="Bitmap"/>.
        /// </summary>
        /// <param name="frame">Frame</param>
        /// <returns>Bitmap</returns>
        public unsafe static ushort[,] ToArray(this DepthFrame frame)
        {
            var width = frame.Width;
            var height = frame.Height;
            var stride = frame.Stride;
            var depth = new ushort[height, width];
            var src = (ushort*)frame.Data.ToPointer();

            // 16 bpp
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    var value = src[x + stride / 2 * y];
                    depth[y, x] = value;
                }
            }

            return depth;
        }
    }
}
