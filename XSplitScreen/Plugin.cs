using BepInEx;
using BepInEx.Configuration;
using R2API.Utils;
using System;
using Rewired;
using System.Reflection;
using UnityEngine;
using System.Linq;
using RoR2.UI.MainMenu;
using RoR2.UI;
using Newtonsoft.Json;
using R2API;
using Dodad.XSplitscreen.Components;
using System.IO;
using UnityEngine.EventSystems;

namespace Dodad.XSplitscreen
{
    [BepInPlugin(PluginGUID, PluginName, "4.1.2")]
    [NetworkCompatibility(CompatibilityLevel.NoNeedForSync, VersionStrictness.DifferentModVersionsAreOk)]
	[BepInDependency(LanguageAPI.PluginGUID, BepInDependency.DependencyFlags.HardDependency)]
	public class Plugin : BaseUnityPlugin
    {
		#region Variables

		public const string PluginGUID = PluginAuthor + "." + PluginName;
		public const string PluginAuthor = "com.dodad";
		public const string PluginName = "XSplitscreen";

		public const int HardMaxPlayers = 16;

		//-----------------------------------------------------------------------------------------------------------

		public static Plugin Singleton { get; private set; }
        public static AssetBundle Resources { get; private set; }
        public static System.Action OnOpenMenu;
		internal static HarmonyLib.Harmony Patcher { get; private set; }

        private static HGButton MainMenuTitleButton;

		private static Color[] _multiplayerColors;

		#endregion

		//-----------------------------------------------------------------------------------------------------------

		#region Mono Methods
		public void Awake()
        {
            if (Singleton != null)
                return;

            Singleton = this;

            if (!LoadLogger())
                return;

            if (!LoadAssets())
                return;
            else
                Log.Print($"Loaded '{Resources.GetAllAssetNames().Length}' assets");

			CreateParticleSystems();
			SetGenericPatches();

            if (!LoadLanguage())
                return;
            else
                Log.Print("Loaded language");

            SetSubscriptions(true);

			/*Log.Init(Logger);

            var assembly = Assembly.GetExecutingAssembly();
            string bundleKey;

            if (XLibrary.Resources.RegisterBundle(assembly, this, $"DoDad.XSplitScreen.{PluginBundle}", out bundleKey))
            {
                Log.LogOutput($"Initializing", Log.LogLevel.Debug);
                XLibrary.Plugin.InitializePlugin(assembly, this, new XLibrary.Language.TokenConfiguration(PluginTag));
                Initialize();
                SceneManager.activeSceneChanged += ActiveSceneChanged;
                Plugin.bundleKey = bundleKey;
            }
            else
            {
                Log.LogOutput($"Unable to register asset bundle. Plugin disabled.", Log.LogLevel.Warning);
                return;
            }*/
		}

		public void OnApplicationQuit()
		{
			Patcher.UnpatchSelf();
		}
		#endregion

		//-----------------------------------------------------------------------------------------------------------

		#region Initialization

		private static void CreateParticleSystems()
		{
			ParticleSystemFactory.CreateAllParticleSystems();
		}

		/// <summary>
		/// Load language file from disk and merge with defaults
		/// </summary>
		private static bool LoadLanguage()
		{
			var baseDir = Path.GetDirectoryName(Singleton.Config.ConfigFilePath);
			var filePath = Path.Combine(baseDir, "XSplitscreen", "language.json");

			try
			{
				// Create default language tokens
				MultiDictionary defaultTokens = CreateDefaultLanguageTokens();
				MultiDictionary userTokens = null;

				// Check if custom language file exists
				if (File.Exists(filePath))
				{
					try
					{
						string fileContent = File.ReadAllText(filePath);
						userTokens = JsonConvert.DeserializeObject<MultiDictionary>(fileContent);

						// Validate the loaded data
						if (userTokens == null)
						{
							Log.Print("Language file exists but contains invalid data. Using defaults.", Log.ELogChannel.Warning);
							userTokens = null;
						}
					}
					catch (JsonException jsonEx)
					{
						Log.Print($"Error parsing language file: {jsonEx.Message}. Using defaults.", Log.ELogChannel.Warning);
					}
					catch (IOException ioEx)
					{
						Log.Print($"Error reading language file: {ioEx.Message}. Using defaults.", Log.ELogChannel.Warning);
					}
				}
				else
				{
					Directory.CreateDirectory(Path.GetDirectoryName(filePath));
					File.Create(filePath).Dispose();
				}

				// Merge user tokens with defaults or use defaults if no valid user file
				MultiDictionary finalTokens = userTokens != null ?
					MergeLanguageTokens(defaultTokens, userTokens) :
					defaultTokens;

				// Save the complete language file for user reference/editing
				File.WriteAllText(filePath, JsonConvert.SerializeObject(finalTokens, Formatting.Indented));

				// Add tokens to LanguageAPI
				foreach (var language in finalTokens)
				{
					foreach (var token in language.Value)
					{
						LanguageAPI.Add(token.Key, token.Value, language.Key);
					}
				}

				return true;
			}
			catch (Exception e)
			{
				Log.Print($"Fatal error in LoadLanguage: {e.Message}", Log.ELogChannel.Fatal);
				Log.Print(e, Log.ELogChannel.Fatal);
				return false;
			}
		}

		/// <summary>
		/// Creates the default language tokens for all supported languages
		/// </summary>
		private static MultiDictionary CreateDefaultLanguageTokens()
		{
			MultiDictionary tokens = new MultiDictionary();

			// Define shared tokens that will be applied to all languages
			StringDictionary sharedTokens = new StringDictionary
			{
				{ "XSS_NAME", "XSplitscreen" },
				{ "XSS_OPTION_DISCORD", "Discord" },
				{ "XSS_UNSET", "- not set -" },
			};

			// English (complete set - reference for other languages)
			string enKey = "en";
			tokens.Add(enKey, new StringDictionary
			{
				{ "XSS_NAME_HOVER", "Modify splitscreen settings." },
				{ "XSS_OPTION_DISCORD_HOVER", "Join for support, feedback or updates" },
				{ "XSS_PRESS_START_KBM", "- click here or press start -" },
				{ "XSS_PRESS_START", "- press start -" },
				{ "XSS_CONFIG_PROFILE", "Guest" },
				{ "XSS_CONFIG_COLOR", "Color" },
				{ "XSS_OPTION_GM_HOVER", "Select Gamemode" },
				{ "XSS_OPTION_MMM", "Multi-Monitor Mode" },
				{ "XSS_OPTION_MMM_HOVER", "Enable Multi-Monitor Mode (cannot be disabled)" },
				{ "XSS_TRAILS", "Trails" },
				{ "XSS_SELECT_SCREEN", "Select Screen" },
				{ "XSS_READY", "Ready" },
				{ "XSS_ONLINE", "Online" },
				{ "XSS_WARN", "Multiple instances of corrupted profiles and lost save data have been reported from users playing in the official local splitscreen mode on consoles. <br><br>XSplitscreen makes no guarantee concerning the safety of your profiles. It is HIGHLY RECOMMENDED to create backups before each session." },
				{ "XSS_CREDITS", "Credits" },
				{ "XSS_CREDITS_HOVER", "Enable or disable credits" },
				{ "XSS_SELECT_MONITOR", "Select Monitor" },
			});

			// Portuguese (Brazilian)
			string ptBrKey = "pt-br";
			tokens.Add(ptBrKey, new StringDictionary
			{
				{ "XSS_NAME_HOVER", "Modificar as configura��es da tela-dividida." },
				{ "XSS_OPTION_DISCORD_HOVER", "Entre para suporte, dar feedback ou ver atualiza��es" },
				{ "XSS_PRESS_START_KBM", "- clique ou pressione start -" },
				{ "XSS_PRESS_START", "- pressione start -" },
				{ "XSS_CONFIG_PROFILE", "Guest" },
				{ "XSS_CONFIG_COLOR", "Cor" },
				{ "XSS_OPTION_MMM", "Modo Multi-Monitor" },
				{ "XSS_OPTION_MMM_HOVER", "Ativar o Modo Multi-Monitor (n�o pode ser desativado)" },
				{ "XSS_TRAILS", "Trails" },
				{ "XSS_SELECT_SCREEN", "Select Screen" },
				{ "XSS_READY", "Ready" },
				{ "XSS_ONLINE", "Online" },
				{ "XSS_WARN", "Multiple instances of corrupted profiles and lost save data have been reported from users playing in the official local splitscreen mode on consoles. <br><br>XSplitscreen makes no guarantee concerning the safety of your profiles. It is HIGHLY RECOMMENDED to create backups before each session." },
				{ "XSS_CREDITS", "Credits" },
				{ "XSS_CREDITS_HOVER", "Enable or disable credits" },
				{ "XSS_SELECT_MONITOR", "Select Monitor" },
			});

			// French
			string frKey = "fr";
			tokens.Add(frKey, new StringDictionary
			{
				{ "XSS_NAME_HOVER", "Modifier les param�tres de splitscreen." },
				{ "XSS_OPTION_DISCORD_HOVER", "Rejoignez nous pour du support, du feedback ou des mises � jour" },
				{ "XSS_PRESS_START_KBM", "- cliquez ou appuyez sur start -" },
				{ "XSS_PRESS_START", "- appuyez sur start -" },
				{ "XSS_CONFIG_PROFILE", "Guest" },
				{ "XSS_CONFIG_COLOR", "Couleur" },
				{ "XSS_OPTION_MMM", "Mode Multi-�crans" },
				{ "XSS_OPTION_MMM_HOVER", "Activer le Mode Multi-�crans (ne peut pas �tre d�sactiv�)" },
				{ "XSS_TRAILS", "Trails" },
				{ "XSS_SELECT_SCREEN", "Select Screen" },
				{ "XSS_READY", "Ready" },
				{ "XSS_ONLINE", "Online" },
				{ "XSS_WARN", "Multiple instances of corrupted profiles and lost save data have been reported from users playing in the official local splitscreen mode on consoles. <br><br>XSplitscreen makes no guarantee concerning the safety of your profiles. It is HIGHLY RECOMMENDED to create backups before each session." },
				{ "XSS_CREDITS", "Credits" },
				{ "XSS_CREDITS_HOVER", "Enable or disable credits" },
				{ "XSS_SELECT_MONITOR", "Select Monitor" },
			});

			// Apply shared tokens to all languages
			foreach (var language in tokens)
			{
				foreach (var token in sharedTokens)
				{
					language.Value[token.Key] = token.Value;
				}
			}

			return tokens;
		}

		/// <summary>
		/// Merges user language tokens with default tokens, preserving user customizations
		/// </summary>
		private static MultiDictionary MergeLanguageTokens(MultiDictionary defaultTokens, MultiDictionary userTokens)
		{
			MultiDictionary result = new MultiDictionary();

			// First add all default languages and tokens
			foreach (var language in defaultTokens)
			{
				result.Add(language.Key, new StringDictionary());
				foreach (var token in language.Value)
				{
					result[language.Key].Add(token.Key, token.Value);
				}
			}

			// Then overlay user customizations
			foreach (var language in userTokens)
			{
				// Create language if it doesn't exist in defaults
				if (!result.ContainsKey(language.Key))
				{
					result.Add(language.Key, new StringDictionary());
				}

				// Add or replace tokens with user values
				foreach (var token in language.Value)
				{
					if (result[language.Key].ContainsKey(token.Key))
					{
						result[language.Key][token.Key] = token.Value;
					}
					else
					{
						result[language.Key].Add(token.Key, token.Value);
					}
				}
			}

			return result;
		}

		//-----------------------------------------------------------------------------------------------------------

		/// <summary>
		/// Subscribe or unsubscribe to events
		/// </summary>
		/// <param name="state"></param>
		private static void SetSubscriptions(bool state)
        {
            if(state)
			{
				MainMenuController.OnMainMenuInitialised += OnMainMenuInitialized;
			}
            else
            {
				MainMenuController.OnMainMenuInitialised -= OnMainMenuInitialized;
			}
		}

		//-----------------------------------------------------------------------------------------------------------

		/// <summary>
		/// Load the user set log levels from the Config file
		/// </summary>
		/// <returns></returns>
		private static bool LoadLogger()
		{
            // Use enum names to retrieve Config entries containing bools to 
            // determine whether or not we should print messages from that 
            // log channel

            Log.ELogChannel channels = Log.ELogChannel.None;

            var logChannelKeys = Enum.GetNames(typeof(Log.ELogChannel))
                .Where(x => x.ToLower() != "none").ToArray();

            int channelCount = logChannelKeys.Length;

			ConfigEntry<bool>[] channelEntries = new ConfigEntry<bool>[channelCount];

			string section = "Log Channels";

			try
			{
                for(int e = 0; e < channelCount; e++)
                {
                    channelEntries[e] = Singleton.Config.Bind(section, logChannelKeys[e], true);

                    if (channelEntries[e].Value)
                        channels |= (Log.ELogChannel)Enum.Parse(typeof(Log.ELogChannel), logChannelKeys[e]);
                }

				Log.Init(Singleton.Logger, channels);

                return true;
			}
			catch (Exception e)
			{
				Log.Init(Singleton.Logger, Log.ELogChannel.All);
				Log.Print(e, Log.ELogChannel.Error);

				return false;
			}
		}

		//-----------------------------------------------------------------------------------------------------------

		/// <summary>
		/// Load the included Asset Bundle and make those resources available to the mod
		/// </summary>
		private static bool LoadAssets()
        {
            try
			{
				var assembly = Assembly.GetExecutingAssembly();

                var resourceStream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Last());

                using (resourceStream)
                    Resources = AssetBundle.LoadFromStream(resourceStream);

                return Resources != null;
			}
            catch(Exception e)
            {
                Log.Print(e, Log.ELogChannel.Fatal);

                return false;
            }
		}

		//-----------------------------------------------------------------------------------------------------------

		/// <summary>
		/// Create hooks for low level splitscreen issues
		/// </summary>
		private static void SetGenericPatches()
		{
			SplitscreenUserManager.OnStateChange += HookManager.OnStateChange;

			Patcher = new HarmonyLib.Harmony("com.dodad.xsplitscreen.hooks");

			// Resize RoR2Application max local players

			var ror2AppResizePlayersOriginal = typeof(RoR2.RoR2Application).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
			var ror2AppResizePlayersPatch = typeof(Plugin).GetMethod("RoR2AppResizePlayers", BindingFlags.Static | BindingFlags.NonPublic);

			Patcher.Patch(ror2AppResizePlayersOriginal, prefix: new HarmonyLib.HarmonyMethod(ror2AppResizePlayersPatch));

			// Add Rewired players

			var rewiredClonePlayersOriginal = typeof(InputManager_Base).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
			var rewiredClonePlayersPatch = typeof(Plugin).GetMethod("RewiredClonePlayers", BindingFlags.Static | BindingFlags.NonPublic);

			Patcher.Patch(rewiredClonePlayersOriginal, prefix: new HarmonyLib.HarmonyMethod(rewiredClonePlayersPatch));

            // UI patches

            var mmcInteractableOriginal = typeof(RoR2.UI.MainMenu.MainMenuController).GetMethod("SetButtonsInteractible", BindingFlags.Instance | BindingFlags.Public);
            var mmcInteractablePatch = typeof(Plugin).GetMethod("SetButtonInteractable", BindingFlags.Static | BindingFlags.NonPublic);

            Patcher.Patch(mmcInteractableOriginal, prefix: new HarmonyLib.HarmonyMethod(mmcInteractablePatch));

			// Fix input

			var mpeAOriginal = typeof(MPEventSystem).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
			var mpeAPatch = typeof(Plugin).GetMethod("MPEventSystem_Update", BindingFlags.Static | BindingFlags.NonPublic);

			Patcher.Patch(mpeAOriginal, prefix: new HarmonyLib.HarmonyMethod(mpeAPatch));

			// Destroy additional canvases

			var cscOriginal = typeof(RoR2.UI.CharacterSelectController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
			var cscPatch = typeof(Plugin).GetMethod("CharacterSelectController_Awake", BindingFlags.Static | BindingFlags.NonPublic);

			Patcher.Patch(cscOriginal, prefix: new HarmonyLib.HarmonyMethod(cscPatch));

			// Set local user names

			var lunOriginal = typeof(RoR2.NetworkUser).GetMethod("UpdateUserName", BindingFlags.Instance | BindingFlags.Public);
			var lunPatch = typeof(Plugin).GetMethod("NetworkUser_UpdateUserName", BindingFlags.Static | BindingFlags.NonPublic);

			Patcher.Patch(lunOriginal, prefix: new HarmonyLib.HarmonyMethod(lunPatch));

			// (specter) Fixes per-player pod labels - vanilla's ProfileNameLabel.LateUpdate reads
			// PlatformSystems.userManager.GetUserName() (a single machine-wide name), so every
			// splitscreen player's pod shows the same Steam name.
			var pnlOriginal = typeof(RoR2.UI.ProfileNameLabel).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
			var pnlPatch = typeof(Plugin).GetMethod("ProfileNameLabel_LateUpdate", BindingFlags.Static | BindingFlags.NonPublic);

			Patcher.Patch(pnlOriginal, postfix: new HarmonyLib.HarmonyMethod(pnlPatch));

			// Combat health bar fix

			var chbOriginal = typeof(RoR2.UI.CombatHealthBarViewer).GetMethod("SetLayoutHorizontal", BindingFlags.Instance | BindingFlags.Public);
			var chbPatch = typeof(Plugin).GetMethod("CombatHealthBarViewer_SetLayoutHorizontal", BindingFlags.Static | BindingFlags.NonPublic);

			Patcher.Patch(chbOriginal, prefix: new HarmonyLib.HarmonyMethod(chbPatch));

			// Nameplate color
			var npcOriginal = typeof(RoR2.UI.Nameplate).GetMethod("Initialize", BindingFlags.Instance | BindingFlags.Public);
			var npcPatch = typeof(Plugin).GetMethod("Nameplate_SetBody", BindingFlags.Static | BindingFlags.NonPublic);

			Patcher.Patch(npcOriginal, prefix: new HarmonyLib.HarmonyMethod(npcPatch));

			// Patch Get Multiplayer Color
			var mcOriginal = typeof(RoR2.ColorCatalog).GetMethod("GetMultiplayerColor", BindingFlags.Static | BindingFlags.Public);
			var mcPatch = typeof(Plugin).GetMethod("ColorCatalog_GetMultiplayerColor", BindingFlags.Static | BindingFlags.NonPublic);

			Patcher.Patch(mcOriginal, prefix: new HarmonyLib.HarmonyMethod(mcPatch));

			// (specter) Guard against a vanilla crash in local-multiplayer camera/canvas setup -
			// see the finalizer method's own comment for the full explanation.
			var rcmOriginal = typeof(RoR2.RunCameraManager).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
			var rcmFinalizer = typeof(Plugin).GetMethod("RunCameraManager_Update_Finalizer", BindingFlags.Static | BindingFlags.NonPublic);

			Patcher.Patch(rcmOriginal, finalizer: new HarmonyLib.HarmonyMethod(rcmFinalizer));
		}

		//-----------------------------------------------------------------------------------------------------------
		/// <summary>
		/// Load and format the main menu UI
		/// </summary>
		private static void CreateTitleUI()
        {
            if (MainMenuTitleButton != null)
                return;

            // Main menu title button

            var container = MainMenuController.instance.singlePlayerButton.transform.parent;

            var titleObject = UIHelper.GetPrefab(UIHelper.EUIPrefabIndex.MainMenuButton);

            if(titleObject == null)
            {
                Log.Print("UIHelper prefab is null (MainMenuButton)");
                return;
            }

			titleObject.name = "GenericMenuButton (XSplitscreen)";
			titleObject.transform.SetParent(container);
			titleObject.transform.SetSiblingIndex(MainMenuController.instance.singlePlayerButton.transform.GetSiblingIndex() - 1);
			titleObject.transform.localScale = Vector3.one;

            var titleButtonLanguage = titleObject.GetComponent<LanguageTextMeshController>();
            titleButtonLanguage.token = "XSS_NAME";

            var titleButton = titleObject.GetComponent<HGButton>();
            titleButton.hoverToken = "XSS_NAME_HOVER";
            titleButton.requiredTopLayer = MainMenuController.instance.singlePlayerButton.requiredTopLayer;
            titleButton.hoverLanguageTextMeshController = MainMenuController.instance.singlePlayerButton.hoverLanguageTextMeshController;
            titleButton.updateTextOnHover = true;

			titleObject.SetActive(true);

            // Create Splitscreen Menu Controller

            CreateMenuForDisplay(0);

			titleButton.onClick.AddListener(() =>
			{
				MainMenuController.instance.SetDesiredMenuScreen(SplitscreenMenuController.Singleton);
                OnOpenMenu?.Invoke();
			});
		}

		//-----------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Create a menu for a display
        /// </summary>
        /// <param name="id"></param>
		internal static SplitscreenMenuController CreateMenuForDisplay(int id)
		{
			Log.Print($"[{Plugin.Singleton.GetType().Name}.{MethodBase.GetCurrentMethod().Name}] : id = '{id}'");

			try
			{
				GameObject menuPrefab = Resources.LoadAsset<GameObject>("menu.prefab");

				var menu = GameObject.Instantiate(menuPrefab);
				menu.name = $"[Display {id}] MENU: XSplitscreen";
				menu.transform.SetParent(MainMenuController.instance.transform, true);

                var menuController = menu.transform.Find("Splitscreen Menu");

				// (specter) Scoped to Main Panel, not the whole canvas - Notification/Credits Panel
				// rely on seeing through to whatever's behind the canvas at their edges.
				SetupAmbientBackground(menuController.Find("Main Panel"));

				var canvas = menuController.gameObject.GetComponent<Canvas>();
                canvas.targetDisplay = id;

				var controller = menuController.gameObject.AddComponent<SplitscreenMenuController>();
                controller.monitorId = id;

                if(id != 0)
                {
                    SplitscreenMenuController.Singleton.onEnter.AddListener(() =>
                    {
                        controller.OnEnter(null);
                    });
					SplitscreenMenuController.Singleton.onExit.AddListener(() =>
					{
						controller.OnExit(null);
					});
				}

                return controller;
			}
			catch (Exception e)
			{
				Log.Print(e, Log.ELogChannel.Error);

                return null;
			}
		}

		/// <summary>
		/// (specter) Adds an opaque #060606 backdrop plus drifting dots behind everything else in Main Panel.
		/// </summary>
		private static void SetupAmbientBackground(Transform mainPanel)
		{
			if (mainPanel == null) return;

			var bg = new GameObject("BG", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
			var bgRect = (RectTransform) bg.transform;
			bgRect.SetParent(mainPanel, false);
			bgRect.SetAsFirstSibling();
			bgRect.anchorMin = Vector2.zero;
			bgRect.anchorMax = Vector2.one;
			bgRect.sizeDelta = Vector2.zero;
			bgRect.anchoredPosition = Vector2.zero;

			var bgImage = bg.GetComponent<UnityEngine.UI.Image>();
			bgImage.color = new Color(6f / 255f, 6f / 255f, 6f / 255f, 1f);
			bgImage.raycastTarget = false;

			bg.AddComponent<AmbientBackground>();
		}

		#endregion

		//-----------------------------------------------------------------------------------------------------------

		#region Public Methods
		/*internal static void RecreateAssignmentScreen()
        {
            Destroy(assignmentScreen);
            CreateInitializationRoutine();
        }*/
		#endregion

		//-----------------------------------------------------------------------------------------------------------

		#region Event Handlers
        /// <summary>
        /// Try to create the main menu UI
        /// </summary>
		private static void OnMainMenuInitialized()
        {
			// (specter) Also fires after quitting an active run, not just backing out of the menu
			// screen - without this a controller stays reassigned to a non-"PlayerMain" Rewired
			// player and can't navigate the main menu.
			if (SplitscreenUserManager.IsSplitscreenEnabled)
				SplitscreenUserManager.DisableSplitscreen();

            if (MainMenuTitleButton != null)
                return;

			UIHelper.Initialize();

            CreateTitleUI();
		}
		/*public static void ActiveSceneChanged(Scene previous, Scene current)
        {
            if (current.name.Equals("title"))
                CreateInitializationRoutine();
        }*/
		#endregion

		//-----------------------------------------------------------------------------------------------------------

		#region Coroutines
		/*internal static IEnumerator WaitToInitializeUI()
        {
            while (!XLibrary.Plugin.uiPrefabsReady)
                yield return null;

            Log.LogOutput($"Initializing UI", Log.LogLevel.Message);

            while (!InitializeTitleButton())
                yield return null;

            while (!InitializeUI())
                yield return null;

            //Log.LogOutput($"WaitToInitializeUI complete");
            initializationCoroutine = null;

            yield return null;
        }*/
		#endregion

		//-----------------------------------------------------------------------------------------------------------

		#region Patching

		private static void Nameplate_SetBody(Nameplate __instance, RoR2.CharacterBody __0)
		{
			if (__0 != null && __0.master != null && __0.master.playerCharacterMasterController != null && __0.master.playerCharacterMasterController.networkUser != null &&
				__0.master.playerCharacterMasterController.networkUser.localUser != null)
			{
				__instance.baseColor = RoR2.ColorCatalog.GetMultiplayerColor(__0.master.playerCharacterMasterController.networkUser.localUser.id);
			}
		}

		public static Color[] multiplayerColors
		{
			get => _multiplayerColors;
		} 

		public static void UpdateMultiplayerColors(Color[] colors)
		{
			_multiplayerColors = colors;
		}

        private static bool ColorCatalog_GetMultiplayerColor(int playerSlot, ref Color __result)
		{
			// (specter) multiplayerColors is only populated once splitscreen has been enabled at
			// least once this session (see HookManager.UpdateMultiplayerColors). Fall back to
			// vanilla behavior instead of throwing when a nameplate/health bar resolves a color
			// before then.
			if (multiplayerColors == null)
			{
				return true;
			}

			if (playerSlot >= 0 && playerSlot < multiplayerColors.Length)
			{
				 __result = multiplayerColors[playerSlot];
			}
			else
			{
				__result = Color.black;
			}

			return false;
		}

		/// <summary>
		/// (specter) Vanilla's RunCameraManager.Update indexes its camera dictionary by player
		/// name with no existence check, assuming every local player already has a camera - not
		/// true until entitlements resolve, and that check gets re-triggered (and the race
		/// reopened) more than once per session. Rather than chase every trigger, swallow the
		/// resulting KeyNotFoundException here and let it retry next frame; anything else rethrows.
		/// </summary>
		private static Exception RunCameraManager_Update_Finalizer(Exception __exception)
		{
			if (__exception is System.Collections.Generic.KeyNotFoundException)
			{
				Log.Print("RunCameraManager.Update threw KeyNotFoundException (vanilla local-multiplayer camera/entitlement race) - suppressed, retrying next frame.", Log.ELogChannel.Warning);
				return null;
			}

			return __exception;
		}

		private static bool CombatHealthBarViewer_SetLayoutHorizontal(RoR2.UI.CombatHealthBarViewer __instance)
		{
			if (__instance.uiCamera)
			{
				CombatHealthBarViewer.doingUIRebuild = true;
				__instance.UpdateAllHealthbarPositions(__instance.uiCamera.cameraRigController.sceneCam, __instance.uiCamera.camera);
				CombatHealthBarViewer.doingUIRebuild = false;
			}

			return false;
		}

		private static bool NetworkUser_UpdateUserName(RoR2.NetworkUser __instance)
		{
			// (specter) Always skip the vanilla body (see TryResolveUserName) - a one-shot
			// "!isLocalPlayer -> let vanilla run" check was the bug: isLocalPlayer isn't true yet
			// on the first UpdateUserName call, so every local player permanently baked in
			// vanilla's single-Steam-account name.
			TryResolveUserName(__instance, retriesLeft: 30);

			return false;
		}

		private static void TryResolveUserName(RoR2.NetworkUser instance, int retriesLeft)
		{
			if (instance == null)
				return;

			if (instance.isLocalPlayer && instance.localUser != null)
			{
				// (specter) Skip vanilla's resolution (the machine's single Steam account) so the
				// per-profile name isn't overwritten.
				instance.userName = instance.localUser.userProfile.name;
				return;
			}

			if (retriesLeft > 0)
			{
				// (specter) Retry for a short grace period rather than bailing immediately - covers
				// isLocalPlayer/localUser still linking, then falls through to remote players.
				ExecuteNextFrame.Invoke(() => TryResolveUserName(instance, retriesLeft - 1));
				return;
			}

			// (specter) Grace period exhausted with no local link - genuinely remote, resolve like vanilla.
			instance.userName = instance.GetNetworkPlayerName().GetResolvedName();
		}

		private static readonly FieldInfo ProfileNameLabelTokenField = typeof(RoR2.UI.ProfileNameLabel).GetField("token", BindingFlags.Instance | BindingFlags.NonPublic);

		private static void ProfileNameLabel_LateUpdate(RoR2.UI.ProfileNameLabel __instance)
		{
			var locator = __instance.GetComponent<MPEventSystemLocator>();
			var localUser = locator?.eventSystem?.localUser;

			if (localUser?.userProfile == null)
				return;

			var label = __instance.GetComponent<TMPro.TextMeshProUGUI>();
			var token = (string)ProfileNameLabelTokenField.GetValue(__instance);

			label.text = RoR2.Language.GetStringFormatted(token, localUser.userProfile.name);
		}

		private static void CharacterSelectController_Awake(RoR2.UI.CharacterSelectController __instance)
		{
			ExecuteNextFrame.Invoke(new System.Action(() =>
			{
				if(__instance.gameObject.GetComponent<Canvas>().worldCamera == null)
				{
					Destroy(__instance.gameObject);
				}
			}));
		}

		private static MethodInfo _mpEventSystemBaseUpdate;

		private static bool MPEventSystem_Update(MPEventSystem __instance)
		{
			var baseUpdate = (EventSystem es) =>
			{
				if (EventSystem.current != __instance)
				{
					return;
				}
				es.TickModules();
				bool flag = false;
				int count = es.m_SystemInputModules.Count;
				for (int i = 0; i < count; i++)
				{
					BaseInputModule baseInputModule = es.m_SystemInputModules[i];
					if (baseInputModule.IsModuleSupported() && baseInputModule.ShouldActivateModule())
					{
						if (es.m_CurrentInputModule != baseInputModule)
						{
							es.ChangeEventModule(baseInputModule);
							flag = true;
						}
						break;
					}
				}
				if (es.m_CurrentInputModule == null)
				{
					for (int j = 0; j < count; j++)
					{
						BaseInputModule baseInputModule2 = es.m_SystemInputModules[j];
						if (baseInputModule2.IsModuleSupported())
						{
							es.ChangeEventModule(baseInputModule2);
							flag = true;
							break;
						}
					}
				}
				if (!flag && es.m_CurrentInputModule != null)
				{
					es.m_CurrentInputModule.Process();
				}
			};

			EventSystem eventSystem = EventSystem.current;
			EventSystem.current = __instance;

			baseUpdate(__instance);

			EventSystem.current = eventSystem;

			__instance.ValidateCurrentSelectedGameobject();

			if (__instance.player.GetButtonDown(25) &&
				(PauseScreenController.instancesList.Count == 0 || SimpleDialogBox.instancesList.Count == 0))
			{
				if (!RoR2.PauseManager.isPaused)
				{
					RoR2.PauseManager.PausingPlayerID = __instance.player.id;
				}
				if (RoR2.PauseManager.PausingPlayerID == __instance.player.id &&
					RoR2.Console.instance != null)
				{
					RoR2.Console.instance.SubmitCmd(null, "pause");
				}
			}

			return false;
		}

		/// <summary>
		/// Update the interactable state of the splitscreen button in the title menu
		/// </summary>
		/// <param name="__instance"></param>
		/// <param name="__0"></param>
		private static void SetButtonInteractable(MainMenuController __instance, bool __0)
        {
            MainMenuTitleButton.interactable = __0;
		}

		//-----------------------------------------------------------------------------------------------------------

		/// <summary>
		/// Add players to the Rewired Input Manager
		/// </summary>
		/// <param name="__instance"></param>
		private static void RewiredClonePlayers(InputManager_Base __instance)
		{
            if (__instance.userData.players.Count != 3)
                return;

            var player = __instance.userData.players[2].Clone();

            for (int e = 0; e < 14; e++)
            {
                var clone = player.Clone();
                clone.name = $"Player{e + 3}";
                clone.descriptiveName = clone.name;

				__instance.userData.players.Add(clone);
            }
		}

		//-----------------------------------------------------------------------------------------------------------

		/// <summary>
		/// Increase max local player count
		/// </summary>
		/// <param name="__instance"></param>
		private static void RoR2AppResizePlayers(RoR2.RoR2Application __instance)
		{
			RoR2.RoR2Application.maxPlayers = HardMaxPlayers;
			RoR2.RoR2Application.hardMaxPlayers = HardMaxPlayers;
			RoR2.RoR2Application.maxLocalPlayers = HardMaxPlayers;

			Patcher.Unpatch(typeof(RoR2.RoR2Application).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic),
				typeof(Plugin).GetMethod("RoR2AppResizePlayers", BindingFlags.Static | BindingFlags.NonPublic));
		}

		#endregion
	}
}