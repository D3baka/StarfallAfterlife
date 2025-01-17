﻿using StarfallAfterlife.Bridge.Collections;
using StarfallAfterlife.Bridge.Database;
using StarfallAfterlife.Bridge.Instances;
using StarfallAfterlife.Bridge.Mathematics;
using StarfallAfterlife.Bridge.Networking;
using StarfallAfterlife.Bridge.Primitives;
using StarfallAfterlife.Bridge.Profiles;
using StarfallAfterlife.Bridge.Realms;
using StarfallAfterlife.Bridge.Serialization;
using StarfallAfterlife.Bridge.Server.Characters;
using StarfallAfterlife.Bridge.Server.Discovery;
using StarfallAfterlife.Bridge.Server.Galaxy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace StarfallAfterlife.Bridge.Server.Matchmakers
{
    public class DiscoveryBattle : MatchmakerBattle
    {
        public int SystemId { get; set; }

        public SystemHex Location { get; set; }

        public StarSystemBattle SystemBattle { get; set; }

        public DiscoveryGalaxy Galaxy => SystemBattle?.System?.Galaxy;

        public List<DiscoveryBattleCharacterInfo> Characters { get; } = new();

        public List<DiscoveryBattleMobInfo> Mobs { get; } = new();

        public List<DiscoveryBattleBossInfo> Bosses { get; } = new();

        public List<string> ServiceFleets { get; } = new();

        public List<BattleMember> PendingMembers { get; } = new();

        public Dictionary<string, JsonArray> Drops { get; } = new();

        protected CancellationTokenSource _cts;
        protected readonly object _lockher = new();

        public virtual void AddToBattle(BattleMember member)
        {
            lock (_lockher)
            {
                if (member?.Fleet is null ||
                    PendingMembers.FirstOrDefault(m => m.Fleet == member.Fleet) is not null)
                    return;

                if (State == MatchmakerBattleState.PendingMatch)
                {
                    PendingMembers.Add(member);
                    return;
                }
                else if (State == MatchmakerBattleState.Started)
                {
                    AddToActiveBattle(member);
                    return;
                }
                else if (State == MatchmakerBattleState.Finished)
                {
                    Galaxy?.BeginPreUpdateAction(g => SystemBattle?.Leave(member, SystemBattle.Hex));
                    return;
                }

                if (member.Fleet is UserFleet)
                {
                    var info = CreateCharacterInfo(member);

                    if (info is not null)
                    {
                        Characters.Add(info);

                        if (State == MatchmakerBattleState.PendingPlayers)
                        {
                            _cts?.Cancel();
                            StartInstance();
                        }
                    }
                }
                else if (member.Fleet is DiscoveryAiFleet)
                {
                    var info = CreateMobInfo(member);

                    if (info is not null)
                        Mobs.Add(info);
                }
            }
        }

        protected virtual void AddToActiveBattle(BattleMember member)
        {
            lock (_lockher)
            {
                if (member?.Fleet is null)
                    return;

                if (member.Fleet is UserFleet)
                {
                    var info = Characters.FirstOrDefault(c => c.Member.Fleet == member.Fleet);

                    if (info is not null)
                    {
                        info.Member = member;
                        JoinToInstance(info);
                    }
                    else if ((info = CreateCharacterInfo(member)) is not null)
                    {
                        Characters.Add(info);
                        GameMode.InstanceManager.JoinNewChar(InstanceInfo, info.InstanceCharacter);
                    }
                }
                else if (member.Fleet is DiscoveryAiFleet)
                {
                    var info = CreateMobInfo(member);

                    if (info is not null)
                        Mobs.Add(info);
                }
            }
        }

        public void Leave(DiscoveryBattleCharacterInfo character, SystemHex spawnHex)
        {
            lock (_lockher)
            {
                Characters.Remove(character);
                Galaxy?.BeginPreUpdateAction(g => SystemBattle?.Leave(character.Member, spawnHex));
            }
        }

        public void Leave(DiscoveryBattleMobInfo mob, SystemHex spawnHex)
        {
            lock (_lockher)
            {
                Mobs.Remove(mob);
                Galaxy?.BeginPreUpdateAction(g => SystemBattle?.Leave(mob.Member, spawnHex));
            }
        }

        protected virtual DiscoveryBattleCharacterInfo CreateCharacterInfo(BattleMember member)
        {
            DiscoveryBattleCharacterInfo info = null;

            if (member.Fleet is UserFleet fleet)
            {
                if (Server?.GetCharacter(fleet) is ServerCharacter character)
                {
                    info = new DiscoveryBattleCharacterInfo(member, character);
                }
            }

            return info;
        }

        protected virtual DiscoveryBattleMobInfo CreateMobInfo(BattleMember member)
        {
            DiscoveryBattleMobInfo info = null;

            if (member.Fleet is DiscoveryAiFleet fleet)
            {
                if (Server?.Realm?.MobsDatabase?.GetMob(fleet.MobId) is DiscoveryMobInfo mobInfo)
                {
                    var mob = mobInfo?.Clone();
                    int fleetId = fleet.Id;
                    int shipId = fleetId * 1000;

                    foreach (var ship in mob.Ships?
                        .Select(s => s.Data)
                        .Where(s => s is not null) ??
                        Enumerable.Empty<ShipConstructionInfo>())
                    {
                        ship.Id = shipId;
                        ship.FleetId = fleetId;
                        shipId++;
                    }

                    info = new DiscoveryBattleMobInfo(member, mobInfo);
                }
            }

            return info;
        }


        protected virtual DiscoveryBattleBossInfo CreateBossInfo(GalaxyMapMob fleet)
        {
            if (fleet is null)
                return null;

            if (Server?.Realm?.MobsDatabase?.GetMob(fleet.MobId) is DiscoveryMobInfo mobInfo)
            {
                var mob = mobInfo.Clone();
                int fleetId = fleet.FleetId;
                int shipId = fleetId * 1000;

                foreach (var ship in mob.Ships?
                    .Select(s => s.Data)
                    .Where(s => s is not null) ??
                    Enumerable.Empty<ShipConstructionInfo>())
                {
                    ship.Id = shipId;
                    ship.FleetId = fleetId;
                    shipId++;
                }


                return new DiscoveryBattleBossInfo(fleetId, mob);
            }

            return null;
        }

        protected virtual DiscoveryBattleBossInfo CreateBossInfo(DiscoveryMobInfo mobInfo)
        {
            if (mobInfo is null)
                return null;

            int fleetId = 2000000;

            foreach (var item in Bosses ?? new())
                if (item is not null)
                    fleetId = Math.Max(fleetId, item.FleetId);

            fleetId++;

            var mob = mobInfo.Clone();
            int shipId = fleetId * 1000;

            foreach (var ship in mob.Ships?
                .Select(s => s.Data)
                .Where(s => s is not null) ??
                Enumerable.Empty<ShipConstructionInfo>())
            {
                ship.Id = shipId;
                ship.FleetId = fleetId;
                shipId++;
            }

            return new DiscoveryBattleBossInfo(fleetId, mob);
        }

        public override void Init()
        {
            base.Init();

            lock (_lockher)
            {
                InstanceInfo.Type = InstanceType.DiscoveryBattle;

                if (SystemBattle is null)
                    return;

                SystemId = SystemBattle.System?.Id ?? -1;
                Location = SystemBattle.Hex;

                foreach (var member in SystemBattle.Members)
                {
                    AddToBattle(member);
                }

                CreateLocationDrop();
            }
        }

        public override void Start()
        {
            lock (_lockher)
            {
                if (Characters.Count > 0)
                {
                    StartInstance();
                }
                else
                {
                    State = MatchmakerBattleState.PendingPlayers;
                    _cts = new();

                    Matchmaker.Invoke(() =>
                    {
                        if (_cts?.IsCancellationRequested == true)
                            return;

                        Stop();
                    }, TimeSpan.FromMinutes(1));
                }
            }
        }

        protected virtual void StartInstance()
        {
            lock (_lockher)
            {
                State = MatchmakerBattleState.PendingMatch;
                InstanceInfo.Characters.AddRange(Characters.Select(c => c.InstanceCharacter));
                InstanceInfo.ExtraData = CreateExtraData().ToJson().ToJsonString();
                GameMode.InstanceManager.StartInstance(InstanceInfo);
            }
        }

        public override void Stop()
        {
            lock (_lockher)
            {
                State = MatchmakerBattleState.Finished;

                foreach (var item in PendingMembers.ToArray())
                    Galaxy?.BeginPreUpdateAction(g => SystemBattle?.Leave(item, SystemBattle.Hex, false));

                PendingMembers.Clear();

                foreach (var character in Characters.ToArray())
                    Leave(character, SystemBattle.Hex);

                foreach (var mob in Mobs.ToArray())
                    Leave(mob, SystemBattle.Hex);

                Bosses?.Clear();

                GameMode.InstanceManager.StopInstance(InstanceInfo);
                Matchmaker.RemoveBattle(this);
                Galaxy?.BeginPreUpdateAction(g => SystemBattle.Finish());
            }
        }

        protected virtual InstanceExtraData CreateExtraData()
        {
            var aiList = ServiceFleets
                .Select(n => new InstanceAIFleet
                {
                    Id = -1,
                    Mob = new InstanceMob { Id = -1, MobInternalName = n, Tags = new() },
                    Cargo = new(),
                })
                .Concat(Mobs?.Select(m => m.InstanceFleet) ?? Enumerable.Empty<InstanceAIFleet>())
                .ToList();

            var tiles = CreateLocationTiles();

            if (tiles is not null && 
                SystemBattle is StarSystemBattle battle)
            {
                var objectType = DiscoveryObjectType.None;
                var objectId = 0;

                if (battle.IsDungeon == true &&
                    battle.DungeonInfo?.Target is StarSystemObject obj)
                {
                    objectType = obj.Type;
                    objectId = obj.Id;
                }

                foreach (var item in Characters
                    .SelectMany(c => c.ServerCharacter?.CreateInstanceTileParams(SystemId, objectType, objectId))
                    .Where(p => p.TileName is not null))
                {
                    var tile = tiles.FirstOrDefault(t => item.TileName.Equals(t.Name, StringComparison.InvariantCultureIgnoreCase));

                    if (tile is null)
                        continue;

                    foreach (var p in (item.Params ?? new()).Where(p => p.Key is not null))
                    {
                        (tile.Params ??= new())[p.Key] = p.Value;
                    }
                }
            }
            
            return new InstanceExtraData
            {
                AiList = aiList,
                Bosses = Bosses?.Select(b => b.InstanceMob).ToList() ?? new(),
                EnviropmentInfo = CreateEnviropment(),
                XpData = CreateXpData(),
                Tiles = tiles,
            };
        }

        protected virtual InstanceEnviropmentInfo CreateEnviropment()
        {
            var env = new InstanceEnviropmentInfo();

            if (SystemBattle is StarSystemBattle battle)
            {
                env.PosX = battle.Hex.X;
                env.PosY = battle.Hex.Y;
                env.DistanceFactor = 1;
                env.SectorLockSeconds = 15;
                env.FloatingAsteroidsCount = battle.FloatingAsteroidsCount;

                if (battle.System is StarSystem system &&
                    system.Info is GalaxyMapStarSystem systemInfo)
                {
                    env.SystemLevel = systemInfo.Level;
                    env.StarId = systemInfo.Id;
                    env.StarSize = systemInfo.Size;
                    env.StarWeight = systemInfo.Weight;
                    env.StarTemp = systemInfo.Temperature;
                    env.StarType = systemInfo.Type;
                    env.EnviropmentEffects = new()
                    {
                        "star_flashes",
                    };

                    if (new Random128().Next(0, 10) == 0)
                        env.EnviropmentEffects.Add("plasma_storm");
                }

                if (battle.IsDungeon == false)
                {
                    var rnd = new Random128();
                    env.HasNebula = battle.HasNebula;

                    if (battle.HasAsteroids == true)
                    {
                        env.FloatingAsteroidsCount = 0;
                        env.RichAsteroidsId = battle.RichAsteroidField?.Id ?? -1;

                        foreach (var item in battle.RichAsteroidField?.CurrentOres ?? new())
                            env.AsteroidsContent[item.Key] = item.Value;
                    }
                    else if(rnd.Next(0, 10) == 0)
                    {
                        env.EnviropmentEffects.Add("plasma_storm");
                    }
                }
            }

            return env;
        }

        protected virtual InstanceXpData CreateXpData()
        {
            var lvl = SystemBattle?.System?.Info?.Level ?? 0;

            return new InstanceXpData()
            {
                DungeonShip = lvl * 500,
                DungeonBoss = lvl * 3000,
                Outpost = lvl * 2500,
                Station = lvl * 5000,
            };
        }

        public DiscoveryBattleCharacterInfo GetCharacter(ServerCharacter character)
        {
            return Characters.FirstOrDefault(c => c.ServerCharacter == character);
        }

        public DiscoveryBattleCharacterInfo GetCharacter(int id)
        {
            return Characters.FirstOrDefault(c => c.ServerCharacter?.UniqueId == id);
        }

        public override void InstanceStateChanged(InstanceState state)
        {
            base.InstanceStateChanged(state);

            lock (_lockher)
            {
                if (state == InstanceState.Started)
                {
                    State = MatchmakerBattleState.Started;

                    foreach (var member in PendingMembers)
                        AddToActiveBattle(member);

                    PendingMembers.Clear();

                    foreach (var character in Characters)
                        JoinToInstance(character);
                }
                else if (state == InstanceState.Finished)
                {
                    Stop();
                }
            }
        }

        public void OnFleetLeavesFromInstance(DiscoveryObjectType fleetType, int fleetId, SystemHex hex)
        {
            lock (_lockher)
            {
                if (fleetType is DiscoveryObjectType.UserFleet)
                {
                    var character = Characters?.FirstOrDefault(c => c.InstanceCharacter.Id == fleetId);
                    Leave(character, (SystemBattle?.Hex ?? SystemHex.Zero) + hex);
                }
                else if (fleetType is DiscoveryObjectType.AiFleet)
                {
                    var mob = Mobs?.FirstOrDefault(c => c.FleetId == fleetId);
                    Leave(mob, (SystemBattle?.Hex ?? SystemHex.Zero) + hex);
                }
            }
        }

        public ServerCharacter GetCharByShipId(int shipId)
        {
            lock ( _lockher)
            {
                return Characters
                    .Select(c => c?.ServerCharacter)
                    .FirstOrDefault(c => c?.GetShipById(shipId) is not null);
            }
        }

        public override JsonNode GetMobData(MobDataRequest request)
        {
            if (request.IsCustom == false)
            {
                lock (_lockher)
                {
                    var isBoss = false;
                    var mob =
                        Mobs?.FirstOrDefault(m => m?.FleetId == request.FleetId)?.Mob;

                    if (mob is null)
                    {
                        mob = Bosses?.FirstOrDefault(b => b?.FleetId == request.FleetId)?.Mob;
                        isBoss = mob is not null;
                    }

                    if (mob is not null)
                    {
                        var ships = new JsonArray();

                        foreach (var ship in mob.Ships ?? Enumerable.Empty<DiscoveryMobShipData>())
                        {
                            if (ship?.Data is null)
                                continue;

                            var serviceData = JsonHelpers.ParseNodeUnbuffered(ship.ServiceData) ?? new JsonObject();

                            if (isBoss == false &&
                                (string)serviceData["bt"] is string bt &&
                                bt.StartsWith("/Game/gameplay/ai/boss/", StringComparison.InvariantCultureIgnoreCase) == true)
                                serviceData["bt"] = null;

                            ships.Add(new JsonObject
                            {
                                ["id"] = SValue.Create(ship.Data.Id),
                                ["data"] = SValue.Create(JsonHelpers.ParseNodeUnbuffered(ship.Data).ToJsonString(false)),
                                ["service_data"] = SValue.Create(serviceData.ToJsonString(false)),
                            });
                        }

                        var doc = new JsonObject
                        {
                            ["id"] = SValue.Create(request.FleetId),
                            ["level"] = SValue.Create(mob.Level),
                            ["faction"] = SValue.Create((byte)mob.Faction),
                            ["internal_name"] = SValue.Create(mob.InternalName),
                            ["battle_bt"] = SValue.Create(mob.BehaviorTreeName),
                            ["tags"] = mob.Tags?.Select(SValue.Create).ToJsonArray(),
                            ["ships"] = ships,
                        };

                        return doc;
                    }
                }
            }

            return base.GetMobData(request);
        }

        public virtual void OnInstanceAuthReady(int characterId, string auth)
        {
            if (Characters?
                .FirstOrDefault(c => c?.InstanceCharacter?.Id == characterId)
                is DiscoveryBattleCharacterInfo info)
                JoinToInstance(info, auth);
        }

        public void JoinToInstance(DiscoveryBattleCharacterInfo character, string newAuth = null)
        {
            lock (_lockher)
            {
                if (character is null)
                    return;

                if (newAuth is not null &&
                    character.InstanceCharacter is InstanceCharacter instanceCharacter)
                    instanceCharacter.Auth = newAuth;

                character.Client?.Invoke(c => c.SendStartBattle(
                    "discovery",
                    Matchmaker?.CreateBattleIpAddress(),
                    InstanceInfo?.Port ?? -1,
                    character.InstanceCharacter?.Auth,
                    character.ServerCharacter?.Fleet?.System.Id ?? 0,
                    character.ServerCharacter?.Fleet?.Id ?? -1));
            }
        }

        public void OnMobDestroyed(string data)
        {
            lock (_lockher)
            {
                if (JsonNode.Parse(data) is JsonObject doc &&
                (DiscoveryObjectType?)(byte?)doc["killer_type"] == DiscoveryObjectType.UserFleet &&
                (int?)doc["killer_id"] is int fleetId &&
                (int?)doc["mob_id"] is int mobFleetId &&
                Server.GetCharacter(fleetId) is ServerCharacter character)
                {
                    var battleMob = Mobs?.FirstOrDefault(m => m?.FleetId == mobFleetId);

                    if (battleMob is not null)
                        SystemBattle.Leave(battleMob.Member, Location, true);

                    var mobInfo = battleMob?.Mob ?? Bosses?.FirstOrDefault(b => b?.FleetId == mobFleetId)?.Mob;
                    var killInfo = new MobKillInfo();

                    killInfo.SystemId = SystemId;
                    killInfo.MobId = mobInfo?.Id ?? -1;
                    killInfo.FleetId = mobFleetId;
                    killInfo.ObjectId = (int?)doc["obj_id"] ?? -1;
                    killInfo.ObjectType = (DiscoveryObjectType?)(byte?)doc["type"] ?? DiscoveryObjectType.None;
                    killInfo.Faction = (Faction?)(byte?)doc["faction"] ?? Faction.None;
                    killInfo.FactionGroup = (int?)doc["faction_group"] ?? -1;
                    killInfo.ShipClass = (int?)doc["ship_class"] ?? 0;
                    killInfo.Level = (int?)doc["level"] ?? 0;
                    killInfo.RepEarned = (int?)doc["rep_earned"] ?? 0;
                    killInfo.IsInAttackEvent = (int?)doc["is_in_attack_event"] ?? 0;
                    killInfo.Tags.AddRange(doc["tags"]?.DeserializeUnbuffered<List<string>>() ?? new());

                    character.Events?.Broadcast<IBattleInstanceListener>(l =>
                        l.OnMobDestroyed(fleetId, killInfo));
                }
            }
        }

        public void UpdateCharacterStats(string data)
        {
            if (JsonNode.Parse(data) is JsonObject doc &&
                (int?)doc["char_id"] is int charId &&
                Server.GetCharacter(charId) is ServerCharacter character)
            {
                var stats = new Dictionary<string, float>();

                foreach (var item in doc["stats"]?.AsArray() ?? new())
                {
                    if ((string)item["stat"] is string stat &&
                        (float?)item["value"] is float value)
                        stats[stat] = value;
                }

                character.AddNewStats(stats);
            }
        }

        internal void OnPiratesAssaultStatusUpdated(string data)
        {
            if (JsonHelpers.ParseNodeUnbuffered(data) is JsonNode doc &&
                (int?)doc["destroyed"] == 1 &&
                SystemBattle.IsDungeon &&
                SystemBattle.DungeonInfo?.Target is StarSystemObject obj &&
                (DiscoveryObjectType?)(byte?)doc["obj_type"] == obj.Type &&
                (int?)doc["obj_id"] == obj.Id)
                Galaxy?.BeginPreUpdateAction(g => SystemBattle.SetDungeonCompleted());
        }

        public override bool ContainsChar(ServerCharacter character)
        {
            if (character is null)
                return false;

            lock (_lockher)
                return Characters?.Any(c => c?.ServerCharacter == character) == true;
        }

        protected virtual List<TileInfo> CreateLocationTiles()
        {
            var rnd = new Random128();

            if (rnd.NextSingle() < 0.2f &&
                SystemBattle is not null and { IsDungeon: false, HasAsteroids: false } &&
                Characters?.Any(c => c?.Member?.Role == BattleRole.Defense) == true)
            {
                if (rnd.Next(0, 2) == 0)
                {
                    return new()
                    {
                        new()
                        {
                            Name = "BP_TG_AutonomousRepairStation",
                            Remove = 0,
                            Priority = 200,
                            NumMin = 1,
                            NumMax = 1,
                            Params = new(),
                        },
                    };
                }
                else
                {
                    return new()
                    {
                        new()
                        {
                            Name = "BP_TG_AutonomObservationStation",
                            Remove = 0,
                            Priority = 200,
                            NumMin = 1,
                            NumMax = 1,
                            Params = new(),
                        },
                    };
                }
            }

            return null;
        }

        protected virtual void CreateLocationDrop()
        {

        }

        public override JsonArray GetDropList(string dropName)
        {
            lock (_lockher)
                return Drops.GetValueOrDefault(dropName) ?? base.GetDropList(dropName);
        }

        public virtual void HandleInstanceObjectInteractEvent(string data)
        {
            lock (_lockher)
            {
                if (JsonHelpers.ParseNodeUnbuffered(data ?? "") is JsonObject doc &&
                    (DiscoveryObjectType?)(byte?)doc["obj_type"] is DiscoveryObjectType.UserFleet &&
                    (int?)doc["obj_id"] is int charId &&
                    GetCharacter(charId)?.ServerCharacter is ServerCharacter character &&
                    (string)doc["event_type"] is string eventType &&
                    (string)doc["event_data"] is string eventData)
                {
                    character.HandleInstanceObjectInteractEvent(SystemId, eventType, eventData);
                }
            }
        }

        public virtual void HandleSecretObjectLooted(string data)
        {
            lock (_lockher)
            {
                if (JsonHelpers.ParseNodeUnbuffered(data ?? "") is JsonObject doc &&
                    (int?)doc["obj_id"] is int secretId)
                {
                    foreach (var item in Characters)
                        item?.ServerCharacter?.HandleSecretObjectLooted(secretId);
                }
            }
        }

        public virtual void HandleOreTaken(InventoryItem[] ores)
        {
            lock (_lockher)
            {
                if (ores is not null &&
                    SystemBattle.RichAsteroidField is StarSystemRichAsteroid field)
                {
                    Galaxy?.BeginPreUpdateAction(_ =>
                    {
                        field.TakeOres(ores
                            .DistinctBy(o => o.Id)
                            .ToDictionary(o => o.Id, o => Math.Max(1, o.Count)));
                    });
                }
            }
        }

        public virtual void UpdatePartyMembers(int partyId, List<CharacterPartyMember> members)
        {
            lock (_lockher)
            {
                GameMode.InstanceManager.UpdatePartyMembers(InstanceInfo, partyId, members ?? new());
            }
        }
    }
}
