using Data.Models;
using Lobby.Handlers;
using Protocol;

namespace Lobby;

public sealed class LobbyDispatcher(LoginHandler login, CommandHandler commands, BattleHandler battles, SocialHandler social, ClubHandler clubs, RoomHandler rooms) : IMessageDispatcher
{
    private readonly HashSet<int> _reportedUnhandled = [];

    private readonly HashSet<int> _seenIds = [];

    public async Task DispatchAsync(LobbySession session, int messageId, int version, byte[] payload, CancellationToken ct)
    {
        if (_seenIds.Add(messageId))
            Console.WriteLine($"[lobby] клиент впервые прислал пакет {messageId}");

        switch (messageId)
        {
            case ClientMessages.ClientHello:
                break;

            case ClientMessages.Login:
                await login.HandleAsync(session, payload, ct);
                break;

            case ClientMessages.KeepAlive:
                await session.SendAsync(ServerMessages.KeepAliveOk, new ByteStreamWriter(), 0, ct);
                break;

            case ClientMessages.GoHome:
            case ClientMessages.GoHomeFromOffline:
                if (session.Player is PlayerState player)
                    await login.SendOwnHomeDataAsync(session, player, ct);
                break;

            case ClientMessages.EndClientTurn:
                await commands.HandleEndClientTurnAsync(session, payload, ct);
                break;

            case ClientMessages.MatchmakeRequest:
                await battles.HandleMatchmakeRequestAsync(session, payload, ct);
                break;

            case ClientMessages.ClientInput:
                battles.HandleClientInput(session, payload);
                break;

            case ClientMessages.CancelMatchmaking:
                await battles.CancelMatchmakingAsync(session, ct);
                break;

            case ClientMessages.AskProfile:
                await social.HandleAskProfileAsync(session, payload, ct);
                break;

            case ClientMessages.GetLeaderboard:
                await social.HandleLeaderboardAsync(session, payload, ct);
                break;

            case ClientMessages.AvatarNameCheckRequest:
                await social.HandleNameCheckAsync(session, payload, ct);
                break;

            case ClientMessages.ChangeAvatarName:
                await social.HandleChangeNameAsync(session, payload, ct);
                break;

            case ClientMessages.AskForJoinableAlliancesList:
                await clubs.HandleJoinableListAsync(session, ct);
                break;

            case ClientMessages.AskForClan:
                await clubs.HandleAskForClanAsync(session, payload, ct);
                break;

            case ClientMessages.AllianceCreate:
                await clubs.HandleCreateAsync(session, payload, ct);
                break;

            case ClientMessages.AllianceJoin:
                await clubs.HandleJoinAsync(session, payload, ct);
                break;

            case ClientMessages.AllianceLeave:
                await clubs.HandleLeaveAsync(session, ct);
                break;

            case ClientMessages.AllianceChat:
                await clubs.HandleChatAsync(session, payload, ct);
                break;

            case ClientMessages.AllianceSearch:
                await clubs.HandleSearchAsync(session, payload, ct);
                break;

            case ClientMessages.TeamCreate:
                await rooms.HandleCreateAsync(session, payload, ct);
                break;

            case ClientMessages.TeamJoin:
                await rooms.HandleJoinAsync(session, payload, ct);
                break;

            case ClientMessages.TeamSpectate:
                await rooms.HandleSpectateAsync(session, payload, ct);
                break;

            case ClientMessages.TeamKick:
                await rooms.HandleKickAsync(session, payload, ct);
                break;

            case ClientMessages.TeamLeave:
                await rooms.HandleLeaveAsync(session, ct);
                break;

            case ClientMessages.TeamChangeMemberSettings:
                await rooms.HandleChangeMemberSettingsAsync(session, payload, ct);
                break;

            case ClientMessages.TeamSetMemberReady:
                await rooms.HandleSetReadyAsync(session, payload, ct);
                break;

            case ClientMessages.TeamTogglePractice:
                await rooms.HandleTogglePracticeAsync(session, ct);
                break;

            case ClientMessages.TeamToggleMemberSide:
                await rooms.HandleToggleSideAsync(session, ct);
                break;

            case ClientMessages.TeamChat:
                await rooms.HandleChatAsync(session, payload, ct);
                break;

            case ClientMessages.TeamPostAd:
                await rooms.HandlePostAdAsync(session, ct);
                break;

            case ClientMessages.TeamMemberStatus:
                await rooms.HandleMemberStatusAsync(session, payload, ct);
                break;

            case ClientMessages.TeamSetRankedLocation:
                await rooms.HandleSetRankedLocationAsync(session, payload, ct);
                break;

            case ClientMessages.TeamSetLocation:
                await rooms.HandleSetLocationAsync(session, payload, ct);
                break;

            case ClientMessages.ClientCapabilities:
            case ClientMessages.ClientInfo:
                break;

            default:
                if (_reportedUnhandled.Add(messageId))
                    Console.WriteLine($"[lobby] пакет {messageId} пока не обработан ({payload.Length} б)");
                break;
        }
    }
}
