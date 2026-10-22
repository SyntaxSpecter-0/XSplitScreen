using RoR2;
using RoR2.UI;
using RoR2.UI.MainMenu;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Dodad.XSplitscreen
{
	/// <summary>
	/// Formats and stores useful UI prefabs. This code was copied over from the 
	/// previous version of the mod and may contain issues
	/// </summary>
	internal static class UIHelper
	{
		private static Dictionary<EUIPrefabIndex, GameObject> prefabs;
		private static GameObject prefabRoot;

		// (specter) Sprites made at runtime are never cleaned up, so each texture gets one shared sprite
		private static readonly Dictionary<Texture2D, Sprite> sprites = new Dictionary<Texture2D, Sprite>();

		internal static Sprite GetSprite(Texture2D texture)
		{
			if (!sprites.TryGetValue(texture, out var sprite) || sprite == null)
			{
				sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
				sprites[texture] = sprite;
			}

			return sprite;
		}

		internal static Sprite GetSprite(string textureAsset) => GetSprite(Plugin.Resources.LoadAsset<Texture2D>(textureAsset));

		//-----------------------------------------------------------------------------------------------------------

		/// <summary>
		/// Generate templates for common UI elements
		/// </summary>
		internal static void Initialize()
		{
			prefabs = new Dictionary<EUIPrefabIndex, GameObject>();
			prefabRoot = new GameObject("XSplitscreen UI Prefabs", typeof(RectTransform));
			prefabRoot.SetActive(false);

			// Main menu button

			GameObject mainMenuButtonTemplate = GameObject.Find("GenericMenuButton (Singleplayer)");
			GameObject mainMenuScreenTemplate = MainMenuController.instance.extraGameModeMenuScreen.gameObject;

			if (mainMenuButtonTemplate == null || mainMenuScreenTemplate == null)
				return;

			GameObject mainMenuButtonPrefab = GameObject.Instantiate(mainMenuButtonTemplate);
			mainMenuButtonPrefab.name = "MainMenuButton";
			mainMenuButtonPrefab.transform.localScale = Vector3.one;

			GameObject.Destroy(mainMenuButtonPrefab.GetComponent<RoR2.ConsoleFunctions>());

			mainMenuButtonPrefab.GetComponents<ViewableTag>().ToList().ForEach(x => GameObject.Destroy(x));

			HGButton button = mainMenuButtonPrefab.GetComponent<HGButton>();

			ClearHGButton(button);

			AddPrefab(EUIPrefabIndex.MainMenuButton, mainMenuButtonPrefab);

			// Toggle

			var togglePrefab = MainMenuController.instance.multiplayerMenuScreen.GetComponentInChildren<MPToggle>(true).gameObject;

			var toggle = GameObject.Instantiate(togglePrefab).GetComponent<MPToggle>();
			toggle.name = "Toggle";
			toggle.mpControlHelper.allowAllEventSystems = true;

			var toggleRect = toggle.GetComponent<RectTransform>();

			toggleRect.pivot = Vector2.one / 2f;

			toggleRect.GetComponent<RectTransform>().anchorMax = Vector2.one;
			toggleRect.GetComponent<RectTransform>().anchorMin = Vector2.zero;

			foreach (Transform child in toggle.GetComponentsInChildren<Transform>(true))
				child.transform.localPosition = Vector3.zero;

			GameObject.Destroy(toggle.GetComponent<LayoutElement>());

			AddPrefab(EUIPrefabIndex.Toggle, toggle.gameObject);
		}

		//-----------------------------------------------------------------------------------------------------------

		/// <summary>
		/// Instantiate a UI prefab
		/// </summary>
		/// <param name="index"></param>
		/// <returns>null if not found</returns>
		internal static GameObject GetPrefab(EUIPrefabIndex index)
		{
			if (prefabs.ContainsKey(index))
				return GameObject.Instantiate(prefabs[index]);

			return null;
		}

		//-----------------------------------------------------------------------------------------------------------

		/// <summary>
		/// Clear a given HGButton making it ready for cloning
		/// </summary>
		/// <param name="button"></param>
		internal static void ClearHGButton(HGButton button, bool clearLanguageController = true)
		{
			RemoveEntitlementGates(button.gameObject);

			button.hoverToken = "XL_UNSET";
			button.requiredTopLayer = null;
			button.updateTextOnHover = false;
			button.hoverLanguageTextMeshController = null;
			button.disableGamepadClick = false;
			button.allowAllEventSystems = true;
			button.defaultFallbackButton = false;
			button.onClick?.m_PersistentCalls?.Clear();
			button.onClick?.RemoveAllListeners();
			button.onSelect = new UnityEngine.Events.UnityEvent();
			button.onDeselect = new UnityEngine.Events.UnityEvent();

			//button.onDeselect?.RemoveAllListeners();
			//button.onSelect?.RemoveAllListeners();

			if (!clearLanguageController)
				return;

			LanguageTextMeshController langController = button.GetComponent<LanguageTextMeshController>();

			if (langController)
				langController.token = "XL_UNSET";
		}

		//-----------------------------------------------------------------------------------------------------------

		/// <summary>
		/// Remove the components that hide UI for players who don't own a DLC. Cloned menu elements
		/// keep them, so without this they vanish for players without the DLC they were copied from.
		/// Removed immediately because Destroy is deferred: anything cloned in the same frame would
		/// still copy the component.
		/// </summary>
		/// <param name="root">Root of the cloned hierarchy (children are included)</param>
		internal static void RemoveEntitlementGates(GameObject root)
		{
			if (root == null)
				return;

			foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
			{
				if (component == null)
					continue;

				// DisableIfNoExpansion is [Obsolete(error: true)] in current game builds, so match it by name
				if (component is GameObjectEntitlementActivator || component.GetType().Name == "DisableIfNoExpansion")
				{
					Log.Diag($"Removed DLC gate '{component.GetType().Name}' from '{component.name}' (cloned from '{root.name}')");
					GameObject.DestroyImmediate(component);
				}
			}
		}

		//-----------------------------------------------------------------------------------------------------------

		/// <summary>
		/// Track a prefab
		/// </summary>
		/// <param name="key"></param>
		/// <param name="prefab"></param>
		/// <param name="useRefresher"></param>
		internal static void AddPrefab(EUIPrefabIndex key, GameObject prefab, bool useRefresher = true)
		{
			if (prefabs.ContainsKey(key))
				return;

			prefab.SetActive(false);
			prefab.transform.SetParent(prefabRoot.transform);

			prefabs.Add(key, prefab);
		}

		//-----------------------------------------------------------------------------------------------------------

		internal enum EUIPrefabIndex
		{
			MainMenuButton,
			SimpleText,
			Toggle,
		}
	}
}
