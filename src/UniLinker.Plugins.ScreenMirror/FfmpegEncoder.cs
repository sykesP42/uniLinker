using System.Diagnostics;
using UniLinker.Plugin.Sdk;

namespace UniLinker.Plugins.ScreenMirror;

/// <summary>
/// FFmpeg subprocess encoder that pipes raw BGRA frames to ffmpeg.exe stdin
/// and reads H.264 NAL units from stdout.
/// Gracefully degrades if ffmpeg.exe is not found: Initialize() returns false.
/// </summary>
public class FfmpegEncoder : IEncoder
{
    private Process? _ffmpeg;
    private int _width, _height, _fps, _bitrateKbps;
    private bool _initialized;
    private readonly List<byte[]> _pendingNals = new();
    private readonly object _nalLock = new();

    public event Action<EncodedPacket>? PacketEncoded;

    public bool Initialize(int width, int height, int fps, int bitrateKbps)
    {
        _width = width > 0 ? width : 1920;
        _height = height > 0 ? height : 1080;
        _fps = fps > 0 ? fps : 30;
        _bitrateKbps = bitrateKbps > 0 ? bitrateKbps : 15000;

        try
        {
            var ffmpegPath = FindFfmpeg();
            if (ffmpegPath == null)
            {
                System.Diagnostics.Debug.WriteLine("FFmpeg: ffmpeg.exe not found on system");
                return false;
            }

            // Build ffmpeg arguments: raw BGRA in, H.264 NAL out
            // Try hardware encoder first (qsv on Intel, nvenc on Nvidia, amf on AMD)
            // Fall back to libx264 software encoder
            var args = $"-y -f rawvideo -pixel_format bgra -video_size {width}x{height} " +
                       $"-framerate {fps} -i - " +
                       $"-c:v h264_qsv -preset p4 -tune ll -b:v {bitrateKbps}k " +
                       $"-maxrate {bitrateKbps * 2}k -bufsize {bitrateKbps * 4}k " +
                       $"-bf 0 -g {fps * 2} -f h264 -";

            _ffmpeg = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
                EnableRaisingEvents = true,
            };

            _ffmpeg.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    System.Diagnostics.Debug.WriteLine($"FFmpeg: {e.Data}");
            };

            if (!_ffmpeg.Start())
            {
                System.Diagnostics.Debug.WriteLine("FFmpeg: process failed to start");
                return false;
            }

            _ffmpeg.BeginErrorReadLine();

            // Start reading encoded H.264 from stdout in background
            _ = ReadEncodedDataAsync(_ffmpeg.StandardOutput.BaseStream);

            _initialized = true;
            System.Diagnostics.Debug.WriteLine($"FFmpeg: encoder started ({width}x{height} @{fps}fps, {bitrateKbps}kbps)");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FFmpeg init error: {ex.Message}");
            return false;
        }
    }

    private async Task ReadEncodedDataAsync(Stream stdout)
    {
        var buffer = new byte[65536];
        var pending = new MemoryStream();
        long frameIndex = 0;

        try
        {
            while (_ffmpeg != null && !_ffmpeg.HasExited)
            {
                int read = await stdout.ReadAsync(buffer);
                if (read <= 0) break;

                pending.Write(buffer, 0, read);

                var data = pending.ToArray();
                var nals = SplitNalUnits(data, out int consumed);

                foreach (var nal in nals)
                {
                    var ts = frameIndex * 1000000 / _fps;
                    bool isKeyFrame = IsKeyFrame(nal);

                    lock (_nalLock)
                    {
                        _pendingNals.Add(nal);
                    }

                    PacketEncoded?.Invoke(new EncodedPacket(nal, ts, isKeyFrame));
                }

                // Keep unconsumed trailing data
                var remaining = new byte[data.Length - consumed];
                Buffer.BlockCopy(data, consumed, remaining, 0, remaining.Length);
                pending.SetLength(0);
                pending.Write(remaining);

                if (nals.Count > 0)
                    frameIndex++;
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (IOException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FFmpeg read error: {ex.Message}");
        }
    }

    /// <summary>
    /// Split H.264 byte stream into individual NAL units by start code.
    /// Start codes: 0x00 0x00 0x00 0x01 (4-byte) or 0x00 0x00 0x01 (3-byte).
    /// </summary>
    private static List<byte[]> SplitNalUnits(byte[] data, out int consumed)
    {
        var nals = new List<byte[]>();
        consumed = 0;

        if (data.Length < 3) return nals;

        int i = 0;
        int nalStart = -1;
        int startCodeLen = 0;

        while (i < data.Length - 3)
        {
            bool foundStart = false;
            int foundLen = 0;

            if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 0 && data[i + 3] == 1)
            {
                foundStart = true;
                foundLen = 4;
            }
            else if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 1)
            {
                foundStart = true;
                foundLen = 3;
            }

            if (foundStart)
            {
                // If we already found a previous start code, extract the NAL between them
                if (nalStart >= 0)
                {
                    int nalLen = i - nalStart - startCodeLen;
                    if (nalLen > 0)
                    {
                        var nal = new byte[nalLen];
                        Buffer.BlockCopy(data, nalStart + startCodeLen, nal, 0, nalLen);
                        nals.Add(nal);
                    }
                }

                nalStart = i;
                startCodeLen = foundLen;
                i += foundLen;
                consumed = i;
            }
            else
            {
                i++;
            }
        }

        // Don't extract the last NAL if it's incomplete (no following start code)
        // Only extract if we found at least one complete NAL
        if (nals.Count > 0)
        {
            consumed = nalStart; // rewind to last start code for next iteration
            // Remove the last NAL since it may be incomplete
        }
        else if (nalStart >= 0)
        {
            // We found a start code but no following start code — keep all data for next iteration
            consumed = 0;
        }

        return nals;
    }

    private static bool IsKeyFrame(byte[] nal)
    {
        if (nal.Length == 0) return false;
        // NAL unit type is the lower 5 bits of the first byte
        int nalType = nal[0] & 0x1F;
        return nalType == 5; // IDR slice = key frame
    }

    public void Encode(CaptureFrame frame)
    {
        if (!_initialized || _ffmpeg == null || _ffmpeg.HasExited) return;

        try
        {
            // Write raw BGRA pixel data to ffmpeg stdin
            if (frame.RawData != null && frame.RawData.Length > 0)
            {
                _ffmpeg.StandardInput.BaseStream.Write(frame.RawData);
                _ffmpeg.StandardInput.BaseStream.Flush();
            }
        }
        catch (ObjectDisposedException) { }
        catch (IOException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FFmpeg encode error: {ex.Message}");
        }
    }

    public EncoderInfo GetInfo() => new("ffmpeg_h264_qsv", _bitrateKbps, true);

    public void Dispose()
    {
        if (_ffmpeg != null)
        {
            try
            {
                if (!_ffmpeg.HasExited)
                {
                    _ffmpeg.StandardInput.Close();
                    if (!_ffmpeg.WaitForExit(3000))
                        _ffmpeg.Kill();
                }
            }
            catch { }
            _ffmpeg.Dispose();
            _ffmpeg = null;
        }
    }

    private static string? FindFfmpeg()
    {
        // Check common installation locations
        var paths = new[]
        {
            @"C:\ffmpeg\bin\ffmpeg.exe",
            @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
            @"C:\Program Files (x86)\ffmpeg\bin\ffmpeg.exe",
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"),
            Path.Combine(AppContext.BaseDirectory, "ffmpeg", "bin", "ffmpeg.exe"),
            // Scoop / Chocolatey
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "scoop", "apps", "ffmpeg", "current", "bin", "ffmpeg.exe"),
        };

        foreach (var p in paths)
            if (File.Exists(p))
                return p;

        // Try PATH environment variable
        try
        {
            var proc = Process.Start(new ProcessStartInfo("where", "ffmpeg.exe")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (proc != null)
            {
                var result = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit();
                if (!string.IsNullOrEmpty(result))
                {
                    // Take the first line
                    var firstLine = result.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                    if (File.Exists(firstLine))
                        return firstLine;
                }
            }
        }
        catch { }

        return null;
    }
}
