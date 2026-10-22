using BepInEx;
using BepInEx.Configuration;
using R2API.Utils;
using System;
using System.Collections.Generic;
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
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [NetworkCompatibility(CompatibilityLevel.NoNeedForSync, VersionStrictness.DifferentModVersionsAreOk)]
	[BepInDependency(LanguageAPI.PluginGUID, BepInDependency.DependencyFlags.HardDependency)]
	[BepInDependency("droppod.lookingglass", BepInDependency.DependencyFlags.SoftDependency)]
	public class Plugin : BaseUnityPlugin
    {
		#region Variables

		public const string PluginGUID = PluginAuthor + "." + PluginName;
		public const string PluginAuthor = "com.dodad";
		public const string PluginName = "XSplitscreen";
		public const string PluginVersion = "4.1.2";

		public const int HardMaxPlayers = 16;

		//-----------------------------------------------------------------------------------------------------------

		public static Plugin Singleton { get; private set; }
        public static AssetBundle Resources { get; private set; }
		internal static HarmonyLib.Harmony Patcher { get; private set; }

        private static HGButton MainMenuTitleButton;

		private static bool LoggedGameBuild;

		private static Color[] _multiplayerColors = HookManager.DefaultMultiplayerColors;

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
			Diagnostics.Initialize();

            if (!LoadLanguage())
                return;
            else
                Log.Print("Loaded language");

            SetSubscriptions(true);

			if (FailedHooks.Count == 0)
				Log.Print($"{PluginName} {PluginVersion} loaded", Log.ELogChannel.Info);
			else
				Log.Print($"{PluginName} {PluginVersion} loaded with {FailedHooks.Count} failed hook(s): {string.Join(", ", FailedHooks)}. Some features will not work.", Log.ELogChannel.Warning);
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
		/// Register the language tokens. Built-in text can be overridden per token in
		/// config/XSplitscreen/language.json; language.default.json is a reference copy of the built-in text.
		/// </summary>
		private static bool LoadLanguage()
		{
			var directory = Path.Combine(Path.GetDirectoryName(Singleton.Config.ConfigFilePath), "XSplitscreen");
			var overridesPath = Path.Combine(directory, "language.json");
			var referencePath = Path.Combine(directory, "language.default.json");

			MultiDictionary defaultTokens = CreateDefaultLanguageTokens();
			MultiDictionary overrides = new MultiDictionary();

			try
			{
				Directory.CreateDirectory(directory);

				// Reference copy for translators, never read back
				File.WriteAllText(referencePath, JsonConvert.SerializeObject(defaultTokens, Formatting.Indented));

				if (File.Exists(overridesPath))
				{
					var loaded = JsonConvert.DeserializeObject<MultiDictionary>(File.ReadAllText(overridesPath));

					if (loaded != null)
						overrides = CleanLanguageOverrides(defaultTokens, loaded);
				}

				// Older versions saved every built-in token here, which then overrode any later text fix
				File.WriteAllText(overridesPath, JsonConvert.SerializeObject(overrides, Formatting.Indented));
			}
			catch (Exception e)
			{
				Log.Print($"Could not load language overrides, using the built-in text: {e.Message}", Log.ELogChannel.Warning);
				overrides = new MultiDictionary();
			}

			try
			{
				foreach (var language in MergeLanguageTokens(defaultTokens, overrides))
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
		/// Keep only real customizations: drop entries identical to the built-in text (older versions saved
		/// every token). Language names are matched to the built-in ones ignoring case.
		/// </summary>
		private static MultiDictionary CleanLanguageOverrides(MultiDictionary defaultTokens, MultiDictionary loaded)
		{
			var cleaned = new MultiDictionary();

			foreach (var language in loaded)
			{
				if (language.Value == null)
					continue;

				var languageName = defaultTokens.Keys.FirstOrDefault(x => string.Equals(x, language.Key, StringComparison.OrdinalIgnoreCase)) ?? language.Key;
				defaultTokens.TryGetValue(languageName, out var builtIn);

				foreach (var token in language.Value)
				{
					// (specter) Old dumps were written before Plugin.cs was UTF-8, so their accents became U+FFFD
					if (token.Value == null || token.Value.Contains('\uFFFD'))
						continue;

					if (builtIn != null && builtIn.TryGetValue(token.Key, out var builtInValue) && builtInValue == token.Value)
						continue;

					if (!cleaned.ContainsKey(languageName))
						cleaned.Add(languageName, new StringDictionary());

					cleaned[languageName][token.Key] = token.Value;
				}
			}

			return cleaned;
		}

		//-----------------------------------------------------------------------------------------------------------

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
				{ "XSS_EMPTY_SCREENS", "Remove empty screens" },
				{ "XSS_STARTING_IN", "Starting in {0}... release your region to cancel" },
				{ "XSS_SWAP_DISPLAY", "Move to Another Display" },
				{ "XSS_PROFILE", "Profile" },
				{ "XSS_CONFIGURE", "Configure" },
				{ "XSS_EMPTY_PROMPT_TITLE", "Start with unassigned screens?" },
				{ "XSS_EMPTY_PROMPT_BODY", "Some screen slots have no player and will stay black." },
				{ "XSS_CONFIRM", "Confirm" },
				{ "XSS_DECLINE", "Decline" },
				{ "XSS_ONLINE", "Online" },
				{ "XSS_BACKUP_WARN", "Multiple instances of corrupted profiles and lost save data have been reported from users playing in the official local splitscreen mode on consoles. <br><br>XSplitscreen makes no guarantee concerning the safety of your profiles. It is HIGHLY RECOMMENDED to create backups before each session. <br><br>If you don't have a separate profile for every player, create them from the main menu BEFORE setting up splitscreen - claiming a screen without one can leave a corrupted profile behind." },
				{ "XSS_NEED_PROFILE", "Create a profile from the main menu first" },
				{ "XSS_CREDITS", "Credits" },
				{ "XSS_CREDITS_HOVER", "Enable or disable credits" },
				{ "XSS_SELECT_MONITOR", "Select Monitor" },
			});

			// Portuguese (Brazilian)
			string ptBrKey = "pt-BR";
			tokens.Add(ptBrKey, new StringDictionary
			{
				{ "XSS_NAME_HOVER", "Modificar as configurações da tela-dividida." },
				{ "XSS_OPTION_DISCORD_HOVER", "Entre para suporte, dar feedback ou ver atualizações" },
				{ "XSS_PRESS_START_KBM", "- clique ou pressione start -" },
				{ "XSS_PRESS_START", "- pressione start -" },
				{ "XSS_CONFIG_PROFILE", "Convidado" },
				{ "XSS_CONFIG_COLOR", "Cor" },
				{ "XSS_OPTION_GM_HOVER", "Selecionar Modo de Jogo" },
				{ "XSS_OPTION_MMM", "Modo Multi-Monitor" },
				{ "XSS_OPTION_MMM_HOVER", "Ativar o Modo Multi-Monitor (não pode ser desativado)" },
				{ "XSS_TRAILS", "Rastros" },
				{ "XSS_SELECT_SCREEN", "Selecionar Tela" },
				{ "XSS_READY", "Pronto" },
				{ "XSS_EMPTY_SCREENS", "Remova as telas vazias" },
				{ "XSS_STARTING_IN", "Começando em {0}... solte sua região para cancelar" },
				{ "XSS_SWAP_DISPLAY", "Mover para Outra Tela" },
				{ "XSS_PROFILE", "Perfil" },
				{ "XSS_CONFIGURE", "Configurar" },
				{ "XSS_EMPTY_PROMPT_TITLE", "Começar com telas sem jogador?" },
				{ "XSS_EMPTY_PROMPT_BODY", "Algumas áreas da tela não têm jogador e ficarão pretas." },
				{ "XSS_CONFIRM", "Confirmar" },
				{ "XSS_DECLINE", "Recusar" },
				{ "XSS_ONLINE", "Online" },
				{ "XSS_BACKUP_WARN", "Vários casos de perfis corrompidos e perda de dados salvos foram relatados por jogadores usando o modo oficial de tela dividida local nos consoles. <br><br>O XSplitscreen não oferece nenhuma garantia quanto à segurança dos seus perfis. É ALTAMENTE RECOMENDADO fazer backups antes de cada sessão. <br><br>Se você não tiver um perfil separado para cada jogador, crie-os no menu principal ANTES de configurar a tela dividida - escolher uma tela sem perfil pode deixar um perfil corrompido." },
				{ "XSS_NEED_PROFILE", "Crie um perfil no menu principal primeiro" },
				{ "XSS_CREDITS", "Créditos" },
				{ "XSS_CREDITS_HOVER", "Ativar ou desativar os créditos" },
				{ "XSS_SELECT_MONITOR", "Selecionar Monitor" },
			});

			// French
			string frKey = "FR";
			tokens.Add(frKey, new StringDictionary
			{
				{ "XSS_NAME_HOVER", "Modifier les paramètres de splitscreen." },
				{ "XSS_OPTION_DISCORD_HOVER", "Rejoignez nous pour du support, du feedback ou des mises à jour" },
				{ "XSS_PRESS_START_KBM", "- cliquez ou appuyez sur start -" },
				{ "XSS_PRESS_START", "- appuyez sur start -" },
				{ "XSS_CONFIG_PROFILE", "Invité" },
				{ "XSS_CONFIG_COLOR", "Couleur" },
				{ "XSS_OPTION_GM_HOVER", "Choisir le mode de jeu" },
				{ "XSS_OPTION_MMM", "Mode Multi-Écrans" },
				{ "XSS_OPTION_MMM_HOVER", "Activer le Mode Multi-Écrans (ne peut pas être désactivé)" },
				{ "XSS_TRAILS", "Traînées" },
				{ "XSS_SELECT_SCREEN", "Choisir l'écran" },
				{ "XSS_READY", "Prêt" },
				{ "XSS_EMPTY_SCREENS", "Retirez les écrans vides" },
				{ "XSS_STARTING_IN", "Lancement dans {0}... libérez votre zone pour annuler" },
				{ "XSS_SWAP_DISPLAY", "Changer d'écran" },
				{ "XSS_PROFILE", "Profil" },
				{ "XSS_CONFIGURE", "Configurer" },
				{ "XSS_EMPTY_PROMPT_TITLE", "Commencer avec des écrans non attribués ?" },
				{ "XSS_EMPTY_PROMPT_BODY", "Certaines zones d'écran n'ont pas de joueur et resteront noires." },
				{ "XSS_CONFIRM", "Confirmer" },
				{ "XSS_DECLINE", "Refuser" },
				{ "XSS_ONLINE", "En ligne" },
				{ "XSS_BACKUP_WARN", "De nombreux cas de profils corrompus et de sauvegardes perdues ont été signalés par des joueurs utilisant le mode écran partagé local officiel sur consoles. <br><br>XSplitscreen ne garantit en rien la sécurité de vos profils. Il est FORTEMENT RECOMMANDÉ de faire des sauvegardes avant chaque session. <br><br>Si vous n'avez pas un profil distinct pour chaque joueur, créez-les depuis le menu principal AVANT de configurer l'écran partagé - choisir un écran sans profil peut laisser un profil corrompu." },
				{ "XSS_NEED_PROFILE", "Créez d'abord un profil depuis le menu principal" },
				{ "XSS_CREDITS", "Crédits" },
				{ "XSS_CREDITS_HOVER", "Activer ou désactiver les crédits" },
				{ "XSS_SELECT_MONITOR", "Choisir le moniteur" },
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
		/// Load the logging options from the Config file
		/// </summary>
		/// <returns></returns>
		private static bool LoadLogger()
		{
			// Note: the old "Log Channels" section is no longer read (everything was on by default)

			var debug = Singleton.Config.Bind("Logging", "Debug", false,
				"Print verbose debug messages. Only useful for development.");

			var diagnostics = Singleton.Config.Bind("Logging", "Diagnostics", false,
				"Print extra information about controllers, players and menus. Turn this on when reporting a bug and include the log file.");

			Log.Init(Singleton.Logger, debug.Value, diagnostics.Value);

			return true;
		}

		//-----------------------------------------------------------------------------------------------------------

		/// <summary>
		/// Names of hooks that could not be applied
		/// </summary>
		internal static readonly List<string> FailedHooks = new List<string>();

		/// <summary>
		/// Patch a method, logging a clear error instead of throwing if the game changed
		/// </summary>
		/// <param name="original">Method to patch (may be null if it was not found)</param>
		/// <param name="prefix"></param>
		/// <param name="postfix"></param>
		/// <param name="finalizer"></param>
		/// <returns>true if the patch was applied</returns>
		internal static bool TryPatch(MethodBase original, MethodInfo prefix = null, MethodInfo postfix = null, MethodInfo finalizer = null)
		{
			string name = original != null ? $"{original.DeclaringType?.Name}.{original.Name}" : (prefix ?? postfix ?? finalizer)?.Name ?? "unknown";

			try
			{
				if (original == null)
					throw new MissingMethodException("Target method not found (the game may have changed)");

				Patcher.Patch(original,
					prefix: prefix == null ? null : new HarmonyLib.HarmonyMethod(prefix),
					postfix: postfix == null ? null : new HarmonyLib.HarmonyMethod(postfix),
					finalizer: finalizer == null ? null : new HarmonyLib.HarmonyMethod(finalizer));

				Log.Print($"Hooked '{name}'");

				return true;
			}
			catch (Exception e)
			{
				if (!FailedHooks.Contains(name))
					FailedHooks.Add(name);

				Log.Print($"Could not hook '{name}', the related feature will not work: {e.Message}", Log.ELogChannel.Error);

				return false;
			}
		}

		/// <summary>
		/// Remove a patch applied with TryPatch
		/// </summary>
		/// <param name="original"></param>
		/// <param name="patch"></param>
		internal static void TryUnpatch(MethodBase original, MethodInfo patch)
		{
			if (original == null || patch == null)
				return;

			try
			{
				Patcher.Unpatch(original, patch);
			}
			catch (Exception e)
			{
				Log.Print($"Could not unhook '{original.DeclaringType?.Name}.{original.Name}': {e.Message}", Log.ELogChannel.Error);
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

                var resourceName = assembly.GetManifestResourceNames().First(x => x.EndsWith("xsplitscreenbundle"));
                var resourceStream = assembly.GetManifestResourceStream(resourceName);

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

			TryPatch(ror2AppResizePlayersOriginal, prefix: ror2AppResizePlayersPatch);

			// Add Rewired players

			var rewiredClonePlayersOriginal = typeof(InputManager_Base).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
			var rewiredClonePlayersPatch = typeof(Plugin).GetMethod("RewiredClonePlayers", BindingFlags.Static | BindingFlags.NonPublic);

			TryPatch(rewiredClonePlayersOriginal, prefix: rewiredClonePlayersPatch);

            // UI patches

            var mmcInteractableOriginal = typeof(RoR2.UI.MainMenu.MainMenuController).GetMethod("SetButtonsInteractible", BindingFlags.Instance | BindingFlags.Public);
            var mmcInteractablePatch = typeof(Plugin).GetMethod("SetButtonInteractable", BindingFlags.Static | BindingFlags.NonPublic);

            TryPatch(mmcInteractableOriginal, prefix: mmcInteractablePatch);


			// Destroy additional canvases

			var cscOriginal = typeof(RoR2.UI.CharacterSelectController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
			var cscPatch = typeof(Plugin).GetMethod("CharacterSelectController_Awake", BindingFlags.Static | BindingFlags.NonPublic);

			TryPatch(cscOriginal, prefix: cscPatch);

			// Set local user names

			var lunOriginal = typeof(RoR2.NetworkUser).GetMethod("UpdateUserName", BindingFlags.Instance | BindingFlags.Public);
			var lunPatch = typeof(Plugin).GetMethod("NetworkUser_UpdateUserName", BindingFlags.Static | BindingFlags.NonPublic);

			TryPatch(lunOriginal, postfix: lunPatch);

			// (specter) Vanilla's ProfileNameLabel shows the machine's single Steam name on every pod -
			// swap in each local player's profile name.
			var pnlOriginal = typeof(RoR2.UI.ProfileNameLabel).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
			var pnlPatch = typeof(Plugin).GetMethod("ProfileNameLabel_LateUpdate", BindingFlags.Static | BindingFlags.NonPublic);

			TryPatch(pnlOriginal, postfix: pnlPatch);

			// Combat health bar fix

			var chbOriginal = typeof(RoR2.UI.CombatHealthBarViewer).GetMethod("SetLayoutHorizontal", BindingFlags.Instance | BindingFlags.Public);
			var chbPatch = typeof(Plugin).GetMethod("CombatHealthBarViewer_SetLayoutHorizontal", BindingFlags.Static | BindingFlags.NonPublic);

			TryPatch(chbOriginal, prefix: chbPatch);

			// Nameplate color
			var npcOriginal = typeof(RoR2.UI.Nameplate).GetMethod("Initialize", BindingFlags.Instance | BindingFlags.Public);
			var npcPatch = typeof(Plugin).GetMethod("Nameplate_SetBody", BindingFlags.Static | BindingFlags.NonPublic);

			TryPatch(npcOriginal, prefix: npcPatch);

			// Patch Get Multiplayer Color
			var mcOriginal = typeof(RoR2.ColorCatalog).GetMethod("GetMultiplayerColor", BindingFlags.Static | BindingFlags.Public);
			var mcPatch = typeof(Plugin).GetMethod("ColorCatalog_GetMultiplayerColor", BindingFlags.Static | BindingFlags.NonPublic);

			TryPatch(mcOriginal, prefix: mcPatch);

			// (specter) Guard against a vanilla camera/entitlement race - see RunCameraManager_Update_Finalizer.
			var rcmOriginal = typeof(RoR2.RunCameraManager).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
			var rcmFinalizer = typeof(Plugin).GetMethod("RunCameraManager_Update_Finalizer", BindingFlags.Static | BindingFlags.NonPublic);

			TryPatch(rcmOriginal, finalizer: rcmFinalizer);

			// (specter) Fit the rule grids to each player's viewport - see RuleCategoryController_SetupGridVoteDisplay.
			var rccOriginal = typeof(RoR2.UI.RuleCategoryController).GetMethod("SetupGridVoteDisplay", BindingFlags.Instance | BindingFlags.NonPublic);
			var rccPatch = typeof(Plugin).GetMethod("RuleCategoryController_SetupGridVoteDisplay", BindingFlags.Static | BindingFlags.NonPublic);

			TryPatch(rccOriginal, postfix: rccPatch);

			// (specter) Same fix for the crafting panel - see CraftingPanel_Awake.
			var cpOriginal = typeof(RoR2.UI.CraftingPanel).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
			var cpPatch = typeof(Plugin).GetMethod("CraftingPanel_Awake", BindingFlags.Static | BindingFlags.NonPublic);

			TryPatch(cpOriginal, postfix: cpPatch);
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
            MainMenuTitleButton = titleButton;
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

				// (specter) Scoped to Main Panel instead of the whole canvas, since Notification/Credits
				// Panel rely on seeing through to whatever's behind the canvas at their edges.
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


		#region Event Handlers
        /// <summary>
        /// Try to create the main menu UI
        /// </summary>
		private static void OnMainMenuInitialized()
        {
			if (!LoggedGameBuild)
			{
				LoggedGameBuild = true;
				Log.Print($"Game build: {RoR2.RoR2Application.GetBuildId()}", Log.ELogChannel.Info);
			}

			Diagnostics.SubscribeToControllerEvents();
			Diagnostics.ReportControllers("main menu loaded");

			// (specter) Also fires after quitting a run - without this, controllers stay assigned to
			// splitscreen players and can't navigate the main menu. Local users can only be
			// changed on the title screen, so this is where the splitscreen session ends.
			if (SplitscreenUserManager.IsSplitscreenEnabled)
				SplitscreenUserManager.DisableSplitscreen();

            if (MainMenuTitleButton != null)
                return;

			UIHelper.Initialize();

            CreateTitleUI();
		}
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

		// (specter) Vanilla indexes its camera dictionary before every local player has a camera
		// (entitlement race) - swallow that KeyNotFoundException and let the next frame retry.
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

		/// <summary>
		/// Show profile names instead of the platform name for local splitscreen players
		/// (postfix: the vanilla method overwrites userName)
		/// </summary>
		private static void NetworkUser_UpdateUserName(RoR2.NetworkUser __instance)
		{
			ApplyLocalProfileName(__instance, 60);
		}

		private static void ApplyLocalProfileName(RoR2.NetworkUser networkUser, int framesLeft)
		{
			if (!networkUser || !SplitscreenUserManager.IsSplitscreenEnabled)
				return;

			if (networkUser.isLocalPlayer && networkUser.localUser?.userProfile != null)
			{
				networkUser.userName = networkUser.localUser.userProfile.name;
			}
			else if (framesLeft > 0)
			{
				// (specter) isLocalPlayer and localUser both link a few frames after the network user spawns
				ExecuteNextFrame.Invoke(() => ApplyLocalProfileName(networkUser, framesLeft - 1));
			}
		}

		private static void ProfileNameLabel_LateUpdate(RoR2.UI.ProfileNameLabel __instance)
		{
			var locator = __instance.GetComponent<MPEventSystemLocator>();
			var localUser = locator?.eventSystem?.localUser;

			if (localUser?.userProfile == null)
				return;

			var label = __instance.GetComponent<TMPro.TextMeshProUGUI>();
			var token = __instance.token;

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

		private static readonly FieldInfo RuleCategoryPopoutInstanceField =
			typeof(RoR2.UI.RuleCategoryController).GetField("popoutPanelInstance", BindingFlags.Instance | BindingFlags.NonPublic);

		// (specter) The rule grids, their popouts and the crafting panel are sized for a full window, so fit them to each player's viewport.
		private static void RuleCategoryController_SetupGridVoteDisplay(RoR2.UI.RuleCategoryController __instance)
		{
			AddFitToViewport(__instance.voteResultGridContainer, 0.9f, 0.22f);
			AddFitToViewport((RuleCategoryPopoutInstanceField?.GetValue(__instance) as HGPopoutPanel)?.transform, 0.95f, 0.9f);
		}

		private static void CraftingPanel_Awake(RoR2.UI.CraftingPanel __instance)
		{
			AddFitToViewport(__instance.transform, 0.95f, 0.9f);
		}

		private static void AddFitToViewport(Transform target, float widthFraction, float heightFraction)
		{
			if (target == null || target.GetComponent<FitToViewport>())
				return;

			var fit = target.gameObject.AddComponent<FitToViewport>();
			fit.widthFraction = widthFraction;
			fit.heightFraction = heightFraction;
		}

		/// <summary>
		/// Update the interactable state of the splitscreen button in the title menu
		/// </summary>
		/// <param name="__instance"></param>
		/// <param name="__0"></param>
		private static void SetButtonInteractable(MainMenuController __instance, bool __0)
        {
            if (MainMenuTitleButton)
                MainMenuTitleButton.interactable = __0;
		}

		//-----------------------------------------------------------------------------------------------------------

		/// <summary>
		/// Add players to the Rewired Input Manager
		/// </summary>
		/// <param name="__instance"></param>
		private static void RewiredClonePlayers(InputManager_Base __instance)
		{
			// (specter) Assumes Rewired starts with exactly PlayerMain/Player2/Player3 - log loudly if not,
			// since bailing here silently disables the whole mod.
            if (__instance.userData.players.Count != 3)
            {
				Log.Print($"RewiredClonePlayers: expected exactly 3 default Rewired players, found {__instance.userData.players.Count} - skipping player cloning. Splitscreen beyond 1 player will not work.", Log.ELogChannel.Error);
                return;
            }

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
			// (specter) readonly in the live game assembly (not in this GameLibs stub) - reflection
			// SetValue bypasses that regardless.
			var appType = typeof(RoR2.RoR2Application);
			appType.GetField("maxPlayers", BindingFlags.Static | BindingFlags.Public).SetValue(null, HardMaxPlayers);
			appType.GetField("hardMaxPlayers", BindingFlags.Static | BindingFlags.Public).SetValue(null, HardMaxPlayers);
			appType.GetField("maxLocalPlayers", BindingFlags.Static | BindingFlags.Public).SetValue(null, HardMaxPlayers);

			TryUnpatch(typeof(RoR2.RoR2Application).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic),
				typeof(Plugin).GetMethod("RoR2AppResizePlayers", BindingFlags.Static | BindingFlags.NonPublic));
		}

		#endregion
	}
}