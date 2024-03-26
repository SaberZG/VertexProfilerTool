using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEditor;

namespace VertexProfilerTool
{
	public class VertexProfilerMultiColumnTreeView : TreeViewWithTreeModel<VertexProfilerTreeElement>
	{
		const float kRowHeights = 20f;
		const float kToggleWidth = 18f;
		public int DisplayTypeIndex = 0;

		// All columns
		enum OnlyTileColumns
		{
			Threshold,
			ThresholdValue,
			TileOffset,
			VertexCount,
			Density,
		}
		enum OnlyMeshColumns
		{
			Threshold,
			ThresholdValue,
			ResourceName,
			VertexCount,
			PixelCount,
			Density,
			HierarchyPath
		}
		enum TileBasedMeshColumns
		{
			Threshold,
			ThresholdValue,
			TileOffset,
			ResourceName,
			VertexCount,
			VertexInfo,
			PixelCount,
			Density,
			HierarchyPath
		}

		public enum SortOption
		{
			Threshold,
			ThresholdValue,
			TileOffset,
			VertexCount,
			VertexInfo,
			PixelCount,
			Density,
			ResourceName,
			HierarchyPath
		}

		// Sort options per column
		private SortOption[] m_SortOptions;

		public static void TreeToList (TreeViewItem root, IList<TreeViewItem> result)
		{
			if (root == null)
				throw new NullReferenceException("root");
			if (result == null)
				throw new NullReferenceException("result");

			result.Clear();

			if (root.children == null)
				return;

			Stack<TreeViewItem> stack = new Stack<TreeViewItem>();
			for (int i = root.children.Count - 1; i >= 0; i--)
				stack.Push(root.children[i]);

			while (stack.Count > 0)
			{
				TreeViewItem current = stack.Pop();
				result.Add(current);

				if (current.hasChildren && current.children[0] != null)
				{
					for (int i = current.children.Count - 1; i >= 0; i--)
					{
						stack.Push(current.children[i]);
					}
				}
			}
		}

		public VertexProfilerMultiColumnTreeView (int displayTypeIndex, TreeViewState state, MultiColumnHeader multicolumnHeader, TreeModel<VertexProfilerTreeElement> model) : base (state, multicolumnHeader, model)
		{
			SetDisplayType(displayTypeIndex);

			// Custom setup
			rowHeight = kRowHeights;
			columnIndexForTreeFoldouts = 0;
			showAlternatingRowBackgrounds = true;
			showBorder = true;
			customFoldoutYOffset = (kRowHeights - EditorGUIUtility.singleLineHeight) * 0.5f; // center foldout in the row since we also center content. See RowGUI
			extraSpaceBeforeIconAndLabel = kToggleWidth;
			multicolumnHeader.sortingChanged += OnSortingChanged;
			Reload();
		}

		public void SetDisplayType(int displayTypeIndex)
		{
			// 对应DisplayType枚举index，数量需要完全对上不然可能报错
			DisplayTypeIndex = displayTypeIndex;
			if (DisplayTypeIndex == (int)DisplayType.OnlyTile)
			{
				m_SortOptions = new SortOption[]
				{
					SortOption.Threshold,
					SortOption.ThresholdValue,
					SortOption.TileOffset,
					SortOption.VertexCount,
					SortOption.Density
				};
			}
			else if (DisplayTypeIndex == (int)DisplayType.OnlyMesh)
			{
				m_SortOptions = new SortOption[]
				{
					SortOption.Threshold,
					SortOption.ThresholdValue,
					SortOption.ResourceName,
					SortOption.VertexCount,
					SortOption.PixelCount,
					SortOption.Density,
					SortOption.HierarchyPath
				};
			}
			else if (DisplayTypeIndex == (int)DisplayType.TileBasedMesh)
			{
				m_SortOptions = new SortOption[]
				{
					SortOption.Threshold,
					SortOption.ThresholdValue,
					SortOption.TileOffset,
					SortOption.ResourceName,
					SortOption.VertexCount,
					SortOption.VertexInfo,
					SortOption.PixelCount,
					SortOption.Density,
					SortOption.HierarchyPath
				};
			}
		}

		// Note we We only build the visible rows, only the backend has the full tree information. 
		// The treeview only creates info for the row list.
		protected override IList<TreeViewItem> BuildRows(TreeViewItem root)
		{
			var rows = base.BuildRows (root);
			SortIfNeeded (root, rows);
			return rows;
		}

		void OnSortingChanged (MultiColumnHeader multiColumnHeader)
		{
			SortIfNeeded (rootItem, GetRows());
		}

		void SortIfNeeded (TreeViewItem root, IList<TreeViewItem> rows)
		{
			if (rows.Count <= 1)
				return;
			
			if (multiColumnHeader.sortedColumnIndex == -1)
			{
				return; // No column to sort for (just use the order the data are in)
			}
			
			// Sort the roots of the existing tree items
			SortByMultipleColumns ();
			TreeToList(root, rows);
			Repaint();
		}

		void SortByMultipleColumns ()
		{
			// 获取当前竖列分类的内容，如果一条都没有就不需要排序
			var sortedColumns = multiColumnHeader.state.sortedColumns;
			if (sortedColumns.Length == 0) return;
			
			// 排序根节点的子节点列表（depth = 0）
			rootItem.children = SortChildrenByMultipleColumns(rootItem.children, sortedColumns);
		}
		// 开始排序
		List<TreeViewItem> SortChildrenByMultipleColumns(List<TreeViewItem> children, int[] sortedColumns)
		{
			var myTypes = children.Cast<TreeViewItem<VertexProfilerTreeElement> >();
			var orderedQuery = InitialOrder (myTypes, sortedColumns);
			for (int i=1; i<sortedColumns.Length; i++)
			{
				SortOption sortOption = m_SortOptions[sortedColumns[i]];
				bool ascending = multiColumnHeader.IsSortedAscending(sortedColumns[i]);

				switch (sortOption)
				{
					case SortOption.TileOffset:
						orderedQuery = orderedQuery.ThenBy(l => l.data.TileIndex, ascending);
						break;
					case SortOption.VertexCount:
						orderedQuery = orderedQuery.ThenBy(l => l.data.VertexCount, ascending);
						break;
					case SortOption.PixelCount:
						orderedQuery = orderedQuery.ThenBy(l => l.data.PixelCount, ascending);
						break;
					case SortOption.Density:
						orderedQuery = orderedQuery.ThenBy(l => l.data.Density, ascending);
						break;
				}
			}
			// 递归子对象排序
			for (int i = 0; i < children.Count; i++)
			{
				TreeViewItem child = children[i];
				if (child != null && child.children != null && child.children.Count > 1)
				{
					child.children = SortChildrenByMultipleColumns(child.children, sortedColumns);
				}
			}
			return orderedQuery.Cast<TreeViewItem>().ToList();;
		}
		
		IOrderedEnumerable<TreeViewItem<VertexProfilerTreeElement>> InitialOrder(IEnumerable<TreeViewItem<VertexProfilerTreeElement>> myTypes, int[] history)
		{
			SortOption sortOption = m_SortOptions[history[0]];
			bool ascending = multiColumnHeader.IsSortedAscending(history[0]);
			switch (sortOption)
			{
				case SortOption.TileOffset:
					return myTypes.Order(l => l.data.TileIndex, ascending);
				case SortOption.VertexCount:
					return myTypes.Order(l => l.data.VertexCount, ascending);
				case SortOption.PixelCount:
					return myTypes.Order(l => l.data.PixelCount, ascending);
				case SortOption.Density:
					return myTypes.Order(l => l.data.Density, ascending);
				default:
					Assert.IsTrue(false, "Unhandled enum");
					break;
			}

			// default
			return myTypes.Order(l => l.data.TileIndex, ascending);
		}

		protected override void RowGUI (RowGUIArgs args)
		{
			var item = (TreeViewItem<VertexProfilerTreeElement>) args.item;
			if (DisplayTypeIndex == (int)DisplayType.OnlyTile)
			{
				for (int i = 0; i < args.GetNumVisibleColumns (); ++i)
				{
					CellGUI(args.GetCellRect(i), item, (OnlyTileColumns)args.GetColumn(i), ref args);
				}
			}
			else if (DisplayTypeIndex == (int)DisplayType.OnlyMesh)
			{
				for (int i = 0; i < args.GetNumVisibleColumns (); ++i)
				{
					CellGUI(args.GetCellRect(i), item, (OnlyMeshColumns)args.GetColumn(i), ref args);
				}
			}
			else if (DisplayTypeIndex == (int)DisplayType.TileBasedMesh)
			{
				for (int i = 0; i < args.GetNumVisibleColumns (); ++i)
				{
					CellGUI(args.GetCellRect(i), item, (TileBasedMeshColumns)args.GetColumn(i), ref args);
				}
			}
			if (Event.current.type == EventType.MouseDown)
			{
				// if (Event.current.clickCount == 1)
				// {
				// 	OnItemClicked(item);
				// }
				// 双击
				if (Event.current.clickCount == 2)
				{
					OnItemDoubleClicked(item);
				}
			}
		}

		void CellGUI (Rect cellRect, TreeViewItem<VertexProfilerTreeElement> item, OnlyTileColumns column, ref RowGUIArgs args)
		{
			// Center cell rect vertically (makes it easier to place controls, icons etc in the cells)
			CenterRectUsingSingleLineHeight(ref cellRect);
			string value = "";
			bool isThresholdItem = item.data.Threshold >= 0;
			switch (column)
			{
				case OnlyTileColumns.Threshold:
					if (isThresholdItem)
						value = item.data.name;
					break;
				case OnlyTileColumns.ThresholdValue:
					if (isThresholdItem)
						value = item.data.Threshold.ToString();
					break;
				case OnlyTileColumns.TileOffset:
					if (!isThresholdItem)
						value = item.data.TileIndex.ToString();
					break;
				case OnlyTileColumns.VertexCount:
					if (!isThresholdItem)
						value = item.data.VertexCount.ToString();
					break;
				case OnlyTileColumns.Density:
					if (!isThresholdItem)
						value = item.data.Density.ToString("f3");
					break;
			}
			DefaultGUI.LabelRightAligned(cellRect, value, args.selected, args.focused);
		}
		void CellGUI (Rect cellRect, TreeViewItem<VertexProfilerTreeElement> item, OnlyMeshColumns column, ref RowGUIArgs args)
		{
			// Center cell rect vertically (makes it easier to place controls, icons etc in the cells)
			CenterRectUsingSingleLineHeight(ref cellRect);
			string value = "";
			bool isThresholdItem = item.data.Threshold >= 0;
			bool alignLeft = false;
			switch (column)
			{
				case OnlyMeshColumns.Threshold:
					if (isThresholdItem)
						value = item.data.name;
					break;
				case OnlyMeshColumns.ThresholdValue:
					if (isThresholdItem)
						value = item.data.Threshold.ToString();
					break;
				case OnlyMeshColumns.ResourceName:
					alignLeft = true;
					if (!isThresholdItem)
						value = item.data.ResourceName;
					break;
				case OnlyMeshColumns.VertexCount:
					if (!isThresholdItem)
						value = item.data.VertexCount.ToString();
					break;
				case OnlyMeshColumns.PixelCount:
					if (!isThresholdItem)
						value = item.data.PixelCount.ToString();
					break;
				case OnlyMeshColumns.Density:
					if (!isThresholdItem)
					{
						if (item.data.Density == float.MaxValue)
						{
							value = "无像素占用";
						}
						else
						{
							value = item.data.Density.ToString("f3");
						}
					}
					break;
				case OnlyMeshColumns.HierarchyPath:
					alignLeft = true;
					if (!isThresholdItem)
						value = item.data.RendererHierarchyPath;
					break;
			}

			if (alignLeft)
			{
				var guiStyle = new GUIStyle(DefaultStyles.labelRightAligned);
				guiStyle.alignment = TextAnchor.MiddleLeft;
				if (UnityEngine.Event.current.type != UnityEngine.EventType.Repaint)
					return;
				guiStyle.Draw(cellRect, new GUIContent(value), false, false, args.selected, args.focused);
			}
			else
			{
				DefaultGUI.LabelRightAligned(cellRect, value, args.selected, args.focused);
			}
		}
		void CellGUI (Rect cellRect, TreeViewItem<VertexProfilerTreeElement> item, TileBasedMeshColumns column, ref RowGUIArgs args)
		{
			// Center cell rect vertically (makes it easier to place controls, icons etc in the cells)
			CenterRectUsingSingleLineHeight(ref cellRect);
			string value = "";
			bool isThresholdItem = item.data.Threshold >= 0;
			bool alignLeft = false;
			switch (column)
			{
				case TileBasedMeshColumns.Threshold:
					if (isThresholdItem)
						value = item.data.name;
					break;
				case TileBasedMeshColumns.ThresholdValue:
					if (isThresholdItem)
						value = item.data.Threshold.ToString();
					break;
				case TileBasedMeshColumns.TileOffset:
					if (!isThresholdItem)
						value = item.data.TileIndex.ToString();
					break;
				case TileBasedMeshColumns.ResourceName:
					alignLeft = true;
					if(!isThresholdItem)
						value = item.data.ResourceName;
					break;
				case TileBasedMeshColumns.VertexCount:
					if(!isThresholdItem)
						value = item.data.VertexCount.ToString();
					break;
				case TileBasedMeshColumns.VertexInfo:
					if(!isThresholdItem)
						value = item.data.VertexInfo;
					break;
				case TileBasedMeshColumns.PixelCount:
					if(!isThresholdItem)
						value = item.data.PixelCount.ToString();
					break;
				case TileBasedMeshColumns.Density:
					if(!isThresholdItem)
					{
						if (item.data.Density == float.MaxValue)
						{
							value = "无像素占用";
						}
						else
						{
							value = item.data.Density.ToString("f3");
						}
					}
					break;
				case TileBasedMeshColumns.HierarchyPath:
					alignLeft = true;
					if(!isThresholdItem)
						value = item.data.RendererHierarchyPath;
					break;
			}

			if (alignLeft)
			{
				var guiStyle = new GUIStyle(DefaultStyles.labelRightAligned);
				guiStyle.alignment = TextAnchor.MiddleLeft;
				if (UnityEngine.Event.current.type != UnityEngine.EventType.Repaint)
					return;
				guiStyle.Draw(cellRect, new GUIContent(value), false, false, args.selected, args.focused);
			}
			else
			{
				DefaultGUI.LabelRightAligned(cellRect, value, args.selected, args.focused);
			}
		}
		
		// private void OnItemClicked(TreeViewItem<VectorProfilerTreeElement> item)
		// {
		// 	Debug.Log("Item clicked: " + item.data.ResourceName);
		// }

		private void OnItemDoubleClicked(TreeViewItem<VertexProfilerTreeElement> item)
		{
			// Debug.Log("Item double clicked: " + item.data.ResourceName);
			// 基于棋盘格的统计数据没有Renderer信息，不知道这里的双击可以做什么
			if (DisplayTypeIndex == (int)DisplayType.OnlyTile) return;
			bool isThresholdItem = item.data.Threshold > 0;
			if (isThresholdItem) return;
			
			GameObject go = GameObject.Find(item.data.RendererHierarchyPath);
			if (go != null)
			{
				EditorGUIUtility.PingObject(go);
				Selection.activeGameObject = go;
			}
			else
			{
				Debug.Log("Item double clicked failed: " + item.data.RendererHierarchyPath);
			}
		}

		// Rename
		//--------

		protected override bool CanRename(TreeViewItem item)
		{
			// Only allow rename if we can show the rename overlay with a certain width (label might be clipped by other columns)
			Rect renameRect = GetRenameRect (treeViewRect, 0, item);
			return renameRect.width > 30;
		}

		protected override void RenameEnded(RenameEndedArgs args)
		{
			// Set the backend name and reload the tree to reflect the new model
			if (args.acceptedRename)
			{
				var element = treeModel.Find(args.itemID);
				element.name = args.newName;
				Reload();
			}
		}

		protected override Rect GetRenameRect (Rect rowRect, int row, TreeViewItem item)
		{
			Rect cellRect = GetCellRectForTreeFoldouts (rowRect);
			CenterRectUsingSingleLineHeight(ref cellRect);
			return base.GetRenameRect (cellRect, row, item);
		}

		// Misc
		//--------

		protected override bool CanMultiSelect (TreeViewItem item)
		{
			return true;
		}

		// protected override bool DoesItemMatchSearch(TreeViewItem oriItem, string search)
		// {
		// 	var item = (TreeViewItem<VertexProfilerTreeElement>) oriItem;
		// 	return item.data.ResourceName.IndexOf(search, StringComparison.OrdinalIgnoreCase) > 0
		// 	       || item.data.RendererHierarchyPath.IndexOf(search, StringComparison.OrdinalIgnoreCase) > 0;
		// }

		protected override void Search(VertexProfilerTreeElement searchFromThis, string search, List<TreeViewItem> result)
		{
			if (string.IsNullOrEmpty(search))
				throw new ArgumentException("Invalid search: cannot be null or empty", "search");

			Stack<VertexProfilerTreeElement> stack = new Stack<VertexProfilerTreeElement>();
			// 反向入栈
			for (int i = searchFromThis.children.Count - 1; i >= 0; i--)
			{
				var element = searchFromThis.children[i];
				stack.Push((VertexProfilerTreeElement)element);
			}
			while (stack.Count > 0)
			{
				VertexProfilerTreeElement current = stack.Pop();
				// 阈值条目必须满足匹配，然后就是内容匹配
				if (current.depth == 0 
				    || current.ResourceName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
				    || current.RendererHierarchyPath.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					result.Add(new TreeViewItem<VertexProfilerTreeElement>(current.id, current.depth, current.name, current));
				}
				// 子对象入栈进入内容匹配
				if (current.children != null && current.children.Count > 0)
				{
					foreach (var element in current.children)
					{
						stack.Push((VertexProfilerTreeElement)element);
					}
				}
			}
		}
	}
}

