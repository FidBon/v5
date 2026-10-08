namespace Lobby;

public static class ClientMessages
{
    public const int ClientHello = 10100;
    public const int Login = 10101;
    public const int ClientCapabilities = 10107;
    public const int KeepAlive = 10108;
    public const int ClientInfo = 10177;
    public const int ChangeAvatarName = 10212;
    public const int ClientInput = 10555;
    public const int GoHome = 14101;
    public const int EndClientTurn = 14102;
    public const int MatchmakeRequest = 14103;
    public const int CancelMatchmaking = 14106;
    public const int GoHomeFromOffline = 14109;
    public const int AskForBattleEnd = 14110;
    public const int AskProfile = 14113;
    public const int GetLeaderboard = 14403;
    public const int AvatarNameCheckRequest = 14600;
    public const int AllianceCreate = 14301;
    public const int AskForClan = 14302;
    public const int AskForJoinableAlliancesList = 14303;
    public const int AllianceJoin = 14305;
    public const int AllianceLeave = 14308;
    public const int AllianceChat = 14315;
    public const int AllianceSearch = 14324;
    public const int TeamCreate = 14350;
    public const int TeamJoin = 14351;
    public const int TeamKick = 14352;
    public const int TeamLeave = 14353;
    public const int TeamChangeMemberSettings = 14354;
    public const int TeamSetMemberReady = 14355;
    public const int TeamTogglePractice = 14356;
    public const int TeamToggleMemberSide = 14357;
    public const int TeamSpectate = 14358;
    public const int TeamChat = 14359;
    public const int TeamPostAd = 14360;
    public const int TeamMemberStatus = 14361;
    public const int TeamSetRankedLocation = 14362;
    public const int TeamSetLocation = 14363;
}

public static class ServerMessages
{
    public const int LoginFailed = 20103;
    public const int LoginOk = 20104;
    public const int KeepAliveOk = 20108;
    public const int OwnHomeData = 24101;
    public const int LobbyInfo = 23457;
    public const int AvailableServerCommand = 24111;
    public const int MatchmakingStatus = 20405;
    public const int MatchmakingCancelled = 20406;
    public const int StartLoading = 20559;
    public const int VisionUpdate = 24109;
    public const int BattleEnd = 23456;
    public const int Profile = 24113;
    public const int Leaderboard = 24403;
    public const int AvatarNameCheckResponse = 20300;
    public const int FriendList = 20105;
    public const int AllianceData = 24301;
    public const int JoinableAlliancesList = 24304;
    public const int ClanStream = 24311;
    public const int AllianceEvent = 24333;
    public const int MyAlliance = 24399;
    public const int AllianceSearchResult = 24324;
    public const int TeamState = 24124;
    public const int TeamLeft = 24125;
    public const int TeamError = 24129;
    public const int TeamGameStarting = 24130;
    public const int TeamStream = 24131;
    public const int AllianceTeams = 24364;
}

public sealed class LoginPayload
{
    public int HighId { get; init; }
    public int LowId { get; init; }
    public string Token { get; init; } = string.Empty;
    public int MajorVersion { get; init; }
    public int MinorVersion { get; init; }
    public int Build { get; init; }
    public string FingerprintSha { get; init; } = string.Empty;
    public string DeviceId { get; init; } = string.Empty;
    public string Device { get; init; } = string.Empty;
    public string Region { get; init; } = "EN";
}
