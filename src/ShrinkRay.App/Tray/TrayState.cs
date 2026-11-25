namespace ShrinkRay.App.Tray;

/// <summary>Which tray icon shows. Priority when several hold: paused, encoding, failed, idle.</summary>
public enum TrayState
{
    /// <summary>The plain cassette.</summary>
    Idle,

    /// <summary>An encode run is going: the window glows in amber phosphor.</summary>
    Encoding,

    /// <summary>The run is paused: the cassette is dimmed.</summary>
    Paused,

    /// <summary>Idle, but the latest episode failed: a small mark that is not red.</summary>
    Failed,
}
