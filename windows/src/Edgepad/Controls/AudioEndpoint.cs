using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace Edgepad.Controls;

/// <summary>
/// The default speakers or microphone. The device is looked up on every call, so plugging in a headset
/// is followed without a restart. A missing device returns false rather than throwing: a laptop with no
/// microphone must not drop the phone's connection.
/// </summary>
internal sealed class AudioEndpoint(DataFlow flow) : IDisposable
{
    // Created on first use, so constructing one touches no COM (tests construct them freely).
    private MMDeviceEnumerator? devices;

    public bool SetLevel(int percent) => With(volume => volume.MasterVolumeLevelScalar = percent / 100f);

    public bool ToggleMute() => With(volume => volume.Mute = !volume.Mute);

    /// <summary>The current level and mute, or null when there is no such device.</summary>
    public (int Percent, bool Muted)? Read()
    {
        (int, bool)? state = null;
        With(volume => state = (ToPercent(volume.MasterVolumeLevelScalar), volume.Mute));
        return state;
    }

    /// <summary>
    /// Reports every change to the device — from the phone, the keyboard's volume keys, anything — until the
    /// returned handle is disposed. Null when there is no such device.
    /// </summary>
    // ponytail: watches the device that was default when the phone connected; a headset plugged in
    // mid-session is set through With() but not watched until the phone reconnects.
    public IDisposable? Watch(Action<int, bool> onChange)
    {
        try
        {
            devices ??= new MMDeviceEnumerator();
            var device = devices.GetDefaultAudioEndpoint(flow, Role.Multimedia);
            var volume = device.AudioEndpointVolume;
            void Handler(AudioVolumeNotificationData data) => onChange(ToPercent(data.MasterVolume), data.Muted);
            volume.OnVolumeNotification += Handler;
            return new Subscription(() =>
            {
                volume.OnVolumeNotification -= Handler;
                device.Dispose();
            });
        }
        catch (COMException)
        {
            return null;
        }
    }

    private bool With(Action<AudioEndpointVolume> change)
    {
        try
        {
            devices ??= new MMDeviceEnumerator();
            using var device = devices.GetDefaultAudioEndpoint(flow, Role.Multimedia);
            change(device.AudioEndpointVolume);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static int ToPercent(float scalar) => (int)Math.Round(Math.Clamp(scalar, 0f, 1f) * 100);

    public void Dispose() => devices?.Dispose();
}
