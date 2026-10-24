using Edgepad.Platform;
using Xunit;

namespace Edgepad.Tests;

public sealed class MprisTests
{
    private const string Spotify = "org.mpris.MediaPlayer2.spotify";
    private const string Firefox = "org.mpris.MediaPlayer2.firefox.instance_1_234";
    private const string Vlc = "org.mpris.MediaPlayer2.vlc";

    [Fact]
    public void TitleArtistPositionAndLengthBecomeTheMediaState()
    {
        var player = Player(Spotify, "Playing", 84_000_000, Track("Blue in Green", ["Miles Davis"], 227_000_000));
        var state = MprisMap.ToState(player);
        Assert.Equal("Blue in Green - Miles Davis", state.NowPlaying);
        Assert.Equal("Spotify", state.App);
        Assert.True(state.Playing);
        Assert.Equal(84, state.PositionSeconds);
        Assert.Equal(227, state.DurationSeconds);
        Assert.Equal(37, state.PositionPercent);
    }

    [Fact]
    public void SeveralArtistsAreJoinedAndNoArtistIsJustTheTitle()
    {
        Assert.Equal(
            "Song - A, B",
            MprisMap.ToState(Player(Vlc, "Playing", 0, Track("Song", ["A", " ", "B"], 1_000_000))).NowPlaying);
        Assert.Equal("Song", MprisMap.ToState(Player(Vlc, "Playing", 0, Track("Song", [], 1_000_000))).NowPlaying);
    }

    [Fact]
    public void ALengthInAnyIntegerTypeIsRead()
    {
        foreach (object length in new object[] { 60_000_000L, 60_000_000, 60_000_000UL })
        {
            var metadata = new Dictionary<string, object?> { ["xesam:title"] = "T", ["mpris:length"] = length };
            Assert.Equal(60, MprisMap.ToState(Player(Vlc, "Paused", 30_000_000, metadata)).DurationSeconds);
        }
    }

    [Fact]
    public void NoLengthMeansNoTimelineAndNoPercent()
    {
        var state = MprisMap.ToState(Player(Firefox, "Playing", 5_000_000, new Dictionary<string, object?> { ["xesam:title"] = "Stream" }));
        Assert.Equal(0, state.DurationSeconds);
        Assert.Equal(0, state.PositionSeconds);
        Assert.Equal(0, state.PositionPercent);
        Assert.Equal("Firefox", state.App);
    }

    [Fact]
    public void APositionPastTheEndStaysOnTheDial()
    {
        var state = MprisMap.ToState(Player(Vlc, "Playing", 999_000_000, Track("T", [], 100_000_000)));
        Assert.Equal(100, state.PositionPercent);
        Assert.Equal(100, state.PositionSeconds);
    }

    [Fact]
    public void ALongTitleFitsATextFrame()
    {
        var state = MprisMap.ToState(Player(Vlc, "Playing", 0, Track(new string('x', 400), [], 1_000_000)));
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(state.NowPlaying) <= 200);
    }

    [Theory]
    [InlineData("org.mpris.MediaPlayer2.spotify", "Spotify")]
    [InlineData("org.mpris.MediaPlayer2.firefox.instance_1_234", "Firefox")]
    [InlineData("org.mpris.MediaPlayer2.", "")]
    [InlineData("vlc", "Vlc")]
    public void TheAppIsNamedFromTheBusName(string busName, string expected) =>
        Assert.Equal(expected, MprisMap.AppName(busName));

    [Fact]
    public void APlayingPlayerBeatsAPausedOneWhateverTheOrder()
    {
        var paused = Player(Spotify, "Paused", 0, Track("A", [], 1));
        var playing = Player(Vlc, "Playing", 0, Track("B", [], 1));
        Assert.Equal(Vlc, MprisMap.Choose([paused, playing], null)?.BusName);
        Assert.Equal(Vlc, MprisMap.Choose([playing, paused], null)?.BusName);
    }

    [Fact]
    public void PausedBeatsStopped()
    {
        var stopped = Player(Spotify, "Stopped", 0, Track("A", [], 1));
        var paused = Player(Vlc, "Paused", 0, Track("B", [], 1));
        Assert.Equal(Vlc, MprisMap.Choose([stopped, paused], null)?.BusName);
    }

    [Fact]
    public void TwoPlayingPlayersKeepTheOneAlreadyFollowed()
    {
        var first = Player(Spotify, "Playing", 0, Track("A", [], 1));
        var second = Player(Vlc, "Playing", 0, Track("B", [], 1));
        Assert.Equal(Spotify, MprisMap.Choose([first, second], null)?.BusName);
        Assert.Equal(Vlc, MprisMap.Choose([first, second], Vlc)?.BusName);
    }

    [Fact]
    public void NoPlayersChoosesNothing() => Assert.Null(MprisMap.Choose([], null));

    [Fact]
    public void TheTrackIdIsOnlyUsableWhenThereIsATrack()
    {
        var withTrack = Player(Vlc, "Playing", 0, new Dictionary<string, object?> { ["mpris:trackid"] = "/org/vlc/track/7" });
        var noTrack = Player(Vlc, "Stopped", 0, new Dictionary<string, object?> { ["mpris:trackid"] = MprisMap.NoTrack });
        var absent = Player(Vlc, "Stopped", 0, new Dictionary<string, object?>());
        Assert.Equal("/org/vlc/track/7", MprisMap.TrackId(withTrack));
        Assert.Null(MprisMap.TrackId(noTrack));
        Assert.Null(MprisMap.TrackId(absent));
    }

    [Theory]
    [InlineData(0, 200_000_000, 0)]
    [InlineData(50, 200_000_000, 100_000_000)]
    [InlineData(100, 200_000_000, 200_000_000)]
    [InlineData(250, 200_000_000, 200_000_000)]
    [InlineData(-3, 200_000_000, 0)]
    public void ASeekPercentIsAPositionInMicroseconds(int percent, long length, long expected) =>
        Assert.Equal(expected, MprisMap.PositionFor(percent, length));

    private static PlayerSnapshot Player(string busName, string status, long position, Dictionary<string, object?> metadata) =>
        new(busName, status, position, metadata);

    private static Dictionary<string, object?> Track(string title, string[] artists, long length) => new()
    {
        ["xesam:title"] = title,
        ["xesam:artist"] = artists,
        ["mpris:length"] = length,
    };
}
