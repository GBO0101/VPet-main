using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using SherpaOnnx;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class VoiceService : IDisposable
{
    private const string ModelDir = @"C:\model";
    private OfflineRecognizer? _recognizer;
    private OfflineTts? _tts;
    private WaveInEvent? _waveIn;
    private MemoryStream? _audioBuffer;
    private readonly object _lock = new();
    private readonly object _micLock = new();

    public bool IsAsrReady => _recognizer != null;
    public bool IsTtsReady => _tts != null;

    public event EventHandler<string>? SpeechRecognized;
    public event EventHandler<bool>? RecordingStateChanged;

    public void InitializeAsr()
    {
        var modelPath = Path.Combine(ModelDir, "sherpa-onnx-paraformer-zh-2023-09-14", "model.int8.onnx");
        var tokensPath = Path.Combine(ModelDir, "sherpa-onnx-paraformer-zh-2023-09-14", "tokens.txt");

        if (!File.Exists(modelPath) || !File.Exists(tokensPath))
        {
            VoiceLogger.Log($"[InitASR] 模型找不到: model={File.Exists(modelPath)}, tokens={File.Exists(tokensPath)}");
            return;
        }

        VoiceLogger.Log($"[InitASR] 開始載入 ASR 模型...");
        try
        {
            var config = new OfflineRecognizerConfig();
            config.ModelConfig.Paraformer.Model = modelPath;
            config.ModelConfig.Tokens = tokensPath;
            config.ModelConfig.Debug = 0;
            config.ModelConfig.NumThreads = 2;
            config.ModelConfig.Provider = "cpu";
            _recognizer = new OfflineRecognizer(config);
            VoiceLogger.Log($"[InitASR] ASR 載入完成");
        }
        catch (Exception ex)
        {
            VoiceLogger.LogError("[InitASR]", ex);
        }
    }

    public void InitializeTts()
    {
        var modelDir = Path.Combine(ModelDir, "vits-zh-aishell3");
        var modelPath = Path.Combine(modelDir, "vits-aishell3.onnx");
        var tokensPath = Path.Combine(modelDir, "tokens.txt");
        var lexiconPath = Path.Combine(modelDir, "lexicon.txt");

        if (!File.Exists(modelPath) || !File.Exists(tokensPath))
        {
            VoiceLogger.Log($"[InitTTS] 模型找不到: model={File.Exists(modelPath)}, tokens={File.Exists(tokensPath)}");
            return;
        }

        VoiceLogger.Log($"[InitTTS] 開始載入 TTS 模型...");
        try
        {
            var config = new OfflineTtsConfig();
            config.Model.Vits.Model = modelPath;
            config.Model.Vits.Tokens = tokensPath;
            config.Model.Vits.Lexicon = lexiconPath;
            config.Model.Vits.NoiseScale = 0.667f;
            config.Model.Vits.NoiseScaleW = 0.8f;
            config.Model.Vits.LengthScale = 1.0f;
            config.Model.NumThreads = 2;
            config.Model.Provider = "cpu";
            config.Model.Debug = 0;
            config.RuleFsts = $"{modelDir}/phone.fst,{modelDir}/date.fst,{modelDir}/number.fst";
            config.RuleFars = $"{modelDir}/rule.far";
            config.MaxNumSentences = 1;
            _tts = new OfflineTts(config);
            VoiceLogger.Log($"[InitTTS] TTS 載入完成");
        }
        catch (Exception ex)
        {
            VoiceLogger.LogError("[InitTTS]", ex);
        }
    }

    public void StartRecording()
    {
        WaveInEvent? waveIn;
        lock (_micLock)
        {
            if (_waveIn != null)
            {
                VoiceLogger.Log("[Mic] StartRecording 跳過：已有錄音進行中");
                return;
            }
            VoiceLogger.Log("[Mic] 開始錄音...");
            _audioBuffer = new MemoryStream();
            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 100
            };
            _waveIn.DataAvailable += OnAudioData;
            _waveIn.RecordingStopped += OnRecordingStopped;
            waveIn = _waveIn;
        }
        waveIn.StartRecording();
        RecordingStateChanged?.Invoke(this, true);
    }

    public void StopRecording()
    {
        WaveInEvent? toDispose;
        lock (_micLock)
        {
            if (_waveIn == null)
            {
                VoiceLogger.Log("[Mic] StopRecording 跳過：沒有進行中的錄音");
                return;
            }
            VoiceLogger.Log("[Mic] 停止錄音...");
            toDispose = _waveIn;
            _waveIn = null;
        }
        toDispose.StopRecording();
        toDispose.Dispose();
    }

    private int _dataCount;
    private void OnAudioData(object? sender, WaveInEventArgs e)
    {
        lock (_lock)
        {
            _audioBuffer?.Write(e.Buffer, 0, e.BytesRecorded);
        }
        _dataCount++;
        if (_dataCount % 50 == 0)
            VoiceLogger.Log($"[Mic] 接收音訊中... {_audioBuffer?.Length ?? 0} bytes");
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        RecordingStateChanged?.Invoke(this, false);

        byte[] audioBytes;
        lock (_lock)
        {
            if (_audioBuffer == null || _audioBuffer.Length == 0)
            {
                VoiceLogger.Log("[Mic] 錄音停止：無音訊資料");
                _audioBuffer?.Dispose();
                _audioBuffer = null;
                return;
            }
            audioBytes = _audioBuffer.ToArray();
            _audioBuffer.Dispose();
            _audioBuffer = null;
        }

        _dataCount = 0;
        VoiceLogger.Log($"[Mic] 錄音停止，取得 {audioBytes.Length} bytes ({audioBytes.Length / 2} samples)");

        var captured = audioBytes;
        Task.Run(() =>
        {
            try
            {
                var text = RecognizeBytes(captured);
                VoiceLogger.Log($"[ASR] 辨識結果: \"{text}\"");
                if (!string.IsNullOrWhiteSpace(text))
                    SpeechRecognized?.Invoke(this, text);
            }
            catch (Exception ex)
            {
                VoiceLogger.LogError("[ASR]", ex);
            }
        });
    }

    private string RecognizeBytes(byte[] audioBytes)
    {
        if (_recognizer == null)
        {
            VoiceLogger.Log("[ASR] 跳過：ASR 未初始化");
            return "";
        }
        if (audioBytes.Length < 100)
        {
            VoiceLogger.Log($"[ASR] 跳過：音訊太短 ({audioBytes.Length} bytes)");
            return "";
        }

        try
        {
            var samples = new float[audioBytes.Length / 2];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = BitConverter.ToInt16(audioBytes, i * 2) / 32768f;

            VoiceLogger.Log($"[ASR] 開始辨識 ({samples.Length} samples)...");
            var stream = _recognizer.CreateStream();
            stream.AcceptWaveform(16000, samples);
            _recognizer.Decode(new List<OfflineStream> { stream });
            var result = stream.Result;
            var text = result.Text ?? "";
            VoiceLogger.Log($"[ASR] 辨識完成: \"{text}\"");
            return text;
        }
        catch (Exception ex)
        {
            VoiceLogger.LogError("[ASR]", ex);
            return "";
        }
    }

    public void Speak(string text)
    {
        if (_tts == null || string.IsNullOrWhiteSpace(text))
            return;

        VoiceLogger.Log($"[TTS] 開始合成: \"{text}\"");
        Task.Run(() =>
        {
            try
            {
                var genConfig = new SherpaOnnx.OfflineTtsGenerationConfig
                {
                    Sid = 66,
                    Speed = 1.0f,
                    SilenceScale = 0.2f
                };
                var audio = _tts.GenerateWithConfig(text, genConfig, null);
                var tempFile = Path.Combine(Path.GetTempPath(), "vpet_tts_" + Guid.NewGuid() + ".wav");
                audio.SaveToWaveFile(tempFile);
                VoiceLogger.Log($"[TTS] 合成完成，儲存至 {tempFile}");

                using var reader = new AudioFileReader(tempFile);
                using var output = new NAudio.Wave.DirectSoundOut();
                var mre = new ManualResetEvent(false);
                output.PlaybackStopped += (_, _) => mre.Set();
                output.Init(reader);
                output.Play();
                VoiceLogger.Log("[TTS] 開始播放");
                mre.WaitOne();
                VoiceLogger.Log("[TTS] 播放結束");

                try { File.Delete(tempFile); } catch { }
            }
            catch (Exception ex)
            {
                VoiceLogger.LogError("[TTS]", ex);
            }
        });
    }

    public void Dispose()
    {
        StopRecording();
        _recognizer?.Dispose();
        _tts?.Dispose();
        VoiceLogger.Log("[VoiceService] 已釋放資源");
    }
}
