using Intel.RealSense;
using System.Drawing;
using System.Drawing.Imaging;
using UMapx.Core;
using UMapx.Imaging;

namespace UMapx.Video.RealSense
{
    public static class DepthTransform
    {
        #region Internal methods

        /// <summary>
        /// Unsafely converts a <see cref="VideoFrame"/> to <see cref="Bitmap"/>.
        /// </summary>
        /// <param name="frame">Frame</param>
        /// <returns>Bitmap</returns>
        internal unsafe static Bitmap ToBitmap(this VideoFrame frame)
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
        internal static Bitmap ToBitmap(this DepthFrame depth)
        {
            return depth.ToArray().ToBitmap();
        }

        #endregion

        #region Public methods
        /// <summary>
        /// Unsafely converts a <see cref="DepthFrame"/> to <see cref="Bitmap"/>.
        /// </summary>
        /// <param name="frame">Frame</param>
        /// <returns>Bitmap</returns>
        internal unsafe static ushort[,] ToArray(this DepthFrame frame)
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

        /// <summary>
        /// Unsafely converts a <see cref="VideoFrame"/> to <see cref="Bitmap"/>.
        /// </summary>
        /// <param name="frame">Frame</param>
        /// <returns>Bitmap</returns>
        public unsafe static Bitmap ToBitmap(this ushort[,] frame)
        {
            var width = frame.GetLength(1);
            var height = frame.GetLength(0);
            var rectangle = new Rectangle(0, 0, width, height);
            var bitmap = new Bitmap(width, height);
            var bmData = bitmap.LockBits(rectangle, ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            var dst = (byte*)bmData.Scan0.ToPointer();
            var stride = bmData.Stride;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    var k = x * 3 + y * stride;
                    dst[k + 0] = dst[k + 1] = dst[k + 2] = Maths.Byte((float)frame[y, x] / byte.MaxValue);
                }
            }

            bitmap.Unlock(bmData);
            return bitmap;
        }

        /// <summary>
        /// Equalizes histogram of the depth.
        /// </summary>
        /// <param name="depth">Depth</param>
        /// <returns>Matrix</returns>
        public static ushort[,] Equalize(this ushort[,] depth)
        {
            var width = depth.GetLength(1);
            var height = depth.GetLength(0);
            var hist = ushort.MaxValue + 1;

            // histogram
            var H = new ushort[hist];

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    H[depth[y, x]]++;
                }
            }

            // cdf
            var factor = ushort.MaxValue / (float)(height * width);
            var cdf = new float[hist];

            // recursion
            cdf[0] = H[0];

            for (int i = 1; i < hist; i++)
            {
                cdf[i] = H[i] + cdf[i - 1];
            }

            // equalization
            var output = new ushort[height, width];

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    output[y, x] = (ushort)(cdf[depth[y, x]] * factor);
                }
            }

            return output;
        }

        /// <summary>
        /// Crops the depth.
        /// </summary>
        /// <param name="depth">Depth</param>
        /// <param name="rectangle">Rectangle</param>
        /// <returns></returns>
        public static ushort[,] Crop(this ushort[,] depth, Rectangle rectangle)
        {
            // image params
            int width = depth.GetLength(1);
            int height = depth.GetLength(0);

            // check section params
            int x = Maths.Range(rectangle.X, 0, width);
            int y = Maths.Range(rectangle.Y, 0, height);
            int w = Maths.Range(rectangle.Width, 0, width - x);
            int h = Maths.Range(rectangle.Height, 0, height - y);

            // exception
            if (x == 0 &&
                y == 0 &&
                w == 0 &&
                h == 0) return depth;

            // output
            var output = new ushort[w, h];

            for (int i = 0; i < w; i++)
            {
                for (int j = 0; j < h; j++)
                {
                    output[j, i] = depth[y + j, x + i];
                }
            }

            return output;
        }

        /// <summary>
        /// Converts the depth to the matrix.
        /// </summary>
        /// <param name="depth">Depth</param>
        /// <returns>Matrix</returns>
        public static float[,] ToFloat(this ushort[,] depth)
        {
            int h = depth.GetLength(0);
            int w = depth.GetLength(1);
            float[,] output = new float[h, w];

            for (int i = 0; i < h; i++)
            {
                for (int j = 0; j < w; j++)
                {
                    output[i, j] = depth[i, j] / (float)ushort.MaxValue;
                }
            }

            return output;
        }
        #endregion
    }
}
