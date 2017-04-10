using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace VideoSocketApp
{
    public sealed partial class MainPage : Page
    {
        private Guid _guid = Guid.NewGuid();
        private MediaPlaybackList _playlist = null;
        private bool Buffering => _playlist.Items.Count == 0;

        public MainPage()
        {
            InitializeComponent();
            Media.MediaEnded += Media_MediaEnded;

            _playlist = new MediaPlaybackList();
            _playlist.CurrentItemChanged += (sender, args) => _playlist.Items.Remove(args.OldItem);
            _playlist.ItemOpened += Playlist_ItemOpened;
            _playlist.ItemFailed += Playlist_ItemFailed;
            Media.SetPlaybackSource(_playlist);
        }

        private void Playlist_ItemOpened(MediaPlaybackList sender, MediaPlaybackItemOpenedEventArgs args)
        {
            Debug.WriteLine("New playlist item Opened");
        }

        private void Playlist_ItemFailed(MediaPlaybackList sender, MediaPlaybackItemFailedEventArgs args)
        {
            Debug.WriteLine("New playlist item Failed!");
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await Task.Delay(TimeSpan.FromSeconds(5));
            await Task.WhenAll(
                PlayNextVideo(),
                DownloadVideos());
        }

        private async Task DownloadVideos()
        {
            var socket = new StreamSocket();
            while (true)
            {
                try
                {
                    await socket.ConnectAsync(new HostName("192.168.56.1"), "13337");
                    break;
                }
                catch { }
            }
            byte[] inbuffer = new byte[1];
            IBuffer result = await socket.InputStream.ReadAsync(inbuffer.AsBuffer(), inbuffer.AsBuffer().Capacity, InputStreamOptions.None);
            while (true)
            {
                Debug.WriteLine($"Requesting next download ({_guid})");
                IBuffer outbuffer = Encoding.UTF8.GetBytes($"{_guid}").AsBuffer();
                await socket.OutputStream.WriteAsync(outbuffer);
                inbuffer = new byte[10000000];
                result = await socket.InputStream.ReadAsync(inbuffer.AsBuffer(), inbuffer.AsBuffer().Capacity, InputStreamOptions.Partial);
                Debug.WriteLine($"Download complete: {result.Length} bytes");
                byte[] guid = result.ToArray().Take(16).ToArray();
                byte[] data = result.ToArray().Skip(16).ToArray();
                _guid = new Guid(guid);
                var stream = new MemoryStream(data);
                var source = MediaSource.CreateFromStream(stream.AsRandomAccessStream(), "video/mp4");
                var item = new MediaPlaybackItem(source);
                _playlist.Items.Add(item);
                Debug.WriteLine($"New playlist item added to list: {data.Length} bytes");
                Debug.WriteLine($"Playlist now contains {_playlist.Items.Count} items");
                if (_playlist.Items.Count > 2)
                {
                    Debug.WriteLine("Playlist stuck, moving next");
                    _playlist.MoveNext();
                }
                Debug.WriteLine($"Media state: {Media.CurrentState}");
                if (Media.CurrentState != MediaElementState.Playing)
                {
                    Debug.WriteLine("Playing...");
                    Media.Play();
                }
                inbuffer = new byte[10000000];
            }
        }

        private async Task PlayNextVideo()
        {
            Debug.WriteLine($"Playing next video...");
            while (true)
            {
                if (!Buffering)
                {
                    BufferingLbl.Visibility = Visibility.Collapsed;
                    Media.Play();
                    break;
                }
                else
                {
                    Debug.WriteLine($"Buffering...");
                    BufferingLbl.Visibility = Visibility.Visible;
                    await Task.Delay(500);
                }
            }
        }

        private async void Media_MediaEnded(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine($"Playback ended");
            await PlayNextVideo();
        }
    }
}