using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;
using Oxide.Game.Rust;
using System.Globalization;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using UnityEngine.SceneManagement;
using Facepunch;

namespace Oxide.Plugins
//comments are wide to the right --->
//RemoveGroup external hook error fix.
//Profile-exists checks moved inside timers.
//supply signal checks improved 
//remove unused config option
{
    [Info("BotSpawn", "Steenamaroo", "1.6.0", ResourceId = 2580)]

    [Description("Spawn tailored AI with kits at monuments and custom locations.")]

    class BotSpawn : RustPlugin
    {
        [PluginReference]
        Plugin Vanish, Kits;

        const string permAllowed = "botspawn.allowed";
        bool HasPermission(string id, string perm) => permission.UserHasPermission(id, perm);

        int no_of_AI = 0;
        static System.Random random = new System.Random();

        #region Data
        class StoredData
        {
            public Dictionary<string, MonumentSettings> CustomProfiles = new Dictionary<string, MonumentSettings>();
            public StoredData()
            {
            }
        }

        public class MonumentSettings
        {
            public bool Activate = false;
            public bool Murderer = false;
            public int Bots = 5;
            public int BotHealth = 100;
            public int Radius = 100;
            public List<string> Kit = new List<string>();
            public string BotName = "randomname";
            public int Bot_Accuracy = 4;
            public float Bot_Damage = 0.4f;
            public int Respawn_Timer = 60;
            public bool Disable_Radio = true;
            public float LocationX;
            public float LocationY;
            public float LocationZ;
            public int Roam_Range = 40;
            public bool Peace_Keeper = true;
            public bool Weapon_Drop = true;
            public bool Keep_Default_Loadout = false;
            public bool Wipe_Belt = true;
            public bool Wipe_Clothing = true;
            public bool Allow_Rust_Loot = true;
            public int Suicide_Timer = 300;
        }

        StoredData storedData;
        #endregion

        public double GetRandomNumber(double minimum, double maximum)
        {
            return random.NextDouble() * (maximum - minimum) + minimum;
        }

        void Init()
        {
            JsonConvert.DefaultSettings = () => new JsonSerializerSettings
            {
                Formatting = Newtonsoft.Json.Formatting.Indented,
                ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore
            };
            var filter = RustExtension.Filter.ToList();                                                                                                     //Thanks Fuji. :)
            filter.Add("cover points");
            filter.Add("resulted in a conflict");
            RustExtension.Filter = filter.ToArray();
            no_of_AI = 0;
            LoadConfigVariables();
        }

        void OnServerInitialized()
        {
            FindMonuments();
        }

        void Loaded()
        {
            lang.RegisterMessages(messages, this);
            permission.RegisterPermission(permAllowed, this);
            storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("BotSpawn");
            Interface.Oxide.DataFileSystem.WriteObject("BotSpawn", storedData);
        }

        void Unload()
        {
            var filter = RustExtension.Filter.ToList();
            filter.Remove("cover points");
            filter.Remove("resulted in a conflict");
            RustExtension.Filter = filter.ToArray();
            Wipe();
        }

        void Wipe()
        {
            foreach (var bot in TempRecord.NPCPlayers)
            {
                if (bot == null)
                    continue;
                else
                    bot.Kill();
            }
            TempRecord.NPCPlayers.Clear();
        }

        bool isAuth(BasePlayer player)
        {
            if (player.net.connection != null)
                if (player.net.connection.authLevel < 2)
                    return false;
            return true;
        }

        object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            NPCPlayerApex Scientist = null;

            if (entity is NPCPlayerApex)
            {
                BasePlayer player = entity as BasePlayer;
                Scientist = entity as NPCPlayerApex;

                if (!TempRecord.NPCPlayers.Contains(Scientist))
                    return null;
                if (info.Initiator?.ToString() == null && configData.Options.Pve_Safe)
                    info.damageTypes.ScaleAll(0);
                if (info.Initiator is BasePlayer)
                {
                    var damagedbot = entity as NPCPlayer;
                    var canNetwork = Vanish?.Call("IsInvisible", info.Initiator);                                                                       //bots wont retaliate to vanished players
                    var bData = Scientist.GetComponent<botData>();
                    if ((canNetwork is bool))
                        if ((bool)canNetwork)
                        {
                            info.Initiator = null;
                        }

                    if (bData.peaceKeeper)                                                                                                          //prevent melee farming with peacekeeper on
                    {
                        var heldMelee = info.Weapon as BaseMelee;
                        var heldTorchWeapon = info.Weapon as TorchWeapon;
                        if (heldMelee != null || heldTorchWeapon != null)
                            info.damageTypes.ScaleAll(0);
                    }

                    float multiplier = 100f / bData.health;
                    info.damageTypes.ScaleAll(multiplier);
                }
            }

            if (info?.Initiator is NPCPlayer && entity is BasePlayer)                                                                                       //add in bot accuracy
            {
                var attacker = info.Initiator as NPCPlayerApex;

                if (TempRecord.NPCPlayers.Contains(attacker))
                {
                    var bData = attacker.GetComponent<botData>();

                    int rand = random.Next(1, 100);
                    float distance = (Vector3.Distance(info.Initiator.transform.position, entity.transform.position));

                    var newAccuracy = (bData.accuracy * 10f);
                    var newDamage = (bData.damage);
                    if (distance > 100f)
                    {
                        newAccuracy = ((bData.accuracy * 10f) / (distance / 100f));
                        newDamage = bData.damage / (distance / 100f);
                    }
                    if (newAccuracy < rand)                                                                                                          //scale bot attack damage
                    {
                        return true;
                    }
                    else
                    {
                        info.damageTypes.ScaleAll(newDamage);
                        return null;
                    }
                }
            }
            return null;
        }

        void OnPlayerDie(BasePlayer player)
        {
            string respawnLocationName = "";
            NPCPlayerApex Scientist = null;
            if (player is NPCPlayerApex)
            {
                Scientist = player as NPCPlayerApex;
                if (!TempRecord.NPCPlayers.Contains(Scientist))
                    return;

                if (TempRecord.NPCPlayers.Contains(Scientist))
                {
                    var bData = Scientist.GetComponent<botData>();
                    Item activeItem = player.GetActiveItem();
                    if (bData.dropweapon == true && activeItem != null)
                    {
                        using (TimeWarning timeWarning = TimeWarning.New("PlayerBelt.DropActive", 0.1f))
                        {
                            activeItem.Drop(player.eyes.position, new Vector3(), new Quaternion());
                            player.svActiveItemID = 0;
                            player.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                            TempRecord.kitRemoveList.Add(player.userID, activeItem.info.name);
                        }
                    }
                    no_of_AI--;
                    respawnLocationName = bData.monumentName;
                    TempRecord.DeadNPCPlayerIds.Add(Scientist.userID);
                    if (TempRecord.MonumentProfiles[respawnLocationName].Disable_Radio == true)
                        Scientist.DeathEffect = new GameObjectRef();                                                                                               //kill radio effects

                    if (bData.respawn == false)
                    {
                        UnityEngine.Object.Destroy(Scientist.GetComponent<botData>());
                        UpdateRecords(Scientist);
                        return;
                    }
                    foreach (var profile in TempRecord.MonumentProfiles)
                    {


                        timer.Once(profile.Value.Respawn_Timer, () => {
                            if (profile.Key == respawnLocationName)
                                SpawnBots(profile.Key, profile.Value, null, null, new Vector3());
                        });
                        UnityEngine.Object.Destroy(Scientist.GetComponent<botData>());
                        UpdateRecords(Scientist);



                    }
                }
            }
        }

        void UpdateRecords(NPCPlayerApex player)
        {
            if (TempRecord.NPCPlayers.Contains(player))
                TempRecord.NPCPlayers.Remove(player);
        }

        // Facepunch.RandomUsernames
        public static string Get(ulong v)                                                                                                                      //credit Fujikura.
        {
            return Facepunch.RandomUsernames.Get((int)(v % 2147483647uL));
        }

        BaseEntity InstantiateSci(Vector3 position, Quaternion rotation, bool murd)                                                                            //Spawn population spam fix - credit Fujikura
        {
            string prefabname = "assets/prefabs/npc/scientist/scientist.prefab";
            if (murd == true)
            {
                prefabname = "assets/prefabs/npc/murderer/murderer.prefab";
            }

            var prefab = GameManager.server.FindPrefab(prefabname);
            GameObject gameObject = Instantiate.GameObject(prefab, position, rotation);
            gameObject.name = prefabname;
            SceneManager.MoveGameObjectToScene(gameObject, Rust.Server.EntityScene);
            if (gameObject.GetComponent<Spawnable>())
                UnityEngine.Object.Destroy(gameObject.GetComponent<Spawnable>());
            if (!gameObject.activeSelf)
                gameObject.SetActive(true);
            BaseEntity component = gameObject.GetComponent<BaseEntity>();
            return component;


        }

        void SpawnBots(string name, MonumentSettings settings, string type, string group, Vector3 location)
        {

            var murd = settings.Murderer;
            var pos = new Vector3(settings.LocationX, settings.LocationY, settings.LocationZ);
            if (location != new Vector3())
                pos = location;
            var zone = settings;

            int X = random.Next((-zone.Radius), (zone.Radius));
            int Z = random.Next((-zone.Radius), (zone.Radius));
            int dropX = random.Next(5, 10);
            int dropZ = random.Next(5, 10);
            int Y = 100;
            var CentrePos = new Vector3((pos.x + X), 200, (pos.z + Z));
            Quaternion rot = Quaternion.Euler(0, 0, 0);
            Vector3 newPos = (CalculateGroundPos(CentrePos));
            NPCPlayer entity = (NPCPlayer)InstantiateSci(newPos, rot, murd);

            var botapex = entity.GetComponent<NPCPlayerApex>();
            var bData = botapex.gameObject.AddComponent<botData>();

            TempRecord.NPCPlayers.Add(botapex);

            if (zone.Roam_Range < 20)
                zone.Roam_Range = 20;
            botapex.Spawn();

            if (group != null)
                bData.group = group;
            else
                bData.group = null;
            bData.spawnPoint = newPos;
            bData.accuracy = zone.Bot_Accuracy;
            bData.damage = zone.Bot_Damage;
            bData.health = zone.BotHealth;
            bData.monumentName = name;
            bData.respawn = true;
            bData.roamRange = zone.Roam_Range;
            bData.dropweapon = zone.Weapon_Drop;
            bData.keepAttire = zone.Keep_Default_Loadout;
            bData.peaceKeeper = zone.Peace_Keeper;

            int suicInt = random.Next((zone.Suicide_Timer), (zone.Suicide_Timer + 10));                                        //slightly randomise suicide de-spawn time
            if (type == "AirDrop" || type == "Attack")
            {
                bData.respawn = false;
                timer.Once(suicInt, () =>
                {
                    if (TempRecord.NPCPlayers.Contains(botapex))
                    {
                        if (botapex != null)
                        {
                            if (botapex.AttackTarget != null && Vector3.Distance(botapex.transform.position, botapex.AttackTarget.transform.position) < 10)
                            {
                                var position = botapex.AttackTarget.transform.position;
                                botapex.svActiveItemID = 0;
                                botapex.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                                botapex.inventory.UpdatedVisibleHolsteredItems();
                                timer.Repeat(0.05f, 100, () =>
                                {
                                    if (botapex == null) return;
                                    botapex.SetDestination(position);
                                });
                            }
                            timer.Once(4, () =>
                            {
                                if (botapex == null) return;
                                Effect.server.Run("assets/prefabs/weapons/rocketlauncher/effects/rocket_explosion.prefab", botapex.transform.position);
                                HitInfo nullHit = new HitInfo();
                                nullHit.damageTypes.Add(Rust.DamageType.Explosion, 10000);
                                botapex.IsInvinsible = false;
                                botapex.Die(nullHit);
                            }
                            );
                        }
                        else
                        {
                            TempRecord.NPCPlayers.Remove(botapex);
                            return;
                        }
                    }
                    else return;
                });
            }

            int kitRnd;
            if (zone.Kit.Count != 0)
            {
                kitRnd = random.Next(zone.Kit.Count);
                if (zone.Kit[kitRnd] != null)
                {
                    object checkKit = (Kits.CallHook("GetKitInfo", zone.Kit[kitRnd], true));
                    if (checkKit == null)
                    {
                        if (murd)
                            PrintWarning($"Kit {zone.Kit[kitRnd]} does not exist - Spawning default Murderer.");
                        else
                            PrintWarning($"Kit {zone.Kit[kitRnd]} does not exist - Spawning default Scientist.");
                    }
                    else
                    {
                        bool weaponInBelt = false;
                        if (checkKit != null && checkKit is JObject)
                        {
                            List<string> contentList = new List<string>();
                            JObject kitContents = checkKit as JObject;

                            JArray items = kitContents["items"] as JArray;
                            foreach (var weap in items)
                            {
                                JObject item = weap as JObject;
                                if (item["container"].ToString() == "belt")
                                    weaponInBelt = true;                                                                                                    //doesn't actually check for weapons - just any item.
                            }
                        }
                        if (!weaponInBelt)
                        {
                            if (murd)
                                PrintWarning($"Kit {zone.Kit[kitRnd]} has no items in belt - Spawning default Murderer.");
                            else
                                PrintWarning($"Kit {zone.Kit[kitRnd]} does not exist - Spawning default Scientist.");
                        }
                        else
                        {
                            if (bData.keepAttire == false)
                                entity.inventory.Strip();
                            Kits.Call($"GiveKit", entity, zone.Kit[kitRnd], true);
                            if (!(TempRecord.kitList.ContainsKey(botapex.userID)))
                            {
                                TempRecord.kitList.Add(botapex.userID, new kitData
                                {
                                    Kit = zone.Kit[kitRnd],
                                    Wipe_Belt = zone.Wipe_Belt,
                                    Wipe_Clothing = zone.Wipe_Clothing,
                                    Allow_Rust_Loot = zone.Allow_Rust_Loot,
                                });
                            }
                        }
                    }
                }
            }
            else
            {
                if (!(TempRecord.kitList.ContainsKey(botapex.userID)))
                {
                    TempRecord.kitList.Add(botapex.userID, new kitData
                    {
                        Kit = "",
                        Wipe_Belt = zone.Wipe_Belt,
                        Wipe_Clothing = zone.Wipe_Clothing,
                        Allow_Rust_Loot = zone.Allow_Rust_Loot,
                    });
                }
            }

            botapex.health = 100;
            timer.Once(10, () => botapex.SetFact(NPCPlayerApex.Facts.CanSwitchWeapon, 0, true, true));
            botapex.WeaponSwitchFrequency = 1000f;
            botapex.ToolSwitchFrequency = 1000f;
            no_of_AI++;

            foreach (Item item in botapex.inventory.containerBelt.itemList)                                                                                 //store organised weapons lists
            {
                var held = item.GetHeldEntity();
                if (held as HeldEntity != null)
                {
                    if (held.name.Contains("bow") || held.name.Contains("launcher"))
                        continue;
                    if (held as BaseMelee != null || held as TorchWeapon != null)
                        bData.MeleeWeapons.Add(item);
                    else
                    {
                        if (held as BaseProjectile != null)
                        {
                            bData.AllProjectiles.Add(item);
                            if (held.name.Contains("m92") || held.name.Contains("pistol") || held.name.Contains("python") || held.name.Contains("waterpipe"))
                                bData.CloseRangeWeapons.Add(item);
                            else if (held.name.Contains("bolt"))
                                bData.LongRangeWeapons.Add(item);
                            else
                                bData.MediumRangeWeapons.Add(item);
                        }
                    }
                }
            }

            if (zone.BotName == "randomname")
                entity.displayName = Get(entity.userID);
            else
                entity.displayName = zone.BotName;

            if (zone.Disable_Radio)
                botapex.RadioEffect = new GameObjectRef();

            timer.Once(1, () => SelectWeapon(botapex, null, false));
        }

        void OnEntitySpawned(BaseEntity entity)
        {
            var KitDetails = new kitData();
            if (entity != null)
            {
                if (entity is NPCPlayerCorpse)
                {
                    var corpse = entity as NPCPlayerCorpse;
                    corpse.ResetRemovalTime(configData.Options.Corpse_Duration);

                    if (TempRecord.kitList.ContainsKey(corpse.playerSteamID))
                    {
                        KitDetails = TempRecord.kitList[corpse.playerSteamID];
                        NextTick(() =>
                        {
                            if (corpse == null)
                                return;
                            if (!KitDetails.Allow_Rust_Loot)
                            {
                                corpse.containers[0].Clear();
                                corpse.containers[1].Clear();
                                corpse.containers[2].Clear();
                            }
                            if (KitDetails.Kit != "")
                            {
                                string[] checkKit = (Kits.CallHook("GetKitContents", KitDetails.Kit)) as string[];

                                var tempbody = GameManager.server.CreateEntity("assets/prefabs/player/player.prefab", (corpse.transform.position - new Vector3(0, -100, 0)), corpse.transform.rotation).ToPlayer();
                                tempbody.Spawn();

                                Kits?.Call($"GiveKit", tempbody, KitDetails.Kit, true);

                                var source = new ItemContainer[] { tempbody.inventory.containerMain, tempbody.inventory.containerWear, tempbody.inventory.containerBelt };

                                for (int i = 0; i < (int)source.Length; i++)
                                {
                                    Item[] array = source[i].itemList.ToArray();
                                    for (int j = 0; j < (int)array.Length; j++)
                                    {
                                        Item item = array[j];
                                        if (!item.MoveToContainer(corpse.containers[i], -1, true))
                                        {
                                            item.Remove(0f);
                                        }
                                    }
                                }
                                tempbody.Kill();
                            }
                            if (TempRecord.kitList[corpse.playerSteamID].Wipe_Belt)
                                corpse.containers[2].Clear();
                            else
                            if (TempRecord.kitRemoveList.ContainsKey(corpse.playerSteamID))
                            {
                                foreach (var thing in corpse.containers[2].itemList)                                                                            //If weapon drop is enabled, this removes the weapon from the corpse's inventory.
                                {
                                    if (TempRecord.kitRemoveList[corpse.playerSteamID] == thing.info.name)
                                    {
                                        thing.Remove();
                                        TempRecord.kitRemoveList.Remove(corpse.playerSteamID);
                                        break;
                                    }
                                }
                            }

                            if (TempRecord.kitList[corpse.playerSteamID].Wipe_Clothing)
                            {
                                corpse.containers[1].Clear();
                            }

                            TempRecord.kitList.Remove(corpse.playerSteamID);
                        });
                    }
                }

                if (entity is DroppedItemContainer)
                {
                    NextTick(() =>
                    {
                        if (entity == null || entity.IsDestroyed) return;
                        var container = entity as DroppedItemContainer;

                        ulong ownerID = container.playerSteamID;
                        if (ownerID == 0) return;
                        if (configData.Options.Remove_BackPacks)
                        {
                            if (TempRecord.DeadNPCPlayerIds.Contains(ownerID))
                            {
                                entity.Kill();
                                TempRecord.DeadNPCPlayerIds.Remove(ownerID);
                                return;
                            }
                        }

                    });
                }

                if (entity.name.Contains("grenade.smoke.deployed"))
                {
                    timer.Once(2.3f, () =>
                    {
                        Puts($"Smoke Grenade Location - {new Vector3(entity.transform.position.x, 0, entity.transform.position.z)}");
                        TempRecord.smokeGrenades.Add(new Vector3(entity.transform.position.x, 0, entity.transform.position.z));
                    });
                }

                if (!(entity.name.Contains("supply_drop")))
                    return;

                Vector3 dropLocation = new Vector3(entity.transform.position.x, 0, entity.transform.position.z);

                if (!(configData.Options.Supply_Enabled))
                {
                    foreach (var location in TempRecord.smokeGrenades)
                    {
                        if (Vector3.Distance(location, dropLocation) < 35f)
                        {
                            TempRecord.smokeGrenades.Remove(location);
                            return;
                        }
                    }
                }
                foreach (var profile in TempRecord.MonumentProfiles)
                {
                    if (profile.Key == "AirDrop" && profile.Value.Activate == true)
                    {
                        timer.Repeat(0f, profile.Value.Bots, () =>
                        {
                            profile.Value.LocationX = entity.transform.position.x;
                            profile.Value.LocationY = entity.transform.position.y;
                            profile.Value.LocationZ = entity.transform.position.z;
                            SpawnBots(profile.Key, profile.Value, "AirDrop", null, new Vector3());
                        }
                        );
                    }
                }
            }
        }

        void SelectWeapon(NPCPlayerApex npcPlayer, BasePlayer victim, bool hasAttacker)
        {
            if (npcPlayer == null)
                return;

            if (npcPlayer.svActiveItemID == 0)
            {
                return;
            }

            var active = npcPlayer.GetActiveItem();
            HeldEntity heldEntity1 = null;

            if (active != null)
                heldEntity1 = active.GetHeldEntity() as HeldEntity;

            var bData = npcPlayer.GetComponent<botData>();

            if (hasAttacker == false)
            {
                List<int> weapons = new List<int>();                                                                                                        //check all their weapons
                foreach (Item item in npcPlayer.inventory.containerBelt.itemList)
                {
                    var held = item.GetHeldEntity();
                    if (held is BaseProjectile || held is BaseMelee || held is TorchWeapon)
                    {
                        weapons.Add(Convert.ToInt16(item.position));
                    }
                }

                if (weapons.Count == 0)
                {
                    PrintWarning(lang.GetMessage("noWeapon", this), bData.monumentName);
                    return;
                }
                int index = random.Next(weapons.Count);
                var currentTime = TOD_Sky.Instance.Cycle.Hour;

                if (currentTime > 20 || currentTime < 8)
                {
                    foreach (Item item in npcPlayer.inventory.containerBelt.itemList)
                    {
                        HeldEntity held = item.GetHeldEntity() as HeldEntity;

                        if (item.ToString().Contains("flashlight"))
                        {
                            if (heldEntity1 != null)
                                heldEntity1.SetHeld(false);
                            var UID = item.uid;

                            ChangeWeapon(npcPlayer, held, UID);
                            return;
                        }
                    }
                }
                else
                {
                    foreach (Item item in npcPlayer.inventory.containerBelt.itemList)                                                                       //pick one at random to start with
                    {
                        HeldEntity held = item.GetHeldEntity() as HeldEntity;

                        if (item.position == weapons[index])
                        {
                            if (heldEntity1 != null)
                                heldEntity1.SetHeld(false);
                            var UID = npcPlayer.inventory.containerBelt.GetSlot(weapons[index]).uid;

                            ChangeWeapon(npcPlayer, held, UID);
                            return;
                        }
                    }
                }
            }

            if (hasAttacker == true)
            {
                bData.canChangeWeapon++;

                if (bData.canChangeWeapon > 3)
                {
                    bData.canChangeWeapon = 0;
                    if (npcPlayer == null)
                        return;

                    if (heldEntity1 == null)
                        bData.currentWeaponRange = 0;

                    float distance = Vector3.Distance(npcPlayer.transform.position, victim.transform.position);
                    int noOfAvailableWeapons = 0;
                    int selectedWeapon;
                    Item chosenWeapon = null;
                    HeldEntity held = null;
                    int newCurrentRange = 0;
                    var currentTime = TOD_Sky.Instance.Cycle.Hour;
                    bool night = false;

                    if (currentTime > 20 || currentTime < 8)
                        night = true;

                    if (npcPlayer.AttackTarget == null && night)
                    {
                        foreach (var weap in bData.MeleeWeapons)
                        {
                            if (weap.ToString().Contains("flashlight"))
                            {
                                chosenWeapon = weap;
                                newCurrentRange = 1;
                            }
                        }
                    }
                    else
                    {
                        if (distance < 2f && bData.MeleeWeapons.Count != 0)
                        {
                            bData.enemyDistance = 1;
                            foreach (var weap in bData.MeleeWeapons)
                            {
                                noOfAvailableWeapons++;
                            }
                            if (noOfAvailableWeapons > 0)
                            {
                                selectedWeapon = random.Next(bData.MeleeWeapons.Count);
                                chosenWeapon = bData.MeleeWeapons[selectedWeapon];
                                newCurrentRange = 1;
                            }
                        }
                        else if (distance > 1f && distance < 10f && bData.CloseRangeWeapons != null)
                        {
                            bData.enemyDistance = 2;
                            foreach (var weap in bData.CloseRangeWeapons)
                            {
                                noOfAvailableWeapons++;
                            }
                            if (noOfAvailableWeapons > 0)
                            {
                                selectedWeapon = random.Next(bData.CloseRangeWeapons.Count);
                                chosenWeapon = bData.CloseRangeWeapons[selectedWeapon];
                                newCurrentRange = 2;
                            }
                            else
                            {
                                foreach (var weap in bData.MediumRangeWeapons)                                                                          //if no close weapon, prioritise medium
                                {
                                    noOfAvailableWeapons++;
                                }
                                if (noOfAvailableWeapons > 0)
                                {
                                    selectedWeapon = random.Next(bData.MediumRangeWeapons.Count);
                                    chosenWeapon = bData.MediumRangeWeapons[selectedWeapon];
                                    newCurrentRange = 3;
                                }
                            }
                        }
                        else if (distance > 9f && distance < 30f && bData.MediumRangeWeapons != null)
                        {
                            bData.enemyDistance = 3;
                            foreach (var weap in bData.MediumRangeWeapons)
                            {
                                noOfAvailableWeapons++;
                            }
                            if (noOfAvailableWeapons > 0)
                            {
                                selectedWeapon = random.Next(bData.MediumRangeWeapons.Count);
                                chosenWeapon = bData.MediumRangeWeapons[selectedWeapon];
                                newCurrentRange = 3;
                            }
                        }
                        else if (distance > 29 && bData.LongRangeWeapons != null)
                        {
                            bData.enemyDistance = 4;
                            foreach (var weap in bData.LongRangeWeapons)
                            {
                                noOfAvailableWeapons++;
                            }
                            if (noOfAvailableWeapons > 0)
                            {
                                selectedWeapon = random.Next(bData.LongRangeWeapons.Count);
                                chosenWeapon = bData.LongRangeWeapons[selectedWeapon];
                                newCurrentRange = 4;
                            }
                            else
                            {
                                foreach (var weap in bData.MediumRangeWeapons)                                                                          //if no long weapon, prioritise medium
                                {
                                    noOfAvailableWeapons++;
                                }
                                if (noOfAvailableWeapons > 0)
                                {
                                    selectedWeapon = random.Next(bData.MediumRangeWeapons.Count);
                                    chosenWeapon = bData.MediumRangeWeapons[selectedWeapon];
                                    newCurrentRange = 3;
                                }
                            }
                        }
                        if (chosenWeapon == null)                                                                                                       //if no weapon suited to range, pick any random bullet weapon
                        {                                                                                                                               //prevents sticking with melee @>2m when no pistol is available
                            bData.enemyDistance = 5;
                            if (heldEntity1 != null && bData.AllProjectiles.Contains(active))                                                               //prevents choosing a random weapon if the existing one is fine
                                return;
                            foreach (var weap in bData.AllProjectiles)
                            {
                                noOfAvailableWeapons++;
                            }
                            if (noOfAvailableWeapons > 0)
                            {
                                selectedWeapon = random.Next(bData.AllProjectiles.Count);
                                chosenWeapon = bData.AllProjectiles[selectedWeapon];
                                newCurrentRange = 5;
                            }
                        }
                    }
                    if (chosenWeapon == null) return;
                    if (newCurrentRange == bData.currentWeaponRange)
                    {
                        return;
                    }
                    else
                    {
                        bData.currentWeaponRange = newCurrentRange;
                        held = chosenWeapon.GetHeldEntity() as HeldEntity;

                        if (heldEntity1 != null && heldEntity1.name == held.name)
                            return;

                        if (heldEntity1 != null && heldEntity1.name != held.name)
                            heldEntity1.SetHeld(false);

                        var UID = chosenWeapon.uid;
                        ChangeWeapon(npcPlayer, held, UID);
                    }
                }
            }
            else
            {
                timer.Once(1, () => SelectWeapon(npcPlayer, victim, false));
            }
        }

        void ChangeWeapon(NPCPlayer npcPlayer, HeldEntity held, uint UID)
        {
            if (npcPlayer == null) return;
            npcPlayer.svActiveItemID = 0;
            npcPlayer.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
            npcPlayer.inventory.UpdatedVisibleHolsteredItems();

            npcPlayer.svActiveItemID = UID;
            npcPlayer.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
            held.SetHeld(true);
            npcPlayer.svActiveItemID = UID;
            npcPlayer.inventory.UpdatedVisibleHolsteredItems();

            AttackEntity heldGun = npcPlayer.GetHeldEntity() as AttackEntity;
            /*if (heldGun != null)
            {
                if (heldGun as BaseMelee != null || heldGun as TorchWeapon != null)
                    heldGun.effectiveRange = 2;
                else if (held.name.Contains("bolt"))
                    heldGun.effectiveRange = 800f;
                else
                    heldGun.effectiveRange = 200f;
                return;      
            }*/
        }
        #region targeting

        object OnNpcPlayerTarget(NPCPlayerApex npcPlayer, BaseEntity entity)
        {
            if (entity is NPCPlayer && configData.Options.NoBotsVBots)
                return true;

            if (!TempRecord.NPCPlayers.Contains(npcPlayer))
                return null;

            if (npcPlayer == null || entity == null)
                return null;

            if (entity is NPCPlayer)
                return true;
            BasePlayer victim = null;
            if (entity is BasePlayer)
            {

                var active = npcPlayer.GetActiveItem();
                var bData = npcPlayer.GetComponent<botData>();

                npcPlayer.AiContext.LastAttacker = entity;

                victim = entity as BasePlayer;
                var currentTime = TOD_Sky.Instance.Cycle.Hour;

                HeldEntity heldEntity1 = null;
                HeldEntity attackerheldEntity1 = null;
                if (active != null)
                    heldEntity1 = active.GetHeldEntity() as HeldEntity;

                if (heldEntity1 == null)                                                                                                                            //freshspawn catch, pre weapon draw.
                    return null;                                                                                                       //RUST IS STILL TRYING TO CHANGE WEAPON FOR SCI ONLY
                if (heldEntity1 != null)
                {
                    if (currentTime > 20 || currentTime < 8)
                        heldEntity1.SetLightsOn(true);
                    else
                        heldEntity1.SetLightsOn(false);
                }

                if (bData.peaceKeeper)
                {
                    if (victim.svActiveItemID == 0u)
                        return true;
                    else
                    {
                        var heldWeapon = victim.GetHeldEntity() as BaseProjectile;
                        var heldFlame = victim.GetHeldEntity() as FlameThrower;
                        if (heldWeapon == null && heldFlame == null)
                            return true;
                    }
                }

                SelectWeapon(npcPlayer, victim, true);

                if (!victim.userID.IsSteamId() && configData.Options.Ignore_HumanNPC)                                                                            //stops bots targeting humannpc
                    return true;
            }
            if (entity.name.Contains("agents/") && configData.Options.Ignore_Animals)                                                                           //stops bots targeting animals
                return true;

            return null;
        }

        object CanBradleyApcTarget(BradleyAPC bradley, BaseEntity target)                                                                                       //stops bradley targeting bots
        {
            if (target is NPCPlayer && configData.Options.APC_Safe)
                return false;
            return null;
        }

        object OnNpcTarget(BaseNpc npc, BaseEntity entity)                                                                                                      //stops animals targeting bots
        {
            if (entity is NPCPlayer && configData.Options.Animal_Safe)
                return true;
            return null;
        }

        object CanBeTargeted(BaseCombatEntity player, MonoBehaviour turret)                                                                                     //stops autoturrets targetting bots
        {
            if (player is NPCPlayer && configData.Options.Turret_Safe)
                return false;
            return null;
        }

        #endregion
        void AttackPlayer(Vector3 location, string name, MonumentSettings profile, string group)
        {
            timer.Repeat(1f, profile.Bots, () =>
            {
                SpawnBots(name, profile, "Attack", group, location);
            }
            );
        }

        //External Hooks.
        [HookMethod("AddGroupSpawn")]
        public string[] AddGroupSpawn(Vector3 location, string profileName, string group)
        {
            if (location == null || profileName == null || group == null)
                return new string[] { "error", "Null parameter" };
            string lowerProfile = profileName.ToLower();

            foreach (var entry in storedData.CustomProfiles)
            {
                if (entry.Key.ToLower() == lowerProfile)
                {
                    var profile = entry.Value;
                    Vector3 targetLocation = (CalculateGroundPos(location));
                    AttackPlayer(targetLocation, entry.Key, profile, group.ToLower());
                    return new string[] { "true", "Group Successfully Added" };
                }
            }
            return new string[] { "false", "Group add failed - Check profile name and try again" };
        }

        [HookMethod("RemoveGroupSpawn")]
        public string[] RemoveGroupSpawn(string group)
        {
            if (group == null)
                return new string[] { "error", "No Group Specified." };

            List<NPCPlayerApex> toDestroy = new List<NPCPlayerApex>();
            foreach (var bot in TempRecord.NPCPlayers)
            {
                if (bot == null)
                    continue;
                var bData = bot.GetComponent<botData>();
                if (bData.group == group.ToLower())
                    toDestroy.Add(bot);
            }
            if (toDestroy.Count == 0)
                return new string[] { "true", $"There are no bots belonging to {group}" };
            foreach (var killBot in toDestroy)
            {
                UpdateRecords(killBot);
                killBot.Kill();
            }
            return new string[] { "true", $"Group {group} was destroyed." };

        }

        [HookMethod("CreateNewProfile")]
        public string[] CreateNewProfile(string name, string profile)
        {
            if (name == null)
                return new string[] { "error", "No Name Specified." };
            if (profile == null)
                return new string[] { "error", "No Profile Settings Specified." };

            MonumentSettings newProfile = JsonConvert.DeserializeObject<MonumentSettings>(profile);

            if (storedData.CustomProfiles.ContainsKey(name))
                return new string[] { "false", "A Profile By This Name Already Exists." };

            storedData.CustomProfiles.Add(name, newProfile);
            Interface.Oxide.DataFileSystem.WriteObject("BotSpawn", storedData);
            return new string[] { "true", $"New Profile {name} Was Created." };
        }

        [HookMethod("ProfileExists")]
        public string[] ProfileExists(string name)
        {
            if (name == null)
                return new string[] { "error", "No Name Specified." };

            if (storedData.CustomProfiles.ContainsKey(name))
                return new string[] { "true", $"{name} Exists." };

            return new string[] { "false", $"{name} Does Not Exist." };
        }

        [HookMethod("RemoveProfile")]
        public string[] RemoveProfile(string name)
        {
            if (name == null)
                return new string[] { "error", "No Name Specified." };

            if (storedData.CustomProfiles.ContainsKey(name))
            {
                foreach (var bot in TempRecord.NPCPlayers)
                {
                    if (bot == null)
                        continue;

                    var bData = bot.GetComponent<botData>();
                    if (bData.monumentName == name)
                        bot.Kill();
                }
                TempRecord.MonumentProfiles.Remove(name);
                storedData.CustomProfiles.Remove(name);
                Interface.Oxide.DataFileSystem.WriteObject("BotSpawn", storedData);
                return new string[] { "true", $"Profile {name} Was Removed." };
            }
            else
            {
                return new string[] { "false", $"Profile {name} Does Not Exist." };
            }
        }

        static BasePlayer FindPlayerByName(string name)
        {
            BasePlayer result = null;
            foreach (BasePlayer current in BasePlayer.activePlayerList)
            {
                if (current.displayName.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    BasePlayer result2 = current;
                    return result2;
                }
                if (current.UserIDString.Contains(name, CompareOptions.OrdinalIgnoreCase))
                {
                    BasePlayer result2 = current;
                    return result2;
                }
                if (current.displayName.Contains(name, CompareOptions.OrdinalIgnoreCase))
                {
                    result = current;
                }
            }
            return result;
        }

        static Vector3 CalculateGroundPos(Vector3 sourcePos)                                                                                                    //credit Wulf & Nogrod 
        {
            RaycastHit hitInfo;

            if (UnityEngine.Physics.Raycast(sourcePos, Vector3.down, out hitInfo, 800f, LayerMask.GetMask("Terrain", "World", "Construction"), QueryTriggerInteraction.Ignore))
            {
                sourcePos.y = hitInfo.point.y;
            }
            sourcePos.y = Mathf.Max(sourcePos.y, TerrainMeta.HeightMap.GetHeight(sourcePos));
            return sourcePos;
        }

        private void FindMonuments()                                                                                                                            //credit K1lly0u 
        {
            TempRecord.MonumentProfiles.Clear();
            var allobjects = UnityEngine.Object.FindObjectsOfType<GameObject>();

            int warehouse = 0;
            int lighthouse = 0;
            int gasstation = 0;
            int spermket = 0;
            int compound = 0;

            foreach (var gobject in allobjects)
            {
                if (gobject.name.Contains("autospawn/monument"))
                {
                    var pos = gobject.transform.position;



                    if (gobject.name.Contains("airfield_1"))
                    {
                        AddProfile("Airfield", configData.Zones.Airfield, pos);
                        continue;
                    }
                    if (gobject.name.Contains("compound") && compound == 0)
                    {
                        AddProfile("Compound", configData.Zones.Compound, pos);
                        compound++;
                        continue;
                    }
                    if (gobject.name.Contains("compound") && compound == 1)
                    {
                        AddProfile("Compound1", configData.Zones.Compound1, pos);
                        compound++;
                        continue;
                    }
                    if (gobject.name.Contains("compound") && compound == 2)
                    {
                        AddProfile("Compound2", configData.Zones.Compound2, pos);
                        compound++;
                        continue;
                    }
                    if (gobject.name.Contains("sphere_tank"))
                    {
                        AddProfile("Dome", configData.Zones.Dome, pos);
                        continue;
                    }
                    if (gobject.name.Contains("gas_station_1") && gasstation == 0)
                    {
                        AddProfile("GasStation", configData.Zones.GasStation, pos);
                        gasstation++;
                        continue;
                    }
                    if (gobject.name.Contains("gas_station_1") && gasstation == 1)
                    {
                        AddProfile("GasStation1", configData.Zones.GasStation1, pos);
                        gasstation++;
                        continue;
                    }
                    if (gobject.name.Contains("harbor_1"))
                    {
                        AddProfile("Harbor1", configData.Zones.Harbor1, pos);
                        continue;
                    }

                    if (gobject.name.Contains("harbor_2"))
                    {
                        AddProfile("Harbor2", configData.Zones.Harbor2, pos);
                        continue;
                    }
                    if (gobject.name.Contains("junkyard"))
                    {
                        AddProfile("Junkyard", configData.Zones.Junkyard, pos);
                        continue;
                    }
                    if (gobject.name.Contains("launch_site"))
                    {
                        AddProfile("Launchsite", configData.Zones.Launchsite, pos);
                        continue;
                    }
                    if (gobject.name.Contains("lighthouse") && lighthouse == 0)
                    {
                        AddProfile("Lighthouse", configData.Zones.Lighthouse, pos);
                        lighthouse++;
                        continue;
                    }

                    if (gobject.name.Contains("lighthouse") && lighthouse == 1)
                    {
                        AddProfile("Lighthouse1", configData.Zones.Lighthouse1, pos);
                        lighthouse++;
                        continue;
                    }

                    if (gobject.name.Contains("lighthouse") && lighthouse == 2)
                    {
                        AddProfile("Lighthouse2", configData.Zones.Lighthouse2, pos);
                        lighthouse++;
                        continue;
                    }

                    if (gobject.name.Contains("military_tunnel_1"))
                    {
                        AddProfile("MilitaryTunnel", configData.Zones.MilitaryTunnel, pos);
                        continue;
                    }
                    if (gobject.name.Contains("powerplant_1"))
                    {
                        AddProfile("PowerPlant", configData.Zones.PowerPlant, pos);
                        continue;
                    }
                    if (gobject.name.Contains("mining_quarry_c"))
                    {
                        AddProfile("QuarryHQM", configData.Zones.QuarryHQM, pos);
                        continue;
                    }
                    if (gobject.name.Contains("mining_quarry_b"))
                    {
                        AddProfile("QuarryStone", configData.Zones.QuarryStone, pos);
                        continue;
                    }
                    if (gobject.name.Contains("mining_quarry_a"))
                    {
                        AddProfile("QuarrySulphur", configData.Zones.QuarrySulphur, pos);
                        continue;
                    }
                    if (gobject.name.Contains("radtown_small_3"))
                    {
                        AddProfile("Radtown", configData.Zones.Radtown, pos);
                        continue;
                    }
                    if (gobject.name.Contains("satellite_dish"))
                    {
                        AddProfile("Satellite", configData.Zones.Satellite, pos);
                        continue;
                    }
                    if (gobject.name.Contains("supermarket_1") && spermket == 0)
                    {
                        AddProfile("SuperMarket", configData.Zones.SuperMarket, pos);
                        spermket++;
                        continue;
                    }

                    if (gobject.name.Contains("supermarket_1") && spermket == 1)
                    {
                        AddProfile("SuperMarket1", configData.Zones.SuperMarket1, pos);
                        spermket++;
                        continue;
                    }
                    if (gobject.name.Contains("trainyard_1"))
                    {
                        AddProfile("Trainyard", configData.Zones.Trainyard, pos);
                        continue;
                    }
                    if (gobject.name.Contains("warehouse") && warehouse == 0)
                    {
                        AddProfile("Warehouse", configData.Zones.Warehouse, pos);
                        warehouse++;
                        continue;
                    }

                    if (gobject.name.Contains("warehouse") && warehouse == 1)
                    {
                        AddProfile("Warehouse1", configData.Zones.Warehouse1, pos);
                        warehouse++;
                        continue;
                    }

                    if (gobject.name.Contains("warehouse") && warehouse == 2)
                    {
                        AddProfile("Warehouse2", configData.Zones.Warehouse2, pos);
                        warehouse++;
                        continue;
                    }
                    if (gobject.name.Contains("water_treatment_plant_1"))
                    {
                        AddProfile("Watertreatment", configData.Zones.Watertreatment, pos);
                        continue;
                    }
                    if (gobject.name.Contains("compound") && compound > 2)
                        continue;
                    if (gobject.name.Contains("gas_station_1") && gasstation > 1)
                        continue;
                    if (gobject.name.Contains("lighthouse") && lighthouse > 2)
                        continue;
                    if (gobject.name.Contains("supermarket_1") && spermket > 1)
                        continue;
                    if (gobject.name.Contains("warehouse") && warehouse > 2)
                        continue;
                }
            }

            var drop = JsonConvert.SerializeObject(configData.Zones.AirDrop);
            MonumentSettings Airdrop = JsonConvert.DeserializeObject<MonumentSettings>(drop);
            TempRecord.MonumentProfiles.Add("AirDrop", Airdrop);

            foreach (var profile in storedData.CustomProfiles)
                TempRecord.MonumentProfiles.Add(profile.Key, profile.Value);

            foreach (var profile in TempRecord.MonumentProfiles)
            {
                if (profile.Value.Kit.Count > 0 && Kits == null)
                {
                    PrintWarning(lang.GetMessage("nokits", this), profile.Key);
                    continue;
                }

                if (profile.Value.Activate == true && profile.Value.Bots > 0 && !profile.Key.Contains("AirDrop"))
                {
                    timer.Repeat(2, profile.Value.Bots, () =>
                    {
                        if (TempRecord.MonumentProfiles.Contains(profile))
                            SpawnBots(profile.Key, profile.Value, null, null, new Vector3());
                    });
                }
            }
        }

        void AddProfile(string name, Zones.CustomSettings monument, Vector3 pos)                                                                                   //bring config data into live data
        {
            var toAdd = JsonConvert.SerializeObject(monument);
            MonumentSettings toAddDone = JsonConvert.DeserializeObject<MonumentSettings>(toAdd);

            TempRecord.MonumentProfiles.Add(name, toAddDone);
            TempRecord.MonumentProfiles[name].LocationX = pos.x;
            TempRecord.MonumentProfiles[name].LocationY = pos.y;
            TempRecord.MonumentProfiles[name].LocationZ = pos.z;
        }

        #region Commands
        [ConsoleCommand("bot.respawn")]
        void cmdBotRespawn()
        {
            Unload();
            Init();
            OnServerInitialized();
        }

        [ConsoleCommand("bot.count")]
        void cmdBotCount()
        {
            int total = 0;
            foreach (var pair in TempRecord.NPCPlayers)
            {
                total++;
            }
            if (total == 1)
                PrintWarning(lang.GetMessage("numberOfBot", this), total);
            else
                PrintWarning(lang.GetMessage("numberOfBots", this), total);
        }

        [ChatCommand("botspawn")]
        void botspawn(BasePlayer player, string command, string[] args)
        {
            if (HasPermission(player.UserIDString, permAllowed) || isAuth(player))
                if (args != null && args.Length == 1)
                {
                    if (args[0] == "list")
                    {
                        var outMsg = lang.GetMessage("ListTitle", this);

                        foreach (var profile in storedData.CustomProfiles)
                        {
                            outMsg += $"\n{profile.Key}";
                        }
                        PrintToChat(player, outMsg);
                    }
                    else
                        SendReply(player, "<color=orange>" + lang.GetMessage("Title", this) + "</color>" + lang.GetMessage("error", this));
                }
                else if (args != null && args.Length == 2)
                {
                    if (args[0] == "add")
                    {
                        var name = args[1];
                        if (storedData.CustomProfiles.ContainsKey(name))
                        {
                            SendReply(player, "<color=orange>" + lang.GetMessage("Title", this) + "</color>" + lang.GetMessage("alreadyexists", this), name);
                            return;
                        }
                        Vector3 pos = player.transform.position;

                        var customSettings = new MonumentSettings()
                        {
                            Activate = false,
                            BotName = "randomname",
                            LocationX = pos.x,
                            LocationY = pos.y,
                            LocationZ = pos.z,
                        };

                        storedData.CustomProfiles.Add(name, customSettings);
                        Interface.Oxide.DataFileSystem.WriteObject("BotSpawn", storedData);
                        SendReply(player, "<color=orange>" + lang.GetMessage("Title", this) + "</color>" + lang.GetMessage("customsaved", this), player.transform.position);
                    }

                    else if (args[0] == "move")
                    {
                        var name = args[1];
                        if (storedData.CustomProfiles.ContainsKey(name))
                        {
                            storedData.CustomProfiles[name].LocationX = player.transform.position.x;
                            storedData.CustomProfiles[name].LocationY = player.transform.position.y;
                            storedData.CustomProfiles[name].LocationZ = player.transform.position.z;
                            Interface.Oxide.DataFileSystem.WriteObject("BotSpawn", storedData);
                            SendReply(player, "<color=orange>" + lang.GetMessage("Title", this) + "</color>" + lang.GetMessage("custommoved", this), name);
                        }
                        else
                            SendReply(player, "<color=orange>" + lang.GetMessage("Title", this) + "</color>" + lang.GetMessage("noprofile", this));
                    }

                    else if (args[0] == "remove")
                    {
                        var name = args[1];
                        if (storedData.CustomProfiles.ContainsKey(name))
                        {
                            foreach (var bot in TempRecord.NPCPlayers)
                            {
                                if (bot == null)
                                    continue;

                                var bData = bot.GetComponent<botData>();
                                if (bData.monumentName == name)
                                    bot.Kill();
                            }
                            TempRecord.MonumentProfiles.Remove(name);
                            storedData.CustomProfiles.Remove(name);
                            Interface.Oxide.DataFileSystem.WriteObject("BotSpawn", storedData);
                            SendReply(player, "<color=orange>" + lang.GetMessage("Title", this) + "</color>" + lang.GetMessage("customremoved", this), name);
                        }
                        else
                            SendReply(player, "<color=orange>" + lang.GetMessage("Title", this) + "</color>" + lang.GetMessage("noprofile", this));
                    }
                    else
                        SendReply(player, "<color=orange>" + lang.GetMessage("Title", this) + "</color>" + lang.GetMessage("error", this));
                }
                else if (args != null && args.Length == 3)
                {
                    if (args[0] == "toplayer")
                    {
                        var name = args[1];
                        var profile = args[2].ToLower();
                        BasePlayer target = FindPlayerByName(name);
                        Vector3 location = (CalculateGroundPos(player.transform.position));
                        var found = false;
                        if (target == null)
                        {
                            SendReply(player, "<color=orange>" + lang.GetMessage("Title", this) + "</color>" + lang.GetMessage("namenotfound", this), name);
                            return;
                        }
                        foreach (var entry in storedData.CustomProfiles)
                        {
                            if (entry.Key.ToLower() == profile)
                            {
                                AttackPlayer(location, entry.Key, entry.Value, null);
                                SendReply(player, "<color=orange>" + lang.GetMessage("Title", this) + "</color>" + lang.GetMessage("deployed", this), entry.Key, target.displayName);
                                found = true;
                                return;
                            }
                        }
                        foreach (var entry in TempRecord.MonumentProfiles)
                        {
                            if (entry.Key.ToLower() == profile)
                            {
                                AttackPlayer(location, entry.Key, entry.Value, null);
                                SendReply(player, "<color=orange>" + lang.GetMessage("Title", this) + "</color>" + lang.GetMessage("deployed", this), entry.Key, target.displayName);
                                found = true;
                                return;
                            }
                        }
                        if (!found)
                        {
                            SendReply(player, "<color=orange>" + lang.GetMessage("Title", this) + "</color>" + lang.GetMessage("noprofile", this));
                            return;
                        }

                    }
                    else
                        SendReply(player, "<color=orange>" + lang.GetMessage("Title", this) + "</color>" + lang.GetMessage("error", this));
                }
                else
                    SendReply(player, "<color=orange>" + lang.GetMessage("Title", this) + "</color>" + lang.GetMessage("error", this));
        }
        #endregion

        #region Config
        private ConfigData configData;

        class TempRecord
        {
            public static List<NPCPlayerApex> NPCPlayers = new List<NPCPlayerApex>();
            public static Dictionary<string, MonumentSettings> MonumentProfiles = new Dictionary<string, MonumentSettings>();
            public static List<ulong> DeadNPCPlayerIds = new List<ulong>();
            public static Dictionary<ulong, kitData> kitList = new Dictionary<ulong, kitData>();
            public static Dictionary<ulong, string> kitRemoveList = new Dictionary<ulong, string>();
            public static List<Vector3> smokeGrenades = new List<Vector3>();
        }

        public class kitData
        {
            public string Kit;
            public bool Wipe_Belt;
            public bool Wipe_Clothing;
            public bool Allow_Rust_Loot;
        }

        public class botData : MonoBehaviour
        {
            public Vector3 spawnPoint;
            public int canChangeWeapon;
            public float enemyDistance;
            public int currentWeaponRange;
            public List<Item> AllProjectiles = new List<Item>();
            public List<Item> MeleeWeapons = new List<Item>();
            public List<Item> CloseRangeWeapons = new List<Item>();
            public List<Item> MediumRangeWeapons = new List<Item>();
            public List<Item> LongRangeWeapons = new List<Item>();
            public int accuracy;
            public float damage;
            public float range;
            public int health;
            public string monumentName;
            public bool dropweapon;
            public bool respawn;
            public int roamRange;
            public bool goingHome;
            public bool keepAttire;
            public bool peaceKeeper;
            public string group; //external hook identifier

            NPCPlayerApex botapex;
            void Start()
            {
                botapex = this.GetComponent<NPCPlayerApex>();
            }
            void Update()
            {
                if (botapex.AttackTarget is BasePlayer && !(botapex.AttackTarget is NPCPlayer))
                {
                    goingHome = false;
                }
                else
                {
                    if (Vector3.Distance(botapex.transform.position, spawnPoint) > roamRange)
                        goingHome = true;
                }
                if (Vector3.Distance(botapex.transform.position, spawnPoint) > (10) && goingHome == true && botapex.GetNavAgent.isOnNavMesh)
                    botapex.GetNavAgent.SetDestination(spawnPoint);
                else
                    goingHome = false;
            }
        }

        class Options
        {
            public bool NoBotsVBots = true;
            public bool Ignore_Animals = true;
            public bool APC_Safe = true;
            public bool Turret_Safe = true;
            public bool Animal_Safe = true;
            public bool Supply_Enabled = false;
            public bool Remove_BackPacks = true;
            public bool Ignore_HumanNPC = true;
            public bool Pve_Safe = true;
            public int Corpse_Duration = 60;
        }
        class Zones
        {
            public AirDropSettings AirDrop = new AirDropSettings { };
            public CustomSettings Airfield = new CustomSettings { };
            public CustomSettings Dome = new CustomSettings { };
            public CustomSettings Compound = new CustomSettings { };
            public CustomSettings Compound1 = new CustomSettings { };
            public CustomSettings Compound2 = new CustomSettings { };
            public CustomSettings GasStation = new CustomSettings { };
            public CustomSettings GasStation1 = new CustomSettings { };
            public CustomSettings Harbor1 = new CustomSettings { };
            public CustomSettings Harbor2 = new CustomSettings { };
            public CustomSettings Junkyard = new CustomSettings { };
            public CustomSettings Launchsite = new CustomSettings { };
            public CustomSettings Lighthouse = new CustomSettings { };
            public CustomSettings Lighthouse1 = new CustomSettings { };
            public CustomSettings Lighthouse2 = new CustomSettings { };
            public CustomSettings MilitaryTunnel = new CustomSettings { };
            public CustomSettings PowerPlant = new CustomSettings { };
            public CustomSettings QuarrySulphur = new CustomSettings { };
            public CustomSettings QuarryStone = new CustomSettings { };
            public CustomSettings QuarryHQM = new CustomSettings { };
            public CustomSettings SuperMarket = new CustomSettings { };
            public CustomSettings SuperMarket1 = new CustomSettings { };
            public CustomSettings Radtown = new CustomSettings { };
            public CustomSettings Satellite = new CustomSettings { };
            public CustomSettings Trainyard = new CustomSettings { };
            public CustomSettings Warehouse = new CustomSettings { };
            public CustomSettings Warehouse1 = new CustomSettings { };
            public CustomSettings Warehouse2 = new CustomSettings { };
            public CustomSettings Watertreatment = new CustomSettings { };

            public class AirDropSettings
            {
                public bool Activate = false;
                public bool Murderer = false;
                public int Bots = 5;
                public int BotHealth = 100;
                public int Radius = 100;
                public List<string> Kit = new List<string>();
                public string BotName = "randomname";
                public int Bot_Accuracy = 4;
                public float Bot_Damage = 0.4f;
                public bool Disable_Radio = true;
                public int Roam_Range = 40;
                public bool Peace_Keeper = true;
                public bool Weapon_Drop = true;
                public bool Keep_Default_Loadout = false;
                public bool Wipe_Belt = true;
                public bool Wipe_Clothing = true;
                public bool Allow_Rust_Loot = true;
                public int Suicide_Timer = 300;
            }

            public class CustomSettings
            {
                public bool Activate = false;
                public bool Murderer = false;
                public int Bots = 5;
                public int BotHealth = 100;
                public int Radius = 100;
                public List<string> Kit = new List<string>();
                public string BotName = "randomname";
                public int Bot_Accuracy = 4;
                public float Bot_Damage = 0.4f;
                public int Respawn_Timer = 60;
                public bool Disable_Radio = true;
                public int Roam_Range = 40;
                public bool Peace_Keeper = true;
                public bool Weapon_Drop = true;
                public bool Keep_Default_Loadout = false;
                public bool Wipe_Belt = true;
                public bool Wipe_Clothing = true;
                public bool Allow_Rust_Loot = true;
                public int Suicide_Timer = 300;
            }
        }

        class ConfigData
        {
            public Options Options = new Options();
            public Zones Zones = new Zones();
        }

        private void LoadConfigVariables()
        {
            configData = Config.ReadObject<ConfigData>();
            SaveConfig(configData);
        }
        protected override void LoadDefaultConfig()
        {
            Puts("Creating new config file.");
            var config = new ConfigData();
            SaveConfig(config);
        }

        void SaveConfig(ConfigData config)
        {
            Config.WriteObject(config, true);
        }
        #endregion
        #region messages
        Dictionary<string, string> messages = new Dictionary<string, string>()
        {
            {"Title", "BotSpawn : " },
            {"error", "/botspawn commands are - list - add - remove - move - toplayer" },
            {"customsaved", "Custom Location Saved @ {0}" },
            {"custommoved", "Custom Location {0} has been moved to your current position." },
            {"alreadyexists", "Custom Location already exists with the name {0}." },
            {"customremoved", "Custom Location {0} Removed." },
            {"deployed", "'{0}' bots deployed to {1}." },
            {"ListTitle", "Custom Locations" },
            {"noprofile", "There is no profile by that name in config or data BotSpawn.json files." },
            {"namenotfound", "Player '{0}' was not found" },
            {"nokits", "Kits is not installed but you have declared custom kits at {0}." },
            {"noWeapon", "A bot at {0} has no weapon. Check your kits." },
            {"numberOfBot", "There is {0} spawned bot alive." },
            {"numberOfBots", "There are {0} spawned bots alive." },

        };
        #endregion
    }
}