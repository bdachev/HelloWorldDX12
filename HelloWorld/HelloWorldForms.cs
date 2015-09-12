using SharpDX;
using SharpDX.DXGI;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;
using Rectangle = System.Drawing.Rectangle;

namespace HelloWorldShared
{
    public partial class HelloWorld
    {
        private Form form;
        private AutoResetEvent eventHandle;

        class TextureLoader : IDisposable
        {
            Bitmap _bitmap;

            public int Width { get { return _bitmap.Width; } }
            public int Height { get { return _bitmap.Height; } }

            public TextureLoader(string fileName)
            {
                _bitmap = new Bitmap(fileName);
            }

            public int CopyImageData(IntPtr ptrBuf)
            {
                int w = _bitmap.Width, h = _bitmap.Height;
                var bmpData = _bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

                var ptrDst = ptrBuf;
                var ptrSrc = bmpData.Scan0;
                int rowPitch = (bmpData.Stride + 255) / 256 * 256;
                for (int y = 0; y < h; y++, ptrDst += rowPitch, ptrSrc += bmpData.Stride)
                    Utilities.CopyMemory(ptrDst, ptrSrc, bmpData.Stride);
                _bitmap.UnlockBits(bmpData);
                return rowPitch;
            }

            public void Dispose()
            {
                _bitmap.Dispose();
            }
        }

        /// <summary>
        /// Initializes this instance.
        /// </summary>
        public void Initialize(Form form)
        {
            this.form = form;
            newWidth = width = form.ClientSize.Width;
            newHeight = height = form.ClientSize.Height;
            LoadPipeline();
            LoadAssets();
        }

        private void CreateSwapChain(ref SwapChainDescription1 swapChainDescription1, Factory4 factory)
        {
            using (var sc1 = new SwapChain1(factory, commandQueue, form.Handle, ref swapChainDescription1))
                swapChain = Collect(sc1.QueryInterface<SwapChain3>());
        }

        byte[] GetResourceBytes(string name)
        {
            using (var stream = typeof(HelloWorld).Assembly.GetManifestResourceStream("Shaders." + name))
                return Utilities.ReadStream(stream);
        }

        void CreateWaitEvent()
        {
            eventHandle = new AutoResetEvent(false);
        }
        void CloseWaitEvent()
        {
            eventHandle.Dispose();
        }

        void SetEventAndWaitForCompletion(long localFence)
        {
            fence.SetEventOnCompletion(localFence, eventHandle.SafeWaitHandle.DangerousGetHandle());
            eventHandle.WaitOne();
        }
    }
}
