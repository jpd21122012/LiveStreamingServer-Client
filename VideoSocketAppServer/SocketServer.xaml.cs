using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Networking;
using Windows.Networking.Connectivity;
using Windows.Networking.Sockets;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace VideoSocketAppServer
{
    public sealed partial class SocketAppServer : Page
    {
        private int _port = 13337;
        private MediaCapture _mediaCap;
        private StreamSocketListener _listener;
        private ManualResetEvent _signal = new ManualResetEvent(false);
        private List<Connection> _connections = new List<Connection>();
        internal CurrentVideo CurrentVideo = new CurrentVideo();
        public SocketAppServer()
        {
            InitializeComponent();
        }
        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await InitialiseVideo();
            await StartListener();
            await BeginRecording();
        }
        private async Task InitialiseVideo()
        {
            Debug.WriteLine($"Initialising video...");
            var settings = ApplicationData.Current.LocalSettings;
            string preferredDeviceName = $"{settings.Values["PreferredDeviceName"]}";
            if (string.IsNullOrWhiteSpace(preferredDeviceName))
                preferredDeviceName = "Microsoft® LifeCam HD-3000";
            var videoDevices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            DeviceInformation device = videoDevices.FirstOrDefault(x => x.Name == preferredDeviceName);
            if (device == null)
                device = videoDevices.FirstOrDefault();
            if (device == null)
                throw new Exception("Cannot find a camera device");
            else
            {
                _mediaCap = new MediaCapture();
                var initSettings = new MediaCaptureInitializationSettings { VideoDeviceId = device.Id };
                await _mediaCap.InitializeAsync(initSettings);
                _mediaCap.Failed += new MediaCaptureFailedEventHandler(MediaCaptureFailed);
            }
            Debug.WriteLine($"Video initialised");
        }

        private void MediaCaptureFailed(MediaCapture sender, MediaCaptureFailedEventArgs errorEventArgs)
        {
            Debug.WriteLine($"Video capture failed: {errorEventArgs.Message}");
        }

        private async Task BeginRecording()
        {
            while (true)
            {
                try
                {
                    Debug.WriteLine($"Recording started");
                    var memoryStream = new InMemoryRandomAccessStream();
                    await _mediaCap.StartRecordToStreamAsync(MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Vga), memoryStream);
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    await _mediaCap.StopRecordAsync();
                    Debug.WriteLine($"Recording finished, {memoryStream.Size} bytes");
                    memoryStream.Seek(0);
                    CurrentVideo.Id = Guid.NewGuid();
                    CurrentVideo.Data = new byte[memoryStream.Size];
                    await memoryStream.ReadAsync(CurrentVideo.Data.AsBuffer(), (uint)memoryStream.Size, InputStreamOptions.None);
                    Debug.WriteLine($"Bytes written to stream");
                    _signal.Set();
                    _signal.Reset();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"StartRecording -> {ex.Message}");
                    break;
                }
            }
        }

        private async Task StartListener()
        {
            Debug.WriteLine($"Starting listener");
            _listener = new StreamSocketListener();
            _listener.ConnectionReceived += (sender, args) =>
            {
                Debug.WriteLine($"Connection received from {args.Socket.Information.RemoteAddress}");
                _connections.Add(new Connection(args.Socket, this));
            };
            HostName host = NetworkInformation.GetHostNames().FirstOrDefault(x => x.IPInformation != null && x.Type == HostNameType.Ipv4);
            await _listener.BindEndpointAsync(host, $"{_port}");
            Debug.WriteLine($"Listener started on {host.DisplayName}:{_listener.Information.LocalPort}");
        }

        internal byte[] GetCurrentVideoDataAsync(Guid guid)
        {
            if (CurrentVideo.Id == Guid.Empty || CurrentVideo.Id == guid)
                 _signal.WaitOne();
            return CurrentVideo.Id.ToByteArray().Concat(CurrentVideo.Data).ToArray();
        }
    }
}