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
        /// Converts a <see cref="VideoFrame"/> to <see cref="Bitmap"/>.
        /// </summary>
        /// <param name="frame">Frame</param>
        /// <returns>Bitmap</returns>
        public unsafe static Bitmap ToBitmap(this VideoFrame frame)
        {
            var width = frame.Width;
            var height = frame.Height;
            var rectangle = new Rectangle(0, 0, width, height);
            var bitmap = new Bitmap(width, height);
            try
            {
                var bmData = bitmap.LockBits(rectangle, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                try
                {
                    for (int y = 0; y < height; y++)
                    {
                        var src = (byte*)frame.Data.ToPointer() + y * frame.Stride;
                        var dst = (byte*)bmData.Scan0.ToPointer() + y * bmData.Stride;
                        for (int x = 0; x < width; x++, dst += 3, src += 3)
                        {
                            // SDK RGB and bitmap BGR rows may have different padding.
                            dst[0] = src[2];
                            dst[1] = src[1];
                            dst[2] = src[0];
                        }
                    }
                }
                finally { bitmap.UnlockBits(bmData); }
                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Converts a <see cref="DepthFrame"/> to <see cref="Bitmap"/>.
        /// </summary>
        /// <param name="depth">Depth</param>
        /// <returns>Bitmap</returns>
        public static Bitmap ToBitmap(this DepthFrame depth)
        {
            return DepthMatrix.FromDepth(depth.ToArray());
        }

        /// <summary>
        /// Converts a <see cref="DepthFrame"/> to <see cref="Bitmap"/>.
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
