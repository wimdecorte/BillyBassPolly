using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Media.Audio;
using Windows.Media.Core;
using Windows.Media.Render;
using Windows.Storage;
using Windows.Foundation;
using Windows.Storage.FileProperties;
using System.Threading;

namespace BillyBassPolly
{

    public sealed class AudioPlayer
    {
        private AudioGraph _graph;
        private Dictionary<string, AudioFileInputNode> _fileInputs = new Dictionary<string, AudioFileInputNode>();
        private AudioDeviceOutputNode _deviceOutput;
        private int length;

        internal async Task LoadFileAsync(StorageFile file)
        {
            if (_deviceOutput == null)
            {
                await CreateAudioGraph();
            }

            var fileInputResult = await _graph.CreateFileInputNodeAsync(file);

            _fileInputs.Add(file.Name, fileInputResult.FileInputNode);
            fileInputResult.FileInputNode.Stop();
            fileInputResult.FileInputNode.AddOutgoingConnection(_deviceOutput);

            MusicProperties properties = await file.Properties.GetMusicPropertiesAsync();
            TimeSpan myTrackDuration = properties.Duration;
            length = ( Convert.ToInt32(myTrackDuration.TotalSeconds) * 1000 ) + 1000;
        }

        public void Play(string key, double gain)
        {
            var sound = _fileInputs[key];
            sound.OutgoingGain = gain;
            sound.Seek(TimeSpan.Zero);
            sound.Start();
            Thread.Sleep(length);
            sound.Stop();
            sound.Dispose();

        }

        private async Task CreateAudioGraph()
        {
            var settings = new AudioGraphSettings(AudioRenderCategory.Media);
            var result = await AudioGraph.CreateAsync(settings);
            _graph = result.Graph;
            var deviceOutputNodeResult = await _graph.CreateDeviceOutputNodeAsync();
            _deviceOutput = deviceOutputNodeResult.DeviceOutputNode;
            _graph.ResetAllNodes();
            _graph.Start();
        }
    }
}
