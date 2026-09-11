namespace Edgepad.Protocol;

/// <summary>What a RUN_ACTION frame may ask for. Mirrors protocol/actions.txt; a test holds them equal.</summary>
internal enum ActionId : byte
{
    MuteToggle = 1,
    PlayPause = 2,
    NextTrack = 3,
    PreviousTrack = 4,
    MicMuteToggle = 5,
    Lock = 6,
    TaskView = 20,
    ShowDesktop = 21,
    Search = 22,
    Notifications = 23,
    DesktopLeft = 24,
    DesktopRight = 25,
    AppSwitchBegin = 26,
    AppSwitchNext = 27,
    AppSwitchPrevious = 28,
    AppSwitchEnd = 29,
}

/// <summary>What a SET_VALUE frame may set and a STATE frame reports. Mirrors protocol/actions.txt.</summary>
internal enum ControlId : byte
{
    Volume = 0,
    Brightness = 1,
    MicLevel = 2,
}
