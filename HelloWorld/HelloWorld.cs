// Copyright (c) 2010-2015 SharpDX - Alexandre Mutel
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
#define USE_WARP
#define USE_DEPTH
#define USE_INSTANCES
#define USE_INDICES
//#define USE_TEXTURE

using System;
using System.Diagnostics;
using System.Threading;
#if NETFX_CORE
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.ApplicationModel;
#else
using System.Windows.Forms;
#endif
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.DXGI;

namespace HelloWorld
{
    using SharpDX.Direct3D12;
    using System.IO;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Threading.Tasks;

    /// <summary>
    /// HelloWorldD3D12 sample demonstrating clearing the screen. with D3D12 API.
    /// </summary>
    public class HelloWorld : DisposeCollector
    {
#if NETFX_CORE
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
#else
        private AutoResetEvent eventHandle;
#endif
        private const int SwapBufferCount = 3;
#if NETFX_CORE
        private SwapChainPanel panel;
#else
        private Form form;
#endif
        private int width, newWidth;
        private int height, newHeight;
        private Device device;
        private CommandAllocator commandListAllocator;
        private CommandQueue commandQueue;
        private SwapChain3 swapChain;
        private DescriptorHeap descriptorHeapRT;
        private GraphicsCommandList commandList;
        private Resource renderTarget;
#if USE_DEPTH
        private Resource depthBuffer;
        private DescriptorHeap descriptorHeapDS;
#endif
        private Rectangle scissorRectangle;
        private ViewportF viewPort;
        private Fence fence;
        private PipelineState pipelineState;
        private RootSignature rootSignature;
        private Resource vertexBuffer;
        private VertexBufferView[] vertexBufferView;
#if USE_INDICES
        private Resource indexBuffer;
        private IndexBufferView indexBufferView;
#endif
#if USE_INSTANCES
        private Resource instancesBuffer;
        private VertexBufferView[] instancesBufferView;
#endif
#if USE_TEXTURE
        private DescriptorHeap[] descriptorsHeaps = new DescriptorHeap[2];
        private DescriptorHeap descriptorHeapCB;
        private DescriptorHeap descriptorHeapS;
        private Resource texture;
#endif
        private Resource transform;
        private long currentFence;
        private readonly Stopwatch clock;

        /// <summary>
        /// Constructor.
        /// </summary>
        public HelloWorld()
        {
            clock = Stopwatch.StartNew();
        }

        public void Resize(int width, int height)
        {
            newWidth = width;
            newHeight = height;
        }

#if NETFX_CORE
        /// <summary>
        /// Initializes this instance.
        /// </summary>
        public void Initialize(SwapChainPanel panel)
        {
            this.panel = panel;
            newWidth = width = (int)panel.ActualWidth;
            newHeight = height = (int)panel.ActualHeight;
#else
        /// <summary>
        /// Initializes this instance.
        /// </summary>
        public void Initialize(Form form)
        {
            this.form = form;
            newWidth = width = form.ClientSize.Width;
            newHeight = height = form.ClientSize.Height;
#endif

            LoadPipeline();
            LoadAssets();
        }
        /// <summary>
        /// Updates this instance.
        /// </summary>
        public void Update()
        {
        }

        /// <summary>
        /// Render scene
        /// </summary>
        public void Render()
        {
            // record all the commands we need to render the scene into the command list
            PopulateCommandLists();

            // execute the command list
            commandQueue.ExecuteCommandList(commandList);

            // swap the back and front buffers
            swapChain.Present(1, 0);

            // wait and reset EVERYTHING
            WaitForPrevFrame();

            RemoveAndDispose(ref renderTarget);
            if (width != newWidth || height != newHeight)
            {
                width = newWidth;
                height = newHeight;
                swapChain.ResizeBuffers(SwapBufferCount, width, height, Format.Unknown, SwapChainFlags.None);
#if USE_DEPTH
                RemoveAndDispose(ref depthBuffer);
                depthBuffer = Collect(device.CreateCommittedResource(
                    new HeapProperties(HeapType.Default),
                    HeapFlags.None,
                    new ResourceDescription(ResourceDimension.Texture2D, 0, width, height, 1, 1, Format.D32_Float, 1, 0, TextureLayout.Unknown, ResourceFlags.AllowDepthStencil),
                    ResourceStates.Common, 
                    new ClearValue
                    {
                        Format = Format.D32_Float,
                        DepthStencil = new DepthStencilValue
                        {
                            Depth = 1,
                            Stencil = 0,
                        }
                    }));
                device.CreateDepthStencilView(depthBuffer, null, descriptorHeapDS.CPUDescriptorHandleForHeapStart);
#endif
                // Create the viewport
                viewPort = new ViewportF(0, 0, width, height);

                // Create the scissor
                scissorRectangle = new Rectangle(0, 0, width, height);
            }
            renderTarget = Collect(swapChain.GetBackBuffer<Resource>(swapChain.CurrentBackBufferIndex));
            device.CreateRenderTargetView(renderTarget, null, descriptorHeapRT.CPUDescriptorHandleForHeapStart);
        }

        /// <summary>
        /// Cleanup allocations
        /// </summary>
        protected override void Dispose(bool disposeManagedResources)
        {
            // wait for the GPU to be done with all resources
            WaitForPrevFrame();

            //swapChain.SetFullscreenState(false, null);

#if NETFX_CORE
            CloseHandle(eventHandle);
#else
            eventHandle.Dispose();
#endif

            base.Dispose(disposeManagedResources);
        }

        /// <summary>
        /// Creates the rendering pipeline.
        /// </summary>
        private void LoadPipeline()
        {
            // create swap chain descriptor
            var swapChainDescription1 = new SwapChainDescription1()
            {
                AlphaMode = AlphaMode.Unspecified,
                BufferCount = SwapBufferCount,
                Usage = Usage.RenderTargetOutput,
                SwapEffect = SwapEffect.FlipSequential,
                SampleDescription = new SampleDescription(1, 0),
                Format = Format.R8G8B8A8_UNorm,
                Width = width,
                Height = height
            };

            // enable debug layer
            using (var debugInterface = DebugInterface.Get())
                debugInterface.EnableDebugLayer();

            // create device
            using (var factory = new Factory4())
            {
#if USE_WARP
                using (var warpAdapter = factory.GetWarpAdapter())
                {
                    device = Collect(new Device(warpAdapter, FeatureLevel.Level_12_0));
                }
#else
                using (var adapter = factory.Adapters[1])
                {
                    device = Collect(new Device(adapter, FeatureLevel.Level_11_0));
                }
#endif
                commandQueue = Collect(device.CreateCommandQueue(new CommandQueueDescription(CommandListType.Direct)));

#if NETFX_CORE
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
#else
                using (var sc1 = new SwapChain1(factory, commandQueue, form.Handle, ref swapChainDescription1))
                    swapChain = Collect(sc1.QueryInterface<SwapChain3>());
#endif
            }

            // create command queue and allocator objects
            commandListAllocator = Collect(device.CreateCommandAllocator(CommandListType.Direct));
        }

        byte[] GetResourceBytes(string name)
        {
#if NETFX_CORE
            var location = Package.Current.InstalledLocation;
            using (var stream = location.OpenStreamForReadAsync("Shaders\\" + name).Result)
                return Utilities.ReadStream(stream);
#else
            using (var stream = typeof(HelloWorld).Assembly.GetManifestResourceStream("Shaders." + name))
                return Utilities.ReadStream(stream);
#endif
        }

        /// <summary>
        /// Setup resources for rendering
        /// </summary>
        private void LoadAssets()
        {
            // Create the main command list
            commandList = Collect(device.CreateCommandList(CommandListType.Direct, commandListAllocator, pipelineState));

            // Create the descriptor heap for the render target view
            descriptorHeapRT = Collect(device.CreateDescriptorHeap(new DescriptorHeapDescription()
            {
                Type = DescriptorHeapType.RenderTargetView,
                DescriptorCount = 1
            }));
#if USE_DEPTH
            descriptorHeapDS = Collect(device.CreateDescriptorHeap(new DescriptorHeapDescription()
            {
                Type = DescriptorHeapType.DepthStencilView,
                DescriptorCount = 1
            }));
#endif
#if USE_TEXTURE
            descriptorHeapCB = Collect(device.CreateDescriptorHeap(new DescriptorHeapDescription()
            {
                Type = DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
                DescriptorCount = 1,
                Flags = DescriptorHeapFlags.ShaderVisible,
            }));
            descriptorHeapS = Collect(device.CreateDescriptorHeap(new DescriptorHeapDescription()
            {
                Type = DescriptorHeapType.Sampler,
                DescriptorCount = 1,
                Flags = DescriptorHeapFlags.ShaderVisible,
            }));
            descriptorsHeaps[0] = descriptorHeapCB;
            descriptorsHeaps[1] = descriptorHeapS;
#endif
#if true // root signature in code 
            var rsparams = new RootParameter[]
            {
                new RootParameter(ShaderVisibility.Vertex, new RootDescriptor(), RootParameterType.ConstantBufferView),
#if USE_TEXTURE
                new RootParameter(ShaderVisibility.Pixel,
                    new DescriptorRange
                    {
                        RangeType = DescriptorRangeType.ShaderResourceView,
                        BaseShaderRegister = 0,
                        DescriptorCount = 1,
                    }),
                new RootParameter(ShaderVisibility.Pixel,
                    new DescriptorRange
                    {
                        RangeType = DescriptorRangeType.Sampler,
                        BaseShaderRegister = 0,
                        DescriptorCount = 1,
                    }),
#endif
            };
            var rs = new RootSignatureDescription(RootSignatureFlags.AllowInputAssemblerInputLayout, rsparams);
            rootSignature = Collect(device.CreateRootSignature(rs.Serialize()));
#else
            var rootSignatureByteCode = Utilities.ReadStream(assembly.GetManifestResourceStream("Shaders.Cube.rs"));
            using (var bufferRootSignature = DataBuffer.Create(rootSignatureByteCode))
                rootSignature = Collect(device.CreateRootSignature(bufferRootSignature));
#endif
            byte[] vertexShaderByteCode = GetResourceBytes("Cube.vso");
            byte[] pixelShaderByteCode = GetResourceBytes("Cube.pso");

            var layout = new InputLayoutDescription(new InputElement[]
            {
                new InputElement("POSITION", 0, Format.R32G32B32_Float, 0),
                new InputElement("NORMAL", 0, Format.R32G32B32_Float, 0),
                new InputElement("TEXCOORD", 0, Format.R32G32_Float, 0),
#if USE_INSTANCES
                new InputElement("OFFSET", 0, Format.R32G32B32_Float, 0, 1, InputClassification.PerInstanceData, 1),
#endif
            });

#region pipeline state
            var psd = new GraphicsPipelineStateDescription
            {
                InputLayout = layout,
                VertexShader = vertexShaderByteCode,
                PixelShader = pixelShaderByteCode,
                RootSignature = rootSignature,
                DepthStencilState = DepthStencilStateDescription.Default(),
                DepthStencilFormat = Format.Unknown,
                BlendState = BlendStateDescription.Default(),
                RasterizerState = RasterizerStateDescription.Default(),
                SampleDescription = new SampleDescription(1, 0),
                RenderTargetCount = 1,
                PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
                SampleMask = -1,
                StreamOutput = new StreamOutputDescription()
            };
            psd.RenderTargetFormats[0] = Format.R8G8B8A8_UNorm;
#if USE_DEPTH
            psd.DepthStencilFormat = Format.D32_Float;
#else
            psd.DepthStencilState.IsDepthEnabled = false;
#endif
            //psd.RasterizerState.CullMode = CullMode.None;
            pipelineState = Collect(device.CreateGraphicsPipelineState(psd));
#endregion pipeline state

#region vertices
            var vertices = new[]
                                 {
                                      -1.0f, -1.0f, -1.0f, 1.0f, 1.0f, 1.0f, 0.0f, 0.0f, // Front
                                      -1.0f,  1.0f, -1.0f, 1.0f, 1.0f, 1.0f, 0.0f, 1.0f,
                                       1.0f, -1.0f, -1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 0.0f,
                                       1.0f,  1.0f, -1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f,
                                       1.0f,  1.0f, -1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f,

                                      -1.0f, -1.0f,  1.0f, 1.0f, 1.0f, 1.0f, 0.0f, 0.0f, // BACK
                                      -1.0f, -1.0f,  1.0f, 1.0f, 1.0f, 1.0f, 0.0f, 0.0f, // BACK
                                       1.0f, -1.0f,  1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 0.0f,
                                      -1.0f,  1.0f,  1.0f, 1.0f, 1.0f, 1.0f, 0.0f, 1.0f,
                                       1.0f,  1.0f,  1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f,
                                       1.0f,  1.0f,  1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f,

                                      -1.0f,  1.0f, -1.0f, 1.0f, 1.0f, 1.0f, 0.0f, 0.0f, // Top
                                      -1.0f,  1.0f, -1.0f, 1.0f, 1.0f, 1.0f, 0.0f, 0.0f, // Top
                                      -1.0f,  1.0f,  1.0f, 1.0f, 1.0f, 1.0f, 0.0f, 1.0f,
                                       1.0f,  1.0f, -1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 0.0f,
                                       1.0f,  1.0f,  1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f,
                                       1.0f,  1.0f,  1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f,

                                      -1.0f, -1.0f, -1.0f, 1.0f, 1.0f, 1.0f, 0.0f, 0.0f, // Bottom
                                      -1.0f, -1.0f, -1.0f, 1.0f, 1.0f, 1.0f, 0.0f, 0.0f, // Bottom
                                       1.0f, -1.0f, -1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 0.0f,
                                      -1.0f, -1.0f,  1.0f, 1.0f, 1.0f, 1.0f, 0.0f, 1.0f,
                                       1.0f, -1.0f,  1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f,
                                       1.0f, -1.0f,  1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f,

                                      -1.0f, -1.0f, -1.0f, 1.0f, 1.0f, 1.0f, 0.0f, 0.0f, // Left
                                      -1.0f, -1.0f, -1.0f, 1.0f, 1.0f, 1.0f, 0.0f, 0.0f, // Left
                                      -1.0f, -1.0f,  1.0f, 1.0f, 1.0f, 1.0f, 0.0f, 1.0f,
                                      -1.0f,  1.0f, -1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 0.0f,
                                      -1.0f,  1.0f,  1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f,
                                      -1.0f,  1.0f,  1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f,

                                       1.0f, -1.0f, -1.0f, 1.0f, 1.0f, 1.0f, 0.0f, 0.0f, // Right
                                       1.0f, -1.0f, -1.0f, 1.0f, 1.0f, 1.0f, 0.0f, 0.0f, // Right
                                       1.0f,  1.0f, -1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 0.0f,
                                       1.0f, -1.0f,  1.0f, 1.0f, 1.0f, 1.0f, 0.0f, 1.0f,
                                       1.0f,  1.0f,  1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f,
                            };
#endregion vertices
#region vertex buffer
            // Instantiate Vertex buiffer from vertex data
            int sizeOfFloat = sizeof(float);
            int sizeInBytes = vertices.Length * sizeOfFloat;
            vertexBuffer = Collect(device.CreateCommittedResource(
                            new HeapProperties(HeapType.Upload),
                            HeapFlags.None,
                            new ResourceDescription(ResourceDimension.Buffer, 0, sizeInBytes, 1, 1, 1, Format.Unknown, 1, 0, TextureLayout.RowMajor, ResourceFlags.None),
                            ResourceStates.GenericRead));
            vertexBufferView = new[]
            {
                new VertexBufferView
                {
                    BufferLocation = vertexBuffer.GPUVirtualAddress,
                    SizeInBytes = sizeInBytes,
                    StrideInBytes = sizeOfFloat * 8,
                }
            };
            var ptr = vertexBuffer.Map(0);
            Utilities.Write(ptr, vertices, 0, vertices.Length);
            vertexBuffer.Unmap(0);
#endregion vertex buffer
#region instances
#if USE_INSTANCES
            var instances = new float[]
            {
                0,  0,  0,
                4,  0,  0,
               -4,  0,  0,
                0,  4,  0,
                0, -4,  0,
                0,  0,  4,
                0,  0, -4,
            };
            int instanceSizeInBytes = sizeOfFloat * instances.Length;
            instancesBuffer = Collect(device.CreateCommittedResource(
                            new HeapProperties(HeapType.Upload),
                            HeapFlags.None,
                            new ResourceDescription(ResourceDimension.Buffer, 0, instanceSizeInBytes, 1, 1, 1, Format.Unknown, 1, 0, TextureLayout.RowMajor, ResourceFlags.None),
                            ResourceStates.GenericRead));
            instancesBufferView = new[]
            {
                new VertexBufferView
                {
                    BufferLocation = instancesBuffer.GPUVirtualAddress,
                    SizeInBytes = instanceSizeInBytes,
                    StrideInBytes = sizeOfFloat * 3,
                }
            };
            ptr = instancesBuffer.Map(0);
            Utilities.Write(ptr, instances, 0, instances.Length);
            instancesBuffer.Unmap(0);
#endif
#endregion instances

#region indices
#if USE_INDICES
            var indexData = new[]
            {
                     0,   1,   2,   3,   4,
                5,   6,   7,   8,   9,  10,
               11,  12,  13,  14,  15,  16,
               17,  18,  19,  20,  21,  22,
               23,  24,  25,  26,  27,  28,
               29,  30,  31,  32,  33
            };
            sizeInBytes = indexData.Length * sizeof(int);
            indexBuffer = Collect(device.CreateCommittedResource(
                            new HeapProperties(HeapType.Upload),
                            HeapFlags.None,
                            new ResourceDescription(ResourceDimension.Buffer, 0, sizeInBytes, 1, 1, 1, Format.Unknown, 1, 0, TextureLayout.RowMajor, ResourceFlags.None),
                            ResourceStates.GenericRead));
            ptr = indexBuffer.Map(0);
            Utilities.Write(ptr, indexData, 0, indexData.Length);
            indexBuffer.Unmap(0);
            indexBufferView = new IndexBufferView
            {
                 BufferLocation = indexBuffer.GPUVirtualAddress,
                 SizeInBytes = sizeInBytes,
                 Format = Format.R32_UInt
            };
#endif
#endregion indices

#region transform
            transform = Collect(device.CreateCommittedResource(
                            new HeapProperties(HeapType.Upload),
                            HeapFlags.None,
                            new ResourceDescription(ResourceDimension.Buffer, 0, Utilities.SizeOf<Matrix>(), 1, 1, 1, Format.Unknown, 1, 0, TextureLayout.RowMajor, ResourceFlags.None),
                            ResourceStates.GenericRead));
#endregion transform

#if USE_TEXTURE
#region texture
            Resource buf;
            using (var bmp = new System.Drawing.Bitmap("GeneticaMortarlessBlocks.jpg"))
            {
                int w = bmp.Width, h = bmp.Height;
                var descrs = new[]
                {
                    new ResourceDescription(ResourceDimension.Texture2D,
                                            0, w, h, 1, 1,
                                            Format.B8G8R8A8_UNorm, 1, 0,
                                            TextureLayout.Unknown,
                                            ResourceFlags.None),
                };
                texture = Collect(device.CreateCommittedResource(
                                            new HeapProperties(HeapType.Default),
                                            HeapFlags.None,
                                            descrs[0],
                                            ResourceStates.CopyDestination)
                        );
                var resAllocInfo = device.GetResourceAllocationInfo(1, 1, descrs);
                buf = device.CreateCommittedResource(
                                                new HeapProperties(HeapType.Upload),
                                                HeapFlags.None,
                                                new ResourceDescription(
                                                    ResourceDimension.Buffer,
                                                    0,
                                                    resAllocInfo.SizeInBytes,
                                                    1, 1, 1,
                                                    Format.Unknown,
                                                    1, 0,
                                                    TextureLayout.RowMajor,
                                                    ResourceFlags.None),
                                                ResourceStates.GenericRead);
                {
                    var ptrBuf = buf.Map(0);
                    var bmpData = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                    var ptrDst = ptrBuf;
                    var ptrSrc = bmpData.Scan0;
                    int rowPitch = (bmpData.Stride + 255) / 256 * 256;
                    for (int y = 0; y < h; y++, ptrDst += rowPitch, ptrSrc += bmpData.Stride)
                        Utilities.CopyMemory(ptrDst, ptrSrc, bmpData.Stride);
                    buf.Unmap(0);
                    bmp.UnlockBits(bmpData);

                    var src = new TextureCopyLocation(buf,
                        new PlacedSubResourceFootprint
                        {
                            Offset = 0,
                            Footprint = new SubResourceFootprint
                            {
                                Format = Format.B8G8R8A8_UNorm_SRgb,
                                Width = w,
                                Height = h,
                                Depth = 1,
                                RowPitch = rowPitch
                            }
                        }
                    );
                    var dst = new TextureCopyLocation(texture, 0);
                    // record copy
                    commandList.CopyTextureRegion(dst, 0, 0, 0, src, null);

                    commandList.ResourceBarrierTransition(texture, ResourceStates.CopyDestination, ResourceStates.GenericRead);
                }
            }
            device.CreateShaderResourceView(texture, null, descriptorHeapCB.CPUDescriptorHandleForHeapStart);
#endregion texture

#region sampler
            device.CreateSampler(new SamplerStateDescription
            {
                AddressU = TextureAddressMode.Wrap,
                AddressV = TextureAddressMode.Wrap,
                AddressW = TextureAddressMode.Wrap,
                Filter = Filter.MaximumMinMagMipLinear,
            }, descriptorHeapS.CPUDescriptorHandleForHeapStart);
#endregion sampler
#endif
            // Get the backbuffer and creates the render target view
            renderTarget = Collect(swapChain.GetBackBuffer<Resource>(0));
            device.CreateRenderTargetView(renderTarget, null, descriptorHeapRT.CPUDescriptorHandleForHeapStart);

#if USE_DEPTH
            depthBuffer = Collect(device.CreateCommittedResource(
                new HeapProperties(HeapType.Default),
                HeapFlags.None,
                new ResourceDescription(ResourceDimension.Texture2D, 0, width, height, 1, 1, Format.D32_Float, 1, 0, TextureLayout.Unknown, ResourceFlags.AllowDepthStencil),
                ResourceStates.Present,
                new ClearValue
                {
                    Format = Format.D32_Float,
                    DepthStencil = new DepthStencilValue
                    {
                        Depth = 1,
                        Stencil = 0,
                    }
                }));
            device.CreateDepthStencilView(depthBuffer, null, descriptorHeapDS.CPUDescriptorHandleForHeapStart);
#endif

            // Create the viewport
            viewPort = new ViewportF(0, 0, width, height);

            // Create the scissor
            scissorRectangle = new Rectangle(0, 0, width, height);

            // Create a fence to wait for next frame
            fence = Collect(device.CreateFence(0, FenceFlags.None));
            currentFence = 1;

            // Close command list
            commandList.Close();
            commandQueue.ExecuteCommandList(commandList);

            // Create an event handle use for VTBL
#if NETFX_CORE
            eventHandle = CreateEventW(IntPtr.Zero, false, false, null);
#else
            eventHandle = new AutoResetEvent(false);
#endif

            // Wait the command list to complete
            WaitForPrevFrame();
#if USE_TEXTURE
            buf.Dispose();
#endif
        }

        /// <summary>
        /// Fill the command list with commands
        /// </summary>
        private void PopulateCommandLists()
        {
            var time = clock.Elapsed.TotalSeconds;

            commandListAllocator.Reset();

            commandList.Reset(commandListAllocator, pipelineState);

            // setup viewport and scissors
            commandList.SetViewport(viewPort);
            commandList.SetScissorRectangles(scissorRectangle);
            var view = Matrix.LookAtLH(new Vector3(0, 0, -5), Vector3.Zero, Vector3.UnitY);
            var proj = Matrix.PerspectiveFovLH(MathUtil.Pi / 4, (float)width / height, 0.1f, 100);
            var world = Matrix.Scaling(0.3f) * Matrix.RotationY((float)time) * Matrix.RotationX((float)time / 2);
            var wvpT = world * view * proj;
            wvpT.Transpose();
            var ptr = transform.Map(0);
            Utilities.Write(ptr, ref wvpT);
            transform.Unmap(0);
            //commandList.PipelineState = pipelineState;
            commandList.SetGraphicsRootSignature(rootSignature);
            commandList.SetGraphicsRootConstantBufferView(0, transform.GPUVirtualAddress);
#if USE_TEXTURE
            commandList.SetDescriptorHeaps(2, descriptorsHeaps);
            commandList.SetGraphicsRootDescriptorTable(1, descriptorHeapCB.GPUDescriptorHandleForHeapStart);
            commandList.SetGraphicsRootDescriptorTable(2, descriptorHeapS.GPUDescriptorHandleForHeapStart);
#endif
            // Use barrier to notify that we are using the RenderTarget to clear it
            commandList.ResourceBarrierTransition(renderTarget, ResourceStates.Present, ResourceStates.RenderTarget);
            //commandList.ResourceBarrierTransition(depthBuffer, ResourceStates.Present, ResourceStates.DepthWrite);

            // Clear the RenderTarget
            commandList.ClearRenderTargetView(descriptorHeapRT.CPUDescriptorHandleForHeapStart, new Color4((float)Math.Sin(time) * 0.25f + 0.5f, (float)Math.Sin(time * 0.5f) * 0.4f + 0.6f, 0.4f, 1.0f), 0, null);
#if USE_DEPTH
            commandList.ClearDepthStencilView(descriptorHeapDS.CPUDescriptorHandleForHeapStart, ClearFlags.FlagsDepth, 1, 0, 0, null);
            commandList.SetRenderTargets(1, descriptorHeapRT.CPUDescriptorHandleForHeapStart, true, descriptorHeapDS.CPUDescriptorHandleForHeapStart);
#else
            commandList.SetRenderTargets(1, descriptorHeapRT.CPUDescriptorHandleForHeapStart, true, null);
#endif
            commandList.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
            commandList.SetVertexBuffers(0, vertexBufferView, 1);
#if USE_INSTANCES
            commandList.SetVertexBuffers(1, instancesBufferView, 1);
#if USE_INDICES
            commandList.SetIndexBuffer(indexBufferView);
            commandList.DrawIndexedInstanced(34, 7, 0, 0, 0);
#else
            commandList.DrawInstanced(34, 7, 0, 0);
#endif
#else
#if USE_INDICES
            commandList.IndexBuffer = indexBufferView;
            commandList.DrawIndexedInstanced(34, 1, 0, 0, 0);
#else
            commandList.DrawInstanced(34, 1, 0, 0);
#endif
#endif

            // Use barrier to notify that we are going to present the RenderTarget
            commandList.ResourceBarrierTransition(renderTarget, ResourceStates.RenderTarget, ResourceStates.Present);
            //commandList.ResourceBarrierTransition(depthBuffer, ResourceStates.DepthWrite, ResourceStates.Present);

            // Execute the command
            commandList.Close();
        }

        /// <summary>
        /// Wait the previous command list to finish executing.
        /// </summary>
        private void WaitForPrevFrame()
        {
	        // WAITING FOR THE FRAME TO COMPLETE BEFORE CONTINUING IS NOT BEST PRACTICE.
	        // This is code implemented as such for simplicity.
            long localFence = currentFence;
            commandQueue.Signal(fence, localFence);
            currentFence++;

            if (fence.CompletedValue < localFence)
            {
#if NETFX_CORE
                fence.SetEventOnCompletion(localFence, eventHandle);
                WaitForSingleObject(eventHandle, INFINITE);

#else
                fence.SetEventOnCompletion(localFence, eventHandle.SafeWaitHandle.DangerousGetHandle());
                eventHandle.WaitOne();
#endif
            }
        }
    }
}