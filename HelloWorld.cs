﻿// Copyright (c) 2010-2015 SharpDX - Alexandre Mutel
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

using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.DXGI;

namespace HelloWorld
{
    using SharpDX.Direct3D12;
    using System.Runtime.InteropServices;


    /// <summary>
    /// HelloWorldD3D12 sample demonstrating clearing the screen. with D3D12 API.
    /// </summary>
    public class HelloWorld : DisposeCollector
    {
        private const int SwapBufferCount = 3;
        private Form form;
        private int width;
        private int height;
        private Device device;
        private CommandAllocator commandListAllocator;
        private CommandQueue commandQueue;
        private SwapChain3 swapChain;
        private DescriptorHeap descriptorHeapRT;
        private DescriptorHeap descriptorHeapCB;
        private DescriptorHeap descriptorHeapS;
        private GraphicsCommandList commandList;
        private Resource renderTarget;
        private Rectangle scissorRectangle;
        private ViewportF viewPort;
        private AutoResetEvent eventHandle;
        private Fence fence;
        private PipelineState pipelineState;
        private RootSignature rootSignature;
        private Resource vertexBuffer;
        private VertexBufferView[] vertexBufferView;
        private CpuDescriptorHandle[] descriptorsRT = new CpuDescriptorHandle[1];
        private DescriptorHeap[] descriptorsHeaps = new DescriptorHeap[2];
        private Resource transform;
        private Resource texture;
        private long currentFence;
        private readonly Stopwatch clock;

        /// <summary>
        /// Constructor.
        /// </summary>
        public HelloWorld()
        {
            clock = Stopwatch.StartNew();
        }

        /// <summary>
        /// Initializes this instance.
        /// </summary>
        /// <param name="form">The form.</param>
        public void Initialize(Form form)
        {
            this.form = form;
            width = form.ClientSize.Width;
            height = form.ClientSize.Height;

            LoadPipeline(form);
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
            if (width != form.ClientSize.Width || height != form.ClientSize.Height)
            {
                width = form.ClientSize.Width;
                height = form.ClientSize.Height;
                swapChain.ResizeBuffers(SwapBufferCount, width, height, Format.Unknown, SwapChainFlags.None);

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

            swapChain.SetFullscreenState(false, null);
            
            eventHandle.Close();

            base.Dispose(disposeManagedResources);
        }

        /// <summary>
        /// Creates the rendering pipeline.
        /// </summary>
        /// <param name="form">The form.</param>
        private void LoadPipeline(Form form)
        {
            // create swap chain descriptor
            var swapChainDescription = new SwapChainDescription()
            {
                BufferCount = SwapBufferCount,
                ModeDescription = new ModeDescription(Format.R8G8B8A8_UNorm),
                Usage = Usage.RenderTargetOutput,
                OutputHandle = form.Handle,
                SwapEffect = SwapEffect.FlipSequential,
                SampleDescription = new SampleDescription(1, 0),
                IsWindowed = true
            };

            // enable debug layer
            using (var deviceDebug = Device.GetDeviceDebug())
                deviceDebug.EnableDebugLayer();

            // create device
            using (var factory = new Factory4())
            {
                using (var warpAdapter = factory.WarpAdapter)
                {
                    device = Collect(new Device(warpAdapter, FeatureLevel.Level_12_0));
                }
                commandQueue = Collect(device.CreateCommandQueue(new CommandQueueDescription(CommandListType.Direct)));
                using (var sc1 = new SwapChain(factory, commandQueue, swapChainDescription))
                    swapChain = Collect(sc1.QueryInterface<SwapChain3>());
            }

            // create command queue and allocator objects
            commandListAllocator = Collect(device.CreateCommandAllocator(CommandListType.Direct));
        }

        /// <summary>
        /// Setup resources for rendering
        /// </summary>
        private void LoadAssets()
        {
            var assembly = typeof(HelloWorld).Assembly;

            // Create the descriptor heap for the render target view
            descriptorHeapRT = Collect(device.CreateDescriptorHeap(new DescriptorHeapDescription()
            {
                Type = DescriptorHeapType.RenderTargetView,
                DescriptorCount = 1
            }));
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

            var rootSignatureByteCode = Utilities.ReadStream(assembly.GetManifestResourceStream("Shaders.Cube.rs"));
            using (var bufferRootSignature = DataBuffer.Create(rootSignatureByteCode))
                rootSignature = Collect(device.CreateRootSignature(bufferRootSignature));

            var vertexShaderByteCode = Utilities.ReadStream(assembly.GetManifestResourceStream("Shaders.Cube.vso"));
            var pixelShaderByteCode = Utilities.ReadStream(assembly.GetManifestResourceStream("Shaders.Cube.pso"));

            var layout = new InputLayoutDescription(new InputElement[]
            {
                new InputElement("POSITION", 0, Format.R32G32B32_Float, 0),
                new InputElement("NORMAL", 0, Format.R32G32B32_Float, 0),
                new InputElement("TEXCOORD", 0, Format.R32G32_Float, 0)
            });

            #region pipeline state
            var psd = new GraphicsPipelineStateDescription
            {
                InputLayout = layout,
                VertexShader = vertexShaderByteCode,
                PixelShader = pixelShaderByteCode,
                RootSignature = rootSignature,
                DepthStencilState = DepthStencilStateDescription.Default(),
                BlendState = BlendStateDescription.Default(),
                RasterizerState = RasterizerStateDescription.Default(),
                SampleDescription = new SampleDescription(1, 0),
                RenderTargetCount = 1,
                PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
                SampleMask = -1,
            };
            psd.RenderTargetFormats[0] = Format.R8G8B8A8_UNorm;
            psd.DepthStencilState.IsDepthEnabled = false;
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

            #region transform
            transform = Collect(device.CreateCommittedResource(
                            new HeapProperties(HeapType.Upload),
                            HeapFlags.None,
                            new ResourceDescription(ResourceDimension.Buffer, 0, Utilities.SizeOf<Matrix>(), 1, 1, 1, Format.Unknown, 1, 0, TextureLayout.RowMajor, ResourceFlags.None),
                            ResourceStates.GenericRead));
            #endregion transform

            #region texture
            using (var bmp = new System.Drawing.Bitmap("GeneticaMortarlessBlocks.jpg"))
            {
                int width = bmp.Width, height = bmp.Height;
                var bmpData = bmp.LockBits(new System.Drawing.Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                texture = Collect(device.CreateCommittedResource(
                                                new HeapProperties(HeapType.Upload),
                                                HeapFlags.None,
                                                new ResourceDescription(ResourceDimension.Texture2D,
                                                            0, width, height, 1, 1,
                                                            Format.B8G8R8A8_UNorm_SRgb, 1, 0,
                                                            TextureLayout.Unknown,
                                                            ResourceFlags.None),
                                                ResourceStates.GenericRead));
                texture.WriteToSubresource(0, null, bmpData.Scan0, bmpData.Stride, 0);
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

            // Create the main command list
            commandList = Collect(device.CreateCommandList(CommandListType.Direct, commandListAllocator, pipelineState));

            // Get the backbuffer and creates the render target view
            renderTarget = Collect(swapChain.GetBackBuffer<Resource>(0));
            device.CreateRenderTargetView(renderTarget, null, descriptorHeapRT.CPUDescriptorHandleForHeapStart);

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
            eventHandle = new AutoResetEvent(false);

            // Wait the command list to complete
            WaitForPrevFrame();
        }

        /// <summary>
        /// Fill the command list with commands
        /// </summary>
        private void PopulateCommandLists()
        {
            var time = clock.Elapsed.TotalSeconds;

            commandListAllocator.Reset();

            commandList.Reset(commandListAllocator, null);

            // setup viewport and scissors
            commandList.SetViewport(viewPort);
            commandList.SetScissorRectangles(scissorRectangle);
            var view = Matrix.LookAtLH(new Vector3(0, 0, -5), Vector3.Zero, Vector3.UnitY);
            var proj = Matrix.PerspectiveFovLH(MathUtil.Pi / 4, (float)width / height, 0.1f, 100);
            var world = Matrix.RotationY((float)time) * Matrix.RotationX((float)time / 2);
            var wvpT = world * view * proj;
            wvpT.Transpose();
            var ptr = transform.Map(0);
            Utilities.Write(ptr, ref wvpT);
            transform.Unmap(0);
            commandList.PipelineState = pipelineState;
            commandList.SetGraphicsRootSignature(rootSignature);
            commandList.SetGraphicsRootConstantBufferView(0, transform.GPUVirtualAddress);
            commandList.SetDescriptorHeaps(2, descriptorsHeaps);
            commandList.Descriptors.SetGraphicsRootDescriptorTable(1, descriptorHeapCB.GPUDescriptorHandleForHeapStart);
            commandList.Descriptors.SetGraphicsRootDescriptorTable(2, descriptorHeapS.GPUDescriptorHandleForHeapStart);

            // Use barrier to notify that we are using the RenderTarget to clear it
            commandList.ResourceBarrierTransition(renderTarget, ResourceStates.Present, ResourceStates.RenderTarget);

	        // Clear the RenderTarget
	        commandList.ClearRenderTargetView(descriptorHeapRT.CPUDescriptorHandleForHeapStart, new Color4((float)Math.Sin(time) * 0.25f + 0.5f, (float)Math.Sin(time * 0.5f) * 0.4f + 0.6f, 0.4f, 1.0f), 0, null);
            descriptorsRT[0] = descriptorHeapRT.CPUDescriptorHandleForHeapStart;
            commandList.SetRenderTargets(1, descriptorsRT, true, null);
            commandList.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
            commandList.SetVertexBuffers(0, 1, vertexBufferView);
            commandList.DrawInstanced(34, 1, 0, 0);

            // Use barrier to notify that we are going to present the RenderTarget
            commandList.ResourceBarrierTransition(renderTarget, ResourceStates.RenderTarget, ResourceStates.Present);

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
                fence.SetEventOnCompletion(localFence, eventHandle.SafeWaitHandle.DangerousGetHandle());
                eventHandle.WaitOne();
            }
        }
    }
}