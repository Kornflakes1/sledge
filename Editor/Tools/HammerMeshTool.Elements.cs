using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HammerUnity.EditorTools
{
	partial class HammerMeshTool
	{
		// Source 2 Hammer's selection colours: light blue mesh wires, yellow selection with an
		// orange tint on faces, cyan hover
		static readonly Color WireColor = new( 0.45f, 0.78f, 1.0f, 0.85f );
		static readonly Color WireHiddenColor = new( 0.45f, 0.78f, 1.0f, 0.12f );
		static readonly Color SelectedColor = new( 1.0f, 0.92f, 0.15f, 1.0f );
		static readonly Color SelectedFillColor = new( 1.0f, 0.85f, 0.1f, 0.3f );
		// Green under the mouse, yellow once selected, for vertices, edges and faces alike
		static readonly Color HoverColor = new( 0.2f, 1.0f, 0.2f, 1.0f );
		static readonly Color HoverFillColor = new( 0.2f, 1.0f, 0.2f, 0.18f );
		// Vertices like Hammer: small light blue squares, a bigger green one under the mouse
		static readonly Color VertexColor = new( 0.45f, 0.78f, 1.0f, 1.0f );

		IMeshElement _hover;
		HammerMesh _hoverMesh;
		Vector2 _mouseDownPosition;
		bool _mouseDown;
		bool _boxSelecting;
		readonly List<Vector2> _lasso = new();
		bool _middleLasso;

		// Dragging with the middle button always lassoes; the left button does when Lasso is on
		bool Lassoing => _middleLasso || HammerSettings.LassoSelect;
		int _clickCount;
		bool _altClick;
		int _controlId;

		void ElementModeGUI( HammerView view )
		{
			var e = Event.current;
			_controlId = GUIUtility.GetControlID( FocusType.Passive );

			if ( e.type == EventType.Layout )
				HandleUtility.AddDefaultControl( _controlId );

			// Selection under the gizmo
			if ( e.type == EventType.Repaint )
			{
				if ( !DrawsInCamera( view ) ) DrawElements();
				DrawDimensions();
				DrawEdgeAngle();
				DrawSnapMarker();

				// Hammer shows the length of the edge under the mouse
				if ( HammerSettings.EdgeLengthPreview && _hover is MeshEdge hoverEdge && hoverEdge.IsValid && !_dragging )
				{
					hoverEdge.GetWorldPoints( out var ha, out var hb );
					HammerGUI.OutlinedLabel( (ha + hb) * 0.5f, $"{Vector3.Distance( ha, hb ) / SourceSpace.UnitScale:0.00}", Color.white );
				}
			}

			// The transform handle gets first go at the mouse
			if ( Selection.Count > 0 && !_boxSelecting )
				TransformHandleGUI();

			if ( _dragging && GUIUtility.hotControl == 0 )
				EndTransform();

			HandleSelectionInput( e );
			HandleCommands( e );
		}

		/// <summary>
		/// Hammer shows the angle between two selected edges that meet: an arc across the corner
		/// with the angle in degrees.
		/// </summary>
		void DrawEdgeAngle()
		{
			if ( _mode != EditMode.Edge || Selection.Count != 2 || _dragging ) return;
			var edges = SelectedEdges.ToList();
			if ( edges.Count != 2 || edges[0].Component != edges[1].Component ) return;

			edges[0].GetWorldPoints( out var a0, out var b0 );
			edges[1].GetWorldPoints( out var a1, out var b1 );

			// The corner they share, and the far end of each
			Vector3 corner, end0, end1;
			const float same = 1e-8f;
			if ( (a0 - a1).sqrMagnitude < same ) { corner = a0; end0 = b0; end1 = b1; }
			else if ( (a0 - b1).sqrMagnitude < same ) { corner = a0; end0 = b0; end1 = a1; }
			else if ( (b0 - a1).sqrMagnitude < same ) { corner = b0; end0 = a0; end1 = b1; }
			else if ( (b0 - b1).sqrMagnitude < same ) { corner = b0; end0 = a0; end1 = a1; }
			else return;

			var d0 = (end0 - corner).normalized;
			var d1 = (end1 - corner).normalized;
			var normal = Vector3.Cross( d0, d1 );
			if ( normal.sqrMagnitude < 1e-10f ) return;
			var angle = Vector3.Angle( d0, d1 );

			// Sized on screen, but never past the shorter edge
			var radius = Mathf.Min( HammerGUI.HandleSize( corner ) * 0.3f, Mathf.Min( (end0 - corner).magnitude, (end1 - corner).magnitude ) * 0.6f );
			Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
			Handles.color = new Color( 1, 1, 1, 0.9f );
			Handles.DrawWireArc( corner, normal.normalized, d0, angle, radius, 2.5f );

			var middle = (Quaternion.AngleAxis( angle * 0.5f, normal.normalized ) * d0).normalized;
			var tick = corner + middle * radius;
			Handles.DrawAAPolyLine( 1.5f, tick - middle * radius * 0.12f, tick + middle * radius * 0.12f );
			HammerGUI.OutlinedLabel( corner + middle * radius * 1.35f, $"{angle:0.00}", Color.white );
		}

		/// <summary>
		/// Unity routes Delete, Ctrl+A, Ctrl+C/V, Ctrl+D and F through editor commands rather than
		/// key events, so take them over here while editing elements.
		/// </summary>
		void HandleCommands( Event e )
		{
			if ( e.type != EventType.ValidateCommand && e.type != EventType.ExecuteCommand )
				return;

			System.Action action = e.commandName switch
			{
				"SoftDelete" or "Delete" => Delete,
				"SelectAll" => SelectAll,
				"FrameSelected" when Selection.Count > 0 => FrameSelection,
				"Copy" when _mode == EditMode.Face => CopyFaces,
				"Paste" when _mode == EditMode.Face => PasteFaces,
				"Duplicate" when _mode == EditMode.Face => QuadSlice,
				_ => null,
			};

			if ( action == null )
				return;

			if ( e.type == EventType.ExecuteCommand )
				action();

			e.Use();
		}

		void HandleSelectionInput( Event e )
		{
			switch ( e.GetTypeForControl( _controlId ) )
			{
				case EventType.MouseMove:
					UpdateHover( e.mousePosition );
					break;

				case EventType.MouseDown:
					// Alt is camera navigation, except Alt+double-click (select coplanar faces)
					var middle = e.button == 2 && !e.alt;
					if ( (e.button != 0 && !middle) || (e.alt && e.clickCount < 2) || GUIUtility.hotControl != 0 || (!middle && HandleUtility.nearestControl != _controlId) )
						break;

					_middleLasso = middle;

					// Hammer: Shift+drag in the 3D view paints a selection over whatever the mouse
					// passes (Shift+drag in the 2D views still boxes)
					_paintSelecting = !middle && e.shift && !e.alt && e.clickCount < 2 && _view != null && !_view.Orthographic;
					if ( _paintSelecting )
					{
						Undo.IncrementCurrentGroup();
						Undo.SetCurrentGroupName( "Paint Selection" );
						_paintSelectLast = e.mousePosition;
						PaintSelect( e.mousePosition );
					}

					GUIUtility.hotControl = _controlId;
					_clickCount = e.clickCount;
					_altClick = e.alt;
					_mouseDown = true;
					_boxSelecting = false;
					_mouseDownPosition = e.mousePosition;
					_lasso.Clear();
					_lasso.Add( e.mousePosition );
					e.Use();
					break;

				case EventType.MouseDrag:
					if ( !_mouseDown || GUIUtility.hotControl != _controlId )
						break;

					if ( _paintSelecting )
					{
						// Every few pixels along the way, so a quick flick doesn't skip faces
						var steps = Mathf.Max( 1, Mathf.CeilToInt( Vector2.Distance( _paintSelectLast, e.mousePosition ) / 6 ) );
						for ( int i = 1; i <= steps; i++ )
							PaintSelect( Vector2.Lerp( _paintSelectLast, e.mousePosition, i / (float)steps ) );
						_paintSelectLast = e.mousePosition;
						e.Use();
						break;
					}

					if ( !_boxSelecting && Vector2.Distance( _mouseDownPosition, e.mousePosition ) > 4 )
						_boxSelecting = true;

					if ( Lassoing && Vector2.Distance( _lasso[^1], e.mousePosition ) > 3 )
						_lasso.Add( e.mousePosition );

					e.Use();
					break;

				case EventType.MouseUp:
					if ( !_mouseDown || GUIUtility.hotControl != _controlId )
						break;

					GUIUtility.hotControl = 0;
					_mouseDown = false;

					if ( _paintSelecting )
					{
						_paintSelecting = false;
						_boxSelecting = false;
						HammerViews.RepaintAll();
					}
					else if ( _boxSelecting )
					{
						if ( Lassoing && _lasso.Count > 2 )
						{
							_lasso.Add( e.mousePosition );
							var lasso = _lasso.ToArray();
							var bounds = RectFromPoints( lasso.Aggregate( Vector2.Min ), lasso.Aggregate( Vector2.Max ) );
							RegionSelect( bounds, p => InsidePolygon( p, lasso ), e.shift, e.control || e.command );
						}
						else
						{
							BoxSelect( _mouseDownPosition, e.mousePosition, e.shift, e.control || e.command );
						}
						_lasso.Clear();
						_boxSelecting = false;
					}
					else if ( !_middleLasso )
					{
						ClickSelect( e.mousePosition, e.shift, e.control || e.command, _clickCount > 1, _altClick );
					}
					_middleLasso = false;

					e.Use();
					break;
			}

			if ( _boxSelecting && e.type == EventType.Repaint )
				DrawSelectionRegion( _mouseDownPosition, e.mousePosition, Lassoing ? _lasso : null );
		}

		bool _paintSelecting;
		Vector2 _paintSelectLast;

		/// <summary>Shift+drag in 3D: add whatever's under the mouse to the selection.</summary>
		void PaintSelect( Vector2 mouse )
		{
			var element = PickElement( mouse );
			if ( element == null || !element.IsValid || Selection.Contains( element ) ) return;
			Selection.Add( element );
			HammerViews.RepaintAll();
		}

		/// <summary>
		/// The box being dragged out, or the lasso so far (closed back to where it started).
		/// </summary>
		static void DrawSelectionRegion( Vector2 start, Vector2 mouse, List<Vector2> lasso )
		{
			Handles.BeginGUI();
			if ( lasso != null && lasso.Count > 1 )
			{
				Handles.color = new Color( 0.4f, 0.7f, 1.0f, 0.9f );
				var points = lasso.Append( mouse ).Select( p => (Vector3)p ).ToList();
				Handles.DrawAAPolyLine( 2.0f, points.ToArray() );
				Handles.color = new Color( 0.4f, 0.7f, 1.0f, 0.45f );
				Handles.DrawDottedLine( points[^1], points[0], 3 );
			}
			else if ( lasso == null )
			{
				var rect = RectFromPoints( start, mouse );
				EditorGUI.DrawRect( rect, new Color( 0.3f, 0.6f, 1.0f, 0.12f ) );
				var c = new Color( 0.4f, 0.7f, 1.0f, 0.9f );
				EditorGUI.DrawRect( new Rect( rect.x, rect.y, rect.width, 1 ), c );
				EditorGUI.DrawRect( new Rect( rect.x, rect.yMax - 1, rect.width, 1 ), c );
				EditorGUI.DrawRect( new Rect( rect.x, rect.y, 1, rect.height ), c );
				EditorGUI.DrawRect( new Rect( rect.xMax - 1, rect.y, 1, rect.height ), c );
			}
			Handles.EndGUI();
		}

		static Rect RectFromPoints( Vector2 a, Vector2 b ) => Rect.MinMaxRect( Mathf.Min( a.x, b.x ), Mathf.Min( a.y, b.y ), Mathf.Max( a.x, b.x ), Mathf.Max( a.y, b.y ) );

		IMeshElement PickElement( Vector2 mouse )
		{
			var meshes = MeshPicking.VisibleMeshes();
			// Hammer's 2D views always select through geometry
			var through = HammerSettings.SelectionThrough || (_view != null && _view.Orthographic);

			switch ( _mode )
			{
				case EditMode.Vertex:
					var v = MeshPicking.PickVertex( mouse, meshes, through );
					return v.IsValid ? v : null;

				case EditMode.Edge:
					var edge = MeshPicking.PickEdge( mouse, meshes, through );
					return edge.IsValid ? edge : null;

				case EditMode.Face:
					return MeshPicking.PickFace( mouse, meshes, out var hit ) ? hit.Face : null;
			}

			return null;
		}

		void UpdateHover( Vector2 mouse )
		{
			HammerTrace.Log( "UpdateHover" );
			IMeshElement hover;
			using ( HammerPerf.Time( "UpdateHover.PickElement" ) )
				hover = _dragging ? null : PickElement( mouse );

			// The mesh under the mouse gets its outline, like Hammer
			HammerMesh hoverMesh = hover?.Component;
			using ( HammerPerf.Time( "UpdateHover.PickFace" ) )
				if ( hoverMesh == null && !_dragging && MeshPicking.PickFace( mouse, MeshPicking.VisibleMeshes(), out var hit ) )
					hoverMesh = hit.Face.Component;

			if ( !Equals( hover, _hover ) || hoverMesh != _hoverMesh )
			{
				_hover = hover;
				_hoverMesh = hoverMesh;
				HammerViews.RepaintAll();
			}
		}

		void ClickSelect( Vector2 mouse, bool add, bool toggle, bool doubleClick, bool alt = false )
		{
			HammerTrace.Log( "ClickSelect" );
			IMeshElement element;
			using ( HammerPerf.Time( "ClickSelect.PickElement" ) )
				element = PickElement( mouse );

			if ( element is null )
			{
				if ( !add && !toggle )
					Selection.Clear();

				HammerViews.RepaintAll();
				return;
			}

			if ( doubleClick && element is MeshFace face )
			{
				DoubleClickFace( face, add, alt );
			}
			else if ( doubleClick && element is MeshEdge edge )
			{
				// Double click selects the loop, like s&box
				edge.Component.Mesh.FindEdgeLoopForEdges( new[] { edge.Handle }, out var loop );
				if ( !add && !toggle ) Selection.Clear();
				foreach ( var he in loop )
					Selection.Add( new MeshEdge( edge.Component, he ) );
			}
			else if ( toggle )
			{
				Selection.Toggle( element );
			}
			else if ( add )
			{
				Selection.Add( element );
			}
			else
			{
				Selection.Set( element );
			}

			// Keep Unity's selection on the objects being edited so the inspector follows along
			var go = element.Component.gameObject;
			if ( !UnityEditor.Selection.gameObjects.Contains( go ) )
			{
				if ( add || toggle )
					UnityEditor.Selection.objects = UnityEditor.Selection.objects.Append( go ).ToArray();
				else
					UnityEditor.Selection.activeGameObject = go;
			}

			HammerViews.RepaintAll();
		}

		/// <summary>
		/// Hammer's face double-clicks: all faces of the mesh; with Alt, the faces on the same
		/// plane; with Shift, the faces between the previously selected face and this one.
		/// </summary>
		void DoubleClickFace( MeshFace face, bool shift, bool alt )
		{
			var component = face.Component;
			var mesh = component.Mesh;

			if ( shift )
			{
				var anchor = Selection.OfType<MeshFace>().Where( x => x.Component == component && x.Index != face.Index ).LastOrDefault();
				if ( anchor.IsValid )
				{
					foreach ( var f in FacePath( mesh, anchor.Handle, face.Handle ) )
						Selection.Add( new MeshFace( component, f ) );
				}

				Selection.Add( face );
				return;
			}

			Selection.Clear();

			mesh.ComputeFaceNormal( face.Handle, out var normal );
			var center = mesh.GetFaceCenter( face.Handle );
			var tolerance = Mathf.Max( 0.01f, HammerSettings.GridSize * 0.01f );

			foreach ( var f in mesh.FaceHandles )
			{
				if ( mesh.IsFaceHidden( f ) ) continue;

				if ( alt )
				{
					mesh.ComputeFaceNormal( f, out var n );
					if ( Sandbox.Vector3.Dot( n, normal ) < 0.999f ) continue;
					if ( Mathf.Abs( Sandbox.Vector3.Dot( mesh.GetFaceCenter( f ) - center, normal ) ) > tolerance ) continue;
				}

				Selection.Add( new MeshFace( component, f ) );
			}
		}

		/// <summary>
		/// Shortest strip of faces from one face to another, walking across shared edges.
		/// </summary>
		static List<HalfEdgeMesh.FaceHandle> FacePath( Sandbox.PolygonMesh mesh, HalfEdgeMesh.FaceHandle from, HalfEdgeMesh.FaceHandle to )
		{
			var previous = new Dictionary<int, HalfEdgeMesh.FaceHandle> { [from.Index] = HalfEdgeMesh.FaceHandle.Invalid };
			var queue = new Queue<HalfEdgeMesh.FaceHandle>();
			queue.Enqueue( from );

			while ( queue.Count > 0 )
			{
				var current = queue.Dequeue();
				if ( current.Index == to.Index ) break;

				foreach ( var he in mesh.GetFaceEdges( current ) )
				{
					mesh.GetFacesConnectedToEdge( he, out var a, out var b );
					var next = a.Index == current.Index ? b : a;
					if ( !next.IsValid || previous.ContainsKey( next.Index ) || mesh.IsFaceHidden( next ) ) continue;
					previous[next.Index] = current;
					queue.Enqueue( next );
				}
			}

			var path = new List<HalfEdgeMesh.FaceHandle>();
			if ( !previous.ContainsKey( to.Index ) ) return path;

			for ( var f = to; f.IsValid; f = previous[f.Index] )
				path.Add( f );

			return path;
		}

		/// <summary>
		/// Is a GUI point inside a polygon (the lasso)? Even-odd rule.
		/// </summary>
		static bool InsidePolygon( Vector2 p, Vector2[] poly )
		{
			var inside = false;
			for ( int i = 0, j = poly.Length - 1; i < poly.Length; j = i++ )
			{
				if ( (poly[i].y > p.y) != (poly[j].y > p.y) &&
					p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x )
					inside = !inside;
			}
			return inside;
		}

		void BoxSelect( Vector2 a, Vector2 b, bool add, bool remove )
		{
			var rect = RectFromPoints( a, b );
			RegionSelect( rect, _ => true, add, remove );
		}

		/// <summary>
		/// Select every element inside a region of the view: a box, or a lasso (its bounds, and a
		/// test for the points inside them).
		/// </summary>
		void RegionSelect( Rect rect, Func<Vector2, bool> inside, bool add, bool remove )
		{
			var meshes = MeshPicking.VisibleMeshes();
			var editMeshes = EditMeshes();
			var candidates = editMeshes.Count > 0 ? editMeshes : meshes;
			// Hammer's 2D views always select through geometry
			var through = HammerSettings.SelectionThrough || (_view != null && _view.Orthographic);
			var found = new List<IMeshElement>();

			bool Visible( Vector3 world, out Vector2 gui )
			{
				var g = HammerGUI.WorldToGUI( world );
				gui = g;
				if ( g.z < 0 || !rect.Contains( gui ) || !inside( gui ) ) return false;
				return through || !MeshPicking.IsOccluded( world, meshes );
			}

			foreach ( var component in candidates )
			{
				var mesh = component.Mesh;

				switch ( _mode )
				{
					case EditMode.Vertex:
						foreach ( var v in mesh.VertexHandles )
						{
							if ( mesh.IsVertexHidden( v ) ) continue;
							if ( Visible( component.SourceToWorld( mesh.GetVertexPosition( v ) ), out _ ) )
								found.Add( new MeshVertex( component, v ) );
						}
						break;

					case EditMode.Edge:
						foreach ( var he in mesh.HalfEdgeHandles )
						{
							var opposite = mesh.GetOppositeHalfEdge( he );
							if ( opposite.IsValid && he.Index > opposite.Index ) continue;
							if ( mesh.IsEdgeHidden( he ) ) continue;
							var line = mesh.GetEdgeLine( he );
							if ( Visible( component.SourceToWorld( line.Start ), out _ ) && Visible( component.SourceToWorld( line.End ), out _ ) )
								found.Add( new MeshEdge( component, he ) );
						}
						break;

					case EditMode.Face:
						foreach ( var f in mesh.FaceHandles )
						{
							if ( mesh.IsFaceHidden( f ) ) continue;
							var verts = mesh.GetFaceVertices( f );
							if ( verts.All( v => Visible( component.SourceToWorld( mesh.GetVertexPosition( v ) ), out _ ) ) )
								found.Add( new MeshFace( component, f ) );
						}
						break;
				}
			}

			if ( !add && !remove )
				Selection.Clear();

			foreach ( var element in found )
			{
				if ( remove ) Selection.Remove( element );
				else Selection.Add( element );
			}

			HammerViews.RepaintAll();
		}

		void DrawElements()
		{
			using ( HammerPerf.Time( "DrawElements" ) )
				DrawElementsInner();
		}

		void DrawElementsInner()
		{
			HammerTrace.Log( "DrawElements" );
			// Every open edge (a hole's border) gets faint ticks, so holes are easy to spot; broken
			// faces get red outlines
			var openLines = new List<Vector3>();
			foreach ( var component in EditMeshes() )
			{
				using ( HammerPerf.Time( "MeshHealth.Draw" ) )
					MeshHealth.Draw( component, Lift );

				using ( HammerPerf.Time( "OpenEdgeTicks" ) )
					foreach ( var he in OpenEdges( component ) )
						DrawOpenEdgeTicks( component, he, OpenEdgeColor, true, openLines );
			}

			if ( openLines.Count > 0 )
			{
				Handles.color = OpenEdgeColor;
				Handles.DrawLines( openLines.ToArray() );
			}

			// Wires (and vertices) only on the mesh under the mouse and meshes with something selected
			var selected = Selection.Components.ToHashSet();
			var meshes = EditMeshes().Where( c => c == _hoverMesh || selected.Contains( c ) );
			var zTest = Handles.zTest;

			foreach ( var component in meshes )
			{
				// Display > Draw Wireframe
				if ( HammerSettings.DrawWireframe )
				{
					var array = LocalLines( component, 0, 0, EdgeLines );
					Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
					Handles.color = WireHiddenColor;
					DrawLocalLines( component, array );

					Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
					Handles.color = WireColor;
					DrawLocalLines( component, array );
				}

				DrawDisplayExtras( component );

				if ( _mode == EditMode.Vertex )
				{
					var points = LocalLines( component, 1, 0, VertexPoints );
					var matrix = component.transform.localToWorldMatrix;
					var dots = new List<Vector3>( points.Length );
					foreach ( var p in points ) dots.Add( matrix.MultiplyPoint3x4( p ) );
					DrawDots( dots, VertexColor, 0.018f );
				}
			}

			// Face tints in the 3D views: one depth tested mesh per object, not a triangle at a time
			var camera = HammerGUI.Camera;
			var perspective = camera != null && !camera.orthographic;
			using ( HammerPerf.Time( "DrawFaceTints" ) )
				if ( perspective ) DrawFaceTints();

			// Selection and hover: faint where hidden behind geometry, solid where visible
			foreach ( var pass in new[] { UnityEngine.Rendering.CompareFunction.Greater, UnityEngine.Rendering.CompareFunction.LessEqual } )
			{
				Handles.zTest = pass;
				var alpha = pass == UnityEngine.Rendering.CompareFunction.Greater ? 0.3f : 1.0f;

				// Face fills only where visible: a faint fill behind the surface fights with it
				var hidden = pass == UnityEngine.Rendering.CompareFunction.Greater || perspective;

				using ( HammerPerf.Time( "DrawSelection" ) )
					if ( Selection.Count > LargeSelection )
						DrawLargeSelection( Fade( SelectedColor, alpha ), hidden ? Color.clear : SelectedFillColor );
					else
						foreach ( var element in Selection )
							DrawElement( element, Fade( SelectedColor, alpha ), hidden ? Color.clear : SelectedFillColor );

				using ( HammerPerf.Time( "DrawHover" ) )
					if ( _hover != null && _hover.IsValid && !_dragging && !Selection.Contains( _hover ) )
						DrawElement( _hover, Fade( HoverColor, alpha ), hidden ? Color.clear : HoverFillColor );
			}

			Handles.zTest = zTest;
		}

		/// <summary>
		/// The selected faces and the face under the mouse, tinted (x-ray shows them through
		/// whatever is in front).
		/// </summary>
		void DrawFaceTints()
		{
			var zTest = HammerSettings.SelectionThrough ? UnityEngine.Rendering.CompareFunction.Always : UnityEngine.Rendering.CompareFunction.LessEqual;

			HashSet<(HammerMesh, int)> selected = null;
			foreach ( var component in Selection.Components )
			{
				var c = component;
				OverlayFill.Draw( c, 1, Selection.Version, f =>
				{
					selected ??= Selection.OfType<MeshFace>().Select( x => (x.Component, x.Handle.Index) ).ToHashSet();
					return selected.Contains( (c, f.Index) );
				}, SelectedFillColor, zTest );
			}

			if ( _hover is MeshFace hover && hover.IsValid && !_dragging && !Selection.Contains( hover ) )
				OverlayFill.Draw( hover.Component, 2, hover.Handle.Index, f => f == hover.Handle, HoverFillColor, zTest );
		}

		/// <summary>
		/// Past this many selected elements they're drawn the quick way: every outline in one
		/// batch of thin lines, fills only checked for facing the camera (not for being hidden
		/// behind something), no 2D crosshatch. Drawing thousands one at a time took seconds.
		/// </summary>
		const int LargeSelection = 12;

		void DrawLargeSelection( Color color, Color fill )
		{
			var dots = new List<Vector3>();
			var camera = HammerGUI.Camera;
			var ortho = camera != null && camera.orthographic;

			// Outlines and vertices: worked out once per selection change, in the mesh's own space
			Handles.color = color;
			foreach ( var component in Selection.Components )
			{
				var c = component;
				DrawLocalLines( c, LocalLines( c, 2, Selection.Version, _ => SelectionOutlines( c ) ) );

				var matrix = c.transform.localToWorldMatrix;
				foreach ( var p in LocalLines( c, 4, Selection.Version, _ => SelectionPoints( c ) ) )
					dots.Add( matrix.MultiplyPoint3x4( p ) );
			}

			// 2D views: a plain tint over the faces facing the view, no crosshatch
			if ( fill.a > 0 && ortho )
				foreach ( var component in Selection.Components )
				{
					var c = component;
					HashSet<int> faces = null;
					OverlayFill.Draw( c, 3, Selection.Version, f =>
					{
						faces ??= Selection.OfType<MeshFace>().Where( x => x.Component == c ).Select( x => x.Handle.Index ).ToHashSet();
						return faces.Contains( f.Index );
					}, new Color( fill.r, fill.g, fill.b, fill.a * 0.6f ), UnityEngine.Rendering.CompareFunction.Always );
				}

			DrawDots( dots, color, 0.05f );
		}

		// Each mesh's triangles grouped by face, worked out once per redraw (looking a face's
		// triangles up by scanning them all, for every selected face, was quadratic)
		static readonly Dictionary<HammerMesh, (object Source, int Count, Dictionary<int, List<int>> ByFace)> _triangles = new();

		static List<int> TrianglesOf( HammerMesh component, HalfEdgeMesh.FaceHandle face )
		{
			var faces = component.Mesh.TriangleFaces;
			// Still the same triangulation? (an edit since makes a new one)
			if ( !_triangles.TryGetValue( component, out var cached ) || !ReferenceEquals( cached.Source, component.Mesh ) || cached.Count != component.Mesh.TriangulationVersion )
			{
				var byFace = new Dictionary<int, List<int>>();
				var indices = component.Mesh.CollisionIndices;
				for ( int tri = 0; tri < faces.Count && tri * 3 + 2 < indices.Count; tri++ )
				{
					if ( !byFace.TryGetValue( faces[tri].Index, out var list ) ) byFace[faces[tri].Index] = list = new List<int>();
					list.Add( tri );
				}
				cached = (component.Mesh, component.Mesh.TriangulationVersion, byFace);
				_triangles[component] = cached;
			}
			return cached.ByFace.TryGetValue( face.Index, out var tris ) ? tris : Empty;
		}

		static readonly List<int> Empty = new();

		/// <summary>
		/// Facing the camera and not hidden behind other geometry (checked at the face centre).
		/// </summary>
		bool FaceVisible( MeshFace face )
		{
			var camera = HammerGUI.Camera;
			if ( camera == null ) return true;

			var center = face.CenterWorld;
			var toCamera = camera.orthographic ? -camera.transform.forward : camera.transform.position - center;
			if ( Vector3.Dot( face.NormalWorld, toCamera ) <= 0 ) return false;

			return camera.orthographic || HammerSettings.SelectionThrough || PartlyVisible( face, MeshPicking.VisibleMeshes() );
		}

		/// <summary>
		/// Is any of the face in view: its centre or a point near each corner? (Checking only the
		/// centre misses faces that are half behind something.)
		/// </summary>
		static bool PartlyVisible( MeshFace face, List<HammerMesh> meshes )
		{
			var center = face.CenterWorld;
			if ( !MeshPicking.IsOccluded( center, meshes ) ) return true;

			var mesh = face.Component.Mesh;
			foreach ( var v in mesh.GetFaceVertices( face.Handle ) )
			{
				var corner = face.Component.SourceToWorld( mesh.GetVertexPosition( v ) );
				if ( !MeshPicking.IsOccluded( Vector3.Lerp( center, corner, 0.85f ), meshes ) ) return true;
			}

			return false;
		}

		/// <summary>
		/// s&amp;box's open edge marker: short ticks along the edge pointing into the face it borders.
		/// </summary>
		// Bright and thick so a hole reads at a glance, whatever's around it
		static readonly Color OpenEdgeColor = new( 1.0f, 0.68f, 0.12f, 1.0f );

		// With <paramref name="batch"/> the ticks (and the edge itself, if asked) are only collected, to be
		// drawn in one go by the caller: a draw call per tick took milliseconds a frame on big maps
		static void DrawOpenEdgeTicks( HammerMesh component, HalfEdgeMesh.HalfEdgeHandle edge, Color color, bool withLine = false, List<Vector3> batch = null )
		{
			var mesh = component.Mesh;
			var face = mesh.GetHalfEdgeFace( edge );
			if ( !face.IsValid ) face = mesh.GetHalfEdgeFace( mesh.GetOppositeHalfEdge( edge ) );
			if ( !face.IsValid ) return;

			var line = mesh.GetEdgeLine( edge );
			var a = component.SourceToWorld( line.Start );
			var b = component.SourceToWorld( line.End );
			var length = Vector3.Distance( a, b );
			if ( length <= 1e-5f ) return;

			mesh.ComputeFaceNormal( face, out var n );
			var normal = component.SourceDirectionToWorld( n ).normalized;
			var direction = (b - a) / length;
			var tangent = Vector3.Cross( normal, direction );
			if ( tangent.sqrMagnitude < 1e-8f ) return;
			tangent.Normalize();

			var faceCenter = component.SourceToWorld( mesh.GetFaceCenter( face ) );
			if ( Vector3.Dot( tangent, faceCenter - (a + b) * 0.5f ) < 0 ) tangent = -tangent;

			var lines = new List<Vector3>();
			var travelled = 0.0f;
			for ( int i = 0; i < 256; i++ )
			{
				var p = Vector3.Lerp( a, b, travelled / length );
				var size = HammerGUI.HandleSize( p );
				lines.Add( Lift( p ) );
				lines.Add( Lift( p + tangent * size * 0.07f ) );
				if ( travelled >= length ) break;
				travelled = Mathf.Min( length, travelled + size * 0.07f );
			}

			if ( batch != null )
			{
				if ( withLine ) { batch.Add( Lift( a ) ); batch.Add( Lift( b ) ); }
				batch.AddRange( lines );
				return;
			}

			Handles.color = color;
			if ( withLine ) Handles.DrawLine( Lift( a ), Lift( b ), Px( 3.5f ) );
			for ( int i = 0; i + 1 < lines.Count; i += 2 )
				Handles.DrawLine( lines[i], lines[i + 1], Px( 2.0f ) );
		}

		// Lines (or points) in a mesh's own space, kept until it's rebuilt or the key changes
		sealed class LocalCacheEntry
		{
			public object Source;
			public int Triangulation;
			public int Key;
			public Vector3[] Points;
		}

		static readonly Dictionary<(HammerMesh, int), LocalCacheEntry> _localCache = new();

		static Vector3[] LocalLines( HammerMesh component, int slot, int key, Func<Sandbox.PolygonMesh, List<Vector3>> build )
		{
			var mesh = component.Mesh;
			if ( !_localCache.TryGetValue( (component, slot), out var entry ) || !ReferenceEquals( entry.Source, mesh )
				|| entry.Triangulation != mesh.TriangulationVersion || entry.Key != key )
			{
				entry = new LocalCacheEntry { Source = mesh, Triangulation = mesh.TriangulationVersion, Key = key, Points = build( mesh ).ToArray() };
				_localCache[(component, slot)] = entry;
			}
			return entry.Points;
		}

		static Vector3 Local( Sandbox.Vector3 v ) => SourceSpace.ToUnityPosition( v );

		static List<Vector3> EdgeLines( Sandbox.PolygonMesh mesh )
		{
			var lines = new List<Vector3>();
			foreach ( var he in mesh.HalfEdgeHandles )
			{
				var opposite = mesh.GetOppositeHalfEdge( he );
				if ( opposite.IsValid && he.Index > opposite.Index ) continue;
				if ( mesh.IsEdgeHidden( he ) ) continue;
				var line = mesh.GetEdgeLine( he );
				lines.Add( Local( line.Start ) );
				lines.Add( Local( line.End ) );
			}
			return lines;
		}

		static List<Vector3> VertexPoints( Sandbox.PolygonMesh mesh )
		{
			var points = new List<Vector3>();
			foreach ( var v in mesh.VertexHandles )
				if ( !mesh.IsVertexHidden( v ) ) points.Add( Local( mesh.GetVertexPosition( v ) ) );
			return points;
		}

		List<Vector3> SelectionOutlines( HammerMesh component )
		{
			var mesh = component.Mesh;
			var lines = new List<Vector3>();
			foreach ( var element in Selection )
			{
				if ( element.Component != component || !element.IsValid ) continue;
				if ( element is MeshEdge e )
				{
					var line = mesh.GetEdgeLine( e.Handle );
					lines.Add( Local( line.Start ) );
					lines.Add( Local( line.End ) );
				}
				else if ( element is MeshFace f )
				{
					var corners = mesh.GetFaceVertices( f.Handle );
					for ( int i = 0; i < corners.Length; i++ )
					{
						lines.Add( Local( mesh.GetVertexPosition( corners[i] ) ) );
						lines.Add( Local( mesh.GetVertexPosition( corners[(i + 1) % corners.Length] ) ) );
					}
				}
			}
			return lines;
		}

		List<Vector3> SelectionPoints( HammerMesh component )
		{
			var mesh = component.Mesh;
			var points = new List<Vector3>();
			foreach ( var element in Selection )
				if ( element is MeshVertex v && v.Component == component && v.IsValid )
					points.Add( Local( mesh.GetVertexPosition( v.Handle ) ) );
			return points;
		}

		// The open edges (a hole's border) of each mesh, found again only after a rebuild
		static readonly Dictionary<HammerMesh, (object Source, int Triangulation, List<HalfEdgeMesh.HalfEdgeHandle> Edges)> _openEdges = new();

		static List<HalfEdgeMesh.HalfEdgeHandle> OpenEdges( HammerMesh component )
		{
			var mesh = component.Mesh;
			if ( _openEdges.TryGetValue( component, out var cached ) && ReferenceEquals( cached.Source, mesh ) && cached.Triangulation == mesh.TriangulationVersion )
				return cached.Edges;

			var edges = new List<HalfEdgeMesh.HalfEdgeHandle>();
			foreach ( var he in mesh.HalfEdgeHandles )
			{
				if ( !mesh.IsEdgeOpen( he ) || mesh.IsEdgeHidden( he ) ) continue;
				var opposite = mesh.GetOppositeHalfEdge( he );
				if ( opposite.IsValid && he.Index > opposite.Index ) continue;
				edges.Add( he );
			}
			_openEdges[component] = (mesh, mesh.TriangulationVersion, edges);
			return edges;
		}

		/// <summary>
		/// Draw lines given in the mesh's own space, nudged towards the camera like <see cref="Lift"/>
		/// (all at once with a matrix: lifting each point took longer than drawing them).
		/// </summary>
		static void DrawLocalLines( HammerMesh component, Vector3[] lines )
		{
			if ( lines.Length == 0 ) return;
			var matrix = Handles.matrix;
			Handles.matrix = LiftMatrix() * component.transform.localToWorldMatrix;
			Handles.DrawLines( lines );
			Handles.matrix = matrix;
		}

		/// <summary>
		/// <see cref="Lift"/> for every point at once: towards the camera by a fixed share of the
		/// distance (3D), or by a fixed amount (2D).
		/// </summary>
		static Matrix4x4 LiftMatrix()
		{
			var camera = HammerGUI.Camera;
			if ( camera == null ) return Matrix4x4.identity;

			var t = camera.transform;
			var probe = t.position + t.forward * 10;
			if ( camera.orthographic )
				return Matrix4x4.Translate( -t.forward * HammerGUI.HandleSize( probe ) * 0.01f );

			// The handle size grows with distance, so a fixed share of it is a scale about the camera
			var share = HammerGUI.HandleSize( probe ) * 0.01f / 10;
			return Matrix4x4.Translate( t.position ) * Matrix4x4.Scale( Vector3.one * (1 - share) ) * Matrix4x4.Translate( -t.position );
		}

		/// <summary>
		/// Square dots facing the camera, all in one go (a handle cap each is slow by the thousand).
		/// <paramref name="size"/> is a fraction of the handle size, like DotHandleCap's.
		/// </summary>
		static void DrawDots( List<Vector3> points, Color color, float size )
		{
			var camera = HammerGUI.Camera;
			if ( points.Count == 0 || camera == null || Event.current.type != EventType.Repaint ) return;

			var right = camera.transform.right;
			var up = camera.transform.up;
			if ( !OverlayFill.SetPass( color, Handles.zTest ) ) return;
			GL.PushMatrix();
			GL.MultMatrix( Handles.matrix * LiftMatrix() );
			GL.Begin( GL.QUADS );
			// The handle size is the distance along the view times a fixed amount (or fixed, in 2D)
			var t = camera.transform;
			var probe = t.position + t.forward * 10;
			var fixedSize = HammerGUI.HandleSize( probe ) * size;
			var perDepth = fixedSize / 10;
			foreach ( var p in points )
			{
				var s = camera.orthographic ? fixedSize : Vector3.Dot( p - t.position, t.forward ) * perDepth;
				var r = right * s;
				var u = up * s;
				GL.Vertex( p - r - u );
				GL.Vertex( p - r + u );
				GL.Vertex( p + r + u );
				GL.Vertex( p + r - u );
			}
			GL.End();
			GL.PopMatrix();
		}

		static Color Fade( Color c, float alpha ) => new( c.r, c.g, c.b, c.a * alpha );

		/// <summary>
		/// Nudge a point towards the camera so overlays don't fight with the surface they sit on.
		/// </summary>
		static Vector3 Lift( Vector3 world )
		{
			var camera = HammerGUI.Camera;
			if ( camera == null ) return world;

			var toCamera = camera.orthographic ? -camera.transform.forward : (camera.transform.position - world).normalized;
			return world + toCamera * HammerGUI.HandleSize( world ) * 0.01f;
		}

		static readonly Color HardEdgeColor = new( 1.0f, 0.45f, 0.15f, 1.0f );
		static readonly Color SoftEdgeColor = new( 0.3f, 0.9f, 0.85f, 1.0f );
		static readonly Color NormalColor = new( 0.4f, 0.75f, 1.0f, 0.9f );

		/// <summary>
		/// Display > Show Normals (a short line out of each face) and Show Hard / Soft Edges
		/// (edges set hard in orange, soft in teal).
		/// </summary>
		void DrawDisplayExtras( HammerMesh component )
		{
			var mesh = component.Mesh;
			Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;

			if ( HammerSettings.ShowNormals )
			{
				var normals = new List<Vector3>();
				foreach ( var f in mesh.FaceHandles )
				{
					if ( mesh.IsFaceHidden( f ) ) continue;
					mesh.ComputeFaceNormal( f, out var n );
					var c = component.SourceToWorld( mesh.GetFaceCenter( f ) );
					normals.Add( c );
					normals.Add( c + component.SourceDirectionToWorld( n ).normalized * HammerGUI.HandleSize( c ) * 0.25f );
				}
				Handles.color = NormalColor;
				Handles.DrawLines( normals.ToArray() );
			}

			if ( HammerSettings.ShowHardSoftEdges && _mode == EditMode.Edge )
			{
				foreach ( var he in mesh.HalfEdgeHandles )
				{
					var opposite = mesh.GetOppositeHalfEdge( he );
					if ( opposite.IsValid && he.Index > opposite.Index ) continue;
					var smoothing = mesh.GetEdgeSmoothing( he );
					if ( smoothing == Sandbox.PolygonMesh.EdgeSmoothMode.Default ) continue;
					var line = mesh.GetEdgeLine( he );
					Handles.color = smoothing == Sandbox.PolygonMesh.EdgeSmoothMode.Hard ? HardEdgeColor : SoftEdgeColor;
					Handles.DrawLine( Lift( component.SourceToWorld( line.Start ) ), Lift( component.SourceToWorld( line.End ) ), Px( 2.0f ) );
				}
			}
		}

		/// <summary>
		/// A line width in screen points, as pixels.
		/// </summary>
		static float Px( float points ) => points * EditorGUIUtility.pixelsPerPoint;

		void DrawElement( IMeshElement element, Color color, Color fill )
		{
			if ( !element.IsValid ) return;

			var component = element.Component;
			var mesh = component.Mesh;

			switch ( element )
			{
				case MeshVertex v:
				{
					var p = Lift( v.PositionWorld );
					Handles.color = color;
					Handles.DotHandleCap( 0, p, Quaternion.identity, HammerGUI.HandleSize( p ) * 0.05f, EventType.Repaint );
					break;
				}

				case MeshEdge e:
				{
					e.GetWorldPoints( out var a, out var b );
					Handles.color = color;
					// Solid and crisp like Hammer (anti-aliased thick lines look like a glow). Open
					// edges (a face on one side only) are a little thinner, with ticks into their face
					var open = e.IsOpen;
					Handles.DrawLine( Lift( a ), Lift( b ), Px( open ? 2.5f : 3.5f ) );
					if ( open ) DrawOpenEdgeTicks( e.Component, e.Handle, color );
					break;
				}

				case MeshFace f:
				{
					var ortho = HammerGUI.Camera?.orthographic ?? false;

					// 2D views: Hammer's orange crosshatch on faces facing the view
					if ( fill.a > 0 && ortho && FaceVisible( f ) )
						DrawFaceHatch( f, fill == SelectedFillColor ? new Color( 1.0f, 0.55f, 0.08f, 1 ) : new Color( fill.r, fill.g, fill.b, 1 ) );

					if ( fill.a > 0 && !ortho && FaceVisible( f ) )
					{
						// The fill is drawn on top rather than depth tested: lying exactly on the
						// surface it would fight with it. Hidden faces are skipped instead.
						var zTest = Handles.zTest;
						Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
						DrawFaceFill( f, fill );
						Handles.zTest = zTest;
					}
					Handles.color = color;
					var verts = mesh.GetFaceVertices( f.Handle ).Select( x => Lift( component.SourceToWorld( mesh.GetVertexPosition( x ) ) ) ).ToList();
					verts.Add( verts[0] );
					for ( int i = 0; i + 1 < verts.Count; i++ )
						Handles.DrawLine( verts[i], verts[i + 1], Px( 2.5f ) );
					break;
				}
			}
		}

		/// <summary>
		/// Crosshatched fill for 2D views: a dim fill with diagonal lines both ways, a few pixels
		/// apart whatever the zoom.
		/// </summary>
		static void DrawFaceHatch( MeshFace face, Color color )
		{
			var camera = HammerGUI.Camera;
			var view = HammerViews.Current;
			if ( camera == null || view == null || view.Rect.height < 1 ) return;

			DrawFaceFill( face, new Color( color.r, color.g, color.b, 0.18f ) );

			var right = camera.transform.right;
			var up = camera.transform.up;
			var spacing = 5 * 2 * camera.orthographicSize / view.Rect.height;

			var component = face.Component;
			var mesh = component.Mesh;
			var faces = mesh.TriangleFaces;
			var indices = mesh.CollisionIndices;
			var vertices = mesh.CollisionVertices;
			var lines = new List<Vector3>();

			for ( int tri = 0; tri < faces.Count && tri * 3 + 2 < indices.Count; tri++ )
			{
				if ( faces[tri] != face.Handle ) continue;

				var w = new[]
				{
					component.SourceToWorld( vertices[indices[tri * 3]] ),
					component.SourceToWorld( vertices[indices[tri * 3 + 1]] ),
					component.SourceToWorld( vertices[indices[tri * 3 + 2]] ),
				};

				foreach ( var dir in new[] { (right + up).normalized, (right - up).normalized } )
				{
					// Lines across the triangle where dot(p, dir) is a multiple of the spacing
					var d = new[] { Vector3.Dot( w[0], dir ), Vector3.Dot( w[1], dir ), Vector3.Dot( w[2], dir ) };
					var min = Mathf.Min( d[0], Mathf.Min( d[1], d[2] ) );
					var max = Mathf.Max( d[0], Mathf.Max( d[1], d[2] ) );

					for ( var c = Mathf.Ceil( min / spacing ) * spacing; c <= max; c += spacing )
					{
						var hits = 0;
						Vector3 a = default, b = default;
						for ( int e = 0; e < 3 && hits < 2; e++ )
						{
							var i0 = e;
							var i1 = (e + 1) % 3;
							var t0 = d[i0] - c;
							var t1 = d[i1] - c;
							if ( t0 * t1 > 0 || Mathf.Approximately( t0, t1 ) ) continue;
							var p = Vector3.Lerp( w[i0], w[i1], t0 / (t0 - t1) );
							if ( hits == 0 ) a = p; else b = p;
							hits++;
						}

						if ( hits == 2 )
						{
							lines.Add( a );
							lines.Add( b );
						}
					}
				}
			}

			Handles.color = new Color( color.r, color.g, color.b, 0.75f );
			Handles.DrawLines( lines.ToArray() );
		}

		static void DrawFaceFill( MeshFace face, Color color )
		{
			var component = face.Component;
			var mesh = component.Mesh;
			var indices = mesh.CollisionIndices;
			var vertices = mesh.CollisionVertices;

			Handles.color = color;

			foreach ( var tri in TrianglesOf( component, face.Handle ) )
			{
				Handles.DrawAAConvexPolygon(
					Lift( component.SourceToWorld( vertices[indices[tri * 3]] ) ),
					Lift( component.SourceToWorld( vertices[indices[tri * 3 + 1]] ) ),
					Lift( component.SourceToWorld( vertices[indices[tri * 3 + 2]] ) ) );
			}
		}
	}
}
