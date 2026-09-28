using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.IO;

/// <summary>
/// Rebuilds userslot.prefab from scratch to match the approved mockup
/// (claude.ai/artifact/StMo5jPYqadb4LKEvLCDed, project/Redesign.dc.html + FullScreen.dc.html):
/// a compact collapsed row (color-swatch outline, name, ready check, Y-toggle-with-chevron) that
/// expands in place to reveal a persistent tab bar (LB chevron / Profile-Color-Trails tabs with
/// active underline + per-tab checkmark / RB chevron) and a per-tab content panel (up/down arrows
/// + value + A for Profile/Trails; left/right arrows + full-spectrum hue bar + swatch + A for
/// Color). Colors/text match the mockup's hex values exactly; TMP fonts fall back to the game's
/// default (Baloo 2 in the mockup CSS is almost certainly just an approximation of RoR2's own
/// existing bold rounded UI font, not a real new asset - not sourced here).
/// </summary>
public static class RebuildUserslotForRedesign
{
    // Mockup colors
    static readonly Color BG = HexColor("#060606");
    static readonly Color TEXT = HexColor("#eef0f2");
    static readonly Color MUTED = HexColor("#8a8d96");
    static readonly Color BORDER = HexColor("#24262b");
    static readonly Color OUTLINE = HexColor("#47555f");
    static readonly Color ACCENT_RED = HexColor("#e6453e");
    static readonly Color CONFIRM_GREEN = HexColor("#3ad15c");

    // (specter) Fixed height for every tab's content panel, so switching tabs never resizes
    // the row. Shrunk from 90 now that both panel shapes are a single row (MakeValuePanel/
    // MakeColorPanel).
    const float PanelHeight = 46;

    public static void Run()
    {
        try
        {
            RunInner();
            EditorApplication.Exit(0);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"RebuildUserslotForRedesign failed: {e}");
            EditorApplication.Exit(1);
        }
    }

    private static void RunInner()
    {
        string path = "Assets/XSplitScreen/RealPrefabs/userslot.prefab";
        var root = new GameObject("userslot", typeof(RectTransform));
        var rootRt = root.GetComponent<RectTransform>();
        rootRt.sizeDelta = new Vector2(720, 60); // collapsed height; grows via ContentSizeFitter when expanded

        var rootImage = root.AddComponent<Image>();
        rootImage.color = new Color(0, 0, 0, 0); // transparent background, per-row look comes from CollapsedRow's left border

        var rootVertical = root.AddComponent<VerticalLayoutGroup>();
        rootVertical.childForceExpandWidth = true;
        rootVertical.childForceExpandHeight = false;
        rootVertical.childControlWidth = true;
        rootVertical.childControlHeight = true;
        rootVertical.spacing = 0;

        var rootFitter = root.AddComponent<ContentSizeFitter>();
        rootFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // ---- Collapsed row ----
        // (specter) Height reverted to 44 - a prior pass shrunk it to save vertical space, but
        // the actual goal was a narrower row (slots are arranged into 2 columns, see
        // LocalUserPanel), not a shorter one.
        var collapsedRow = MakeChild(root.transform, "CollapsedRow");
        var collapsedLayout = collapsedRow.AddComponent<LayoutElement>();
        collapsedLayout.preferredHeight = 44;
        collapsedLayout.minHeight = 44;
        var collapsedImage = collapsedRow.AddComponent<Image>();
        collapsedImage.color = new Color(1, 1, 1, 0.02f); // near-invisible, just gives the row a hit target
        var collapsedHorizontal = collapsedRow.AddComponent<HorizontalLayoutGroup>();
        collapsedHorizontal.padding = new RectOffset(10, 10, 0, 0);
        collapsedHorizontal.spacing = 12;
        collapsedHorizontal.childAlignment = TextAnchor.MiddleLeft;
        collapsedHorizontal.childForceExpandWidth = false;
        collapsedHorizontal.childForceExpandHeight = true;
        collapsedHorizontal.childControlWidth = true;
        collapsedHorizontal.childControlHeight = true;

        // Left accent border strip (the "border-left: 3px solid accent" from the mockup)
        var accentStrip = MakeChild(collapsedRow.transform, "AccentStrip");
        var accentImage = accentStrip.AddComponent<Image>();
        accentImage.color = Color.white; // tinted per-player at runtime
        SetPreferredSize(accentStrip, 3, 0);

        // Device icon - tinted to the player's chosen color at runtime (see
        // LocalUserSlot.UpdateDeviceIconAlpha), which already does the job a separate color
        // swatch would - no need for a second color indicator next to it.
        var deviceIcon = MakeChild(collapsedRow.transform, "DeviceIcon");
        SetPreferredSize(deviceIcon, 35, 35);
        var deviceIconImg = deviceIcon.AddComponent<Image>();
        deviceIconImg.color = TEXT;
        deviceIconImg.preserveAspect = true; // device icon PNGs aren't square - don't squish them into the box

        // Name text
        var nameGO = MakeText(collapsedRow.transform, "NameText", "Player", 17, TEXT, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        var nameLayout = nameGO.AddComponent<LayoutElement>();
        nameLayout.flexibleWidth = 0;
        nameLayout.minWidth = 120;

        // Title/message text - reused by SlotOptions.SetMessage for "press start" / profile-list
        // placeholder text when the slot is empty. Occupies the same slot as NameText but is a
        // distinct object so empty-slot vs occupied-slot text can coexist without stomping.
        var titleGO = MakeText(collapsedRow.transform, "TitleText", "", 15, TEXT, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        var titleLayout = titleGO.AddComponent<LayoutElement>();
        titleLayout.flexibleWidth = 1;

        // Ready checkmark
        var readyGO = MakeText(collapsedRow.transform, "ReadyCheck", "✓", 18, CONFIRM_GREEN, FontStyles.Bold, TextAlignmentOptions.Midline);
        SetPreferredSize(readyGO, 20, 20);

        // Spacer
        var spacer = MakeChild(collapsedRow.transform, "Spacer");
        var spacerLayout = spacer.AddComponent<LayoutElement>();
        spacerLayout.flexibleWidth = 1;

        // Y toggle: thin circle "Y" + chevron, both driven by code (rotation for chevron)
        var yToggle = MakeChild(collapsedRow.transform, "YToggle");
        // MPButton is added at runtime by LocalUserSlot.cs (RoR2 types are not available in this Unity project)
        var yToggleHorizontal = yToggle.AddComponent<HorizontalLayoutGroup>();
        yToggleHorizontal.spacing = 6;
        yToggleHorizontal.childAlignment = TextAnchor.MiddleCenter;
        yToggleHorizontal.childForceExpandWidth = false;
        yToggleHorizontal.childForceExpandHeight = false;
        yToggleHorizontal.childControlWidth = true;
        yToggleHorizontal.childControlHeight = true;

        var yCircle = MakeChild(yToggle.transform, "YCircle");
        SetPreferredSize(yCircle, 20, 20);
        var yCircleImg = yCircle.AddComponent<Image>();
        yCircleImg.color = new Color(0, 0, 0, 0);
        // outline via a thin child inset - simplest robust approach: two nested images like the swatch
        var yCircleOutline = yCircle.AddComponent<Outline>();
        yCircleOutline.effectColor = OUTLINE;
        yCircleOutline.effectDistance = new Vector2(1, -1);
        var yLabel = MakeText(yCircle.transform, "Label", "Y", 11, MUTED, FontStyles.Bold, TextAlignmentOptions.Midline);
        var yLabelRt = yLabel.GetComponent<RectTransform>();
        yLabelRt.anchorMin = Vector2.zero;
        yLabelRt.anchorMax = Vector2.one;
        yLabelRt.offsetMin = Vector2.zero;
        yLabelRt.offsetMax = Vector2.zero;

        var yChevron = MakeText(yToggle.transform, "Chevron", "▼", 13, MUTED, FontStyles.Normal, TextAlignmentOptions.Midline);
        SetPreferredSize(yChevron, 16, 20);

        // ---- Expanded content (hidden by default) ----
        var expandedContent = MakeChild(root.transform, "ExpandedContent");
        var expandedVertical = expandedContent.AddComponent<VerticalLayoutGroup>();
        // (specter) Tightened from (10,10,12,12)/16 alongside the single-row panel shapes below.
        expandedVertical.padding = new RectOffset(10, 10, 8, 8);
        expandedVertical.spacing = 10;
        expandedVertical.childForceExpandWidth = true;
        expandedVertical.childForceExpandHeight = false;
        expandedVertical.childControlWidth = true;
        expandedVertical.childControlHeight = true;
        var expandedFitter = expandedContent.AddComponent<ContentSizeFitter>();
        expandedFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var separator = MakeChild(expandedContent.transform, "Separator");
        SetPreferredSize(separator, 0, 1);
        var separatorLayout = separator.GetComponent<LayoutElement>();
        separatorLayout.flexibleWidth = 1;
        var separatorImg = separator.AddComponent<Image>();
        separatorImg.color = BORDER;

        // Tab bar: LB chevron, N tabs, RB chevron
        var tabBar = MakeChild(expandedContent.transform, "TabBar");
        var tabBarHorizontal = tabBar.AddComponent<HorizontalLayoutGroup>();
        tabBarHorizontal.spacing = 14;
        tabBarHorizontal.childAlignment = TextAnchor.MiddleCenter;
        tabBarHorizontal.childForceExpandWidth = false;
        tabBarHorizontal.childForceExpandHeight = true;
        tabBarHorizontal.childControlWidth = true;
        tabBarHorizontal.childControlHeight = true;

        var lbButton = MakeShoulderButton(tabBar.transform, "LBButton", "‹", "LB");
        var tabsContainer = MakeChild(tabBar.transform, "TabsContainer");
        var tabsLayout = tabsContainer.AddComponent<LayoutElement>();
        tabsLayout.flexibleWidth = 1;
        var tabsHorizontal = tabsContainer.AddComponent<HorizontalLayoutGroup>();
        tabsHorizontal.spacing = 26;
        tabsHorizontal.childAlignment = TextAnchor.MiddleCenter;
        tabsHorizontal.childForceExpandWidth = false;
        tabsHorizontal.childForceExpandHeight = true;
        tabsHorizontal.childControlWidth = true;
        tabsHorizontal.childControlHeight = true;
        // 3 tab slots are pre-built (Profile/Color/Trails, matching the 3 shipped configurators);
        // SlotOptions positions/labels them from the actual cyclable list at runtime.
        for (int i = 0; i < 3; i++)
            MakeTab(tabsContainer.transform, $"Tab{i}");

        var rbButton = MakeShoulderButton(tabBar.transform, "RBButton", "›", "RB");

        // Content panels - one per configurator "shape". SlotOptions/the active configurator
        // toggles which is active; all three are pre-built here so nothing needs runtime
        // construction on tab switch.
        MakeValuePanel(expandedContent.transform, "ProfileContent");
        MakeColorPanel(expandedContent.transform, "ColorContent");
        MakeValuePanel(expandedContent.transform, "TrailsContent");

        expandedContent.SetActive(false);

        PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
        Debug.Log($"RebuildUserslotForRedesign success={success}");

        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
    }

    private static GameObject MakeShoulderButton(Transform parent, string name, string chevron, string label)
    {
        var go = MakeChild(parent, name);
        var vertical = go.AddComponent<VerticalLayoutGroup>();
        vertical.spacing = 2;
        vertical.childAlignment = TextAnchor.MiddleCenter;
        vertical.childForceExpandWidth = false;
        vertical.childForceExpandHeight = false;
        vertical.childControlWidth = true;
        vertical.childControlHeight = true;
        MakeText(go.transform, "Chevron", chevron, 16, MUTED, FontStyles.Normal, TextAlignmentOptions.Midline);
        MakeText(go.transform, "Label", label, 9, MUTED, FontStyles.Bold, TextAlignmentOptions.Midline);
        return go;
    }

    private static GameObject MakeTab(Transform parent, string name)
    {
        var go = MakeChild(parent, name);
        var vertical = go.AddComponent<VerticalLayoutGroup>();
        vertical.spacing = 8;
        vertical.childAlignment = TextAnchor.MiddleCenter;
        // Underline needs to stretch full tab width (a VerticalLayoutGroup only stretches
        // children's width via forceExpandWidth, not via each child's own flexibleWidth).
        vertical.childForceExpandWidth = true;
        vertical.childForceExpandHeight = false;
        vertical.childControlWidth = true;
        vertical.childControlHeight = true;

        var labelRow = MakeChild(go.transform, "LabelRow");
        var labelRowHorizontal = labelRow.AddComponent<HorizontalLayoutGroup>();
        labelRowHorizontal.spacing = 6;
        labelRowHorizontal.childAlignment = TextAnchor.MiddleCenter;
        labelRowHorizontal.childForceExpandWidth = false;
        labelRowHorizontal.childForceExpandHeight = false;
        labelRowHorizontal.childControlWidth = true;
        labelRowHorizontal.childControlHeight = true;
        MakeText(labelRow.transform, "Label", "Tab", 14, MUTED, FontStyles.Bold, TextAlignmentOptions.Midline);
        MakeText(labelRow.transform, "Check", "✓", 12, CONFIRM_GREEN, FontStyles.Bold, TextAlignmentOptions.Midline);

        var underline = MakeChild(go.transform, "Underline");
        SetPreferredSize(underline, 0, 2);
        var underlineLayout = underline.GetComponent<LayoutElement>();
        underlineLayout.flexibleWidth = 1;
        var underlineImg = underline.AddComponent<Image>();
        underlineImg.color = new Color(0, 0, 0, 0);

        return go;
    }

    /// <summary>
    /// (specter) The Profile/Trails shape: collapsed from a 3-tier vertical stack (up arrow /
    /// value+A button / down arrow) to a single horizontal row, matching MakeColorPanel's shape.
    /// </summary>
    private static GameObject MakeValuePanel(Transform parent, string name)
    {
        var go = MakeChild(parent, name);
        SetFixedPanelHeight(go);
        var horizontal = go.AddComponent<HorizontalLayoutGroup>();
        horizontal.spacing = 12;
        horizontal.childAlignment = TextAnchor.MiddleCenter;
        horizontal.childForceExpandWidth = false;
        horizontal.childForceExpandHeight = true;
        horizontal.childControlWidth = true;
        horizontal.childControlHeight = true;

        MakeText(go.transform, "UpArrow", "▲", 15, MUTED, FontStyles.Normal, TextAlignmentOptions.Midline);

        var valueText = MakeText(go.transform, "ValueText", "Value", 19, TEXT, FontStyles.Bold, TextAlignmentOptions.Midline);
        var valueLayout = valueText.AddComponent<LayoutElement>();
        valueLayout.minWidth = 140;
        valueLayout.flexibleWidth = 1;

        MakeAButton(go.transform);

        MakeText(go.transform, "DownArrow", "▼", 15, MUTED, FontStyles.Normal, TextAlignmentOptions.Midline);

        go.SetActive(false);
        return go;
    }

    /// <summary>
    /// The Color shape: [left arrow, hue bar with indicator, right arrow], then [swatch, A button].
    /// </summary>
    private static GameObject MakeColorPanel(Transform parent, string name)
    {
        var go = MakeChild(parent, name);
        SetFixedPanelHeight(go);
        var vertical = go.AddComponent<VerticalLayoutGroup>();
        vertical.spacing = 12;
        vertical.childAlignment = TextAnchor.MiddleCenter;
        vertical.childForceExpandWidth = true;
        vertical.childForceExpandHeight = false;
        vertical.childControlWidth = true;
        vertical.childControlHeight = true;

        var hueRow = MakeChild(go.transform, "HueRow");
        var hueRowHorizontal = hueRow.AddComponent<HorizontalLayoutGroup>();
        hueRowHorizontal.spacing = 16;
        hueRowHorizontal.childAlignment = TextAnchor.MiddleCenter;
        hueRowHorizontal.childForceExpandWidth = false;
        hueRowHorizontal.childForceExpandHeight = false;
        hueRowHorizontal.childControlWidth = true;
        hueRowHorizontal.childControlHeight = true;

        MakeText(hueRow.transform, "LeftArrow", "←", 15, MUTED, FontStyles.Normal, TextAlignmentOptions.Midline);

        var hueBarContainer = MakeChild(hueRow.transform, "HueBarContainer");
        var hueBarLayout = hueBarContainer.AddComponent<LayoutElement>();
        hueBarLayout.flexibleWidth = 1;
        hueBarLayout.preferredHeight = 22;
        var hueBarImg = hueBarContainer.AddComponent<Image>();
        hueBarImg.color = Color.white; // gradient sprite assigned at runtime by ColorConfigurator

        var indicator = MakeChild(hueBarContainer.transform, "Indicator");
        var indicatorRt = indicator.GetComponent<RectTransform>();
        indicatorRt.anchorMin = new Vector2(0.5f, 0f);
        indicatorRt.anchorMax = new Vector2(0.5f, 1f);
        indicatorRt.sizeDelta = new Vector2(3, 8);
        var indicatorImg = indicator.AddComponent<Image>();
        indicatorImg.color = TEXT;

        MakeText(hueRow.transform, "RightArrow", "→", 15, MUTED, FontStyles.Normal, TextAlignmentOptions.Midline);

        var swatchRow = MakeChild(go.transform, "SwatchRow");
        var swatchRowHorizontal = swatchRow.AddComponent<HorizontalLayoutGroup>();
        swatchRowHorizontal.spacing = 12;
        swatchRowHorizontal.childAlignment = TextAnchor.MiddleCenter;
        swatchRowHorizontal.childForceExpandWidth = false;
        swatchRowHorizontal.childForceExpandHeight = false;
        swatchRowHorizontal.childControlWidth = true;
        swatchRowHorizontal.childControlHeight = true;

        var swatch = MakeChild(swatchRow.transform, "Swatch");
        SetPreferredSize(swatch, 22, 22);
        var swatchImg = swatch.AddComponent<Image>();
        swatchImg.color = Color.white;

        MakeAButton(swatchRow.transform);

        go.SetActive(false);
        return go;
    }

    private static GameObject MakeAButton(Transform parent)
    {
        var go = MakeChild(parent, "AButton");
        SetPreferredSize(go, 26, 26);
        var img = go.AddComponent<Image>();
        img.color = new Color(0, 0, 0, 0);
        var outline = go.AddComponent<Outline>();
        outline.effectColor = OUTLINE;
        outline.effectDistance = new Vector2(1, -1);
        var label = MakeText(go.transform, "Label", "A", 11, MUTED, FontStyles.Bold, TextAlignmentOptions.Midline);
        var labelRt = label.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;
        return go;
    }

    private static GameObject MakeChild(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void SetPreferredSize(GameObject go, float width, float height)
    {
        var layout = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        layout.preferredWidth = width;
        layout.preferredHeight = height;
        layout.minWidth = width;
        layout.minHeight = height;
    }

    /// <summary>
    /// Pins a content panel's height to PanelHeight, leaving width alone (ExpandedContent's own
    /// VerticalLayoutGroup already stretches every panel to full width via forceExpandWidth).
    /// </summary>
    private static void SetFixedPanelHeight(GameObject go)
    {
        var layout = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        layout.preferredHeight = PanelHeight;
        layout.minHeight = PanelHeight;
    }

    private static GameObject MakeText(Transform parent, string name, string text, float size, Color color, FontStyles style, TextAlignmentOptions alignment)
    {
        var go = MakeChild(parent, name);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.fontStyle = style;
        tmp.alignment = alignment;
        tmp.raycastTarget = false;
        tmp.enableWordWrapping = false;
        return go;
    }

    private static Color HexColor(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out var c);
        return c;
    }
}
