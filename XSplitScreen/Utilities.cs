using RoR2.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Dodad.XSplitscreen
{
	internal static class Utilities
	{
		/// <summary>
		/// Sets the child RectTransform to fill a percentage of the parent's area with a uniform margin.
		/// For example, margin = 0.9 means the child fills 90% of parent (10% margin).
		/// This preserves the aspect ratio correctly and centers the child in the parent.
		/// </summary>
		/// <param name="child">The child RectTransform to size</param>
		/// <param name="fillRatio">The ratio of parent space to fill (0-1)</param>
		public static void SetChildRectToPixelMargin(RectTransform child, float marginPx)
		{
			if (child == null) return;
			RectTransform parent = child.parent as RectTransform;
			if (parent == null) return;

			float parentWidth = parent.rect.width;
			float parentHeight = parent.rect.height;

			float childWidth = Mathf.Max(0, parentWidth - 2 * marginPx);
			float childHeight = Mathf.Max(0, parentHeight - 2 * marginPx);

			child.anchorMin = child.anchorMax = new Vector2(0.5f, 0.5f);
			child.pivot = new Vector2(0.5f, 0.5f);
			child.anchoredPosition = Vector2.zero;
			child.sizeDelta = new Vector2(childWidth, childHeight);
		}

		/// <summary>
		/// Utility class for finding the first Selectable UI element under a virtual cursor within a container.
		/// Highly optimized for frequent calls (10+ per frame).
		/// </summary>
		public static class Hovercast
		{
			private static readonly List<Selectable> selectables = new List<Selectable>();

			/// <summary>
			/// Returns the topmost interactable Selectable under the virtual cursor within the container, or null.
			/// </summary>
			public static Selectable GetSelectableUnderCursor(RectTransform container, RectTransform virtualCursor)
			{
				if (container == null || virtualCursor == null)
					return null;

				// (specter) Checks every Selectable directly. Layout groups let buttons overflow their parent's rect
				// (the Game Mode and Credits buttons do), so walking down through parent rects missed them.
				container.GetComponentsInChildren(false, selectables);

				Selectable hovered = null;

				// Later in the hierarchy draws on top, and a carousel's arrow buttons come after the carousel itself
				for (int i = selectables.Count - 1; i >= 0; i--)
				{
					var selectable = selectables[i];
					var rect = (RectTransform) selectable.transform;

					if (!selectable.IsInteractable() || !Contains(rect, virtualCursor.position))
						continue;

					// Skip buttons scrolled out of view in a masked list
					var mask = selectable.GetComponentInParent<RectMask2D>();
					if (mask == null || Contains(mask.rectTransform, virtualCursor.position))
					{
						hovered = selectable;
						break;
					}
				}

				selectables.Clear();

				return hovered;
			}

			private static bool Contains(RectTransform rect, Vector3 worldPosition) => rect.rect.Contains(rect.InverseTransformPoint(worldPosition));
		}
	}
}
