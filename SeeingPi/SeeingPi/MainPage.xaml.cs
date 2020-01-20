using RaspberryModules.App.Modules;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using TheSeeingPi;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Windows.System.Display;
using Windows.System.Threading;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace SeeingPi
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
            StartVideoPreviewAsync();
            LoadModelAsync();
            _spiDisplay.InitAll();
        }

        private readonly DisplayRequest _displayRequest = new DisplayRequest();
        private readonly MediaCapture _mediaCapture = new MediaCapture();

        private async Task StartVideoPreviewAsync()
        {
            await _mediaCapture.InitializeAsync();
            _displayRequest.RequestActive();
            PreviewControl.Source = _mediaCapture;
            await _mediaCapture.StartPreviewAsync();

            TimeSpan timerInterval = TimeSpan.FromMilliseconds(66); //15fps
            _frameProcessingTimer = ThreadPoolTimer.CreatePeriodicTimer(new TimerElapsedHandler(ProcessCurrentVideoFrame), timerInterval);
            VideoProperties = _mediaCapture.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.VideoPreview) as VideoEncodingProperties;
        }

        private string _modelFileName = "mycustomvision.onnx";
        private MyCustomVisionModel _model = null;
        
        private async Task LoadModelAsync()
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => StatusText.Text = $"Loading {_modelFileName}");
            var modelFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri($"ms-appx:///Assets/{_modelFileName}"));
            _model = await MyCustomVisionModel.CreateFromStreamAsync(modelFile);
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => StatusText.Text = $"Loaded {_modelFileName}");
        }

        private readonly SemaphoreSlim _frameProcessingSemaphore = new SemaphoreSlim(1);
        private ThreadPoolTimer _frameProcessingTimer;
        public VideoEncodingProperties VideoProperties;

        private async void ProcessCurrentVideoFrame(ThreadPoolTimer timer)
        {
            if (_mediaCapture.CameraStreamState != Windows.Media.Devices.CameraStreamState.Streaming || !_frameProcessingSemaphore.Wait(0))
            {
                return;
            }
            try
            {
                using (VideoFrame previewFrame = new VideoFrame(BitmapPixelFormat.Bgra8, (int)VideoProperties.Width, (int)VideoProperties.Height))
                {
                    await _mediaCapture.GetPreviewFrameAsync(previewFrame);

                    await Task.Run(async () =>
                    {
                        await EvaluateVideoFrameAsync(previewFrame);
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception with ProcessCurrentVideoFrame: " + ex);
            }
            finally
            {
                _frameProcessingSemaphore.Release();
            }
        }

        private async Task EvaluateVideoFrameAsync(VideoFrame frame)
        {
            if (frame != null)
            {
                try
                {
                    MyCustomVisionModelInput inputData = new MyCustomVisionModelInput
                    {
                        Data = frame
                    };
                    var output = await _model.EvaluateAsync(inputData);
                    var product = output.ClassLabel.GetAsVectorView()[0];
                    var loss = output.Loss[0][product];
                    var message = string.Join(",  ", product + " " + (loss * 100.0f).ToString("#0.00") + "%");
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => StatusText.Text = message);
                    Debug.WriteLine(message);
                    _spiDisplay.WriteLinesToScreen(new List<string> { message });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"error: {ex.Message}");
                }
            }
        }

        private readonly SPIDisplay _spiDisplay = new SPIDisplay();

    }
}
