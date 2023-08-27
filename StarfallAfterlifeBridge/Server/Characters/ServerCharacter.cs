﻿using StarfallAfterlife.Bridge.Database;
using StarfallAfterlife.Bridge.Profiles;
using StarfallAfterlife.Bridge.Serialization;
using StarfallAfterlife.Bridge.Server.Discovery;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using StarfallAfterlife.Bridge.Mathematics;
using StarfallAfterlife.Bridge.Server.Quests;
using StarfallAfterlife.Bridge.Events;
using StarfallAfterlife.Bridge.Server.Inventory;
using StarfallAfterlife.Bridge.Instances;
using System.Collections;
using System.Text.Json.Nodes;

namespace StarfallAfterlife.Bridge.Server.Characters
{
    public class ServerCharacter
    {
        public DiscoveryClient DiscoveryClient { get; set; }

        public SfaDatabase Database => DiscoveryClient?.Server?.Realm?.Database;

        public bool InGalaxy { get; set; } = false;

        public int Id { get; set; } = -1;

        public string Name { get; set; }

        public int UniqueId { get; set; } = -1;

        public string UniqueName { get; set; }

        public string HouseTag { get; set; }

        public Faction Faction { get; set; }

        public int Level { get; set; }

        public int AccessLevel { get; set; }

        public float Xp { get; set; }

        public float XpFactor { get; set; }

        public int BonusXp { get; set; }

        public int IGC { get; set; }

        public int BGC { get; set; }

        public int CurrentDetachment { get; set; } = 0;

        public List<int> DetachmentSlots { get; set; } = new List<int>();

        public List<ShipConstructionInfo> Ships { get; protected set; } = new();

        public UserFleet Fleet { get; set; }

        public Dictionary<int, SystemHexMap> ExplorationProgress => Progress.Systems;

        public CharacterProgress Progress { get; protected set; } = new();

        public List<QuestListener> ActiveQuests { get; } = new();

        public CharacterInventory Inventory { get; protected set; }

        public MulticastEvent Events { get; protected set; } = new();

        public SelectionInfo Selection { get; set; } = null;

        public void LoadFromCharacterData(JsonNode doc)
        {
            if (doc is null)
                return;

            XpFactor = (float?)doc["c_xp_boost"] ?? 1;
            BonusXp = (int?)doc["bonus_xp"] ?? 0;
            Xp = (int?)doc["xp"] ?? 0;
            Level = (int?)doc["level"] ?? 0;
            IGC = (int?)doc["igc"] ?? 0;
            BGC = (int?)doc["bgc"] ?? 0;
            Faction = (Faction?)(int?)doc["faction"] ?? Faction.None;
            AccessLevel = (int?)doc["access_level"] ?? 0;
            CurrentDetachment = (int?)doc["currentdetachment"] ?? 0;
            Ships = new();
            DetachmentSlots = new();

            List<ShipConstructionInfo> allShips = new();

            if (doc["ships"]?.AsArraySelf() is JsonArray ships)
            {
                foreach (var ship in ships)
                {
                    if ((string)ship["data"] is string shipDataString)
                    {
                        try
                        {
                            var shipInfo = JsonSerializer.Deserialize<ShipConstructionInfo>(shipDataString);
                            shipInfo.FleetId = Convert.ToInt32(UniqueId);
                            allShips.Add(shipInfo);
                        }
                        catch { }
                    }
                }
            }

            var detachment = doc["detachments"]?.AsArray()?.FirstOrDefault(d => (int?)d["id"] == CurrentDetachment);

            if (detachment["slots"]?.AsObjectSelf() is JsonObject slotsNode &&
                DiscoveryClient?.Server?.Realm?.Database is SfaDatabase database)
            {
                foreach (var slotData in slotsNode)
                {
                    int slotId;
                    int shipId = (int?)slotData.Value ?? -1;

                    if (int.TryParse(slotData.Key, out slotId) == false)
                        slotId = -1;

                    DetachmentSlots.Add(slotId);

                    if (allShips.FirstOrDefault(s => s.Id == shipId) is ShipConstructionInfo ship)
                    {
                        ship.CargoHoldSize = database.GetShipCargo(ship.Hull);
                        ship.Detachment = CurrentDetachment;
                        ship.Slot = slotId;
                        Ships.Add(ship);
                    }
                }
            }
            Inventory = new(this);
            Events?.Broadcast<ICharacterListener>(l => l.OnCurrencyUpdated(this));
        }

        public void LoadActiveShips(JsonNode doc)
        {
            (Ships ??= new()).Clear();

            if (doc is JsonArray shipsData)
            {
                foreach (var item in shipsData)
                {
                    if (JsonHelpers.DeserializeUnbuffered<ShipConstructionInfo>(item) is ShipConstructionInfo ship)
                    {
                        ship.FleetId = UniqueId;
                        Ships.Add(ship);
                    }
                }
            }
        }

        public JsonNode ToDiscoveryCharacterData()
        {
            var jsonOptions = new JsonSerializerOptions();
            jsonOptions.Converters.Add(new InventoryAsCargoJsonConverter());

            return new JsonObject
            {
                ["faction"] = (int)Faction,
                ["charactname"] = UniqueName ?? "",
                ["xp_factor"] = XpFactor,
                ["bonus_xp"] = BonusXp,
                ["access_level"] = AccessLevel,
                ["level"] = Level,
                ["house_tag"] = HouseTag ?? "",
                ["ships_list"] = Ships.Select(s => new JsonObject
                {
                    ["id"] = s.Id,
                    ["data"] = JsonHelpers.ParseNodeUnbuffered(s, jsonOptions)?.ToJsonString()
                }).ToJsonArray(),
                ["ship_groups"] = new JsonArray(),
            };
        }

        public void AcceptQuest(int questId)
        {
            Progress.CompletedQuests ??= new();
            Progress.ActiveQuests ??= new();

            if (Progress.CompletedQuests.Contains(questId) == true ||
                Progress.ActiveQuests.ContainsKey(questId) == true)
                return;

            var questProgress = new QuestProgress();

            if (QuestListener.Create(questId, this) is QuestListener quest)
            {
                Progress.ActiveQuests[questId] = questProgress;
                ActiveQuests.Add(quest);
                quest.StartListening();
                DiscoveryClient?.SyncAcceptNewQuest(questId);
                quest.RaiseInitialActions();
            }
        }


        public void UpdateQuestProgress(int questId, QuestProgress questProgress)
        {
            Progress.CompletedQuests ??= new();
            Progress.ActiveQuests ??= new();

            if (Progress.ActiveQuests.ContainsKey(questId) == true &&
                Progress.CompletedQuests.Contains(questId) == false)
            {
                Progress.ActiveQuests[questId] = questProgress;
                DiscoveryClient?.SyncQuestProgress(questId, questProgress);
            }
        }


        public void FinishQuest(int questId)
        {
            Progress.CompletedQuests ??= new();
            Progress.ActiveQuests ??= new();

            if (ActiveQuests.FirstOrDefault(q => q?.Id == questId) is QuestListener quest &&
                quest.CheckCompleteon() == true)
            {
                quest.StopListening();
                quest.State = QuestState.Done;
                ActiveQuests.Remove(quest);
                Progress.ActiveQuests.Remove(questId);
                Progress.CompletedQuests.Add(questId);
                DiscoveryClient?.SendQuestCompleteData(quest);
                DiscoveryClient?.SyncQuestCompleted(questId);
                Events?.Broadcast<ICharacterListener>(l => l.OnQuestCompleted(this, quest));
            }
        }

        public InventoryStorage GetShipCargoByStockName(string stockName) =>
            GetShipByStockName(stockName)?.Cargo;

        public ShipConstructionInfo GetShipByStockName(string stockName)
        {
            return Ships?.FirstOrDefault(s => stockName == "cargo_ship_" + s.Id);
        }

        public ShipConstructionInfo GetShipById(int shipId)
        {
            return Ships?.FirstOrDefault(s => s.Id == shipId);
        }

        public int AddItemToStocks(int id, int count = 0, bool includeInventory = false)
        {
            if (count < 1)
                return 0;

            if (Database?.GetItem(id) is SfaItem item)
            {
                var fleet = CargoTransactionEndPoint.CreateForCharacterFleet(this);
                var result = fleet.Receive(item, count);

                if (result < count)
                {
                    var inv = CargoTransactionEndPoint.CreateForCharacterInventory(this);
                    result += inv.Receive(item, count);
                }

                RaseStockUpdated();

                DiscoveryClient?.Invoke(c =>
                {
                    c.SendFleetCargo();
                    c.SynckSessionFleetInfo();
                });

                return result;
            }

            return 0;
        }

        public void RaseStockUpdated()
        {
            Events?.Broadcast<IStockListener>(l => l.OnStockUpdated());
        }

        public int GetMainShipHull()
        {
            return Ships?.LastOrDefault()?.Hull ?? 0;
        }

        public UserFleet CreateNewFleet()
        {
            return Fleet = new UserFleet()
            {
                Id = UniqueId,
                Name = UniqueName,
                Faction = Faction,
                Level = Level,
                State = FleetState.WaitingGalaxy,
                Hull = GetMainShipHull()
            };
        }

        public void UpdateShipStatus(int shipId, string shipData = null, string shipStats = null)
        {
            if (Ships?.FirstOrDefault(s => s.Id == shipId) is ShipConstructionInfo ship &&
                JsonHelpers.ParseNodeUnbuffered(shipData) is JsonObject data)
            {

                ship.ArmorDelta = (float?)data["armor"] ?? 0;
                ship.StructureDelta = (float?)data["structure"] ?? 0;

                if ((int?)data["destroyed"] is int isShipdDestroyed &&
                    isShipdDestroyed != 0)
                {
                    Ships.RemoveAll(s => s.Id == shipId);

                    if (Ships.Count < 1)
                        DiscoveryClient?.FinishGalaxySession();
                }
                else
                {
                    if (data["hplist"]?.DeserializeUnbuffered<List<ShipHardpoint>>() is List<ShipHardpoint> hplist)
                    {
                        ship.HardpointList ??= new();
                        ship.HardpointList.Clear();
                        ship.HardpointList.AddRange(hplist);
                    }

                    (ship.Cargo ??= new()).Clear();

                    if (data["cargo_list"] is JsonArray cargo &&
                        DiscoveryClient?.Server?.Realm?.Database is SfaDatabase database)
                    {
                        foreach (var item in cargo)
                        {
                            if (item is JsonObject &&
                                (int?)item["entity"] is int entity &&
                                (int?)item["count"] is int count &&
                                database.GetItem(entity) is SfaItem itemImfo)
                                ship.Cargo.Add(itemImfo, count);
                            continue;
                        }
                    }
                }

                DiscoveryClient?.SynckSessionFleetInfo();
                DiscoveryClient?.SendFleetCargo();
            }
        }

        public void ReacallShip(int shipId, int slotId)
        {
            DiscoveryClient?.SendFleetRecallStateUpdate(FleetRecallState.Started, 1, slotId);

            DiscoveryClient?.RequestShipData(shipId).ContinueWith(t =>
            {
                if (t.Result is ShipConstructionInfo ship)
                {
                    ship.Detachment = CurrentDetachment;
                    ship.Slot = slotId;

                    if (DiscoveryClient?.Server?.Realm?.Database is SfaDatabase database)
                        ship.CargoHoldSize = database.GetShipCargo(ship.Hull);

                    Ships.Add(ship);
                    DiscoveryClient?.SynckSessionFleetInfo();
                    DiscoveryClient?.SendFleetCargo();
                    DiscoveryClient?.SendFleetRecallStateUpdate(FleetRecallState.Done, 0, slotId);
                }
                else
                {
                    DiscoveryClient?.SendFleetRecallStateUpdate(FleetRecallState.Failed, 0, slotId);
                }
            });
        }

        public void LoadQuestsFromProgress()
        {
            foreach (var item in ActiveQuests)
            {
                item?.StopListening();
                item.State = QuestState.Abandoned;
                DiscoveryClient.SendQuestCompleteData(item);
            }

            ActiveQuests.Clear();

            foreach (var item in Progress?.ActiveQuests ?? new())
            {
                if (QuestListener.Create(item.Key, this, item.Value) is QuestListener quest)
                {
                    ActiveQuests.Add(quest);
                    quest.StartListening();
                    DiscoveryClient.SendQuestCompleteData(quest);
                }
            }

            DiscoveryClient.SendQuestDataUpdate();
        }

        public void LoadProgress(CharacterProgress progress)
        {
            Progress = progress ?? new();
            LoadQuestsFromProgress();
        }

        public void AddCharacterShipsXp(Dictionary<int, int> ships)
        {
            DiscoveryClient?.SendAddCharacterCurrencies(UniqueId, shipsXp: ships);
        }

        public void AddCharacterXp(int xp)
        {
            Xp += xp;
            DiscoveryClient?.SendAddCharacterCurrencies(UniqueId, xp: xp);
            Events?.Broadcast<ICharacterListener>(l => l.OnCurrencyUpdated(this));
        }

        public List<DiscoveryDropRule> CreateDropRules()
        {
            return ActiveQuests?
                .Select(q => q.CreateDropRules())
                .Where(r => r is not null)
                .SelectMany(r => r)
                .Where(r => r is not null)
                .ToList() ?? new();
        }

        public void ProcessSyncCharacterCurrencies(JsonNode doc)
        {
            if (doc is not JsonObject)
                return;

            Xp = (int?)doc["xp"] ?? 0;
            Level = (int?)doc["level"] ?? 0;
            AccessLevel = (int?)doc["access_level"] ?? 0;
            IGC = (int?)doc["igc"] ?? 0;
            BGC = (int?)doc["bgc"] ?? 0;

            Events?.Broadcast<ICharacterListener>(l => l.OnCurrencyUpdated(this));
        }


        public void ProcessSyncCharacterNewResearch(JsonNode doc)
        {
            if (doc is not JsonObject)
                return;

            if (doc["new_items"]?.DeserializeUnbuffered<List<int>>() is List<int> newItems)
            {
                DiscoveryClient?.Invoke(() =>
                {
                    foreach (var item in newItems)
                        Events?.Broadcast<ICharacterListener>(l => l.OnProjectResearch(this, item));
                });
            }
        }

        public Task<bool> CheckResearch(int entityId)
        {
            return DiscoveryClient?.Client?.SendRequest(
                SfaServerAction.RequestItemResearch, 
                new JsonObject { ["entity_id"] = entityId })
                .ContinueWith(t =>
                {
                    if (t.Result is SfaClientResponse response &&
                        response.IsSuccess == true &&
                        JsonHelpers.ParseNodeUnbuffered(response.Text) is JsonObject doc &&
                        (bool?)doc["explored"] == true)
                        return true;

                    return false;
                }) ?? Task.FromResult(false);
        }

        public void AddNewStats(Dictionary<string, float> stats)
        {
            if (stats is null)
                return;

            foreach (var item in stats)
                DiscoveryClient?
                    .Invoke(s => Events?
                    .Broadcast<ICharacterListener>(l => l
                    .OnNewStatsReceived(this, item.Key, item.Value)));
        }
    }
}
