using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace HelloWorldShared
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        HelloWorld _helloWorld;

        void UpdateAndRender()
        {
            if (_helloWorld != null)
            {
                _helloWorld.Update();
                _helloWorld.Render();
            }
        }

        public MainPage()
        {
            this.InitializeComponent();

            Loaded += (o, e) =>
            {
                _helloWorld = new HelloWorld();
                _helloWorld.Initialize(__SwapChainPanel);
                __SwapChainPanel.SizeChanged += (o1, e1) =>
                {
                    _helloWorld.Resize((int)__SwapChainPanel.ActualWidth, (int)__SwapChainPanel.ActualHeight);
                };
                CompositionTarget.Rendering += CompositionTarget_Rendering;
            };

            Unloaded += (o, e) =>
            {
                CompositionTarget.Rendering -= CompositionTarget_Rendering;

                if (_helloWorld != null)
                {
                    _helloWorld.Dispose();
                    _helloWorld = null;
                }
            };
        }

        private void CompositionTarget_Rendering(object sender, object e)
        {
            UpdateAndRender();
        }
    }
}
