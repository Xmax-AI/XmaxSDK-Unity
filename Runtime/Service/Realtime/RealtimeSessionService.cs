//
//  RealtimeSessionService.cs
//  XmaxSDK
//
//  Author: 翁跃庭 (Yueting Weng)
//  GitHub: https://github.com/XingMai/XmaxSDK-Unity
//
//  Copyright (c) 2026 XMAX.AI PTE. LTD.
//

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Xmax.SDK
{

    /// <summary>
    /// The server session creation, heartbeat, and closure service.
    /// </summary>
    internal interface IRealtimeSessionService
    {

        /// <summary>
        /// Creates a server session for the selected regional generation model.
        /// </summary>
        Task<XmaxSession> CreateSessionAsync(
            string model,
            CancellationToken cancellationToken
        );

        /// <summary>
        /// Renews a server session and reads its latest state.
        /// </summary>
        Task<XmaxSession> HeartbeatSessionAsync(
            string id,
            CancellationToken cancellationToken
        );

        /// <summary>
        /// Closes the server session; request failures are returned through the task.
        /// </summary>
        Task CloseSessionAsync(
            string id,
            CancellationToken cancellationToken
        );
    }

    /// <summary>
    /// Converts session API responses into server-assigned Agora credentials.
    /// </summary>
    internal sealed class RealtimeSessionService : IRealtimeSessionService
    {

        /// <summary>
        /// The shared authenticated HTTP service.
        /// </summary>
        private readonly IApiService _api;

        /// <summary>
        /// Creates a realtime session service.
        /// </summary>
        internal RealtimeSessionService(
            IApiService api
        )
        {
            _api = api;
        }

        /// <summary>
        /// Creates a server session for the selected regional generation model.
        /// </summary>
        public async Task<XmaxSession> CreateSessionAsync(
            string model,
            CancellationToken cancellationToken
        )
        {
            var body = JsonCodec.Encode(new System.Collections.Generic.Dictionary<string, object> { ["model"] = model });

            return ParseSession(await _api.RequestAsync(
                    "POST",
                    "/session",
                    body,
                    cancellationToken
                ));
        }

        /// <summary>
        /// Renews a server session and reads its latest state.
        /// </summary>
        public async Task<XmaxSession> HeartbeatSessionAsync(
            string id,
            CancellationToken cancellationToken
        )
        {
            return ParseSession(await _api.RequestAsync(
                    "PUT",
                    "/session/" + Uri.EscapeDataString(id) + "/heartbeat",
                    null,
                    cancellationToken
                ));
        }

        /// <summary>
        /// Closes the server session; request failures are returned through the task.
        /// </summary>
        public async Task CloseSessionAsync(
            string id,
            CancellationToken cancellationToken
        )
        {
            await _api.RequestAsync(
                "DELETE",
                "/session/" + Uri.EscapeDataString(id),
                null,
                cancellationToken
            );
        }

        /// <summary>
        /// Parses a session whose RTC credentials may be an object or a JSON string.
        /// </summary>
        internal static XmaxSession ParseSession(
            JsonValue data
        )
        {
            if (data == null || !data.IsObject)
            {
                throw new XmaxException(
                    XmaxErrorCode.SessionError,
                    "Invalid session response."
                );
            }

            var sessionUid = ReadString(
                data,
                "sessionUid"
            );
            if (string.IsNullOrEmpty(sessionUid))
            {
                throw new XmaxException(
                    XmaxErrorCode.SessionError,
                    "Session response does not contain sessionUid."
                );
            }

            var session = new XmaxSession
            {
                SessionUid = sessionUid,
                UserUid = ReadString(
                    data,
                    "userUid"
                ),
                Status = ReadString(
                    data,
                    "status"
                )
            };

            session.CloseReason = ReadString(
                data,
                "closeReason"
            );
            JsonValue modelExtra = null;
            if (data.ContainsKey("modelExtra"))
            {
                var raw = data["modelExtra"];
                if (raw != null && raw.IsString)
                {
                    try
                    {
                        modelExtra = JsonCodec.Decode((string)raw);
                    }
                    catch
                    {
                        modelExtra = null;
                    }
                }
                else
                {
                    modelExtra = raw;
                }
            }

            if (modelExtra != null && modelExtra.IsObject)
            {
                session.JoinInfo = new RoomJoinConfiguration
                {
                    AppId = ReadString(
                        modelExtra,
                        "rtc_app_id"
                    ),
                    RoomId = ReadString(
                        modelExtra,
                        "room_id"
                    ),
                    UserId = ReadString(
                        modelExtra,
                        "rtc_user_id"
                    ),
                    Token = ReadString(
                        modelExtra,
                        "room_token"
                    ),
                    BotName = ReadString(
                        modelExtra,
                        "rtc_bot_id"
                    )
                };
            }

            return session;
        }

        /// <summary>
        /// Requires complete room credentials belonging to the SDK's Agora application.
        /// </summary>
        internal static RoomJoinConfiguration RequireJoinInfo(
            XmaxSession session
        )
        {
            var info = session?.JoinInfo;
            if (info == null || string.IsNullOrEmpty(info.AppId) || string.IsNullOrEmpty(info.RoomId) ||
                string.IsNullOrEmpty(info.UserId) || string.IsNullOrEmpty(info.Token) || string.IsNullOrEmpty(info.BotName))
            {
                throw new XmaxException(
                    XmaxErrorCode.SessionError,
                    "Session does not contain complete RTC join information."
                );
            }

            if (!string.Equals(
                info.AppId,
                RtcEngineManager.DefaultAppId,
                StringComparison.Ordinal
            ))
            {
                throw new XmaxException(
                    XmaxErrorCode.SessionError,
                    "Session RTC application does not match the SDK configuration."
                );
            }

            return info;
        }

        /// <summary>
        /// Returns normalized nonempty object text, or null for a missing or invalid field.
        /// </summary>
        private static string ReadString(
            JsonValue data,
            string key
        )
        {
            if (data == null || !data.IsObject || !data.ContainsKey(key))
            {
                return null;
            }

            var value = data[key];
            if (value == null || !value.IsString)
            {
                return null;
            }

            var text = ((string)value)?.Trim();

            return string.IsNullOrEmpty(text) ? null : text;
        }

    }
}
