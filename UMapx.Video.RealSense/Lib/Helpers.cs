using Intel.RealSense;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using UMapx.Core;
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

        /// <summary>
        /// Unsafely converts a <see cref="VideoFrame"/> to <see cref="Bitmap"/>
        /// </summary>
        /// <param name="frame">Frame value</param>
        /// <returns></returns>
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
        /// Unsafely converts a <see cref="VideoFrame"/> to <see cref="Bitmap"/>
        /// </summary>
        /// <param name="frame">Frame value</param>
        /// <returns></returns>
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="frame"></param>
        /// <returns></returns>
        public static float[,] Equalize(this ushort[,] frame)
        {
            var width = frame.GetLength(1);
            var height = frame.GetLength(0);
            var hist = ushort.MaxValue + 1;

            // histogram
            var H = new ushort[hist];

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    H[frame[y, x]]++;
                }
            }

            // cdf
            var factor = 1.0f / (height * width);
            var cdf = new float[hist];

            // recursion
            cdf[0] = H[0];

            for (int i = 1; i < hist; i++)
            {
                cdf[i] = H[i] + cdf[i - 1];
            }

            // equalization
            var depth = new float[height, width];

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    depth[y, x] = cdf[frame[y, x]] * factor;
                }
            }

            return depth;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="frame"></param>
        /// <param name="rectangle"></param>
        /// <returns></returns>
        public static ushort[,] Crop(this ushort[,] frame, Rectangle rectangle)
        {
            // image params
            int width = frame.GetLength(1);
            int height = frame.GetLength(0);

            // check section params
            int x = Range(rectangle.X, 0, width);
            int y = Range(rectangle.Y, 0, height);
            int w = Range(rectangle.Width, 0, width - x);
            int h = Range(rectangle.Height, 0, height - y);

            // exception
            if (x == 0 &&
                y == 0 &&
                w == 0 &&
                h == 0) return frame;

            // output
            var output = new ushort[w, h];

            for (int i = 0; i < w; i++)
            {
                for (int j = 0; j < h; j++)
                {
                    output[j, i] = frame[y + j, x + i];
                }
            }

            return output;
        }

        /// <summary>
        /// Fixes value in range.
        /// </summary>
        /// <param name="x">Value</param>
        /// <param name="min">Min</param>
        /// <param name="max">Max</param>
        /// <returns>Value</returns>
        private static int Range(int x, int min, int max)
        {
            if (x < min)
            {
                return min;
            }
            else if (x > max)
            {
                return max;
            }
            return x;
        }

    }
}
