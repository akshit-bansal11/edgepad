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

    public void Dispose() => devices?.Dispose();
}
