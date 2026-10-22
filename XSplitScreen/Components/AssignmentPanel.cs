using RoR2.UI;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Dodad.XSplitscreen.Components
{
	public class AssignmentPanel : MonoBehaviour
	{
		private static List<AssignmentPanel> Instances;

		internal SplitscreenMenuController Controller { get; private set; }

		public AssignmentDisplayController Display => _display;
		private Graph _graph;
		private AssignmentDisplayController _display;
		private Image _initialImage;
		private MPButton[] _insertButtons;
		//-----------------------------------------------------------------------------------------------------------

		public void Initialize(SplitscreenMenuController controller)
		{
			Controller = controller;
			
			Instances ??= new();
			Instances.Add(this);

			_display = transform.Find("DisplayContainer/Display").gameObject.AddComponent<AssignmentDisplayController>();

			_display.Initialize(this);

			// Initial button

			var plusSprite = UIHelper.GetSprite("plus.png");

			_initialImage = new GameObject("AddInitialButton").AddComponent<Image>();
			_initialImage.sprite = plusSprite;
			_initialImage.transform.SetParent(_display.transform.Find("VerticalLayout/Middle/AssignmentArea"));
			_initialImage.transform.localScale = Vector3.one;
			_initialImage.transform.localPosition = Vector3.zero;
			_initialImage.GetComponent<RectTransform>().sizeDelta = new Vector2(64f, 64f);
			_initialImage.gameObject.AddComponent<MPButton>().onClick.AddListener(() => AddInitialFace());

			// Reset button

			_display.transform.Find("VerticalLayout/Bottom/ResetButton").gameObject.AddComponent<MPButton>().onClick.AddListener(() => ResetGraph());

			// Insert buttons

			_insertButtons = new MPButton[4];
			_insertButtons[0] = _display.transform.Find("VerticalLayout/Top/InsertTopButton").gameObject.AddComponent<MPButton>();
			_insertButtons[1] = _display.transform.Find("VerticalLayout/Bottom/InsertBottomButton").gameObject.AddComponent<MPButton>();
			_insertButtons[2] = _display.transform.Find("VerticalLayout/Middle/InsertLeftButton").gameObject.AddComponent<MPButton>();
			_insertButtons[3] = _display.transform.Find("VerticalLayout/Middle/InsertRightButton").gameObject.AddComponent<MPButton>();

			_insertButtons[0].onClick.AddListener(() => InsertFromEdge("top"));
			_insertButtons[1].onClick.AddListener(() => InsertFromEdge("bottom"));
			_insertButtons[2].onClick.AddListener(() => InsertFromEdge("left"));
			_insertButtons[3].onClick.AddListener(() => InsertFromEdge("right"));

			// Juice

			var canvas = gameObject.AddComponent<CanvasGroup>();
			var juice = gameObject.AddComponent<UIJuice>();

			juice.canvasGroup = canvas;
			juice.transitionDuration = 0.5f;
			juice.originalAlpha = 1f;

			SplitscreenMenuController.Singleton.onEnter.AddListener(() =>
			{
				juice.TransitionAlphaFadeIn();
			});

			_graph = new(1);
			UpdateButtonStates();
		}

		private void Start()
		{
			if (Display.Panel.Controller.monitorId != 0)
				return;

			var toggleContainer = _display.transform.Find("VerticalLayout/Bottom/Filler");
			var containerLayout = toggleContainer.gameObject.AddComponent<HorizontalLayoutGroup>();
			containerLayout.spacing = 24;

			var onlineToggle = UIHelper.GetPrefab(UIHelper.EUIPrefabIndex.Toggle);
			onlineToggle.transform.SetParent(toggleContainer.transform);
			onlineToggle.transform.localPosition = Vector3.zero;
			onlineToggle.transform.localScale = Vector3.one;
			var toggleLayout = onlineToggle.AddComponent<LayoutElement>();
			toggleLayout.flexibleWidth = 1;
			toggleLayout.preferredWidth = 96;

			var toggle = onlineToggle.GetComponentInChildren<MPToggle>();
			toggle.SetIsOnWithoutNotify(SplitscreenUserManager.OnlineMode);
			toggle.onValueChanged.AddListener((x) =>
			{
				Log.Print($"Updating online to '{x}'");
				SplitscreenUserManager.OnlineMode = x;
			});
			onlineToggle.gameObject.SetActive(true);

			var onlineText = UIHelper.GetPrefab(UIHelper.EUIPrefabIndex.SimpleText);
			onlineText.transform.SetParent(toggleContainer.transform);
			onlineText.transform.localPosition = Vector3.zero;
			onlineText.transform.localScale = Vector3.one;
			onlineText.GetComponent<HGTextMeshProUGUI>().enableWordWrapping = false;
			onlineText.GetComponent<HGTextMeshProUGUI>().raycastTarget = false;
			onlineText.GetComponent<LanguageTextMeshController>().token = "XSS_ONLINE";
			onlineText.gameObject.SetActive(true);
		}

		//-----------------------------------------------------------------------------------------------------------

		public void OnDestroy()
		{
			Instances.Remove(this);
		}

		//-----------------------------------------------------------------------------------------------------------

		// (specter) Every screen region needs a player, or the run starts with empty viewports.
		// Counted from the graphs, since region objects are hidden while re-laying out and destroyed a frame late.
		internal static bool AllRegionsClaimed => Instances == null || Instances.Sum(panel => panel._graph.Faces.Count) <= AssignmentConfigurator.Instances.Count(x => x.IsReady);

		private void OnGraphUpdated()
		{
			_display.UpdateDisplay(_graph.Faces);
			UpdateButtonStates();

			// Removing an empty region can be what makes everyone ready
			AssignmentConfigurator.OnClaimUpdated?.Invoke(AssignmentConfigurator.Instances.Any(x => x.IsReady));
		}

		public void Subdivide(Graph.Face face, bool vertical)
		{
			_graph.Subdivide(face.Index, vertical);
			OnGraphUpdated();
		}

		public void Remove(Graph.Face face)
		{
			_graph.RemoveFace(face.Index);
			OnGraphUpdated();
		}

		private void AddInitialFace()
		{
			_graph.AddInitialFace();
			OnGraphUpdated();
		}

		private void UpdateButtonStates()
		{
			bool hasFaces = _graph.Faces.Count != 0;
			_initialImage.enabled = !hasFaces;

			foreach (var button in _insertButtons)
				button.interactable = hasFaces;
		}

		private void ResetGraph()
		{
			_graph.Clear();
			OnGraphUpdated();
		}

		private void InsertFromEdge(string edge)
		{
			_graph.InsertFromEdge(edge);
			OnGraphUpdated();
		}
	}
}
