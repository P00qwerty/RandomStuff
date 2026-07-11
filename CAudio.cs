using Android.Media;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AgOpenGPS
{
    /*
    if (CheckSelfPermission(Android.Manifest.Permission.RecordAudio) != Permission.Granted)
    {
        RequestPermissions(new string[] { Android.Manifest.Permission.RecordAudio }, 10);
    }
    else
    {
        if (recording)
        {
            audio.StopRecording();
            // Play recorded audio
            audio.PlayRecording();
        }
        else
        {
            audio.StartRecording();
        }
        recording = !recording;
    }
    */

    public class AudioRecorderPlayer
    {
        const int SampleRate = 16000;
        const ChannelIn ChannelInConfig = ChannelIn.Mono;
        const ChannelOut ChannelOutConfig = ChannelOut.Mono;
        const Android.Media.Encoding AudioEncoding = Android.Media.Encoding.Pcm16bit;

        readonly int _bufferSize;
        AudioRecord _recorder;
        AudioTrack _player;
        List<byte[]> _recordedPackets;
        CancellationTokenSource _cts;

        public AudioRecorderPlayer()
        {
            _bufferSize = AudioRecord.GetMinBufferSize(SampleRate, ChannelInConfig, AudioEncoding);
            _recorder = new AudioRecord(AudioSource.Mic, SampleRate, ChannelInConfig, AudioEncoding, _bufferSize);
            _player = new AudioTrack(Stream.Music, SampleRate, ChannelOutConfig, AudioEncoding, _bufferSize, AudioTrackMode.Stream);
            _recordedPackets = new List<byte[]>();
        }

        public void StartRecording()
        {
            if (_cts != null) return;

            _recordedPackets.Clear();
            _cts = new CancellationTokenSource();
            _recorder.StartRecording();

            Task.Run(() => RecordLoop(_cts.Token));
        }

        public void StopRecording()
        {
            if (_cts == null) return;

            _cts.Cancel();
            _recorder.Stop();
            _cts.Dispose();
            _cts = null;
        }

        public void PlayRecording()
        {
            if (_recordedPackets.Count == 0) return;

            _player.Play();

            Task.Run(() =>
            {
                foreach (var packet in _recordedPackets)
                {
                    _player.Write(packet, 0, packet.Length);
                }
                _player.Stop();
            });
        }

        private void RecordLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    byte[] buffer = new byte[_bufferSize];
                    int read = _recorder.Read(buffer, 0, buffer.Length);
                    if (read > 0)
                    {
                        byte[] packet = new byte[read];
                        Buffer.BlockCopy(buffer, 0, packet, 0, read);
                        _recordedPackets.Add(packet);
                    }
                }
            }
            catch (Exception ex)
            {
                glm.WriteErrorLog(ex);
            }
        }
    }
}