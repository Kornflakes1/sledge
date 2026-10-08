using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace HammerUnity.EditorTools
{
	public enum EditMode
	{
		Vertex,
		Edge,
		Face,
		Object,
		Primitive,
		Paint,
	}

	public enum MoveMode
	{
		/// <summary>
		/// Pick only, no gizmo (Hammer's Select tool).
		/// </summary>
		Select,
		Position,
		Rotate,
		Scale,
		/// <summary>
		/// Move the pivot: where rotate/scale turn around, or an object's origin.
		/// </summary>
		Pivot,
	}

	/// <summary>
	/// The Hammer mesh editing tool: select and transform vertices, edges and faces of
	/// <see cref="HammerMesh"/> objects, and draw new primitives. Mirrors the s&amp;box mesh tool.
	/// </summary>
	[EditorTool( "Hammer Mesh Tool" )]
	public sealed partial class HammerMeshTool : EditorTool
	{
		/// <summary>
		/// The tool instance while it is the active scene view tool.
		/// </summary>
		public static HammerMeshTool Active { get; private set; }

		/// <summary>
		/// The instance keyboard shortcuts act on: the Hammer window's while it has focus.
		/// </summary>
		public static HammerMeshTool Focused => HammerWindow.FocusedTool;

		static EditMode _mode = EditMode.Face;
		static MoveMode _moveMode = MoveMode.Position;

		public readonly MeshSelection Selection = new();

		HammerView _view;

		/// <summary>
		/// The view currently being handled.
		/// </summary>
		public HammerView View => _view;

		GUIContent _icon;

		public override GUIContent toolbarIcon => _icon ??= new GUIContent( EditorGUIUtility.IconContent( "EditCollider" ).image, "Hammer Mesh Tool (M)" );

		public EditMode Mode
		{
			get => _mode;
			set
			{
				if ( _mode == value ) return;

				_subTool?.Cancel();

				if ( _mode == EditMode.Primitive )
					CancelPrimitive();

				// Keep the selection meaningful when switching element types, like s&box does
				if ( !_skipConvert )
					ConvertSelection( _mode, value );

				_mode = value;
				_hover = null;

				// Hammer: after drawing a shape, switching to Vertices, Edges or Faces selects all
				// of the new shape's
				if ( _justCreated != null && value is EditMode.Vertex or EditMode.Edge or EditMode.Face && Selection.Count == 0
					&& UnityEditor.Selection.activeGameObject == _justCreated.gameObject )
				{
					var c = _justCreated;
					var mesh = c.Mesh;
					if ( value == EditMode.Vertex ) foreach ( var v in mesh.VertexHandles ) Selection.Add( new MeshVertex( c, v ) );
					else if ( value == EditMode.Face ) foreach ( var f in mesh.FaceHandles ) Selection.Add( new MeshFace( c, f ) );
					else foreach ( var h in mesh.HalfEdgeHandles ) Selection.Add( new MeshEdge( c, h ) );
				}
				if ( value is not EditMode.Object ) _justCreated = null;

				HammerViews.RepaintAll();
			}
		}

		public MoveMode MoveMode
		{
			get => _moveMode;
			set
			{
				_moveMode = value;
				HammerViews.RepaintAll();
			}
		}

		MoveMode _beforePivot = MoveMode.Position;

		/// <summary>
		/// Insert: into the Pivot tool, and out again to whichever tool was in use before.
		/// </summary>
		public void TogglePivotMode()
		{
			if ( _moveMode == MoveMode.Pivot )
			{
				MoveMode = _beforePivot;
				return;
			}
			_beforePivot = _moveMode;
			MoveMode = MoveMode.Pivot;
		}

		/// <summary>
		/// Leave the Hammer tool and go back to whichever Unity tool was active before.
		/// </summary>
		public static void Deactivate()
		{
			if ( Active == null ) return;

			ToolManager.RestorePreviousTool();

			// If there was no previous tool to go back to, fall back to Unity's move tool
			if ( Active != null )
				UnityEditor.Tools.current = UnityEditor.Tool.Move;
		}

		public static void Activate()
		{
			ToolManager.SetActiveTool<HammerMeshTool>();
		}

		public static void Activate( EditMode mode )
		{
			ToolManager.SetActiveTool<HammerMeshTool>();
			if ( Active != null )
				Active.Mode = mode;
			else
				_mode = mode;
		}

		public override void OnActivated()
		{
			Active = this;
			Attach();
		}

		public override void OnWillBeDeactivated()
		{
			Detach();

			if ( Active == this )
				Active = null;
		}

		/// <summary>
		/// Start listening for undo and selection changes. Called by the scene tool on activation
		/// and by the Hammer window for its own instance.
		/// </summary>
		public void Attach()
		{
			HammerSettings.SyncUnityGrid();
			Undo.undoRedoPerformed += OnUndoRedo;
			UnityEditor.Selection.selectionChanged += OnUnitySelectionChanged;
			HammerViews.RepaintAll();
		}

		public void Detach()
		{
			_subTool?.Cancel();

			if ( _mode == EditMode.Primitive )
				CancelPrimitive();

			if ( _dragging )
				EndTransform();

			Undo.undoRedoPerformed -= OnUndoRedo;
			UnityEditor.Selection.selectionChanged -= OnUnitySelectionChanged;
		}

		void OnUndoRedo()
		{
			HammerTrace.Log( "OnUndoRedo" );
			_dragging = false;
			_objectDragging = false;

			// Rebuild the undone meshes straight away so every view shows the result this frame
			foreach ( var c in UnityEngine.Object.FindObjectsByType<HammerMesh>( FindObjectsSortMode.None ) )
				c.RebuildIfReloaded();

			Selection.SyncFromUndo();
			Selection.RemoveInvalid();
			// The pivot came back with the undo step: the restored selection mustn't clear it
			_pivotSelectionVersion = Selection.Version;
			HammerViews.RepaintAll();
		}

		void OnUnitySelectionChanged()
		{
			Selection.RemoveInvalid();
		}

		/// <summary>
		/// Meshes whose wireframes are drawn: selected objects, plus anything with selected elements.
		/// </summary>
		public List<HammerMesh> EditMeshes()
		{
			var set = new HashSet<HammerMesh>();

			foreach ( var go in UnityEditor.Selection.gameObjects )
			{
				foreach ( var m in go.GetComponentsInChildren<HammerMesh>() )
					set.Add( m );
			}

			foreach ( var c in Selection.Components )
				set.Add( c );

			if ( _hover?.Component != null )
				set.Add( _hover.Component );

			set.RemoveWhere( x => x == null || !x.isActiveAndEnabled );
			return set.ToList();
		}

		public IEnumerable<MeshVertex> SelectedVertices => Selection.OfType<MeshVertex>().Where( x => x.IsValid );
		public IEnumerable<MeshEdge> SelectedEdges => Selection.OfType<MeshEdge>().Where( x => x.IsValid );
		public IEnumerable<MeshFace> SelectedFaces => Selection.OfType<MeshFace>().Where( x => x.IsValid );

		/// <summary>
		/// Every vertex touched by the current selection.
		/// </summary>
		public HashSet<MeshVertex> SelectionVertices() => new( CachedSelectionVertices() );

		/// <summary>
		/// How many vertices the selection touches.
		/// </summary>
		public int SelectionVertexCount => CachedSelectionVertices().Count;

		// The selection's vertices and their world bounds, worked out again only when the
		// selection, its meshes or their objects change (the views ask several times a frame,
		// which added up with thousands selected)
		HashSet<MeshVertex> _selectionVertices;
		Bounds _selectionVertexBounds;
		int _selectionVerticesStamp;
		static int _dirtyStamp = int.MinValue / 2;

		int SelectionStamp()
		{
			unchecked
			{
				var h = Selection.Version * 397 + Selection.Count;
				foreach ( var c in Selection.Components )
				{
					// (edited since its last rebuild: can't trust anything kept)
					if ( c.Mesh.IsDirty ) return ++_dirtyStamp;
					h = h * 31 + c.Mesh.TriangulationVersion * 7 + c.Mesh.GetHashCode() + c.transform.localToWorldMatrix.GetHashCode();
				}
				return h;
			}
		}

		HashSet<MeshVertex> CachedSelectionVertices()
		{
			var stamp = SelectionStamp();
			if ( _selectionVertices != null && stamp == _selectionVerticesStamp )
				return _selectionVertices;

			_selectionVertices = FindSelectionVertices();
			_selectionVerticesStamp = stamp;
			var first = true;
			foreach ( var v in _selectionVertices )
			{
				var p = v.PositionWorld;
				if ( first ) { _selectionVertexBounds = new Bounds( p, Vector3.zero ); first = false; }
				else _selectionVertexBounds.Encapsulate( p );
			}
			if ( first ) _selectionVertexBounds = default;
			return _selectionVertices;
		}

		/// <summary>
		/// World bounds of every vertex the selection touches; false when nothing is selected.
		/// </summary>
		bool SelectionVertexBounds( out Bounds bounds )
		{
			var any = CachedSelectionVertices().Count > 0;
			bounds = _selectionVertexBounds;
			return any;
		}

		HashSet<MeshVertex> FindSelectionVertices()
		{
			var result = new HashSet<MeshVertex>();

			foreach ( var element in Selection )
			{
				if ( !element.IsValid ) continue;
				var mesh = element.Component.Mesh;

				switch ( element )
				{
					case MeshVertex v:
						result.Add( v );
						break;

					case MeshEdge e:
						mesh.GetEdgeVertices( e.Handle, out var a, out var b );
						result.Add( new MeshVertex( e.Component, a ) );
						result.Add( new MeshVertex( e.Component, b ) );
						break;

					case MeshFace f:
						foreach ( var hv in mesh.GetFaceVertices( f.Handle ) )
							result.Add( new MeshVertex( f.Component, hv ) );
						break;
				}
			}

			return result;
		}

		void ConvertSelection( EditMode from, EditMode to )
		{
			if ( from == to || Selection.Count == 0 )
				return;

			if ( to is EditMode.Object or EditMode.Primitive or EditMode.Paint )
				return;

			var elements = Selection.ToList();
			Selection.Clear();

			foreach ( var element in elements )
			{
				if ( !element.IsValid ) continue;
				var component = element.Component;
				var mesh = component.Mesh;

				switch ( to )
				{
					case EditMode.Vertex:
						if ( element is MeshEdge e )
						{
							mesh.GetEdgeVertices( e.Handle, out var a, out var b );
							Selection.Add( new MeshVertex( component, a ) );
							Selection.Add( new MeshVertex( component, b ) );
						}
						else if ( element is MeshFace f )
						{
							foreach ( var v in mesh.GetFaceVertices( f.Handle ) )
								Selection.Add( new MeshVertex( component, v ) );
						}
						else Selection.Add( element );
						break;

					case EditMode.Edge:
						if ( element is MeshFace face )
						{
							foreach ( var he in mesh.GetFaceEdges( face.Handle ) )
								Selection.Add( new MeshEdge( component, he ) );
						}
						else if ( element is MeshVertex vertex )
						{
							mesh.GetEdgesConnectedToVertex( vertex.Handle, out var edges );
							foreach ( var he in edges )
							{
								mesh.GetEdgeVertices( he, out var a, out var b );
								if ( elements.Contains( new MeshVertex( component, a ) ) && elements.Contains( new MeshVertex( component, b ) ) )
									Selection.Add( new MeshEdge( component, he ) );
							}
						}
						else Selection.Add( element );
						break;

					case EditMode.Face:
						if ( element is MeshFace ) Selection.Add( element );
						break;
				}
			}

			if ( to == EditMode.Face )
			{
				// Faces whose vertices (or edges) were all selected
				var vertices = elements.OfType<MeshVertex>().ToHashSet();
				var edgeSet = elements.OfType<MeshEdge>().ToHashSet();

				foreach ( var component in elements.Select( x => x.Component ).Where( x => x != null ).Distinct() )
				{
					var mesh = component.Mesh;
					foreach ( var f in mesh.FaceHandles )
					{
						var fv = mesh.GetFaceVertices( f );
						var allVerts = vertices.Count > 0 && fv.All( v => vertices.Contains( new MeshVertex( component, v ) ) );
						var allEdges = edgeSet.Count > 0 && mesh.GetFaceEdges( f ).All( e => edgeSet.Contains( new MeshEdge( component, e ) ) );
						if ( allVerts || allEdges )
							Selection.Add( new MeshFace( component, f ) );
					}
				}
			}
		}

		public override void OnToolGUI( EditorWindow window )
		{
			if ( window is not SceneView sceneView )
				return;

			// Hover highlighting and the primitive height stage need mouse move events
			sceneView.wantsMouseMove = true;

			ViewGUI( HammerViews.FromSceneView( sceneView ) );
		}

		/// <summary>
		/// Draw and handle input for one viewport. Handles' camera must already be set to the view's.
		/// </summary>
		public void ViewGUI( HammerView view )
		{
			HammerTrace.Log( $"ViewGUI {view.Name} {Event.current.type} mode={_mode} sel={Selection.Count}" );

			using ( HammerPerf.Time( $"ViewGUI {Event.current.type}" ) )
				ViewGUIInner( view );
		}

		void ViewGUIInner( HammerView view )
		{
			_view = view;
			HammerViews.Current = view;
			TrackMouse( view );
			using ( HammerPerf.Time( "RemoveInvalid" ) )
				Selection.RemoveInvalid();

			// Keep primitives oriented to the camera, like s&box does with the active viewport
			Sandbox.Primitives.PrimitiveBuilder.CameraForward = SourceSpace.ToSourceDirection( view.Camera.transform.forward );

			if ( WorkplanePickGUI( view ) )
			{
				DrawStatus( view );
				return;
			}

			// A number typed after a drag redoes it by exactly that much
			if ( Event.current.type == EventType.KeyDown && _subTool == null && _mode is EditMode.Vertex or EditMode.Edge or EditMode.Face
				&& TypeKey( Event.current.keyCode, Event.current.character ) )
			{
				Event.current.Use();
				return;
			}

			// Esc cancels, Enter applies (Unity doesn't allow these as shortcut bindings)
			var key = Event.current;
			if ( key.type == EventType.KeyDown )
			{
				if ( key.keyCode == KeyCode.Escape )
				{
					Cancel();
					key.Use();
				}
				else if ( key.keyCode is KeyCode.Return or KeyCode.KeypadEnter )
				{
					Confirm();
					key.Use();
				}
			}

			// Shift+right-click picks a material, Ctrl+right-click paints it (any mesh mode)
			var materialMouse = MaterialMouseGUI();

			if ( _subTool != null )
			{
				_subTool.ViewGUI( view );
				DrawStatus( view );
				return;
			}

			if ( materialMouse )
			{
				DrawStatus( view );
				return;
			}

			// Tab held: a click puts the pivot there instead of selecting
			if ( PlacingPivot && _mode is EditMode.Vertex or EditMode.Edge or EditMode.Face or EditMode.Object )
			{
				PivotPlaceGUI();
				DrawStatus( view );
				return;
			}

			switch ( _mode )
			{
				case EditMode.Object:
					ObjectModeGUI( view );
					break;

				case EditMode.Primitive:
					PrimitiveGUI( view );
					break;

				case EditMode.Paint:
					PaintGUI( view );
					break;

				default:
					ElementModeGUI( view );
					break;
			}

			DrawStatus( view );
		}

		/// <summary>
		/// The Hammer window draws the selection into each view's camera image (see
		/// <see cref="DrawSceneOverlay"/>); the scene view draws it with the GUI.
		/// </summary>
		static bool DrawsInCamera( HammerView view ) => view.Window is HammerWindow;

		/// <summary>
		/// Selection, wires and hover, drawn while the view's camera renders so they're depth tested
		/// against the scene.
		/// </summary>
		public void DrawSceneOverlay( HammerView view )
		{
			_view = view;
			if ( _subTool != null ) return;

			switch ( _mode )
			{
				case EditMode.Vertex:
				case EditMode.Edge:
				case EditMode.Face:
					Selection.RemoveInvalid();
					DrawElements();
					break;

				case EditMode.Object:
					DrawSelectedObjects();
					break;
			}
		}

		/// <summary>
		/// One line for the Hammer window's status bar: what's selected and how big it is.
		/// </summary>
		public string StatusText
		{
			get
			{
				if ( _typed != null ) return $"Type an amount: {_typed}   (Enter applies, Esc cancels)";
				if ( _pickingWorkplane ) return "Click a face to lay the workplane on it  ·  Esc: back to the world grid";
				if ( _subTool != null ) return $"{_subTool.Title}  ·  {_subTool.Help}";

				var count = _mode switch
				{
					EditMode.Vertex => Plural( Selection.Count, "vertex", "vertices" ),
					EditMode.Edge => Plural( Selection.Count, "edge", "edges" ),
					EditMode.Face => Plural( Selection.Count, "face", "faces" ),
					EditMode.Object => Plural( UnityEditor.Selection.gameObjects.Length, "object", "objects" ),
					EditMode.Primitive => "Block tool",
					_ => "Vertex paint",
				};

				if ( SelectionBounds( out var b ) )
				{
					var s = b.size / SourceSpace.UnitScale;
					// Hammer's w/l/h are its X/Y/Z: Unity's Z/X/Y
					count += $"        {s.z:0.##}w {s.x:0.##}l {s.y:0.##}h";
				}

				if ( MeshHealth.Warning != null )
					count += "        " + MeshHealth.Warning;

				return count;
			}
		}

		static string Plural( int n, string one, string many ) => n == 0 ? "Nothing selected" : $"{n} {(n == 1 ? one : many)}";

		void DrawStatus( HammerView view )
		{
			if ( Event.current.type != EventType.Repaint || DrawsInCamera( view ) )
				return;

			Handles.BeginGUI();
			var text = _subTool != null ? $"Hammer  ·  {_subTool.Title}  ·  {_subTool.Help}" : $"Hammer  ·  {_mode}  ·  {(_mode is EditMode.Vertex or EditMode.Edge or EditMode.Face ? _moveMode + "  ·  " : "")}Grid {HammerSettings.GridSize:0.###}{(HammerSettings.GridSnap ? "" : " (off)")}";
			if ( _subTool == null && Selection.Count > 0 ) text += $"  ·  {Selection.Count} selected";
			var style = EditorStyles.whiteMiniLabel;
			var size = style.CalcSize( new GUIContent( text ) );
			var rect = new Rect( view.Rect.x + 8, view.Rect.yMax - size.y - (view.Window is SceneView ? 30 : 8), size.x + 8, size.y + 2 );
			EditorGUI.DrawRect( rect, new Color( 0, 0, 0, 0.5f ) );
			GUI.Label( new Rect( rect.x + 4, rect.y + 1, size.x, size.y ), text, style );
			Handles.EndGUI();
		}

		/// <summary>
		/// Mark meshes changed during an interactive edit so they re-bake immediately.
		/// </summary>
		internal static void RebuildNow( IEnumerable<HammerMesh> components )
		{
			foreach ( var c in components )
			{
				if ( c == null ) continue;
				c.Mesh.IsDirty = true;
				c.RebuildRenderMesh();
				c.MarkModified();
			}
		}
	}
}
