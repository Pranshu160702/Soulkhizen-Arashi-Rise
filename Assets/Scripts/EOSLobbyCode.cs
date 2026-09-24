using System;
using UnityEngine;
using Epic.OnlineServices;
using Epic.OnlineServices.Sessions;

namespace EpicTransport
{
    public class EOSLobbyCode : MonoBehaviour
    {
        private const string SESSION_NAME = "GameSession";
        private const string CODE_ATTRIBUTE = "LOBBY_CODE";

        public static EOSLobbyCode Instance { get; private set; }

        private SessionsInterface Sessions
        {
            get
            {
                var s = EOSSDKComponent.GetSessionsInterface();
                if (s == null) Debug.LogError("[EOSLobbyCode] SessionsInterface is null — EOS not initialized?");
                return s;
            }
        }

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            _recentlyDestroyedOwners.Clear();
            Debug.Log("[EOSLobbyCode] Awake — Instance set");
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Debug.Log("[EOSLobbyCode] OnDestroy");
        }

        public static string GenerateCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var rng = new System.Random();
            char[] code = new char[6];
            for (int i = 0; i < 6; i++) code[i] = chars[rng.Next(chars.Length)];
            string result = new string(code);
            Debug.Log($"[EOSLobbyCode] Generated code: {result}");
            return result;
        }

        public void CreateSession(string lobbyCode, Action<string> onReady, Action onFailed = null)
        {
            Debug.Log($"[EOSLobbyCode] CreateSession called with code: {lobbyCode}");
            Debug.Log($"[EOSLobbyCode] LocalUserProductId: {EOSSDKComponent.LocalUserProductId}");

            if (EOSSDKComponent.LocalUserProductId == null)
            {
                Debug.LogError("[EOSLobbyCode] LocalUserProductId is null — EOS login not complete");
                onFailed?.Invoke();
                return;
            }

            var modOptions = new CreateSessionModificationOptions()
            {
                SessionName = SESSION_NAME,
                BucketId = "INDG",
                MaxPlayers = 10,
                LocalUserId = EOSSDKComponent.LocalUserProductId,
                PresenceEnabled = false,
                SanctionsEnabled = false
            };

            Result modResult = Sessions.CreateSessionModification(ref modOptions, out var modification);
            Debug.Log($"[EOSLobbyCode] CreateSessionModification result: {modResult}");
            if (modResult != Result.Success || modification == null)
            {
                Debug.LogError($"[EOSLobbyCode] Failed to create session modification: {modResult}");
                onFailed?.Invoke();
                return;
            }

            var maxPlayersOptions = new SessionModificationSetMaxPlayersOptions() { MaxPlayers = 10 };
            modification.SetMaxPlayers(ref maxPlayersOptions);

            var bucketOptions = new SessionModificationSetBucketIdOptions() { BucketId = "INDG" };
            modification.SetBucketId(ref bucketOptions);

            var joinOptions = new SessionModificationSetJoinInProgressAllowedOptions() { AllowJoinInProgress = true };
            modification.SetJoinInProgressAllowed(ref joinOptions);

            var permOptions = new SessionModificationSetPermissionLevelOptions() { PermissionLevel = OnlineSessionPermissionLevel.PublicAdvertised };
            modification.SetPermissionLevel(ref permOptions);

            var attrOptions = new SessionModificationAddAttributeOptions()
            {
                SessionAttribute = new AttributeData() { Key = CODE_ATTRIBUTE, Value = new AttributeDataValue() { AsUtf8 = lobbyCode } },
                AdvertisementType = SessionAttributeAdvertisementType.Advertise
            };
            Result attrResult = modification.AddAttribute(ref attrOptions);
            Debug.Log($"[EOSLobbyCode] AddAttribute result: {attrResult}");

            // Also advertise bucket so FindAllSessions can filter by it
            var bucketAttrOptions = new SessionModificationAddAttributeOptions()
            {
                SessionAttribute = new AttributeData() { Key = "bucket", Value = new AttributeDataValue() { AsUtf8 = "INDG" } },
                AdvertisementType = SessionAttributeAdvertisementType.Advertise
            };
            modification.AddAttribute(ref bucketAttrOptions);

            // Initial player count = 1 (the host)
            var playerCountOptions = new SessionModificationAddAttributeOptions()
            {
                SessionAttribute = new AttributeData() { Key = "PLAYER_COUNT", Value = new AttributeDataValue() { AsInt64 = 1 } },
                AdvertisementType = SessionAttributeAdvertisementType.Advertise
            };
            modification.AddAttribute(ref playerCountOptions);

            var updateOptions = new UpdateSessionOptions() { SessionModificationHandle = modification };
            Sessions.UpdateSession(ref updateOptions, null, (ref UpdateSessionCallbackInfo info) =>
            {
                modification.Release();
                Debug.Log($"[EOSLobbyCode] UpdateSession callback: {info.ResultCode}");
                if (info.ResultCode == Result.Success)
                {
                    Debug.Log($"[EOSLobbyCode] Session created successfully with code: {lobbyCode}");
                    onReady?.Invoke(lobbyCode);
                }
                else
                {
                    Debug.LogError($"[EOSLobbyCode] Failed to create session: {info.ResultCode}");
                    onFailed?.Invoke();
                }
            });
        }

        public void FindSession(string lobbyCode, Action<string> onResolved, Action onFailed)
        {
            Debug.Log($"[EOSLobbyCode] FindSession called for code: {lobbyCode}");

            var searchOptions = new CreateSessionSearchOptions() { MaxSearchResults = 10 };
            Result searchResult = Sessions.CreateSessionSearch(ref searchOptions, out var search);
            Debug.Log($"[EOSLobbyCode] CreateSessionSearch result: {searchResult}");
            if (searchResult != Result.Success || search == null) { onFailed?.Invoke(); return; }

            var paramOptions = new SessionSearchSetParameterOptions()
            {
                Parameter = new AttributeData() { Key = CODE_ATTRIBUTE, Value = new AttributeDataValue() { AsUtf8 = lobbyCode } },
                ComparisonOp = ComparisonOp.Equal
            };
            search.SetParameter(ref paramOptions);

            var findOptions = new SessionSearchFindOptions() { LocalUserId = EOSSDKComponent.LocalUserProductId };
            search.Find(ref findOptions, null, (ref SessionSearchFindCallbackInfo findInfo) =>
            {
                Debug.Log($"[EOSLobbyCode] FindSession search result: {findInfo.ResultCode}");
                if (findInfo.ResultCode != Result.Success) { search.Release(); onFailed?.Invoke(); return; }

                var countOptions = new SessionSearchGetSearchResultCountOptions();
                uint count = search.GetSearchResultCount(ref countOptions);
                Debug.Log($"[EOSLobbyCode] FindSession sessions found: {count}");
                if (count == 0) { search.Release(); onFailed?.Invoke(); return; }

                var copyOptions = new SessionSearchCopySearchResultByIndexOptions() { SessionIndex = 0 };
                search.CopySearchResultByIndex(ref copyOptions, out var sessionDetails);
                search.Release();

                if (sessionDetails == null) { Debug.LogError("[EOSLobbyCode] sessionDetails is null"); onFailed?.Invoke(); return; }

                var infoOptions = new SessionDetailsCopyInfoOptions();
                sessionDetails.CopyInfo(ref infoOptions, out var sessionInfo);
                sessionDetails.Release();

                if (sessionInfo == null) { Debug.LogError("[EOSLobbyCode] sessionInfo is null"); onFailed?.Invoke(); return; }

                string hostId = sessionInfo.Value.OwnerUserId.ToString();
                Debug.Log($"[EOSLobbyCode] Found host ID: {hostId}");
                onResolved?.Invoke(hostId);
            });
        }

        public struct RoomInfo
        {
            public string HostId;
            public string LobbyCode;
        }

        public void FindAllSessions(Action<System.Collections.Generic.List<RoomInfo>> onDone)
        {
            Debug.Log("[EOSLobbyCode] FindAllSessions called");

            var searchOptions = new CreateSessionSearchOptions() { MaxSearchResults = 20 };
            Result searchResult = Sessions.CreateSessionSearch(ref searchOptions, out var search);
            if (searchResult != Result.Success || search == null)
            {
                onDone?.Invoke(new System.Collections.Generic.List<RoomInfo>());
                return;
            }

            var bucketParam = new SessionSearchSetParameterOptions()
            {
                Parameter = new AttributeData() { Key = "bucket", Value = new AttributeDataValue() { AsUtf8 = "INDG" } },
                ComparisonOp = ComparisonOp.Equal
            };
            search.SetParameter(ref bucketParam);

            var findOptions = new SessionSearchFindOptions() { LocalUserId = EOSSDKComponent.LocalUserProductId };
            search.Find(ref findOptions, null, (ref SessionSearchFindCallbackInfo findInfo) =>
            {
                var rooms = new System.Collections.Generic.List<RoomInfo>();
                if (findInfo.ResultCode != Result.Success) { search.Release(); onDone?.Invoke(rooms); return; }

                var countOptions = new SessionSearchGetSearchResultCountOptions();
                uint count = search.GetSearchResultCount(ref countOptions);
                Debug.Log($"[EOSLobbyCode] FindAllSessions: found {count} sessions");

                for (uint i = 0; i < count; i++)
                {
                    var copyOptions = new SessionSearchCopySearchResultByIndexOptions() { SessionIndex = i };
                    search.CopySearchResultByIndex(ref copyOptions, out var sessionDetails);
                    if (sessionDetails == null) continue;

                    var infoOptions = new SessionDetailsCopyInfoOptions();
                    sessionDetails.CopyInfo(ref infoOptions, out var sessionInfo);

                    var attrOptions = new SessionDetailsCopySessionAttributeByKeyOptions() { AttrKey = CODE_ATTRIBUTE };
                    sessionDetails.CopySessionAttributeByKey(ref attrOptions, out var attr);

                    var countAttrOptions = new SessionDetailsCopySessionAttributeByKeyOptions() { AttrKey = "PLAYER_COUNT" };
                    sessionDetails.CopySessionAttributeByKey(ref countAttrOptions, out var countAttr);
                    sessionDetails.Release();

                    if (sessionInfo == null) continue;

                    var ownerId = sessionInfo.Value.OwnerUserId?.ToString() ?? "";
                    if (_recentlyDestroyedOwners.Contains(ownerId))
                    {
                        Debug.Log($"[EOSLobbyCode] Skipping recently destroyed session from {ownerId}");
                        continue;
                    }

                    // Skip ghost sessions: no players present (PLAYER_COUNT == 0 or missing)
                    long playerCount = countAttr?.Data?.Value.AsInt64 ?? 0;
                    if (playerCount <= 0)
                    {
                        Debug.Log($"[EOSLobbyCode] Skipping empty/ghost session from {ownerId} (playerCount={playerCount})");
                        continue;
                    }

                    rooms.Add(new RoomInfo
                    {
                        HostId = ownerId,
                        LobbyCode = attr?.Data?.Value.AsUtf8 ?? ""
                    });
                }
                search.Release();
                Debug.Log($"[EOSLobbyCode] FindAllSessions returning {rooms.Count} rooms");
                onDone?.Invoke(rooms);
            });
        }

        // Track recently destroyed session owners to filter stale EOS results
        private static readonly System.Collections.Generic.HashSet<string> _recentlyDestroyedOwners = new();

        public void UpdateSessionPlayerCount(int playerCount)
        {
            var modOptions = new CreateSessionModificationOptions()
            {
                SessionName = SESSION_NAME,
                BucketId = "INDG",
                MaxPlayers = 10,
                LocalUserId = EOSSDKComponent.LocalUserProductId,
                PresenceEnabled = false,
                SanctionsEnabled = false
            };
            if (Sessions.CreateSessionModification(ref modOptions, out var mod) != Result.Success || mod == null) return;

            var openSlots = new SessionModificationSetMaxPlayersOptions() { MaxPlayers = (uint)Mathf.Max(1, 10 - playerCount) };
            // EOS doesn't have a direct "set open connections" API on modification;
            // instead we re-advertise with updated NumOpenPublicConnections via RegisterPlayers.
            // Simplest workaround: store count as a session attribute.
            var attrOptions = new SessionModificationAddAttributeOptions()
            {
                SessionAttribute = new AttributeData() { Key = "PLAYER_COUNT", Value = new AttributeDataValue() { AsInt64 = playerCount } },
                AdvertisementType = SessionAttributeAdvertisementType.Advertise
            };
            mod.AddAttribute(ref attrOptions);

            var updateOptions = new UpdateSessionOptions() { SessionModificationHandle = mod };
            Sessions.UpdateSession(ref updateOptions, null, (ref UpdateSessionCallbackInfo info) =>
            {
                mod.Release();
                Debug.Log($"[EOSLobbyCode] UpdateSessionPlayerCount result: {info.ResultCode} count={playerCount}");
            });
        }

        public void DestroySession()
        {
            Debug.Log("[EOSLobbyCode] DestroySession called");
            var myId = EOSSDKComponent.LocalUserProductId?.ToString();
            if (!string.IsNullOrEmpty(myId))
            {
                _recentlyDestroyedOwners.Add(myId);
                StartCoroutine(RemoveDestroyedOwner(myId, 10f));
            }

            var options = new DestroySessionOptions() { SessionName = SESSION_NAME };
            Sessions?.DestroySession(ref options, null, (ref DestroySessionCallbackInfo info) =>
                Debug.Log($"[EOSLobbyCode] DestroySession result: {info.ResultCode}")
            );
        }

        System.Collections.IEnumerator RemoveDestroyedOwner(string ownerId, float delay)
        {
            yield return new WaitForSeconds(delay);
            _recentlyDestroyedOwners.Remove(ownerId);
        }
    }
}
