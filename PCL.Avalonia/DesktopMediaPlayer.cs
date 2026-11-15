using LibVLCSharp.Shared;

namespace PCL.Avalonia;

/// <summary>Native media is loaded only when playback is requested, keeping headless/startup paths independent.</summary>
public sealed class DesktopMediaPlayer : IDisposable
{
    private LibVLC? _vlc;
    private MediaPlayer? _player;
    private Media? _media;
    public MediaPlayer Player => _player ?? throw new InvalidOperationException("播放器尚未初始化。");
    public void Initialize(string? libraryDirectory)
    {
        if (_player is not null) return;
        try
        {
            if (string.IsNullOrWhiteSpace(libraryDirectory)) LibVLCSharp.Shared.Core.Initialize();
            else LibVLCSharp.Shared.Core.Initialize(libraryDirectory);
            _vlc = new LibVLC("--no-video-title-show", "--no-metadata-network-access");
            _player = new MediaPlayer(_vlc);
        }
        catch (Exception ex) { Dispose(); throw new InvalidOperationException("无法载入 VLC 播放库。请安装 VLC，或在外观设置中指定 libvlc 所在目录。", ex); }
    }
    public void Play(string file, int volume, bool loop)
    {
        if (_vlc is null || _player is null) throw new InvalidOperationException("播放器尚未初始化。");
        _player.Stop(); _media?.Dispose(); _media = new Media(_vlc, new Uri(Path.GetFullPath(file)));
        if (loop) _media.AddOption(":input-repeat=65535");
        _player.Volume = Math.Clamp(volume, 0, 100);
        if (!_player.Play(_media)) throw new InvalidOperationException("无法播放所选媒体。");
    }
    public void Pause() { if (_player is not null) _player.Pause(); }
    public void Stop() { if (_player is not null) _player.Stop(); }
    public void Volume(int value) { if (_player is not null) _player.Volume = Math.Clamp(value, 0, 100); }
    public void Dispose() { _player?.Stop(); _player?.Dispose(); _player = null; _media?.Dispose(); _media = null; _vlc?.Dispose(); _vlc = null; }
}
