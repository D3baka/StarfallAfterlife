﻿using StarfallAfterlife.Bridge.Database;
using StarfallAfterlife.Bridge.Primitives;
using StarfallAfterlife.Bridge.Profiles;
using StarfallAfterlife.Bridge.Serialization;
using StarfallAfterlife.Bridge.Server;
using StarfallAfterlife.Bridge.Server.Galaxy;
using StarfallAfterlife.Bridge.Server.Quests;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace StarfallAfterlife.Bridge.Realms
{
    public class SfaRealm
    {
        public string Id { get; set; } = "3af8cc9c641f452fbf69b9c99d6f2569";

        public int Seed { get; set; } = 0;

        public string Name { get; set; }

        public string Description { get; set; }

        public int Version { get; set; } = CurrentVersion;

        public const int CurrentVersion = 1;

        public string LastAuth { get; set; }

        public string GalaxyMapHash { get; set; }

        public SfaDatabase Database { get; set; }

        public GalaxyMap GalaxyMap { get; set; }

        public string GalaxyMapCache { get; set; }

        public MobsDatabase MobsDatabase { get; set; }

        public CharacterRewardDatabase CharacterRewardDatabase { get; } = new();

        public RichAsteroidsMap RichAsteroidsMap { get; set; }

        public SecretObjectsMap SecretObjectsMap { get; set; }

        public ShopsMap ShopsMap { get; set; }

        public MobsMap MobsMap { get; set; }

        public DiscoveryQuestsDatabase QuestsDatabase { get; set; }

        public WeeklyQuestsInfo Seasons { get; set; } = new();

        public List<BGShopItem> BGShop { get; set; } = new();

        public SfaServer CreateServer()
        {
            return new SfaServer(this);
        }

        public virtual void Load(string directory)
        {
            LoadInfo(directory);
            LoadDatabase(directory);
        }

        public virtual bool LoadInfo(string directory)
        {
            if (Directory.Exists(directory) == false)
                return false;

            string realmPath = Path.Combine(directory, "Realm.json");

            if (File.Exists(realmPath) == true)
            {

                JsonNode doc = JsonNode.Parse(File.ReadAllText(realmPath));


                if (doc is not null)
                {
                    Id = (string)doc["id"];
                    Seed = (int?)doc["seed"] ?? 0;
                    Name = (string)doc["name"];
                    Description = (string)doc["description"];
                    Version = (int?)doc["version"] ?? CurrentVersion;
                    GalaxyMapHash = (string)doc["galaxy_hash"];
                    LastAuth = (string)doc["last_auth"];
                }

                return true;
            }

            return false;
        }

        public virtual bool LoadDatabase(string directory)
        {
            if (Directory.Exists(directory) == false)
                return false;

            string galaxyMapPath = Path.Combine(directory, "GalaxyMap.json");
            string richAsteroidsMapPath = Path.Combine(directory, "RichAsteroidsMap.json");
            string secretObjectsMapPath = Path.Combine(directory, "SecretObjectsMap.json");
            string shopsMappPath = Path.Combine(directory, "ShopsMap.json");
            string mobsMapPath = Path.Combine(directory, "MobsMap.json");
            string questsDatabasePath = Path.Combine(directory, "QuestsDatabase.json");
            string seasonsPath = Path.Combine(directory, "Seasons.json");
            string bgShopPath = Path.Combine(directory, "BGShop.json");

            static JsonNode ReadJson(string path) =>
                JsonHelpers.ParseNodeUnbuffered(File.ReadAllText(path));

            if (File.Exists(galaxyMapPath) == true)
            {
                GalaxyMapCache = File.ReadAllText(galaxyMapPath);
                GalaxyMap = JsonHelpers.DeserializeUnbuffered<GalaxyMap>(GalaxyMapCache);
                GalaxyMap?.Init();
            }

            if (File.Exists(richAsteroidsMapPath) == true)
            {
                RichAsteroidsMap = new();
                RichAsteroidsMap.LoadFromJson(ReadJson(richAsteroidsMapPath));
            }

            if (File.Exists(secretObjectsMapPath) == true)
            {
                SecretObjectsMap = new();
                SecretObjectsMap.LoadFromJson(ReadJson(secretObjectsMapPath));
            }

            if (File.Exists(shopsMappPath) == true)
            {
                ShopsMap = new();
                ShopsMap.LoadFromJson(ReadJson(shopsMappPath));
            }

            if (File.Exists(mobsMapPath) == true)
            {
                MobsMap = new();
                MobsMap.LoadFromJson(ReadJson(mobsMapPath));
            }

            if (File.Exists(questsDatabasePath) == true)
            {
                QuestsDatabase = new();
                QuestsDatabase.LoadFromJson(ReadJson(questsDatabasePath));
            }

            if (File.Exists(seasonsPath) == true)
            {
                var text = File.ReadAllText(seasonsPath);
                Seasons = JsonHelpers.DeserializeUnbuffered<WeeklyQuestsInfo>(text);
            }

            if (File.Exists(bgShopPath) == true)
            {
                var text = File.ReadAllText(bgShopPath);
                BGShop = JsonHelpers.DeserializeUnbuffered<List<BGShopItem>>(text);
            }

            return true;
        }

        public virtual void Save(string directory)
        {
            SaveInfo(directory);
            SaveDatabase(directory);
        }

        public virtual void SaveInfo(string directory)
        {
            try
            {
                string realmPath = Path.Combine(directory, "Realm.json");

                var doc = JsonHelpers.ParseNodeFromFileUnbuffered(realmPath)?
                    .AsObjectSelf() ?? new JsonObject();

                doc.Override(new Dictionary<string, JsonNode>
                {
                    ["id"] = Id,
                    ["seed"] = Seed,
                    ["name"] = Name,
                    ["description"] = Description,
                    ["version"] = Version,
                    ["galaxy_hash"] = GalaxyMapHash,
                    ["last_auth"] = LastAuth,
                });

                doc.WriteToFileUnbuffered(realmPath, new() { WriteIndented = true });
            }
            catch { }
        }

        public virtual void SaveDatabase(string directory)
        {
            if (Directory.Exists(directory) == false)
                Directory.CreateDirectory(directory);

            string galaxyMapPath = Path.Combine(directory, "GalaxyMap.json");
            string richAsteroidsMapPath = Path.Combine(directory, "RichAsteroidsMap.json");
            string secretObjectsMapPath = Path.Combine(directory, "SecretObjectsMap.json");
            string shopsMappPath = Path.Combine(directory, "ShopsMap.json");
            string mobsMapPath = Path.Combine(directory, "MobsMap.json");
            string questsDatabasePath = Path.Combine(directory, "QuestsDatabase.json");
            string seasonsPath = Path.Combine(directory, "Seasons.json");
            string bgShopPath = Path.Combine(directory, "BGShop.json");

            if (GalaxyMap is not null)
                File.WriteAllText(
                    galaxyMapPath,
                    JsonHelpers.SerializeUnbuffered(GalaxyMap, new JsonSerializerOptions()),
                    Encoding.UTF8);
            else if (GalaxyMapCache is not null)
                File.WriteAllText(galaxyMapPath, GalaxyMapCache);

            if (RichAsteroidsMap is not null)
                File.WriteAllText(richAsteroidsMapPath, RichAsteroidsMap.ToJson().ToJsonString(false), Encoding.UTF8);

            if (SecretObjectsMap is not null)
                File.WriteAllText(secretObjectsMapPath, SecretObjectsMap.ToJson().ToJsonString(false), Encoding.UTF8);

            if (ShopsMap is not null)
                File.WriteAllText(shopsMappPath, ShopsMap.ToJson().ToJsonString(false), Encoding.UTF8);

            if (MobsMap is not null)
                File.WriteAllText(mobsMapPath, MobsMap.ToJson().ToJsonString(false), Encoding.UTF8);

            if (QuestsDatabase is not null)
                File.WriteAllText(questsDatabasePath, QuestsDatabase.ToJson().ToJsonString(false), Encoding.UTF8);

            if (Seasons is not null)
                File.WriteAllText(seasonsPath, JsonHelpers.SerializeUnbuffered(Seasons), Encoding.UTF8);

            if (BGShop is not null)
                File.WriteAllText(bgShopPath, JsonHelpers.SerializeUnbuffered(BGShop), Encoding.UTF8);
        }

        public JsonNode CreateGalaxyMapResponse(bool onlyVariableMap)
        {
            JsonArray exploredSystems = new JsonArray();
            JsonArray exploredPortals = new JsonArray();
            JsonArray exploredPlanets = new JsonArray();
            JsonArray exploredQuickTravelGates = new JsonArray();
            JsonArray exploredMotherships = new JsonArray();

            //foreach (var system in GalaxyMap.Systems)
            //{
            //    exploredSystems.Add(new JsonObject
            //    {
            //        ["id"] = SValue.Create(system.Id),
            //        ["mask"] = SValue.Create("/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////w==")
            //    });

            //    foreach (var item in system.Planets ?? new())
            //    {
            //        exploredPlanets.Add(SValue.Create(item.Id));
            //    }

            //    foreach (var item in system.Portals ?? new())
            //    {
            //        exploredPortals.Add(SValue.Create(item.Id));
            //    }

            //    foreach (var item in system.QuickTravalGates ?? new())
            //    {
            //        exploredQuickTravelGates.Add(SValue.Create(item.Id));
            //    }

            //    foreach (var item in system.Motherships ?? new())
            //    {
            //        exploredMotherships.Add(SValue.Create(item.Id));
            //    }
            //}

            string galaxyMap = null;

            if (onlyVariableMap == false)
                galaxyMap = (GalaxyMapCache ??= JsonHelpers.SerializeUnbuffered(GalaxyMap));

            JsonNode doc = new JsonObject()
            {
                ["galaxymap"] = SValue.Create(galaxyMap),

                ["variablemap"] = new JsonObject
                {
                    ["renamedsystems"] = new JsonArray(),
                    ["renamedplanets"] = new JsonArray(),
                    ["faction_event"] = new JsonArray(),
                },

                ["charactmap"] = new JsonObject
                {
                    ["exploredneutralplanets"] = exploredPlanets,
                    ["exploredportals"] = exploredPortals,
                    ["exploredmotherships"] = exploredMotherships,
                    ["exploredrepairstations"] = new JsonArray(),
                    ["exploredfuelstations"] = new JsonArray(),
                    ["exploredtradestations"] = new JsonArray(),
                    ["exploredmms"] = new JsonArray(),
                    ["exploredscs"] = new JsonArray(),
                    ["exploredpiratesstations"] = new JsonArray(),
                    ["exploredquicktravelgate"] = exploredQuickTravelGates,
                    ["exploredsystems"] = exploredSystems,
                    ["exploredsecretloc"] = new JsonArray(),
                },
            };

            if (onlyVariableMap == true)
                doc["ok"] = 1;

            return doc;
        }
    }
}
