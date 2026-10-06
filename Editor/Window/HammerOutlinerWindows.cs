using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Hammer's Outliner pane: every object in the map under "World", meshes with their face
	/// counts. Click to select (Ctrl / Shift for several), the eye to hide or show, and a filter
	/// to search by name.
	/// </summary>
	public sealed class HammerOutlinerWindow : EditorWindow
	{
		Vector2 _scroll;
		string _filter = "";
		bool _meshesOnly;
		readonly HashSet<int> _collapsed = new();
		GameObject _lastClicked;

		[MenuItem( "Window/Hammer/Outliner", false, 2103 )]
		public static void Open()
		{
			var w = GetWindow<HammerOutlinerWindow>( "Outliner" );
			w.minSize = new Vector2( 220, 160 );
			w.Show();
		}

		void OnHierarchyChange() => Repaint();
		void OnSelectionChange() => Repaint();

		static GUIStyle _row;
		static GUIStyle Row => _row ??= new GUIStyle( EditorStyles.label ) { padding = new RectOffset( 2, 2, 1, 1 ) };

		void OnGUI()
		{
			using ( new GUILayout.HorizontalScope( EditorStyles.toolbar ) )
			{
				_filter = GUILayout.TextField( _filter, EditorStyles.toolbarSearchField, GUILayout.ExpandWidth( true ) );
				_meshesOnly = GUILayout.Toggle( _meshesOnly, new GUIContent( "Meshes", "Only show Hammer meshes" ), EditorStyles.toolbarButton, GUILayout.Width( 56 ) );
			}

			var visible = new List<GameObject>();
			_scroll = EditorGUILayout.BeginScrollView( _scroll );

			for ( int s = 0; s < SceneManager.sceneCount; s++ )
			{
				var scene = SceneManager.GetSceneAt( s );
				if ( !scene.isLoaded ) continue;

				EditorGUILayout.LabelField( SceneManager.sceneCount > 1 ? $"World ({scene.name})" : "World", EditorStyles.boldLabel );
				foreach ( var root in scene.GetRootGameObjects() )
					ObjectRow( root, 1, visible );
			}

			EditorGUILayout.EndScrollView();
		}

		bool Matches( GameObject go )
		{
			if ( _meshesOnly && go.GetComponent<HammerMesh>() == null ) return false;
			return string.IsNullOrEmpty( _filter ) || go.name.IndexOf( _filter, System.StringComparison.OrdinalIgnoreCase ) >= 0;
		}

		bool AnyMatch( GameObject go )
		{
			if ( Matches( go ) ) return true;
			foreach ( Transform child in go.transform )
				if ( AnyMatch( child.gameObject ) ) return true;
			return false;
		}

		void ObjectRow( GameObject go, int depth, List<GameObject> visible )
		{
			if ( (go.hideFlags & HideFlags.HideInHierarchy) != 0 || !AnyMatch( go ) ) return;

			var filtering = !string.IsNullOrEmpty( _filter ) || _meshesOnly;
			var hasChildren = go.transform.childCount > 0;
			var open = filtering || !_collapsed.Contains( go.GetInstanceID() );

			if ( Matches( go ) || !filtering )
			{
				visible.Add( go );
				var mesh = go.GetComponent<HammerMesh>();
				var label = mesh != null && mesh.Mesh != null ? $"{go.name} ({mesh.Mesh.FaceHandles.Count()} faces)" : go.name;
				var rect = GUILayoutUtility.GetRect( 10, 18, GUILayout.ExpandWidth( true ) );
				var selected = UnityEditor.Selection.Contains( go );

				if ( Event.current.type == EventType.Repaint && selected )
					EditorGUI.DrawRect( rect, new Color( 0.17f, 0.36f, 0.53f, 1 ) );

				// Eye: hide / show in the views
				var eyeRect = new Rect( rect.x + 2, rect.y + 1, 16, 16 );
				var hidden = SceneVisibilityManager.instance.IsHidden( go );
				var eye = EditorGUIUtility.IconContent( hidden ? "scenevis_hidden_hover" : "scenevis_visible_hover" );
				if ( GUI.Button( eyeRect, eye, GUIStyle.none ) )
				{
					if ( hidden ) SceneVisibilityManager.instance.Show( go, true );
					else SceneVisibilityManager.instance.Hide( go, true );
					HammerViews.RepaintAll();
				}

				var x = rect.x + 20 + depth * 14;
				if ( hasChildren && !filtering )
				{
					var foldRect = new Rect( x - 14, rect.y + 1, 14, 16 );
					var newOpen = EditorGUI.Foldout( foldRect, open, GUIContent.none );
					if ( newOpen != open )
					{
						if ( newOpen ) _collapsed.Remove( go.GetInstanceID() );
						else _collapsed.Add( go.GetInstanceID() );
					}
				}

				var icon = mesh != null ? EditorGUIUtility.IconContent( "Mesh Icon" ).image : EditorGUIUtility.IconContent( "GameObject Icon" ).image;
				var labelRect = new Rect( x, rect.y, rect.xMax - x, rect.height );
				var style = Row;
				style.normal.textColor = hidden ? new Color( 0.5f, 0.5f, 0.5f ) : go.activeInHierarchy ? new Color( 0.82f, 0.82f, 0.82f ) : new Color( 0.55f, 0.55f, 0.55f );
				if ( GUI.Button( labelRect, new GUIContent( label, icon ), style ) )
					Click( go, visible );
			}

			if ( !open ) return;
			foreach ( Transform child in go.transform )
				ObjectRow( child.gameObject, depth + 1, visible );
		}

		void Click( GameObject go, List<GameObject> visible )
		{
			var e = Event.current;
			var current = UnityEditor.Selection.gameObjects.ToList();

			if ( e.clickCount > 1 )
			{
				// Double-click frames it in the Hammer views
				var renderer = go.GetComponentInChildren<Renderer>();
				var window = HasOpenInstances<HammerWindow>() ? GetWindow<HammerWindow>( false, null, false ) : null;
				if ( renderer != null && window != null ) window.FrameAll( renderer.bounds );
			}

			if ( e.control || e.command )
			{
				if ( !current.Remove( go ) ) current.Add( go );
			}
			else if ( e.shift && _lastClicked != null && visible.Contains( _lastClicked ) )
			{
				var a = visible.IndexOf( _lastClicked );
				var b = visible.IndexOf( go );
				for ( int i = Mathf.Min( a, b ); i <= Mathf.Max( a, b ); i++ )
					if ( !current.Contains( visible[i] ) ) current.Add( visible[i] );
			}
			else
			{
				current = new List<GameObject> { go };
			}

			_lastClicked = go;
			UnityEditor.Selection.objects = current.Cast<Object>().ToArray();
			HammerViews.RepaintAll();
		}
	}

	/// <summary>
	/// Hammer's Object Properties pane: the selected object's settings (for a mesh: smoothing
	/// angle, subdivision and the rest), with a filter to find a property by name.
	/// </summary>
	public sealed class HammerObjectPropertiesWindow : EditorWindow
	{
		Vector2 _scroll;
		string _filter = "";
		readonly Dictionary<Object, Editor> _editors = new();

		[MenuItem( "Window/Hammer/Object Properties", false, 2104 )]
		public static void Open()
		{
			var w = GetWindow<HammerObjectPropertiesWindow>( "Object Properties" );
			w.minSize = new Vector2( 260, 160 );
			w.Show();
		}

		void OnSelectionChange() => Repaint();

		void OnDisable()
		{
			foreach ( var editor in _editors.Values )
				if ( editor != null ) DestroyImmediate( editor );
			_editors.Clear();
		}

		Editor EditorFor( Object target )
		{
			if ( !_editors.TryGetValue( target, out var editor ) || editor == null )
				_editors[target] = editor = Editor.CreateEditor( target );
			return editor;
		}

		void OnGUI()
		{
			using ( new GUILayout.HorizontalScope( EditorStyles.toolbar ) )
				_filter = GUILayout.TextField( _filter, EditorStyles.toolbarSearchField, GUILayout.ExpandWidth( true ) );

			var go = UnityEditor.Selection.activeGameObject;
			if ( go == null )
			{
				EditorGUILayout.HelpBox( "Select an object to see its properties.", MessageType.None );
				return;
			}

			_scroll = EditorGUILayout.BeginScrollView( _scroll );

			var count = UnityEditor.Selection.gameObjects.Length;
			EditorGUILayout.LabelField( count > 1 ? $"{go.name}  (+{count - 1} more)" : go.name, EditorStyles.boldLabel );

			foreach ( var component in go.GetComponents<Component>() )
			{
				if ( component == null || (component.hideFlags & HideFlags.HideInInspector) != 0 ) continue;
				if ( component is MeshFilter ) continue; // made by the mesh, nothing to edit

				EditorGUILayout.Space( 4 );
				EditorGUILayout.LabelField( ObjectNames.NicifyVariableName( component.GetType().Name ), EditorStyles.miniBoldLabel );

				if ( string.IsNullOrEmpty( _filter ) )
				{
					EditorFor( component ).OnInspectorGUI();
					continue;
				}

				// Filtered: just the matching properties
				var so = new SerializedObject( component );
				var p = so.GetIterator();
				var any = false;
				for ( var enter = true; p.NextVisible( enter ); enter = false )
				{
					if ( p.name == "m_Script" ) continue;
					if ( p.displayName.IndexOf( _filter, System.StringComparison.OrdinalIgnoreCase ) < 0 ) continue;
					EditorGUILayout.PropertyField( p, true );
					any = true;
				}
				if ( !any ) EditorGUILayout.LabelField( "(nothing matches)", EditorStyles.centeredGreyMiniLabel );
				so.ApplyModifiedProperties();
			}

			EditorGUILayout.EndScrollView();
		}
	}
}
