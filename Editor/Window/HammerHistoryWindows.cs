using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Hammer's Command History pane: recent edits; pick one or several and repeat them a number
	/// of times (Shift+G repeats just the last).
	/// </summary>
	public sealed class HammerCommandHistoryWindow : EditorWindow
	{
		Vector2 _scroll;
		readonly HashSet<int> _picked = new();
		int _times = 1;

		[MenuItem( "Window/Hammer/Command History", false, 2101 )]
		public static void Open()
		{
			var w = GetWindow<HammerCommandHistoryWindow>( "Command History" );
			w.minSize = new Vector2( 260, 200 );
			w.Show();
		}

		void OnEnable() => HammerMeshTool.HistoryChanged += Repaint;
		void OnDisable() => HammerMeshTool.HistoryChanged -= Repaint;

		static HammerMeshTool Tool => HammerMeshTool.Focused ?? HammerWindow.OpenTool;

		void OnGUI()
		{
			var tool = Tool;
			if ( tool == null )
			{
				EditorGUILayout.HelpBox( "Open the Hammer window (Ctrl+Shift+H) to use the command history.", MessageType.Info );
				return;
			}

			var history = tool.History;
			EditorGUILayout.LabelField( "Click to pick commands (Ctrl/Shift for several), then repeat them.", EditorStyles.wordWrappedMiniLabel );

			_scroll = EditorGUILayout.BeginScrollView( _scroll );
			for ( int i = history.Count - 1; i >= 0; i-- )
			{
				var picked = _picked.Contains( i );
				var style = picked ? EditorStyles.helpBox : EditorStyles.label;
				if ( GUILayout.Button( $"{i + 1}.  {history[i].Name}", style ) )
				{
					var e = Event.current;
					if ( e.control || e.command ) { if ( !_picked.Remove( i ) ) _picked.Add( i ); }
					else if ( e.shift && _picked.Count > 0 )
					{
						var from = _picked.Min();
						var to = _picked.Max();
						for ( int k = Mathf.Min( from, i ); k <= Mathf.Max( to, i ); k++ ) _picked.Add( k );
					}
					else { _picked.Clear(); _picked.Add( i ); }
				}
			}
			EditorGUILayout.EndScrollView();

			_picked.RemoveWhere( i => i >= history.Count );

			using ( new GUILayout.HorizontalScope() )
			{
				GUILayout.Label( "Times", GUILayout.Width( 40 ) );
				_times = Mathf.Clamp( EditorGUILayout.IntField( _times, GUILayout.Width( 40 ) ), 1, 100 );
				using ( new EditorGUI.DisabledScope( _picked.Count == 0 ) )
				{
					if ( GUILayout.Button( "Repeat Selected" ) )
						tool.RepeatCommands( _picked.OrderBy( i => i ).ToList(), _times );
				}
				if ( GUILayout.Button( "Clear", GUILayout.Width( 50 ) ) ) { tool.ClearHistory(); _picked.Clear(); }
			}
		}
	}

	/// <summary>
	/// Hammer's Selection Sets pane: named groups of objects (saved with the scene) to select,
	/// hide or show together. Ctrl+R makes a set from the selection.
	/// </summary>
	public sealed class HammerSelectionSetsWindow : EditorWindow
	{
		Vector2 _scroll;
		int _renaming = -1;

		[MenuItem( "Window/Hammer/Selection Sets", false, 2102 )]
		public static void Open()
		{
			var w = GetWindow<HammerSelectionSetsWindow>( "Selection Sets" );
			w.minSize = new Vector2( 280, 160 );
			w.Show();
		}

		void OnHierarchyChange() => Repaint();
		void OnSelectionChange() => Repaint();

		/// <summary>
		/// Ctrl+R: a new set of the selected objects.
		/// </summary>
		public static void CreateFromSelection()
		{
			var objects = UnityEditor.Selection.gameObjects.ToList();
			if ( objects.Count == 0 ) return;

			var sets = HammerSelectionSets.Get( true );
			Undo.RecordObject( sets, "New Selection Set" );
			sets.Sets.Add( new HammerSelectionSets.Set { Name = $"Set {sets.Sets.Count + 1}", Objects = objects } );
			EditorUtility.SetDirty( sets );

			Open();
			var w = GetWindow<HammerSelectionSetsWindow>();
			w._renaming = sets.Sets.Count - 1;
		}

		void OnGUI()
		{
			var sets = HammerSelectionSets.Get( false );

			using ( new GUILayout.HorizontalScope( EditorStyles.toolbar ) )
			{
				if ( GUILayout.Button( "New From Selection  (Ctrl+R)", EditorStyles.toolbarButton ) ) CreateFromSelection();
				GUILayout.FlexibleSpace();
			}

			if ( sets == null || sets.Sets.Count == 0 )
			{
				EditorGUILayout.HelpBox( "No sets yet. Select some objects and press Ctrl+R in the Hammer window.", MessageType.None );
				return;
			}

			_scroll = EditorGUILayout.BeginScrollView( _scroll );
			for ( int i = 0; i < sets.Sets.Count; i++ )
			{
				var set = sets.Sets[i];
				set.Objects.RemoveAll( o => o == null );

				using ( new GUILayout.HorizontalScope() )
				{
					// Eye: show / hide every object in the set
					var shown = GUILayout.Toggle( !set.Hidden, new GUIContent( "", "Show / hide" ), GUILayout.Width( 18 ) );
					if ( shown == set.Hidden )
					{
						Undo.RecordObject( sets, "Show/Hide Selection Set" );
						set.Hidden = !shown;
						foreach ( var o in set.Objects )
						{
							if ( set.Hidden ) SceneVisibilityManager.instance.Hide( o, true );
							else SceneVisibilityManager.instance.Show( o, true );
						}
						EditorUtility.SetDirty( sets );
						HammerViews.RepaintAll();
					}

					if ( _renaming == i )
					{
						GUI.SetNextControlName( "rename" );
						var name = EditorGUILayout.DelayedTextField( set.Name );
						EditorGUI.FocusTextInControl( "rename" );
						if ( name != set.Name ) { Undo.RecordObject( sets, "Rename Selection Set" ); set.Name = name; EditorUtility.SetDirty( sets ); _renaming = -1; }
					}
					else if ( GUILayout.Button( $"{set.Name}  ({set.Objects.Count})", EditorStyles.label ) )
					{
						if ( Event.current.clickCount > 1 ) _renaming = i;
						else UnityEditor.Selection.objects = set.Objects.Cast<Object>().ToArray();
					}

					if ( GUILayout.Button( new GUIContent( "+", "Add the selection to this set" ), EditorStyles.miniButtonLeft, GUILayout.Width( 22 ) ) )
					{
						Undo.RecordObject( sets, "Add to Selection Set" );
						foreach ( var o in UnityEditor.Selection.gameObjects ) if ( !set.Objects.Contains( o ) ) set.Objects.Add( o );
						EditorUtility.SetDirty( sets );
					}
					if ( GUILayout.Button( new GUIContent( "−", "Take the selection out of this set" ), EditorStyles.miniButtonMid, GUILayout.Width( 22 ) ) )
					{
						Undo.RecordObject( sets, "Remove from Selection Set" );
						set.Objects.RemoveAll( o => UnityEditor.Selection.gameObjects.Contains( o ) );
						EditorUtility.SetDirty( sets );
					}
					if ( GUILayout.Button( new GUIContent( "×", "Delete the set (not its objects)" ), EditorStyles.miniButtonRight, GUILayout.Width( 22 ) ) )
					{
						Undo.RecordObject( sets, "Delete Selection Set" );
						sets.Sets.RemoveAt( i );
						EditorUtility.SetDirty( sets );
						GUIUtility.ExitGUI();
					}
				}
			}
			EditorGUILayout.EndScrollView();
			EditorGUILayout.LabelField( "Click a set to select it, double-click to rename.", EditorStyles.centeredGreyMiniLabel );
		}
	}
}
