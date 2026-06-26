using System;
using System.Collections.Generic;
using UnityEngine;

namespace MudShip.MotionRecorder
{
    /// <summary>
    /// 入力デバイス（<see cref="Microphone"/>）から PCM を 1 つの .msra へ記録するセッション。
    /// 毎フレーム <see cref="CaptureFrame"/> でリングバッファの新規サンプルを吸い出して書き出す。
    /// 録画中は完全ブロック単位でのみ消費して GC フリー（端数は次回／停止時に回収）。
    /// </summary>
    public sealed class AudioRecorderSession : IRecorderSession
    {
        const int BufferSeconds = 5;       // Microphone のリングバッファ長
        const int ReadBlockFrames = 2048;  // 1 回に読むサンプルフレーム数（GC フリーの単位）

        readonly RecorderSettings _settings;
        readonly string _requestedDevice;
        readonly int _requestedSampleRate;

        ChunkedStreamWriter _writer;
        AudioClip _clip;
        string _device;
        int _sampleRate;
        int _channels;
        int _clipFrames;
        int _lastHead;
        float[] _floatBuf;
        byte[] _byteBuf;
        bool _micStarted;
        float _peak; // 診断用: 記録中の最大振幅

        public string FilePath { get; private set; }
        public bool IsRecording { get; private set; }

        /// <summary>記録済みサンプルフレーム数。</summary>
        public int FrameCount => _writer?.FrameCount ?? 0;

        public bool Faulted => _writer?.Faulted ?? false;
        public Exception FaultException => _writer?.FaultException;

        /// <summary>実際に使用しているデバイス名（Start 後に有効）。</summary>
        public string Device => _device;

        /// <summary>記録中に観測した最大振幅（0..1）。入力レベル確認用。</summary>
        public float Peak => _peak;

        /// <summary>実サンプルレート(Hz)（Start 後に有効）。</summary>
        public int SampleRate => _sampleRate;

        public AudioRecorderSession(string device, int sampleRate, RecorderSettings settings)
        {
            _requestedDevice = device;
            _requestedSampleRate = sampleRate;
            _settings = settings;
        }

        public void Start(string filePath)
        {
            if (IsRecording)
                throw new InvalidOperationException("Session is already recording.");

            var devices = Microphone.devices;
            if (devices == null || devices.Length == 0)
                throw new InvalidOperationException("録音可能な入力デバイスがありません。");

            _device = _requestedDevice;
            if (string.IsNullOrEmpty(_device) || Array.IndexOf(devices, _device) < 0)
                _device = devices[0]; // フォールバック: 先頭デバイス

            int freq = _requestedSampleRate > 0 ? _requestedSampleRate : 48000;
            Microphone.GetDeviceCaps(_device, out int minFreq, out int maxFreq);
            if (maxFreq > 0)
                freq = Mathf.Clamp(freq, minFreq > 0 ? minFreq : 1, maxFreq);

            _clip = Microphone.Start(_device, true, BufferSeconds, freq);
            if (_clip == null)
                throw new InvalidOperationException($"Microphone.Start に失敗しました: {_device}");
            _micStarted = true;

            _sampleRate = _clip.frequency;
            _channels = Mathf.Max(1, _clip.channels);
            _clipFrames = _clip.samples;
            _lastHead = 0;

            _floatBuf = new float[ReadBlockFrames * _channels];
            _byteBuf = new byte[ReadBlockFrames * _channels * 2];

            var settings = _settings.Normalized();
            byte[] header = BuildHeader(_sampleRate, _channels, out long frameCountPos);
            int stride = MsraFormat.ComputeStride(_channels);
            _writer = new ChunkedStreamWriter(filePath, header, frameCountPos, stride, settings);

            FilePath = filePath;
            IsRecording = true;
        }

        public void CaptureFrame(double timestamp)
        {
            if (!IsRecording)
                return;

            Drain(includeRemainder: false);

            if (_writer.Faulted)
                Stop();
        }

        public void Stop()
        {
            if (!IsRecording)
                return;
            IsRecording = false;

            try
            {
                if (_micStarted)
                    Drain(includeRemainder: true); // 残りサンプルを回収（End する前に）
            }
            catch (Exception)
            {
                // ドレイン失敗は致命ではない。確定処理へ進む。
            }
            finally
            {
                if (_micStarted)
                {
                    Microphone.End(_device);
                    _micStarted = false;
                }
                _writer.Finish();

                if (FrameCount == 0)
                    Debug.LogWarning("[MS_Recorder][Audio] サンプルが 0 でした。デバイスから信号が来ていません。" +
                                     "Windows のマイク権限（デスクトップアプリの許可）、Windows サウンド設定での録音デバイス有効化/レベル、" +
                                     "インターフェースが WASAPI 録音デバイスとして見えているか（ASIO 専用だと Unity からは録れない）を確認してください。");
                else if (_peak <= 0f)
                    Debug.LogWarning("[MS_Recorder][Audio] 無音（peak=0）でした。入力ソース/チャンネル/ゲイン、権限を確認してください。");
            }
        }

        public void Dispose()
        {
            try
            {
                Stop();
            }
            finally
            {
                if (_micStarted)
                {
                    try { Microphone.End(_device); } catch (Exception) { }
                    _micStarted = false;
                }
                _writer?.Dispose();
                _writer = null;
                _clip = null;
            }
        }

        void Drain(bool includeRemainder)
        {
            int head = Microphone.GetPosition(_device);
            if (head < 0)
                return;

            int avail = head - _lastHead;
            if (avail < 0)
                avail += _clipFrames; // リングのラップ

            while (avail >= ReadBlockFrames)
            {
                ReadBlock(ReadBlockFrames, _floatBuf, _byteBuf);
                avail -= ReadBlockFrames;
            }

            if (includeRemainder && avail > 0)
            {
                // 停止時のみ端数を回収（一時確保。録画中は通らない）。
                var fb = new float[avail * _channels];
                var bb = new byte[avail * _channels * 2];
                ReadBlock(avail, fb, bb);
            }
        }

        void ReadBlock(int frames, float[] fbuf, byte[] bbuf)
        {
            _clip.GetData(fbuf, _lastHead); // _lastHead から frames*channels サンプルを（ラップして）読む
            int count = frames * _channels;
            for (int i = 0; i < count; i++)
            {
                float f = fbuf[i];
                float a = f < 0f ? -f : f;
                if (a > _peak) _peak = a;

                int v = (int)(Mathf.Clamp(f, -1f, 1f) * 32767f);
                bbuf[i * 2] = (byte)v;
                bbuf[i * 2 + 1] = (byte)(v >> 8);
            }

            _writer.WriteFrames(new ReadOnlySpan<byte>(bbuf, 0, count * 2));

            _lastHead += frames;
            if (_lastHead >= _clipFrames)
                _lastHead -= _clipFrames;
        }

        static byte[] BuildHeader(int sampleRate, int channels, out long frameCountPos)
        {
            var b = new List<byte>(32);
            BinaryLE.Bytes(b, MsraFormat.Magic);
            BinaryLE.U16(b, MsraFormat.Version);
            BinaryLE.U32(b, 0u); // flags 予約
            BinaryLE.U32(b, (uint)sampleRate);
            BinaryLE.U16(b, (ushort)channels);
            BinaryLE.U16(b, (ushort)MsraFormat.BitsPerSample);

            // startOffset (float64) = 0.0 （IEEE754 で全ゼロ）。将来の精密同期用に確保。
            for (int i = 0; i < 8; i++)
                b.Add(0);

            frameCountPos = b.Count;
            BinaryLE.U32(b, 0); // sampleFrameCount プレースホルダ（Finish で確定）
            return b.ToArray();
        }
    }
}
