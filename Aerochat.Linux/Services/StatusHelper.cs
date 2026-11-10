using DSharpPlus.Entities;

namespace Aerochat.Linux.Services;

public static class StatusHelper
{
    public static readonly (string Label, UserStatus Status)[] Options =
    {
        ("Available", UserStatus.Online),
        ("Busy", UserStatus.DoNotDisturb),
        ("Away", UserStatus.Idle),
        ("Appear offline", UserStatus.Invisible)
    };

    public static string Label(UserStatus status) => status switch
    {
        UserStatus.Online => "Available",
        UserStatus.Idle => "Away",
        UserStatus.DoNotDisturb => "Busy",
        UserStatus.Invisible => "Appear offline",
        _ => "Offline"
    };

    public static string Color(UserStatus status) => status switch
    {
        UserStatus.Online => "#4CA64C",
        UserStatus.Idle => "#D6A400",
        UserStatus.DoNotDisturb => "#C0392B",
        _ => "#8A8A8A"
    };

    public static UserStatus FromLabel(string? label) => label switch
    {
        "Busy" => UserStatus.DoNotDisturb,
        "Away" => UserStatus.Idle,
        "Appear offline" => UserStatus.Invisible,
        _ => UserStatus.Online
    };

    public static UserStatus Of(DiscordUser? user)
    {
        var presence = user?.Presence;
        if (presence is null)
            return UserStatus.Offline;

        return presence.Status;
    }
}
