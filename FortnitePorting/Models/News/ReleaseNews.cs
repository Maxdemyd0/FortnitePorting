using System;
using FortnitePorting.Models.API.Responses;

namespace FortnitePorting.Models.News;

public static class ReleaseNews
{
    public static NewsEntry Version440 => new()
    {
        Title = "Fortnite Porting 4.4.0",
        SubTitle = "Tray-first startup, background content preparation, online improvements, new themes, and a smoother asset experience.",
        Tag = "v4.4.0",
        Image = "avares://FortnitePorting/Assets/News/4.4.0.png",
        Date = new DateTime(2026, 8, 15),
        Description = """
                      Fortnite Porting 4.4.0
                      =====================

                      This update focuses on getting Fortnite Porting ready before you need it, while making the app more comfortable to leave running throughout the day.

                      System tray and startup
                      ------------------------
                      • Fortnite Porting can minimize to the system tray instead of closing.
                      • Left-click the tray icon to open the app. Its context menu includes the current version, Open, and Exit.
                      • Launch on Startup starts Fortnite Porting directly in the tray, rather than opening a window.
                      • Minimize to Tray, Launch on Startup, and background loading are available from App Settings; dependent options are disabled until tray mode is enabled.

                      Background content preparation
                      ------------------------------
                      • Pakchunks, asset catalogs, thumbnails, and the file index can load while the app is in the tray.
                      • Opening the app during preparation shows the real loading status and progress instead of an incomplete asset grid.
                      • Loading runs once per session and returns to your configured default asset type when ready.
                      • The first-run flow now begins loading after setup is complete.

                      Asset browser
                      -------------
                      • Asset thumbnails are prepared before a catalog is shown, replacing empty-looking placeholders with icon progress.
                      • Asset context menus now include Properties.
                      • The asset-category list animates in the direction of navigation, from Cosmetics through Fall Guys.

                      Appearance
                      ----------
                      • Added Emerald and Sunset themes, each with its own background, accent, two-tone Fortnite Porting logo, and picker icon.
                      • Theme changes retain live resources while switching, so title and logo colors update correctly.

                      Audio
                      -----
                      • Audio playback recreates its output safely when loading audio or changing output devices, preventing operations on an uninitialized player.
                      • Playback now uses the Windows default output device.
                      """
    };

    public static NewsEntry Version441 => new()
    {
        Title = "Fortnite Porting 4.4.1",
        SubTitle = "A polish update for online sign-in, Chat, theme selection, and app reliability.",
        Tag = "v4.4.1",
        Image = "avares://FortnitePorting/Assets/News/4.4.0.png",
        Date = new DateTime(2026, 8, 16),
        Description = """
                      Fortnite Porting 4.4.1
                      =====================

                      This follow-up improves the online experience and fixes edge cases found after the 4.4.0 release.

                      Account and Chat
                      ----------------
                      • Chat and Leaderboard now explain that login is required instead of appearing unavailable.
                      • Send in Chat is available from assets for signed-in users; signed-out users receive the same login prompt.
                      • Chat sessions cleanly reconnect after signing out and back in.
                      • Discord sign-in has a Close option, times out after two minutes, and closes automatically after authentication completes.
                      • Added a Stay Logged In option, enabled by default, to restore your Discord session after restarting Fortnite Porting.
                      • Chat now includes a Powered by Discord footer.

                      Polish and reliability
                      ----------------------
                      • Emerald and Sunset remain correctly selected in the collapsed theme picker.
                      • Secondary launches safely activate the existing app instance, and the activation listener recovers from interrupted requests.
                      • Online subscriptions are cleaned up on sign-out to prevent duplicate updates after re-login.
                      • Background UI dispatch and missing installation-profile handling have been hardened against avoidable crashes.
                      """
    };
}
