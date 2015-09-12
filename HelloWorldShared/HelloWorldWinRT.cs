using SharpDX;
using SharpDX.DXGI;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.UI.Xaml.Controls;

namespace HelloWorldShared
{
    public partial class HelloWorld
    {
        [DllImport("kernel32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateEventW(IntPtr lpEventAttributes,
                                                [In, MarshalAs(UnmanagedType.Bool)] bool bManualReset,
                                                [In, MarshalAs(UnmanagedType.Bool)] bool bIntialState,
                                                [In, MarshalAs(UnmanagedType.BStr)] string lpName);

        [DllImport("kernel32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode)]
        public static extern bool CloseHandle([In]IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern Int32 WaitForSingleObject(IntPtr Handle, Int32 Wait);

        public const Int32 INFINITE = -1;
        public const Int32 WAIT_ABANDONED = 0x80;
        public const Int32 WAIT_OBJECT_0 = 0x00;
        public const Int32 WAIT_TIMEOUT = 0x102;
        public const Int32 WAIT_FAILED = -1;

        private IntPtr eventHandle;

        private SwapChainPanel panel;

        class TextureLoader : IDisposable
        {
            byte[] _bitmapData;
            public int Width { get; private set; }
            public int Height { get; private set; }

            public TextureLoader(string fileName)
            {
                Windows.System.Threading.ThreadPool.RunAsync(wi => _bitmapData = LoadFrameAsync(fileName).Result).AsTask().Wait();
            }

            async Task<byte[]> LoadFrameAsync(string fileName)
            {
                var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///" + fileName));
                using (var stream = await file.OpenReadAsync())
                {
                    var decoder = await BitmapDecoder.CreateAsync(stream);
                    Width = (int)decoder.PixelWidth;
                    Height = (int)decoder.PixelHeight;
                    var data = decoder.BitmapPixelFormat == BitmapPixelFormat.Bgra8 ?
                                    await decoder.GetPixelDataAsync() :
                                    await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform(), ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
                    return data.DetachPixelData();
                }
            }

            public int CopyImageData(IntPtr ptrBuf)
            {
                var ptrDst = ptrBuf;
                int stride = Width * 4;
                int rowPitch = (stride + 255) / 256 * 256;
                for (int y = 0, h = (int)Height; y < h; y++, ptrDst += rowPitch)
                    Utilities.Write(ptrDst, _bitmapData, y * stride, stride);

                return rowPitch;
            }

            public void Dispose()
            {
            }
        }

        /// <summary>
        /// Initializes this instance.
        /// </summary>
        public void Initialize(SwapChainPanel panel)
        {
            this.panel = panel;
            newWidth = width = (int)panel.ActualWidth;
            newHeight = height = (int)panel.ActualHeight;

            LoadPipeline();
            LoadAssets();
        }

        private void CreateSwapChain(ref SwapChainDescription1 swapChainDescription1, Factory4 factory)
        {
            using (var sc1 = new SwapChain1(factory, commandQueue, ref swapChainDescription1))
            {
                swapChain = Collect(sc1.QueryInterface<SwapChain3>());
                using (var comPtr = new ComObject(panel))
                {
                    using (var native = comPtr.QueryInterface<ISwapChainPanelNative>())
                    {
                        native.SwapChain = swapChain;
                    }
                }
            }
        }

        byte[] GetResourceBytes(string name)
        {
            var location = Package.Current.InstalledLocation;
            using (var stream = location.OpenStreamForReadAsync("Shaders\\" + name).Result)
                return Utilities.ReadStream(stream);
        }

        void CreateWaitEvent()
        {
            eventHandle = CreateEventW(IntPtr.Zero, false, false, null);
        }
        void CloseWaitEvent()
        {
            CloseHandle(eventHandle);
        }

        void SetEventAndWaitForCompletion(long localFence)
        {
            fence.SetEventOnCompletion(localFence, eventHandle);
            WaitForSingleObject(eventHandle, INFINITE);
        }
    }
}
