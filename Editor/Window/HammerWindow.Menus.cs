using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Hammer's menu bar along the top of the window: Edit, View, Tools and Help, each item with
	/// its shortcut (read from the Shortcut Manager, so rebinding shows here too).
	/// </summary>
	partial class HammerWindow
	{
		const float MenuBarHeight = 22;

		/// <summary>One menu line: a separator when Label is null.</summary>
		sealed class BarItem
		{
			public string Label;
			public string Shortcut;
			public Func<bool> Enabled;
			public Func<bool> Checked;
			public Action Run;
		}

		static readonly BarItem Separator = new();

		static Dictionary<string, MethodInfo> _shortcutMethods;

		/// <summary>The handler behind a shortcut id in <see cref="HammerShortcuts"/>, so menu items do exactly what the key does.</summary>
		static Action Shortcut( string id )
		{
			_shortcutMethods ??= typeof( HammerShortcuts )
				.GetMethods( BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic )
				.Select( m => (m, a: m.GetCustomAttribute<ShortcutAttribute>()) )
				.Where( x => x.a != null )
				.GroupBy( x => ShortcutId( x.a ) )
				.ToDictionary( g => g.Key, g => g.First().m );

			return _shortcutMethods.TryGetValue( id, out var method ) ? () => method.Invoke( null, null ) : null;
		}

		// The attribute keeps its id to itself
		static string ShortcutId( ShortcutAttribute a )
		{
			var t = typeof( ShortcutAttribute );
			const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
			foreach ( var name in new[] { "identifier", "m_Identifier", "Identifier", "id" } )
			{
				if ( t.GetField( name, any )?.GetValue( a ) is string f ) return f;
				if ( t.GetProperty( name, any )?.GetValue( a ) is string q ) return q;
			}
			return t.GetFields( any ).Where( f => f.FieldType == typeof( string ) ).Select( f => f.GetValue( a ) as string ).FirstOrDefault( v => v != null && v.StartsWith( "Hammer/" ) ) ?? "";
		}

		static string Keys( string id )
		{
			try
			{
				var binding = ShortcutManager.instance.GetShortcutBinding( id );
				var text = binding.ToString();
				return text.Replace( "Alpha", "" ).Replace( "Keypad", "Num" ).Replace( "LeftBracket", "[" ).Replace( "RightBracket", "]" ).Replace( "Return", "Enter" );
			}
			catch ( ArgumentException )
			{
				return "";
			}
		}

		BarItem Item( string label, string shortcutId, Func<bool> enabled = null, Func<bool> check = null ) => new()
		{
			Label = label,
			Shortcut = Keys( shortcutId ),
			Run = Shortcut( shortcutId ),
			Enabled = enabled,
			Checked = check,
		};

		BarItem Do( string label, string keys, Action run, Func<bool> enabled = null, Func<bool> check = null ) => new()
		{
			Label = label,
			Shortcut = keys,
			Run = run,
			Enabled = enabled,
			Checked = check,
		};

		bool InMode( params EditMode[] modes ) => _tool != null && modes.Contains( _tool.Mode );
		bool Meshes => InMode( EditMode.Object );
		bool Elements => InMode( EditMode.Vertex, EditMode.Edge, EditMode.Face );

		List<BarItem> EditMenu()
		{
			var undo = Undo.GetCurrentGroupName();
			var repeat = _tool?.LastActionName;
			return new List<BarItem>
			{
				Item( string.IsNullOrEmpty( undo ) ? "Undo" : $"Undo: {undo}", "Hammer/Undo" ),
				Item( "Redo", "Hammer/Redo" ),
				Item( repeat == null ? "Repeat command" : $"Repeat command: {repeat}", "Hammer/Repeat Last", () => repeat != null ),
				Separator,
				Do( "Copy", "Ctrl+C", () => _tool.CopyFaces(), () => InMode( EditMode.Face ) && _tool.SelectedFaces.Any() ),
				Do( "Paste", "Ctrl+V", () => _tool.PasteFaces(), () => InMode( EditMode.Face ) ),
				Item( "Delete Selected", "Hammer/Delete" ),
				Separator,
				Do( "Clear Selection", "Esc", ClearSelection ),
				Item( "Select All", "Hammer/Select All" ),
				Item( "Invert Selection", "Hammer/Invert Selection" ),
				Item( "Grow Selection", "Hammer/Grow Selection", () => Elements ),
				Item( "Shrink Selection", "Hammer/Shrink Selection", () => Elements ),
				Item( "Select Loop", "Hammer/Select Loop", () => InMode( EditMode.Edge, EditMode.Face ) ),
				Item( "Select Ring", "Hammer/Select Ring · Thicken (G)", () => InMode( EditMode.Edge ) ),
				Do( "Select Contiguous", "", () => _tool.SelectContiguous(), () => Elements ),
				Item( "Create Selection Set", "Hammer/New Selection Set" ),
			};
		}

		void ClearSelection()
		{
			Undo.IncrementCurrentGroup();
			_tool.Selection.Clear();
			if ( _tool.Mode == EditMode.Object ) UnityEditor.Selection.objects = new UnityEngine.Object[0];
			HammerViews.RepaintAll();
		}

		List<BarItem> ViewMenu() => new()
		{
			Do( "Show Grid", "", () => HammerSettings.ShowGrid = !HammerSettings.ShowGrid, null, () => HammerSettings.ShowGrid ),
			Do( "Show Mesh Edges in 3D", "", () => HammerSettings.ShowWires = !HammerSettings.ShowWires, null, () => HammerSettings.ShowWires ),
			Do( "Show Normals", "", () => HammerSettings.ShowNormals = !HammerSettings.ShowNormals, null, () => HammerSettings.ShowNormals ),
			Item( "Smaller Grid", "Hammer/Grid Smaller" ),
			Item( "Bigger Grid", "Hammer/Grid Larger" ),
			Separator,
			Item( "Frame Views on Selection", "Hammer/Frame Selection" ),
			Item( "Maximize View", "Hammer/Maximize View" ),
			Item( "Cycle 2D View", "Hammer/Cycle 2D View" ),
			Item( "Top", "Hammer/View Top" ),
			Item( "Front", "Hammer/View Front" ),
			Item( "Side", "Hammer/View Side" ),
			Item( "3D Fullbright", "Hammer/View 3D Fullbright" ),
			Item( "3D Lit", "Hammer/View 3D Lit" ),
			Item( "Fly Camera", "Hammer/Fly Mode" ),
			Separator,
			Item( "Pick Workplane From Surface", "Hammer/Pick Workplane" ),
			Item( "Align Workplane to Selected Object", "Hammer/Align Workplane To Selected Object", () => UnityEditor.Selection.activeTransform != null ),
			Do( "Reset Workplane", "", () => { Workplane.Reset(); HammerViews.RepaintAll(); }, () => Workplane.Active ),
			Separator,
			Item( "Hide Selected Faces", "Hammer/Hard Normals · Hide Faces (H)", () => InMode( EditMode.Face ) ),
			Item( "Unhide All", "Hammer/Unhide Faces" ),
		};

		List<BarItem> ToolsMenu() => new()
		{
			Item( "Snap Selected to Grid", "Hammer/Snap To Grid" ),
			Item( "Set Origin to Pivot", "Hammer/Quad Slice", () => Meshes ),
			Item( "Set Origin to Object Center", "Hammer/Center Origin · Reset Pivot (End)", () => Meshes ),
			Item( "Set Origin to Target Under Cursor", "Hammer/Set Origin To Target", () => Meshes ),
			Item( "Align to Target Under Cursor", "Hammer/Align to Target", () => Meshes ),
			Item( "Rotate to Target Under Cursor", "Hammer/Rotate to Target", () => Meshes ),
			Item( "Pin to Target Under Cursor", "Hammer/Path Extrude", () => Meshes ),
			Item( "Snap Position to Last Selected", "Hammer/Snap To Vertex · Bridge (B)", () => Meshes ),
			Item( "Align to Last Selected", "Hammer/Bridge Tool", () => Meshes ),
			Item( "Align Selected Objects to Workplane", "Hammer/Align Selected Objects To Workplane", () => Meshes ),
			Item( "Move Down by Tracing", "Hammer/Move Path Trace Down" ),
			Item( "Clear Rotation and Scale", "Hammer/Clear Rotation and Scale", () => Meshes ),
			Do( "Freeze Transform", "", () => _tool.FreezeTransform(), () => Meshes ),
			Separator,
			Do( "Create Instance", "", () =>
			{
				var selected = UnityEditor.Selection.gameObjects.Select( g => g.GetComponent<HammerMesh>() ).Where( c => c != null ).ToList();
				var active = UnityEditor.Selection.activeGameObject != null ? UnityEditor.Selection.activeGameObject.GetComponent<HammerMesh>() : null;
				if ( active != null ) HammerInstances.MakeInstances( selected, active );
			}, () => Meshes && UnityEditor.Selection.activeGameObject != null ),
			Do( "Collapse Instance", "", () => HammerInstances.Collapse( UnityEditor.Selection.gameObjects.Select( g => g.GetComponent<HammerMesh>() ).Where( c => c != null ).ToList() ), () => Meshes ),
			Separator,
			Item( "Block Tool", "Hammer/Primitive Tool" ),
			Item( "Polygon Tool", "Hammer/Polygon Tool" ),
			Item( "Clipping Tool", "Hammer/Clipping Tool", () => InMode( EditMode.Face, EditMode.Object ) ),
			Item( "Edge Cut Tool", "Hammer/Edge Cut Tool", () => Elements ),
			Item( "Mirror Tool", "Hammer/Mirror Tool", () => InMode( EditMode.Face, EditMode.Object ) ),
			Item( "Bevel Tool", "Hammer/Bevel Tool", () => InMode( EditMode.Edge ) ),
			Item( "Bridge Tool", "Hammer/Bridge Tool", () => InMode( EditMode.Edge, EditMode.Face ) ),
			Item( "Edge Arch Tool", "Hammer/Edge Arch Tool", () => InMode( EditMode.Edge ) ),
			Item( "Path Extrude", "Hammer/Path Extrude", () => InMode( EditMode.Edge ) ),
			Item( "Displacement Tool", "Hammer/Displacement Tool" ),
			Item( "Vertex Paint", "Hammer/Vertex Paint (Shift+V)" ),
			Item( "Fast Texture Tool", "Hammer/Select Ribs · Fast Texture Tool", () => InMode( EditMode.Face ) ),
		};

		List<BarItem> HelpMenu() => new()
		{
			Do( "How do I...", "", () => HammerGuides.OpenPicker( _tool ) ),
			Do( "Take the Tour", "", () => HammerGuides.Start( HammerGuides.Tour, _tool ) ),
			Do( "Stop Guide", "", HammerGuides.Stop, () => HammerGuides.Running ),
			Separator,
			Item( "Command List", "Hammer/Command List" ),
			Do( "Source 2 Level Design Docs", "", () => Application.OpenURL( "https://developer.valvesoftware.com/wiki/Source_2/Docs/Level_Design" ) ),
		};

		static GUIStyle _menuTitle;

		void DrawMenuBar( Rect rect )
		{
			if ( Event.current.type == EventType.Repaint )
			{
				EditorGUI.DrawRect( rect, HammerIcons.Background );
				EditorGUI.DrawRect( new Rect( rect.x, rect.yMax - 1, rect.width, 1 ), HammerIcons.Divider );
			}

			_menuTitle ??= new GUIStyle( EditorStyles.label ) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset( 8, 8, 0, 0 ) };

			var x = rect.x + 4;
			void Title( string name, Func<List<BarItem>> items )
			{
				var width = _menuTitle.CalcSize( new GUIContent( name ) ).x;
				var r = new Rect( x, rect.y + 1, width, rect.height - 2 );
				x += width;
				if ( Event.current.type == EventType.Repaint && r.Contains( Event.current.mousePosition ) )
					EditorGUI.DrawRect( r, HammerIcons.ButtonHover );
				if ( GUI.Button( r, name, _menuTitle ) )
					PopupWindow.Show( r, new MenuPopup( items(), this ) );
			}

			Title( "Edit", EditMenu );
			Title( "View", ViewMenu );
			Title( "Tools", ToolsMenu );
			Title( "Help", HelpMenu );
		}

		/// <summary>The drop-down list for one menu, drawn like Hammer's: label left, shortcut right, greyed when it can't run.</summary>
		sealed class MenuPopup : PopupWindowContent
		{
			const float Row = 22, SeparatorHeight = 9;
			readonly List<BarItem> _items;
			readonly HammerWindow _window;
			static GUIStyle _label, _keys;

			public MenuPopup( List<BarItem> items, HammerWindow window )
			{
				_items = items;
				_window = window;
			}

			public override Vector2 GetWindowSize()
			{
				Styles();
				var labels = _items.Where( i => i.Label != null ).Max( i => _label.CalcSize( new GUIContent( i.Label ) ).x );
				var keys = _items.Where( i => i.Label != null ).Select( i => _keys.CalcSize( new GUIContent( i.Shortcut ?? "" ) ).x ).DefaultIfEmpty( 0 ).Max();
				var height = _items.Sum( i => i.Label == null ? SeparatorHeight : Row ) + 8;
				return new Vector2( 28 + labels + 32 + keys + 16, height );
			}

			static void Styles()
			{
				_label ??= new GUIStyle( EditorStyles.label ) { alignment = TextAnchor.MiddleLeft };
				_keys ??= new GUIStyle( EditorStyles.label ) { alignment = TextAnchor.MiddleRight };
			}

			public override void OnGUI( Rect rect )
			{
				Styles();
				var e = Event.current;
				if ( e.type == EventType.Repaint ) EditorGUI.DrawRect( rect, new Color( 0.17f, 0.17f, 0.17f ) );
				if ( e.type == EventType.MouseMove ) editorWindow.Repaint();

				var y = rect.y + 4;
				foreach ( var item in _items )
				{
					if ( item.Label == null )
					{
						if ( e.type == EventType.Repaint )
							EditorGUI.DrawRect( new Rect( rect.x + 6, y + SeparatorHeight / 2, rect.width - 12, 1 ), new Color( 0.32f, 0.32f, 0.32f ) );
						y += SeparatorHeight;
						continue;
					}

					var r = new Rect( rect.x, y, rect.width, Row );
					y += Row;
					var enabled = item.Run != null && (item.Enabled == null || item.Enabled());
					var hover = enabled && r.Contains( e.mousePosition );

					if ( e.type == EventType.Repaint )
					{
						if ( hover ) EditorGUI.DrawRect( r, new Color( 0.24f, 0.37f, 0.6f ) );
						var color = enabled ? new Color( 0.88f, 0.88f, 0.88f ) : new Color( 0.5f, 0.5f, 0.5f );
						_label.normal.textColor = _keys.normal.textColor = color;
						if ( item.Checked != null && item.Checked() )
							_label.Draw( new Rect( r.x + 8, r.y, 16, r.height ), new GUIContent( "✓" ), false, false, false, false );
						_label.Draw( new Rect( r.x + 28, r.y, r.width - 28, r.height ), new GUIContent( item.Label ), false, false, false, false );
						_keys.Draw( new Rect( r.x, r.y, r.width - 12, r.height ), new GUIContent( item.Shortcut ?? "" ), false, false, false, false );
					}

					if ( enabled && e.type == EventType.MouseUp && e.button == 0 && r.Contains( e.mousePosition ) )
					{
						e.Use();
						editorWindow.Close();
						// After the menu has gone, with the Hammer window in charge again
						var run = item.Run;
						var window = _window;
						EditorApplication.delayCall += () =>
						{
							if ( window != null ) window.Focus();
							run();
							HammerViews.RepaintAll();
						};
						GUIUtility.ExitGUI();
					}
				}
			}
		}
	}
}
