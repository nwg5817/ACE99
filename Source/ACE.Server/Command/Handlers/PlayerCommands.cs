using System;
using System.Collections.Generic;

using log4net;

using ACE.Common;
using ACE.Database;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;


namespace ACE.Server.Command.Handlers
{
    public static class PlayerCommands
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        // pop
        [CommandHandler("pop", AccessLevel.Player, CommandHandlerFlag.None, 0,
            "Show current world population",
            "")]
        public static void HandlePop(Session session, params string[] parameters)
        {
            session.Network.EnqueueSend(new GameMessageSystemChat($"Current world population: {PlayerManager.GetOnlineCount().ToString()}\n", ChatMessageType.Broadcast));
        }

        // quest info (uses GDLe formatting to match plugin expectations)
        [CommandHandler("myquests", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, "Shows your quest log")]
        public static void HandleQuests(Session session, params string[] parameters)
        {
            if (!PropertyManager.GetBool("quest_info_enabled").Item)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat("The command \"myquests\" is not currently enabled on this server.", ChatMessageType.Broadcast));
                return;
            }

            if (session.Player.QuestManager.Quests.Count == 0)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat("Quest list is empty.", ChatMessageType.Broadcast));
                return;
            }

            foreach (var playerQuest in session.Player.QuestManager.Quests)
            {
                var text = "";
                var questName = QuestManager.GetQuestName(playerQuest.QuestName);
                var quest = DatabaseManager.World.GetCachedQuest(questName);
                if (quest == null)
                {
                    Console.WriteLine($"Couldn't find quest {playerQuest.QuestName}");
                    continue;
                }

                var minDelta = quest.MinDelta;
                if (QuestManager.CanScaleQuestMinDelta(quest))
                    minDelta = (uint)(quest.MinDelta * PropertyManager.GetDouble("quest_mindelta_rate").Item);

                text += $"{playerQuest.QuestName.ToLower()} - {playerQuest.NumTimesCompleted} solves ({playerQuest.LastTimeCompleted})";
                text += $"\"{quest.Message}\" {quest.MaxSolves} {minDelta}";

                session.Network.EnqueueSend(new GameMessageSystemChat(text, ChatMessageType.Broadcast));
            }
        }

        /// <summary>
        /// For characters/accounts who currently own multiple houses, used to select which house they want to keep
        /// </summary>
        [CommandHandler("house-select", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, 1, "For characters/accounts who currently own multiple houses, used to select which house they want to keep")]
        public static void HandleHouseSelect(Session session, params string[] parameters)
        {
            HandleHouseSelect(session, false, parameters);
        }

        public static void HandleHouseSelect(Session session, bool confirmed, params string[] parameters)
        {
            if (!int.TryParse(parameters[0], out var houseIdx))
                return;

            // ensure current multihouse owner
            if (!session.Player.IsMultiHouseOwner(false))
            {
                log.Warn($"{session.Player.Name} tried to /house-select {houseIdx}, but they are not currently a multi-house owner!");
                return;
            }

            // get house info for this index
            var multihouses = session.Player.GetMultiHouses();

            if (houseIdx < 1 || houseIdx > multihouses.Count)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat($"Please enter a number between 1 and {multihouses.Count}.", ChatMessageType.Broadcast));
                return;
            }

            var keepHouse = multihouses[houseIdx - 1];

            // show confirmation popup
            if (!confirmed)
            {
                var houseType = $"{keepHouse.HouseType}".ToLower();
                var loc = HouseManager.GetCoords(keepHouse.SlumLord.Location);

                var msg = $"Are you sure you want to keep the {houseType} at\n{loc}?";
                session.Player.ConfirmationManager.EnqueueSend(new Confirmation_Custom(session.Player.Guid, () => HandleHouseSelect(session, true, parameters)), msg);
                return;
            }

            // house to keep confirmed, abandon the other houses
            var abandonHouses = new List<House>(multihouses);
            abandonHouses.RemoveAt(houseIdx - 1);

            foreach (var abandonHouse in abandonHouses)
            {
                var house = session.Player.GetHouse(abandonHouse.Guid.Full);

                HouseManager.HandleEviction(house, house.HouseOwner ?? 0, true);
            }

            // set player properties for house to keep
            var player = PlayerManager.FindByGuid(keepHouse.HouseOwner ?? 0, out bool isOnline);
            if (player == null)
            {
                log.Error($"{session.Player.Name}.HandleHouseSelect({houseIdx}) - couldn't find HouseOwner {keepHouse.HouseOwner} for {keepHouse.Name} ({keepHouse.Guid})");
                return;
            }

            player.HouseId = keepHouse.HouseId;
            player.HouseInstance = keepHouse.Guid.Full;

            player.SaveBiotaToDatabase();

            // update house panel for current player
            var actionChain = new ActionChain();
            actionChain.AddDelaySeconds(3.0f);  // wait for slumlord inventory biotas above to save
            actionChain.AddAction(session.Player, session.Player.HandleActionQueryHouse);
            actionChain.EnqueueChain();

            Console.WriteLine("OK");
        }

        [CommandHandler("debugcast", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, "Shows debug information about the current magic casting state")]
        public static void HandleDebugCast(Session session, params string[] parameters)
        {
            var physicsObj = session.Player.PhysicsObj;

            var pendingActions = physicsObj.MovementManager.MoveToManager.PendingActions;
            var currAnim = physicsObj.PartArray.Sequence.CurrAnim;

            session.Network.EnqueueSend(new GameMessageSystemChat(session.Player.MagicState.ToString(), ChatMessageType.Broadcast));
            session.Network.EnqueueSend(new GameMessageSystemChat($"IsMovingOrAnimating: {physicsObj.IsMovingOrAnimating}", ChatMessageType.Broadcast));
            session.Network.EnqueueSend(new GameMessageSystemChat($"PendingActions: {pendingActions.Count}", ChatMessageType.Broadcast));
            session.Network.EnqueueSend(new GameMessageSystemChat($"CurrAnim: {currAnim?.Value.Anim.ID:X8}", ChatMessageType.Broadcast));
        }

        [CommandHandler("fixcast", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, "Fixes magic casting if locked up for an extended time")]
        public static void HandleFixCast(Session session, params string[] parameters)
        {
            var magicState = session.Player.MagicState;

            if (magicState.IsCasting && DateTime.UtcNow - magicState.StartTime > TimeSpan.FromSeconds(5))
            {
                session.Network.EnqueueSend(new GameEventCommunicationTransientString(session, "Fixed casting state"));
                session.Player.SendUseDoneEvent();
                magicState.OnCastDone();
            }
        }

        [CommandHandler("castmeter", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, "Shows the fast casting efficiency meter")]
        public static void HandleCastMeter(Session session, params string[] parameters)
        {
            if (parameters.Length == 0)
            {
                session.Player.MagicState.CastMeter = !session.Player.MagicState.CastMeter;
            }
            else
            {
                if (parameters[0].Equals("on", StringComparison.OrdinalIgnoreCase))
                    session.Player.MagicState.CastMeter = true;
                else
                    session.Player.MagicState.CastMeter = false;
            }
            session.Network.EnqueueSend(new GameMessageSystemChat($"Cast efficiency meter {(session.Player.MagicState.CastMeter ? "enabled" : "disabled")}", ChatMessageType.Broadcast));
        }

        private static List<string> configList = new List<string>()
        {
            "Common settings:\nConfirmVolatileRareUse, MainPackPreferred, SalvageMultiple, SideBySideVitals, UseCraftSuccessDialog",
            "Interaction settings:\nAcceptLootPermits, AllowGive, AppearOffline, AutoAcceptFellowRequest, DragItemOnPlayerOpensSecureTrade, FellowshipShareLoot, FellowshipShareXP, IgnoreAllegianceRequests, IgnoreFellowshipRequests, IgnoreTradeRequests, UseDeception",
            "UI settings:\nCoordinatesOnRadar, DisableDistanceFog, DisableHouseRestrictionEffects, DisableMostWeatherEffects, FilterLanguage, LockUI, PersistentAtDay, ShowCloak, ShowHelm, ShowTooltips, SpellDuration, TimeStamp, ToggleRun, UseMouseTurning",
            "Chat settings:\nHearAllegianceChat, HearGeneralChat, HearLFGChat, HearRoleplayChat, HearSocietyChat, HearTradeChat, HearPKDeaths, StayInChatMode",
            "Combat settings:\nAdvancedCombatUI, AutoRepeatAttack, AutoTarget, LeadMissileTargets, UseChargeAttack, UseFastMissiles, ViewCombatTarget, VividTargetingIndicator",
            "Character display settings:\nDisplayAge, DisplayAllegianceLogonNotifications, DisplayChessRank, DisplayDateOfBirth, DisplayFishingSkill, DisplayNumberCharacterTitles, DisplayNumberDeaths"
        };

        /// <summary>
        /// Mapping of GDLE -> ACE CharacterOptions
        /// </summary>
        private static Dictionary<string, string> translateOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Common
            { "ConfirmVolatileRareUse", "ConfirmUseOfRareGems" },
            { "MainPackPreferred", "UseMainPackAsDefaultForPickingUpItems" },
            { "SalvageMultiple", "SalvageMultipleMaterialsAtOnce" },
            { "SideBySideVitals", "SideBySideVitals" },
            { "UseCraftSuccessDialog", "UseCraftingChanceOfSuccessDialog" },

            // Interaction
            { "AcceptLootPermits", "AcceptCorpseLootingPermissions" },
            { "AllowGive", "LetOtherPlayersGiveYouItems" },
            { "AppearOffline", "AppearOffline" },
            { "AutoAcceptFellowRequest", "AutomaticallyAcceptFellowshipRequests" },
            { "DragItemOnPlayerOpensSecureTrade", "DragItemToPlayerOpensTrade" },
            { "FellowshipShareLoot", "ShareFellowshipLoot" },
            { "FellowshipShareXP", "ShareFellowshipExpAndLuminance" },
            { "IgnoreAllegianceRequests", "IgnoreAllegianceRequests" },
            { "IgnoreFellowshipRequests", "IgnoreFellowshipRequests" },
            { "IgnoreTradeRequests", "IgnoreAllTradeRequests" },
            { "UseDeception", "AttemptToDeceiveOtherPlayers" },

            // UI
            { "CoordinatesOnRadar", "ShowCoordinatesByTheRadar" },
            { "DisableDistanceFog", "DisableDistanceFog" },
            { "DisableHouseRestrictionEffects", "DisableHouseRestrictionEffects" },
            { "DisableMostWeatherEffects", "DisableMostWeatherEffects" },
            { "FilterLanguage", "FilterLanguage" },
            { "LockUI", "LockUI" },
            { "PersistentAtDay", "AlwaysDaylightOutdoors" },
            { "ShowCloak", "ShowYourCloak" },
            { "ShowHelm", "ShowYourHelmOrHeadGear" },
            { "ShowTooltips", "Display3dTooltips" },
            { "SpellDuration", "DisplaySpellDurations" },
            { "TimeStamp", "DisplayTimestamps" },
            { "ToggleRun", "RunAsDefaultMovement" },
            { "UseMouseTurning", "UseMouseTurning" },

            // Chat
            { "HearAllegianceChat", "ListenToAllegianceChat" },
            { "HearGeneralChat", "ListenToGeneralChat" },
            { "HearLFGChat", "ListenToLFGChat" },
            { "HearRoleplayChat", "ListentoRoleplayChat" },
            { "HearSocietyChat", "ListenToSocietyChat" },
            { "HearTradeChat", "ListenToTradeChat" },
            { "HearPKDeaths", "ListenToPKDeathMessages" },
            { "StayInChatMode", "StayInChatModeAfterSendingMessage" },

            // Combat
            { "AdvancedCombatUI", "AdvancedCombatInterface" },
            { "AutoRepeatAttack", "AutoRepeatAttacks" },
            { "AutoTarget", "AutoTarget" },
            { "LeadMissileTargets", "LeadMissileTargets" },
            { "UseChargeAttack", "UseChargeAttack" },
            { "UseFastMissiles", "UseFastMissiles" },
            { "ViewCombatTarget", "KeepCombatTargetsInView" },
            { "VividTargetingIndicator", "VividTargetingIndicator" },

            // Character Display
            { "DisplayAge", "AllowOthersToSeeYourAge" },
            { "DisplayAllegianceLogonNotifications", "ShowAllegianceLogons" },
            { "DisplayChessRank", "AllowOthersToSeeYourChessRank" },
            { "DisplayDateOfBirth", "AllowOthersToSeeYourDateOfBirth" },
            { "DisplayFishingSkill", "AllowOthersToSeeYourFishingSkill" },
            { "DisplayNumberCharacterTitles", "AllowOthersToSeeYourNumberOfTitles" },
            { "DisplayNumberDeaths", "AllowOthersToSeeYourNumberOfDeaths" },
        };

        /// <summary>
        /// Manually sets a character option on the server. Use /config list to see a list of settings.
        /// </summary>
        [CommandHandler("config", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, 1, "Manually sets a character option on the server.\nUse /config list to see a list of settings.", "<setting> <on/off>")]
        public static void HandleConfig(Session session, params string[] parameters)
        {
            if (!PropertyManager.GetBool("player_config_command").Item)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat("The command \"config\" is not currently enabled on this server.", ChatMessageType.Broadcast));
                return;
            }

            // /config list - show character options
            if (parameters[0].Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var line in configList)
                    session.Network.EnqueueSend(new GameMessageSystemChat(line, ChatMessageType.Broadcast));

                return;
            }

            // translate GDLE CharacterOptions for existing plugins
            if (!translateOptions.TryGetValue(parameters[0], out var param) || !Enum.TryParse(param, out CharacterOption characterOption))
            {
                session.Network.EnqueueSend(new GameMessageSystemChat($"Unknown character option: {parameters[0]}", ChatMessageType.Broadcast));
                return;
            }

            var option = session.Player.GetCharacterOption(characterOption);

            // modes of operation:
            // on / off / toggle

            // - if none specified, default to toggle
            var mode = "toggle";

            if (parameters.Length > 1)
            {
                if (parameters[1].Equals("on", StringComparison.OrdinalIgnoreCase))
                    mode = "on";
                else if (parameters[1].Equals("off", StringComparison.OrdinalIgnoreCase))
                    mode = "off";
            }

            // set character option
            if (mode.Equals("on"))
                option = true;
            else if (mode.Equals("off"))
                option = false;
            else
                option = !option;

            session.Player.SetCharacterOption(characterOption, option);

            session.Network.EnqueueSend(new GameMessageSystemChat($"Character option {parameters[0]} is now {(option ? "on" : "off")}.", ChatMessageType.Broadcast));

            // update client
            session.Network.EnqueueSend(new GameEventPlayerDescription(session));
        }

        /// <summary>
        /// Force resend of all visible objects known to this player. Can fix rare cases of invisible object bugs.
        /// Can only be used once every 5 mins max.
        /// </summary>
        [CommandHandler("objsend", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, "Force resend of all visible objects known to this player. Can fix rare cases of invisible object bugs. Can only be used once every 5 mins max.")]
        public static void HandleObjSend(Session session, params string[] parameters)
        {
            // a good repro spot for this is the first room after the door in facility hub
            // in the portal drop / staircase room, the VisibleCells do not have the room after the door
            // however, the room after the door *does* have the portal drop / staircase room in its VisibleCells (the inverse relationship is imbalanced)
            // not sure how to fix this atm, seems like it triggers a client bug..

            if (DateTime.UtcNow - session.Player.PrevObjSend < TimeSpan.FromMinutes(5))
            {
                session.Player.SendTransientError("You have used this command too recently!");
                return;
            }

            var knownObjs = session.Player.GetKnownObjects();

            foreach (var knownObj in knownObjs)
            {
                session.Player.RemoveTrackedObject(knownObj, false);
                session.Player.TrackObject(knownObj);
            }
            session.Player.PrevObjSend = DateTime.UtcNow;
        }

        // show player ace server versions
        [CommandHandler("aceversion", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, "Shows this server's version data")]
        public static void HandleACEversion(Session session, params string[] parameters)
        {
            if (!PropertyManager.GetBool("version_info_enabled").Item)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat("The command \"aceversion\" is not currently enabled on this server.", ChatMessageType.Broadcast));
                return;
            }

            var msg = ServerBuildInfo.GetVersionInfo();

            session.Network.EnqueueSend(new GameMessageSystemChat(msg, ChatMessageType.WorldBroadcast));
        }

        // reportbug < code | content > < description >
        [CommandHandler("reportbug", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, 2,
            "Generate a Bug Report",
            "<category> <description>\n" +
            "This command generates a URL for you to copy and paste into your web browser to submit for review by server operators and developers.\n" +
            "Category can be the following:\n" +
            "Creature\n" +
            "NPC\n" +
            "Item\n" +
            "Quest\n" +
            "Recipe\n" +
            "Landblock\n" +
            "Mechanic\n" +
            "Code\n" +
            "Other\n" +
            "For the first three options, the bug report will include identifiers for what you currently have selected/targeted.\n" +
            "After category, please include a brief description of the issue, which you can further detail in the report on the website.\n" +
            "Examples:\n" +
            "/reportbug creature Drudge Prowler is over powered\n" +
            "/reportbug npc Ulgrim doesn't know what to do with Sake\n" +
            "/reportbug quest I can't enter the portal to the Lost City of Frore\n" +
            "/reportbug recipe I cannot combine Bundle of Arrowheads with Bundle of Arrowshafts\n" +
            "/reportbug code I was killed by a Non-Player Killer\n"
            )]
        public static void HandleReportbug(Session session, params string[] parameters)
        {
            if (!PropertyManager.GetBool("reportbug_enabled").Item)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat("The command \"reportbug\" is not currently enabled on this server.", ChatMessageType.Broadcast));
                return;
            }

            var category = parameters[0];
            var description = "";

            for (var i = 1; i < parameters.Length; i++)
                description += parameters[i] + " ";

            description.Trim();

            switch (category.ToLower())
            {
                case "creature":
                case "npc":
                case "quest":
                case "item":
                case "recipe":
                case "landblock":
                case "mechanic":
                case "code":
                case "other":
                    break;
                default:
                    category = "Other";
                    break;
            }

            var sn = ConfigManager.Config.Server.WorldName;
            var c = session.Player.Name;

            var st = "ACE";

            //var versions = ServerBuildInfo.GetVersionInfo();
            var databaseVersion = DatabaseManager.World.GetVersion();
            var sv = ServerBuildInfo.FullVersion;
            var pv = databaseVersion.PatchVersion;

            //var ct = PropertyManager.GetString("reportbug_content_type").Item;
            var cg = category.ToLower();

            var w = "";
            var g = "";

            if (cg == "creature" || cg == "npc" || cg == "item" || cg == "item")
            {
                var objectId = new ObjectGuid();
                if (session.Player.HealthQueryTarget.HasValue || session.Player.ManaQueryTarget.HasValue || session.Player.CurrentAppraisalTarget.HasValue)
                {
                    if (session.Player.HealthQueryTarget.HasValue)
                        objectId = new ObjectGuid((uint)session.Player.HealthQueryTarget);
                    else if (session.Player.ManaQueryTarget.HasValue)
                        objectId = new ObjectGuid((uint)session.Player.ManaQueryTarget);
                    else
                        objectId = new ObjectGuid((uint)session.Player.CurrentAppraisalTarget);

                    //var wo = session.Player.CurrentLandblock?.GetObject(objectId);

                    var wo = session.Player.FindObject(objectId.Full, Player.SearchLocations.Everywhere);

                    if (wo != null)
                    {
                        w = $"{wo.WeenieClassId}";
                        g = $"0x{wo.Guid:X8}";
                    }
                }
            }

            var l = session.Player.Location.ToLOCString();

            var issue = description;

            var urlbase = $"https://www.accpp.net/bug?";

            var url = urlbase;
            if (sn.Length > 0)
                url += $"sn={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(sn))}";
            if (c.Length > 0)
                url += $"&c={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(c))}";
            if (st.Length > 0)
                url += $"&st={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(st))}";
            if (sv.Length > 0)
                url += $"&sv={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(sv))}";
            if (pv.Length > 0)
                url += $"&pv={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(pv))}";
            //if (ct.Length > 0)
            //    url += $"&ct={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(ct))}";
            if (cg.Length > 0)
            {
                if (cg == "npc")
                    cg = cg.ToUpper();
                else
                    cg = char.ToUpper(cg[0]) + cg.Substring(1);
                url += $"&cg={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(cg))}";
            }
            if (w.Length > 0)
                url += $"&w={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(w))}";
            if (g.Length > 0)
                url += $"&g={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(g))}";
            if (l.Length > 0)
                url += $"&l={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(l))}";
            if (issue.Length > 0)
                url += $"&i={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(issue))}";

            var msg = "\n\n\n\n";
            msg += "Bug Report - Copy and Paste the following URL into your browser to submit a bug report\n";
            msg += "-=-\n";
            msg += $"{url}\n";
            msg += "-=-\n";
            msg += "\n\n\n\n";

            session.Network.EnqueueSend(new GameMessageSystemChat(msg, ChatMessageType.AdminTell));
        }

        static List<ActivityRecommendation> Recommendations = new List<ActivityRecommendation>()
        {
            new ActivityRecommendation(1, 10, new HashSet<string>{ "HoltburgAfrinCorn1204", "HoltburgAfrinRye1204", "HoltburgAfrinWheat1204","HoltburgAfrinDrudge1204"}, "XP: Recover Stolen Supplies from the Drudge Hideout near Holtburg at 41.3N, 33.3E and deliver them to Alfrin in Holtburg."),
            new ActivityRecommendation(1, 10, new HashSet<string>{ "AxeBrogordQuest", "HoltburgNoteBrogord1204"}, "XP: Recover Brogord's Axe and a Letter to Ryndya from the Cave of Alabree near Holtburg at 41.8N, 32.1E and deliver them to Flinrala Ryndmad in Holtburg."),
            new ActivityRecommendation(1, 10, new HashSet<string>{ "HoltburgRedoubtCandlestick1204", "HoltburgRedoubtBowl1204", "AntiquePlatterQuest", "HoltburgRedoubtLamp1204","HoltburgRedoubtHandbell1204","HardunnaBandQuest","HoltburgRedoubtMug1204","HoltburgRedoubtGoblet1204"}, "XP: Recover Heirlooms from the Holtburg Redoubt near Holtburg at 40.4N, 34.4E and return them to Worcer in Holtburg."),
            new ActivityRecommendation(1, 10, new HashSet<string>{ "RITHWICMINDORLALETTER"}, "XP: Visit Rithwic East and help Mindorla with an errand."),
            new ActivityRecommendation(1, 10, new HashSet<string>{ "RITHWICCELCYNDRING"}, "XP: Recover a letter from the Old Warehouse near Rithwic at 7.6N, 58.4E and deliver it to Celcynd the Dour in Rithwic."),
            new ActivityRecommendation(14, 25, "OlthoiHunting1", "XP: Kill Olthois in the Abandoned Tumerok Site near Redspire at 42.0N, 82.2W and bring a Harvester Pincer to Behdo Yii in Redspire."),
            new ActivityRecommendation(18, 30, "OlthoiHunting2", "XP: Kill Olthois in the Dark Lair near Greenspire at 43.8N, 68.4W and bring a Gardener Pincer to Behdo Yii in Redspire."),
            new ActivityRecommendation(30, 999, "OlthoiHunting3", "XP: Kill Olthois in the Crumbling Empyrean Mansion near Greenspire at 46.8N, 67.8W and bring a Worker Pincer to Behdo Yii in Redspire."),
            new ActivityRecommendation(35, 999, "OlthoiHunting4", "XP: Kill Olthois in An Olthoi Soldier Nest in the Marescent Plateau at 45.2N, 76.3W and bring a Soldier Pincer to Behdo Yii in Redspire."),
            new ActivityRecommendation(40, 999, "OlthoiHunting5", "XP: Kill Olthois in the Ancient Empyrean Grotto in the Marescent Plateau at 52.6N, 73.1W and bring a Legionary Pincer to Behdo Yii in Redspire."),
            new ActivityRecommendation(50, 999, "OlthoiHunting6", "XP: Kill Olthois in the Lair of the Eviscerators in the Marescent Plateau at 53.7N, 76.6W and bring an Eviscerator Pincer to Behdo Yii in Redspire."),
            new ActivityRecommendation(70, 999, "OlthoiHunting7", "XP: Kill Olthois in the Olthoi Warrior Nest in the Marescent Plateau at 46.9N, 81.2W and bring a Warrior Pincer to Behdo Yii in Redspire."),
            new ActivityRecommendation(80, 999, "OlthoiHunting8", "XP: Kill Olthois in the Mutilator Tunnels in the Marescent Plateau at 52.8N, 78.1W and bring a Mutilator Pincer to Behdo Yii in Redspire."),
            new ActivityRecommendation(14, 25, "TuskFemalePickup", "XP: Kill Tuskers in the Tusker Burrow in Alphus Lassel at 2.0N, 97.9E and bring a Female Tusker Tusk to Brighteyes in Oolutanga's Refuge. You can get to Alphus lassel via the Tusker Temples: 10.5S 65.6E for levels 1-20, 59.8N 28.4E for levels 20-40 and 0.7N 68.1W for levels 40+."),
            new ActivityRecommendation(18, 30, "TuskMalePickup", "XP: Kill Tuskers in the Tusker Lodge in Alphus Lassel at 0.1N, 98.1E and bring a Male Tusker Tusk to Brighteyes in Oolutanga's Refuge. You can get to Alphus lassel via the Tusker Temples: 10.5S 65.6E for levels 1-20, 59.8N 28.4E for levels 20-40 and 0.7N 68.1W for levels 40+."),
            new ActivityRecommendation(25, 999, "TuskCrimsonbackPickup", "XP: Kill Tuskers in the Tusker Cave in Alphus Lassel at 0.4N, 97.4E and bring a Tusker Crimsonback Tusk to Brighteyes in Oolutanga's Refuge. You can get to Alphus lassel via the Tusker Temples: 10.5S 65.6E for levels 1-20, 59.8N 28.4E for levels 20-40 and 0.7N 68.1W for levels 40+."),
            new ActivityRecommendation(30, 999, "TuskGoldenbackPickup", "XP: Kill Tuskers in the Tusker Cavern in Alphus Lassel at 1.0N, 96.9E and bring a Goldenback Tusker Tusk to Brighteyes in Oolutanga's Refuge. You can get to Alphus lassel via the Tusker Temples: 10.5S 65.6E for levels 1-20, 59.8N 28.4E for levels 20-40 and 0.7N 68.1W for levels 40+."),
            new ActivityRecommendation(35, 999, "TuskRedeemerPickup", "XP: Kill Tuskers in the Tusker Abode in Alphus Lassel at 3.2S, 94.9E and bring a Tusker Redeemer Tusk to Brighteyes in Oolutanga's Refuge. You can get to Alphus lassel via the Tusker Temples: 10.5S 65.6E for levels 1-20, 59.8N 28.4E for levels 20-40 and 0.7N 68.1W for levels 40+."),
            new ActivityRecommendation(40, 999, "TuskLiberatorPickup", "XP: Kill Tuskers in the Tusker Habitat in Alphus Lassel at 0.5S, 95.9E and bring a Tusker Liberator Tusk to Brighteyes in Oolutanga's Refuge. You can get to Alphus lassel via the Tusker Temples: 10.5S 65.6E for levels 1-20, 59.8N 28.4E for levels 20-40 and 0.7N 68.1W for levels 40+."),
            new ActivityRecommendation(45, 999, "TuskSlavePickup", "XP: Kill Tuskers in the Tusker Quarters in Alphus Lassel at 2.3S, 95.6E and bring a Tusker Slave Tusk to Brighteyes in Oolutanga's Refuge. You can get to Alphus lassel via the Tusker Temples: 10.5S 65.6E for levels 1-20, 59.8N 28.4E for levels 20-40 and 0.7N 68.1W for levels 40+."),
            new ActivityRecommendation(50, 999, "TuskGuardPickup", "XP: Kill Tuskers in the Tusker Barracks in Alphus Lassel at 0.3S, 90.8E and bring a Tusker Guard Tusk to Brighteyes in Oolutanga's Refuge. You can get to Alphus lassel via the Tusker Temples: 10.5S 65.6E for levels 1-20, 59.8N 28.4E for levels 20-40 and 0.7N 68.1W for levels 40+."),
            new ActivityRecommendation(55, 999, "TuskSilverPickup", "XP: Kill Tuskers in the Tusker Pits in Alphus Lassel at 1.3N, 91.8E and bring a Silver Tusker Tusk to Brighteyes in Oolutanga's Refuge. You can get to Alphus lassel via the Tusker Temples: 10.5S 65.6E for levels 1-20, 59.8N 28.4E for levels 20-40 and 0.7N 68.1W for levels 40+."),
            new ActivityRecommendation(60, 999, "TuskArmoredPickup", "XP: Kill Tuskers in the Tusker Armory in Alphus Lassel at 0.0N, 89.4E and bring a Armored Tusker Tusk to Brighteyes in Oolutanga's Refuge. You can get to Alphus lassel via the Tusker Temples: 10.5S 65.6E for levels 1-20, 59.8N 28.4E for levels 20-40 and 0.7N 68.1W for levels 40+."),
            new ActivityRecommendation(65, 999, "TuskRampagerPickup", "XP: Kill Tuskers in the Tusker Holding in Alphus Lassel at 3.5S, 85.3E and bring a Rampager Tusk to Brighteyes in Oolutanga's Refuge. You can get to Alphus lassel via the Tusker Temples: 10.5S 65.6E for levels 1-20, 59.8N 28.4E for levels 20-40 and 0.7N 68.1W for levels 40+."),
            new ActivityRecommendation(70, 999, "TuskPlatedPickup", "XP: Kill Tuskers in the Tusker Tunnels in Alphus Lassel at 0.4N, 86.4E and bring a Plated Tusker Tusk to Brighteyes in Oolutanga's Refuge. You can get to Alphus lassel via the Tusker Temples: 10.5S 65.6E for levels 1-20, 59.8N 28.4E for levels 20-40 and 0.7N 68.1W for levels 40+."),
            new ActivityRecommendation(80, 999, "TuskAssailerPickup", "XP: Kill Tuskers in the Tusker Honeycombs in Alphus Lassel at 1.3S, 86.9E and bring a Assailer Tusk to Brighteyes in Oolutanga's Refuge. You can get to Alphus lassel via the Tusker Temples: 10.5S 65.6E for levels 1-20, 59.8N 28.4E for levels 20-40 and 0.7N 68.1W for levels 40+."),
            new ActivityRecommendation(100, 999, "TuskDevastatorPickup", "XP: Kill Tuskers in the Tusker Lacuna in Alphus Lassel at 9.9S, 90.7E and bring a Devastator Tusk to Brighteyes in Oolutanga's Refuge. You can get to Alphus lassel via the Tusker Temples: 10.5S 65.6E for levels 1-20, 59.8N 28.4E for levels 20-40 and 0.7N 68.1W for levels 40+."),
            new ActivityRecommendation(1, 20, "Equipment: Collect Red and Gold Letters, gather stamps and trade them in for Exploration Society Equipment. For more information talk to Exploration Society Agents, usually located in taverns wearing green clothes."),
            new ActivityRecommendation(10, 80, new HashSet<Skill>{Skill.Axe, Skill.Dagger, Skill.Mace, Skill.Spear, Skill.Staff, Skill.Sword, Skill.UnarmedCombat}, "Equipment: Hunt Golems for Motes to craft Atlan and Isparian Weapons at the Crater Lake Village."),
            new ActivityRecommendation(10, 80, new HashSet<Skill>{Skill.Bow, Skill.Crossbow, Skill.ThrownWeapon, Skill.WarMagic, Skill.LifeMagic}, "Equipment: Hunt Golems for Motes to craft Isparian Weapons at the Crater Lake Village."),
            new ActivityRecommendation(15, 126, "Equipment: Hunt Shadows and Crystals for shards to craft Shadow Armor near Eastham at 18.5N, 62.8E, near Al-Jalima at 7.1N, 3.0E or near Kara at 82.9S, 46.0E."),
            new ActivityRecommendation(15, 126, Skill.Lockpick, "XP: Hunt Undead for Mnemosynes. Unlock them with keys made using Lockpicking from Golem Hearts, and turn them in at the Mnemosyne Collection Site near Samsur at 2.5S, 16.4E."),
            new ActivityRecommendation(15, 126, Skill.Lockpick, "Equipment: Hunt Undead for Mnemosynes. Unlock them with keys made using Lockpicking from Golem Hearts, and turn them in at the Undead Hunter's tent near Tufa at 13.3S, 5.1E."),
            new ActivityRecommendation(15, 25, "Equipment: Explore the Glenden Wood Dungeon near Glenden Wood at 29.9N, 26.4E for the Platemail Hauberk of the Ogre."),
            new ActivityRecommendation(18, 25, "Equipment: Explore the Halls of the Helm near Zaikhal at 15.8N, 2.1E for the Fiery Shield and Superior Helmet."),
            new ActivityRecommendation(15, 25, "GivenTibriSpear", "XP/Equipment: Explore a Cave near Cragstone at 23.2N, 44.0E for Tibri's Fire Spear and deliver it to Tibri also in the cave or keep it."),
            new ActivityRecommendation(15, 25, "Equipment: Explore Trothyr's Rest near Rithwic at 10.3N, 54.9E for Trothyr's War Hammer and Trothyr's Shield. Talk to Ringoshu the Apple Seller at 13.6N, 50.7E for more information."),
            new ActivityRecommendation(10, 20, "Equipment: Explore the Green Mire Grave near Shoushi at 27.8S, 71.6E for the Green Mire Warrior's Yoroi Cuirass and the Green Mire Yari."),
            new ActivityRecommendation(5, 20, Skill.Lockpick, "Equipment: Explore Eastham Sewer near Eastham at 18.7N, 63.4E for the Metal Round Shield. Bring lockpicks!"),
            new ActivityRecommendation(10, 20, Skill.Dagger, "Equipment: Explore the Folthid Estate near Yanshi at 8.6S, 52.9EE for the Dagger of Tikola and Dull Dagger. Speak with Raxanza Folthid at 8.8S, 53.7E for more information."),
            new ActivityRecommendation(12, 126, "Hunting Grounds: Hunt Olthois in the Olthoi Arcade near Redspire at 39.1N 81.2W."),
            new ActivityRecommendation(10, 20, "Hunting Grounds: Eastham Beach - Hunt following the coastline out of Eastham."),
            new ActivityRecommendation(20, 30, "Hunting Grounds: Lost Wish Range - Go through the Mountain Shortcut portal near Arwic at 34.9N, 56.0E and hunt along the mountains."),
            new ActivityRecommendation(20, 30, "Hunting Grounds: Northern Tiofor Woods - Hunt Shadows for Dark Slivers around 60.0N 19.0E."),
            new ActivityRecommendation(25, 35, "Hunting Grounds: The very bottom of the Fenmalain Chamber is a great place to hunt Fragments for Tiny Shards. To get there you need to use Fenmalain Keys at the bottom of the Fenmalain Vestibule near Baishi at 46.9S, 55.2E."),
            new ActivityRecommendation(25, 30, Skill.Axe, "Equipment: Explore Suntik Village near Zaikhal at 16.2N 4.3E for the Acid Axe."),
            new ActivityRecommendation(25, 30, Skill.Axe, "Equipment: Explore the Bellig Tower near Zaikhal at 17.8N, 16.0E for the Hammer of Lightning."),
            new ActivityRecommendation(25, 35, Skill.Mace, "JitteKrauLiLesser", "Equipment: Explore the Catacombs of the Forgotten in the Plains of Gaerwel at 17.3N, 32.8E for Mi Krau-Li's Jitte."),
            new ActivityRecommendation(10, 20, Skill.Mace, new HashSet<string>{ "BanderlingMaceShaft", "BanderlingMaceHead"}, "Equipment: Explore Banderling Conquest near Sawato at 29.0S, 50.5E for the Banderling Mace Shaft and Mosswart Maze near Al-Arqas at 25.2S, 19.4E for Banderling Mace Head and bring them to Olivier Rognath in Eastham for the Mace of the Explorer."),
        };

        [CommandHandler("rec", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, "Recommend activities appropriate to the character.")]
        public static void HandleRecommend(Session session, params string[] parameters)
        {
            if (Common.ConfigManager.Config.Server.WorldRuleset != Common.Ruleset.CustomDM)
            {
                session.Network.EnqueueSend(new GameMessageSystemChat($"Unknown command: rec", ChatMessageType.Help));
                return;
            }

            List<ActivityRecommendation> validRecommendations = new List<ActivityRecommendation>();
            foreach (var recommendation in Recommendations)
            {
                if (recommendation.IsApplicable(session.Player))
                {
                    validRecommendations.Add(recommendation);
                }
            }

            if (validRecommendations.Count == 0)
                session.Network.EnqueueSend(new GameMessageSystemChat("No recommendations at the moment.", ChatMessageType.WorldBroadcast));
            else
            {
                session.Network.EnqueueSend(new GameMessageSystemChat("Activity Recommendations:", ChatMessageType.WorldBroadcast));
                foreach (var recommendation in validRecommendations)
                {
                    session.Network.EnqueueSend(new GameMessageSystemChat(recommendation.RecommendationText, ChatMessageType.WorldBroadcast));
                }
            }
        }

        [CommandHandler("xptracker", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, 0, "Return XP tracking information.", "<reset>")]
        public static void HandleXpTracker(Session session, params string[] parameters)
        {
            bool reset = false;
            if (parameters.Length > 0)
                reset = parameters[0].ToLower() == "reset";

            if (!reset)
            {
                if (!session.Player.XpTrackerStartTimestamp.HasValue || !session.Player.XpTrackerTotalXp.HasValue)
                {
                    session.Player.XpTrackerStartTimestamp = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
                    session.Player.XpTrackerTotalXp = 0;
                    session.Network.EnqueueSend(new GameMessageSystemChat($"XP tracking has been enabled for your character.\n", ChatMessageType.Broadcast));
                    return;
                }

                var currUnixTimestamp = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
                var durationSeconds = currUnixTimestamp - session.Player.XpTrackerStartTimestamp.Value;

                if (session.Player.XpTrackerTotalXp.Value > 0 && durationSeconds > 0)
                {
                    var durationTimespan = TimeSpan.FromSeconds(durationSeconds);
                    var xpPerSecond = session.Player.XpTrackerTotalXp.Value / (double)(durationSeconds);
                    var xpPerHour = xpPerSecond * 60 * 60;
                    var msg = $"You've earned {String.Format("{0:0,0}", session.Player.XpTrackerTotalXp.Value)} XP in {durationTimespan.Hours} hr, {(durationTimespan.Minutes)} min, {durationTimespan.Seconds} sec \nfor {String.Format("{0:0,0}", xpPerHour)} XP/hr.";
                    session.Network.EnqueueSend(new GameMessageSystemChat(msg, ChatMessageType.Broadcast));
                }
                else
                {
                    session.Network.EnqueueSend(new GameMessageSystemChat("No XP has been tracked for your character yet.", ChatMessageType.Broadcast));
                }
            }
            else
            {
                session.Player.XpTrackerStartTimestamp = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
                session.Player.XpTrackerTotalXp = 0;
                session.Network.EnqueueSend(new GameMessageSystemChat($"Your character's xp tracking data has been reset.\n", ChatMessageType.Broadcast));
            }
        }

        public class ActivityRecommendation
        {
            public HashSet<Skill> Skills = new HashSet<Skill>();
            public int MinLevel = 0;
            public int MaxLevel = int.MaxValue;
            public int MinSkill = 0;
            public int MaxSkill = int.MaxValue;
            public HashSet<string> QuestFlags = new HashSet<string>();
            public string RecommendationText;

            public ActivityRecommendation(int minLevel, int maxLevel, string recommendation)
                : this(minLevel, maxLevel, new HashSet<Skill>(), 0, int.MaxValue, new HashSet<string>(), recommendation) { }

            public ActivityRecommendation(int minLevel, int maxLevel, Skill skill, string recommendation)
                : this(minLevel, maxLevel, new HashSet<Skill> { skill }, 0, int.MaxValue, new HashSet<string>(), recommendation) { }

            public ActivityRecommendation(int minLevel, int maxLevel, HashSet<Skill> skills, string recommendation)
                : this(minLevel, maxLevel, skills, 0, int.MaxValue, new HashSet<string>(), recommendation) { }

            public ActivityRecommendation(int minLevel, int maxLevel, string questFlag, string recommendation)
                : this(minLevel, maxLevel, new HashSet<Skill>(), 0, int.MaxValue, new HashSet<string> { questFlag }, recommendation) { }

            public ActivityRecommendation(int minLevel, int maxLevel, HashSet<string> questFlags, string recommendation)
                : this(minLevel, maxLevel, new HashSet<Skill>(), 0, int.MaxValue, questFlags, recommendation) { }

            public ActivityRecommendation(int minLevel, int maxLevel, Skill skill, int minSkill, int maxSkill, string recommendation)
                : this(minLevel, maxLevel, new HashSet<Skill> { skill }, minSkill, maxSkill, new HashSet<string>(), recommendation) { }

            public ActivityRecommendation(int minLevel, int maxLevel, Skill skill, string questFlag, string recommendation)
                : this(minLevel, maxLevel, new HashSet<Skill> { skill }, 0, int.MaxValue, new HashSet<string> { questFlag }, recommendation) { }

            public ActivityRecommendation(int minLevel, int maxLevel, Skill skill, HashSet<string> questFlags, string recommendation)
                : this(minLevel, maxLevel, new HashSet<Skill> { skill }, 0, int.MaxValue, questFlags, recommendation) { }

            public ActivityRecommendation(int minLevel, int maxLevel, Skill skill, int minSkill, int maxSkill, string questFlag, string recommendation)
                : this(minLevel, maxLevel, new HashSet<Skill> { skill }, minSkill, maxSkill, new HashSet<string> { questFlag }, recommendation) { }

            public ActivityRecommendation(int minLevel, int maxLevel, HashSet<Skill> skills, int minSkill, int maxSkill, HashSet<string> questFlags, string recommendation)
            {
                MinLevel = minLevel;
                MaxLevel = maxLevel;
                Skills = skills;
                MinSkill = minSkill;
                MaxSkill = maxSkill;
                QuestFlags = questFlags;
                RecommendationText = recommendation;
            }

            public bool IsApplicable(Player player)
            {
                if (player.Level < MinLevel || player.Level > MaxLevel)
                    return false;

                foreach (var questFlag in QuestFlags)
                {
                    if (!player.QuestManager.CanSolve(questFlag))
                        return false;
                }

                if (Skills.Count == 0)
                    return true;

                foreach (var skill in Skills)
                {
                    var playerSkill = player.GetCreatureSkill(player.ConvertToMoASkill(skill));
                    if (playerSkill.AdvancementClass == SkillAdvancementClass.Trained || playerSkill.AdvancementClass == SkillAdvancementClass.Specialized)
                    {
                        if (playerSkill.Current >= MinSkill && playerSkill.Current <= MaxSkill)
                            return true;
                    }
                }

                return false;
            }

            [CommandHandler("taunt", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, "Toggles taunting opponents.")]
            public static void HandleTaunt(Session session, params string[] parameters)
            {
                if (Common.ConfigManager.Config.Server.WorldRuleset != Common.Ruleset.CustomDM)
                    session.Network.EnqueueSend(new GameMessageSystemChat($"Unknown command: taunt", ChatMessageType.Help));
                else
                {
                    if (session.Player.ToggleTauntSetting())
                        session.Network.EnqueueSend(new GameMessageSystemChat($"You will now start attempting to taunt opponents into attacking you.", ChatMessageType.Broadcast));
                    else
                        session.Network.EnqueueSend(new GameMessageSystemChat($"You will no longer attempt to taunt opponents.", ChatMessageType.Broadcast));
                }
            }
        }
    }
}
