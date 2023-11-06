﻿using StarfallAfterlife.Bridge.Server.Discovery;
using StarfallAfterlife.Bridge.Server;
using StarfallAfterlife.Bridge.Networking;
using StarfallAfterlife.Bridge.Networking.Channels;
using StarfallAfterlife.Bridge.Networking.MgrHandlers;
using StarfallAfterlife.Bridge.Profiles;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using static StarfallAfterlife.Bridge.Diagnostics.SfaDebug;
using StarfallAfterlife.Bridge.Server.Galaxy;
using System.Runtime;
using System.Text.Json.Nodes;
using StarfallAfterlife.Bridge.Serialization;

namespace StarfallAfterlife.Bridge.Game
{
    public partial class SfaGame
    {
        public ChatConsoleChannel DeprivedChatChannel { get; protected set; }

        public ChatConsoleChannel EclipseChatChannel { get; protected set; }

        public ChatConsoleChannel VanguardChatChannel { get; protected set; }

        public FriendChannel CharacterFriendsChannel { get; protected set; }

        public QuickMatchChannel QuickMatchChannel { get; protected set; }

        public GameChannel CharactPartyChannel { get; protected set; }

        public BattleGroundChannel BattleGroundChannel { get; protected set; }

        public DiscoveryChannel DiscoveryChannel { get; protected set; }

        public GalacticChannel GalacticChannel { get; protected set; }

        protected MgrServer RealmMgrServer { get; set; }

        protected GameChannelManager RealmMgrChannelManager { get; set; }

        protected RealmMgrHandler RealmMgrHandler { get; set; }

        protected virtual void RealmInput(HttpListenerContext context, SfaHttpQuery query)
        {
            object response = null;

            Print($"Request ({query})", "realmmgr");

            Profile?.Use(p =>
            {
                switch (query.Function)
                {
                    case "auth":
                    case "authcompletion":
                        response = new JsonObject
                        {
                            ["address"] = SValue.Create(RealmMgrChannelManager.Address.Host),
                            ["port"] = SValue.Create(RealmMgrChannelManager.Address.Port.ToString()),
                            ["temporarypass"] = SValue.Create(GameProfile.TemporaryPass),
                            ["auth"] = SValue.Create(GameProfile.TemporaryPass),
                            ["tutorial_complete"] = SValue.Create(true),
                            ["realmname"] = SValue.Create("NewRealm"),
                            ["userbm"] = SValue.Create(1)
                        };
                        break;

                    case "getcharacterdata":
                        if (Enum.TryParse((string)query["data_flags"] ?? string.Empty, out UserDataFlag userDataFlags))
                        {
                            SyncGalaxySessionData();
                            Print($"GetCharacterData(Flags=({userDataFlags}))");
                            response = CreateCharacterDataResponse(userDataFlags);
                        }
                        else
                        {
                            SyncGalaxySessionData();
                            response = CreateCharacterDataResponse();
                        }
                        break;

                    case "discovery_charactgetdata":
                        SyncGalaxySessionData();
                        response = new JsonObject
                        {
                            ["result_data"] = SValue.Create(GameProfile?.CurrentCharacter?.CreateDiscoveryCharacterDataResponse())
                        };
                        break;

                    case "get_charact_quests":
                        response = null;
                        break;

                    case "menucurrentdetachment":
                        response = RealmMgrHandler.ReceiveMenuCurrentDetachment(query);
                        p.SaveGameProfile();
                        break;

                    case "detachmentsave":
                        response = RealmMgrHandler.ReceiveDetachmentSave(query);
                        p.SaveGameProfile();
                        break;

                    case "detachmentabilitysave":
                        response = RealmMgrHandler.ReceiveDetachmentAbilitySave(query);
                        p.SaveGameProfile();
                        break;

                    case "galaxymapload":
                        response = CreateGalaxyMapResponse((string)query["hash"] == Realm.GalaxyMapHash);
                        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
                        break;

                    case "get_charact_stats":
                        response = EmptyMgrResponse;
                        break;

                    case "set_charact_event_checked":
                        response = EmptyMgrResponse;
                        break;

                    case "save_charact_progress_stats":
                        response = EmptyMgrResponse;
                        break;

                    case "ship.save":
                        response = HandleSaveShip(query);
                        p.SaveGameProfile();
                        SfaClient?.SyncCharacterCurrencies(Profile?.GameProfile?.CurrentCharacter);
                        break;

                    case "favorite_ship":
                        response = RealmMgrHandler.ReceiveFavoriteShip(query);
                        p.SaveGameProfile();
                        break;

                    case "disassemble_items":
                        response = HandleDisassembleItems(query);
                        break;

                    case "sellinventory":
                        response = HandleSellInventory(query);
                        break;

                    case "startcrafting":
                        response = HandleStartCrafting(query);
                        p.SaveGameProfile();
                        break;

                    case "acquirecrafteditem":
                        response = HandleAcquireCraftedItem(query);
                        p.SaveGameProfile();
                        break;

                    case "acquireallcrafteditems":
                        response = HandleAcquireAllCraftedItems(query);
                        p.SaveGameProfile();
                        break;

                    case "swap_in_queue":
                        response = HandleSwapCraftingQueue(query);
                        p.SaveGameProfile();
                        break;

                    case "ship.delete":
                        response = RealmMgrHandler.ReceiveShipDelete(query);
                        p.SaveGameProfile();
                        SfaClient?.SyncCharacterCurrencies(Profile?.GameProfile?.CurrentCharacter);
                        break;

                    case "buy_battle_ground_shop_item":
                        response = RealmMgrHandler.ReceiveBuyBattleGroundShopItem(query);
                        p.SaveGameProfile();
                        SfaClient?.SyncCharacterCurrencies(Profile?.GameProfile?.CurrentCharacter);
                        break;

                    case "set_session_reward":
                        response = HandleConfirmSessionReward(query);
                        p.SaveGameProfile();
                        break;

                    default:
                        break;
                }
            });

            
            if (response is JsonNode sr)
            {
                sr = new JsonObject { ["doc"] = sr };
                RealmMgrServer.Send(context, JsonHelpers.ToJsonStringUnbuffered(sr, false));
                sr.AsObject().Clear();
            }
        }

        protected virtual void InitRealmMgr()
        {
            RealmMgrServer ??= new MgrServer(RealmInput);
            RealmMgrChannelManager ??= new GameChannelManager();

            DeprivedChatChannel ??= new ChatConsoleChannel("Deprived Chat", 6, this);
            EclipseChatChannel ??= new ChatConsoleChannel("Eclipse Chat", 7, this);
            VanguardChatChannel ??= new ChatConsoleChannel("Vanguard Chat", 8, this);
            CharacterFriendsChannel ??= new FriendChannel("CharacterFriends", 9, this);
            QuickMatchChannel ??= new QuickMatchChannel("QuickMatch", 10, this);
            CharactPartyChannel ??= new GameChannel("CharactParty", 11);
            BattleGroundChannel ??= new BattleGroundChannel("BattleGround", 12, this);
            DiscoveryChannel ??= new DiscoveryChannel("Discovery", 13, this);
            GalacticChannel ??= new GalacticChannel("Galactic", 14, this);

            RealmMgrChannelManager.Add(DeprivedChatChannel);
            RealmMgrChannelManager.Add(EclipseChatChannel);
            RealmMgrChannelManager.Add(VanguardChatChannel);
            RealmMgrChannelManager.Add(CharacterFriendsChannel);
            RealmMgrChannelManager.Add(QuickMatchChannel);
            RealmMgrChannelManager.Add(CharactPartyChannel);
            RealmMgrChannelManager.Add(BattleGroundChannel);
            RealmMgrChannelManager.Add(DiscoveryChannel);
            RealmMgrChannelManager.Add(GalacticChannel);
        }

        public void SyncGalaxySessionData()
        {
            var character = GameProfile?.CurrentCharacter;

            if (character is null)
                return;

            var result = SfaClient?.RequestFullGalaxySesionData().Result;

            if (result is not null)
                SfaClient.SyncSessionData(result);
        }
    }
}
