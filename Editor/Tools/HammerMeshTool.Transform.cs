using System.Collections.Generic;
using System.Linq;
using HalfEdgeMesh;
using UnityEditor;
using UnityEngine;

namespace HammerUnity.EditorTools
{
	partial class HammerMeshTool
	{
		bool _dragging;
		Vector3 _pivotStart;
		Vector3 _handlePosition;
		Quaternion _handleRotation = Quaternion.identity;
		Quaternion _rotationStart = Quaternion.identity;
		Vector3 _handleScale = Vector3.one;
		readonly Dictionary<MeshVertex, Vector3> _startPositions = new();
		List<MeshFace> _extrudeSideFaces;
		HammerMesh[] _dragComponents;

		/// <summary>
		/// Centre of the selection in world space.
		/// </summary>
		public Vector3 SelectionCenter()
		{
			return SelectionVertexBounds( out var bounds ) ? bounds.center : Vector3.zero;
		}

		Quaternion SelectionBasis()
		{
			// World axes: the workplane's, when there is one
			if ( HammerSettings.GlobalSpace )
				return Workplane.Rotation;

			var face = SelectedFaces.FirstOrDefault();
			if ( face.IsValid )
			{
				var normal = face.NormalWorld;
				var up = Mathf.Abs( Vector3.Dot( normal, Vector3.up ) ) > 0.99f ? Vector3.forward : Vector3.up;
				return Quaternion.LookRotation( normal, up );
			}

			var first = Selection.FirstOrDefault( x => x.IsValid );
			return first != null ? first.Component.transform.rotation : Quaternion.identity;
		}

		/// <summary>
		/// Set with the pivot tool: where rotate and scale turn around. Cleared when the
		/// selection changes.
		/// </summary>
		Vector3? _customPivot
		{
			get => PivotState.HasPivot ? PivotState.Pivot : null;
			set { PivotState.HasPivot = value.HasValue; PivotState.Pivot = value ?? default; }
		}
		int _pivotSelectionVersion = -1;

		/// <summary>
		/// Where the pivots live, so moving one is a step in Unity's undo history like any other
		/// edit (and undoing a move that carried the pivot along takes it back too).
		/// </summary>
		sealed class PivotUndoState : ScriptableObject
		{
			public bool HasPivot;
			public Vector3 Pivot;
			public bool HasObjectPivot;
			public Vector3 ObjectPivot;
			public int ObjectPivotFor;
		}

		PivotUndoState _pivotState;
		PivotUndoState PivotState
		{
			get
			{
				if ( _pivotState == null )
				{
					_pivotState = ScriptableObject.CreateInstance<PivotUndoState>();
					_pivotState.hideFlags = HideFlags.HideAndDontSave;
				}
				return _pivotState;
			}
		}

		/// <summary>Put the pivots as they are now into the current undo step, before changing them.</summary>
		void RecordPivot( string name = "Move Pivot" ) => Undo.RecordObject( PivotState, name );

		public bool HasCustomPivot => _customPivot.HasValue;

		public void ClearPivot()
		{
			if ( _customPivot.HasValue ) RecordPivot( "Clear Pivot" );
			_customPivot = null;
			HammerViews.RepaintAll();
		}

		void TransformHandleGUI()
		{
			HammerTrace.Log( "TransformHandleGUI" );

			ForgetPivotIfSelectionChanged();

			if ( _moveMode == MoveMode.Pivot )
			{
				PivotHandleGUI();
				return;
			}

			if ( !_dragging )
			{
				_handlePosition = _customPivot ?? SelectionCenter();
				_handleRotation = SelectionBasis();
				_handleScale = Vector3.one;
			}

			var snap = HammerSettings.GridSnap ^ (Event.current.control || Event.current.command);
			var extrude = Event.current.shift;

			EditorGUI.BeginChangeCheck();

			switch ( _moveMode )
			{
				case MoveMode.Position:
				{
					var position = HammerGizmos.PositionHandle( _handlePosition, _handleRotation );
					if ( EditorGUI.EndChangeCheck() )
					{
						if ( !_dragging ) BeginTransform( extrude );

						var delta = position - _pivotStart;
						var target = _pivotStart + delta;

						if ( snap )
						{
							// Only snap the axes being moved so off-grid selections don't jump on the others
							var local = Quaternion.Inverse( _handleRotation ) * delta;
							var moved = new Vector3( Mathf.Abs( local.x ) > 1e-6f ? 1 : 0, Mathf.Abs( local.y ) > 1e-6f ? 1 : 0, Mathf.Abs( local.z ) > 1e-6f ? 1 : 0 );

							if ( _handleRotation == Quaternion.identity )
							{
								target = HammerSettings.SnapWorld( target, moved.x > 0, moved.y > 0, moved.z > 0 );
							}
							else
							{
								// Snap the distance travelled along the handle's own axes
								var grid = HammerSettings.GridSize * SourceSpace.UnitScale;
								local = new Vector3( Mathf.Round( local.x / grid ) * grid, Mathf.Round( local.y / grid ) * grid, Mathf.Round( local.z / grid ) * grid );
								target = _pivotStart + _handleRotation * local;
							}
						}

						if ( TrySnapMove( _pivotStart, null, out var snapped ) )
							target = snapped;

						_handlePosition = target;
						ApplyTranslate( target - _pivotStart );
					}
					break;
				}

				case MoveMode.Rotate:
				{
					var rotation = HammerGizmos.RotationHandle( _handleRotation, _handlePosition );
					if ( EditorGUI.EndChangeCheck() )
					{
						// Shift extrudes first, as with moving (a turned extrude: arches, bends)
						if ( !_dragging ) BeginTransform( extrude );

						var delta = rotation * Quaternion.Inverse( _rotationStart );

						if ( snap )
						{
							delta.ToAngleAxis( out var angle, out var axis );
							angle = Mathf.Round( angle / HammerSettings.AngleSnap ) * HammerSettings.AngleSnap;
							delta = Quaternion.AngleAxis( angle, axis );
						}

						_handleRotation = delta * _rotationStart;
						ApplyRotate( _pivotStart, delta );
					}
					break;
				}

				case MoveMode.Scale:
				{
					var size = HammerGUI.HandleSize( _handlePosition );
					var scale = HammerGizmos.ScaleHandle( _handleScale, _handlePosition, _handleRotation, size );
					if ( EditorGUI.EndChangeCheck() )
					{
						// Shift extrudes first, as with moving (a tapered extrude)
						if ( !_dragging ) BeginTransform( extrude );

						if ( snap )
							scale = new Vector3( Mathf.Round( scale.x * 8 ) / 8, Mathf.Round( scale.y * 8 ) / 8, Mathf.Round( scale.z * 8 ) / 8 );

						_handleScale = scale;
						ApplyScale( _pivotStart, _handleRotation, scale );
					}
					break;
				}

				default:
					EditorGUI.EndChangeCheck();
					break;
			}
		}

		// ── Hold Tab and click: put the pivot on whatever is under the mouse ──

		bool _placingPivot;
		bool _pivotPlaced;
		int _pivotPlaceId;

		public bool PlacingPivot => _placingPivot || _pivotDragging;
		bool _pivotDragging;

		public void BeginPivotPlacing()
		{
			// (a held key repeats: only the first press counts)
			if ( _placingPivot ) return;
			_placingPivot = true;
			_pivotPlaced = false;
			HammerViews.RepaintAll();
		}

		/// <summary>
		/// Tab let go. True if nothing was clicked meanwhile: it was a tap, so swap the axes.
		/// </summary>
		public bool EndPivotPlacing()
		{
			var tap = _placingPivot && !_pivotPlaced;
			_placingPivot = false;
			HammerViews.RepaintAll();
			return tap;
		}

		/// <summary>
		/// Where the pivot is now, and the axes the gizmo uses.
		/// </summary>
		(Vector3 Position, Quaternion Rotation) CurrentPivot()
		{
			if ( _mode == EditMode.Object )
				return (ObjectPivot(), Tools.pivotRotation == PivotRotation.Local ? Tools.handleRotation : Workplane.Rotation);

			ForgetPivotIfSelectionChanged();
			return (_customPivot ?? SelectionCenter(), SelectionBasis());
		}

		/// <summary>
		/// A new selection gets its own pivot. Recorded with the selection change, so undoing
		/// that brings the old pivot back with the old selection.
		/// </summary>
		void ForgetPivotIfSelectionChanged()
		{
			if ( _pivotSelectionVersion == Selection.Version || _dragging )
				return;

			if ( _customPivot.HasValue )
			{
				RecordPivot( "Selection" );
				_customPivot = null;
			}
			_pivotSelectionVersion = Selection.Version;
		}

		/// <summary>
		/// The point under the mouse a click would put the pivot on: a vertex, else the nearest
		/// point of an edge, else the surface; off the meshes, the grid.
		/// </summary>
		bool PivotPointUnder( Vector2 mouse, out Vector3 point )
		{
			var meshes = MeshPicking.VisibleMeshes();
			var through = _view != null && _view.Orthographic;

			var vertex = MeshPicking.PickVertex( mouse, meshes, through );
			if ( vertex.IsValid )
			{
				point = vertex.PositionWorld;
				return true;
			}

			var edge = MeshPicking.PickEdge( mouse, meshes, through );
			if ( edge.IsValid )
			{
				edge.GetWorldPoints( out var a, out var b );
				var ray = HammerGUI.GUIToRay( mouse );
				point = ClosestOnSegment( ray, a, b );
				if ( HammerSettings.GridSnap ) point = SnapAlong( a, b, point );
				return true;
			}

			if ( MeshPicking.PickFace( mouse, meshes, out var hit ) )
			{
				point = hit.Point;
				return true;
			}

			// The grid (the workplane's, or the ground), snapped
			var r = HammerGUI.GUIToRay( mouse );
			var plane = _view != null && _view.Orthographic
				? new Plane( -_view.Camera.transform.forward, SelectionCenter() )
				: new Plane( Workplane.Up, Workplane.ToWorld( Vector3.zero ) );
			if ( plane.Raycast( r, out var t ) )
			{
				point = HammerSettings.GridSnap ? HammerSettings.SnapWorld( r.GetPoint( t ), true, true, true ) : r.GetPoint( t );
				return true;
			}

			point = default;
			return false;
		}

		static Vector3 ClosestOnSegment( Ray ray, Vector3 a, Vector3 b )
		{
			// Closest points of the ray and the segment's line, clamped to the segment
			var d = b - a;
			var w = a - ray.origin;
			var dd = Vector3.Dot( d, d );
			var de = Vector3.Dot( d, ray.direction );
			var denom = dd - de * de;
			var s = denom > 1e-8f ? (de * Vector3.Dot( ray.direction, w ) - Vector3.Dot( d, w )) / denom : 0;
			return a + d * Mathf.Clamp01( s );
		}

		/// <summary>
		/// Snap a point on an edge to the grid distance along it (and always allow the middle).
		/// </summary>
		static Vector3 SnapAlong( Vector3 a, Vector3 b, Vector3 p )
		{
			var length = Vector3.Distance( a, b );
			if ( length < 1e-6f ) return a;
			var grid = HammerSettings.GridSize * SourceSpace.UnitScale;
			var along = Vector3.Distance( a, p );
			var snapped = Mathf.Clamp( Mathf.Round( along / grid ) * grid, 0, length );
			if ( Mathf.Abs( along - length * 0.5f ) < Mathf.Abs( along - snapped ) ) snapped = length * 0.5f;
			return Vector3.Lerp( a, b, snapped / length );
		}

		void PlacePivotAt( Vector2 mouse )
		{
			if ( !PivotPointUnder( mouse, out var point ) ) return;
			if ( _mode == EditMode.Object )
			{
				SetObjectPivot( point );
			}
			else
			{
				CurrentPivot();
				RecordPivot();
				_customPivot = point;
				_pivotSelectionVersion = Selection.Version;
			}
			_pivotPlaced = true;
			HammerViews.RepaintAll();
		}

		void PivotPlaceGUI()
		{
			var e = Event.current;
			_pivotPlaceId = GUIUtility.GetControlID( FocusType.Passive );

			// A drag that ended somewhere this never saw mustn't keep pivot placing on
			if ( _pivotDragging && GUIUtility.hotControl != _pivotPlaceId && e.type != EventType.MouseDown )
				_pivotDragging = false;

			switch ( e.GetTypeForControl( _pivotPlaceId ) )
			{
				case EventType.Layout:
					HandleUtility.AddDefaultControl( _pivotPlaceId );
					break;

				case EventType.MouseMove:
					HammerViews.RepaintAll();
					break;

				// Click, or drag, to put the pivot under the mouse. The drag is followed to the end
				// even if Tab is let go first, so the gizmo never gets the rest of it.
				case EventType.MouseDown when e.button == 0 && !e.alt:
					GUIUtility.hotControl = _pivotPlaceId;
					_pivotDragging = true;
					PlacePivotAt( e.mousePosition );
					e.Use();
					break;

				case EventType.MouseDrag when _pivotDragging:
					PlacePivotAt( e.mousePosition );
					e.Use();
					break;

				case EventType.MouseUp when _pivotDragging || e.type == EventType.MouseUp:
					if ( GUIUtility.hotControl == _pivotPlaceId ) GUIUtility.hotControl = 0;
					_pivotDragging = false;
					e.Use();
					HammerViews.RepaintAll();
					break;

				case EventType.Repaint:
				{
					// Hammer swaps the gizmo for a yellow dot with thin axis lines while Tab is held
					var (pivot, rotation) = CurrentPivot();
					var size = HammerGUI.HandleSize( pivot );
					Handles.color = new Color( 0.75f, 0.75f, 0.75f, 0.9f );
					Handles.DrawLine( pivot, pivot + rotation * Vector3.up * size * 0.9f );
					Handles.DrawLine( pivot, pivot + rotation * Vector3.right * size * 0.9f );
					Handles.DrawLine( pivot, pivot + rotation * Vector3.forward * size * 0.9f );
					HammerGizmos.Diamond( pivot, size * 0.07f, new Color( 1.0f, 0.88f, 0.1f, 1.0f ) );

					// Where a click would put it
					if ( !_pivotDragging && PivotPointUnder( e.mousePosition, out var under ) && Vector3.Distance( under, pivot ) > size * 0.01f )
						HammerGizmos.Diamond( under, HammerGUI.HandleSize( under ) * 0.045f, new Color( 1.0f, 0.88f, 0.1f, 0.45f ) );
					break;
				}
			}
		}

		void PivotHandleGUI()
		{
			var position = _customPivot ?? SelectionCenter();
			EditorGUI.BeginChangeCheck();
			var moved = HammerGizmos.PositionHandle( position, Quaternion.identity, pivotDiamond: true );
			if ( EditorGUI.EndChangeCheck() )
			{
				var snap = HammerSettings.GridSnap ^ (Event.current.control || Event.current.command);
				var delta = moved - position;
				RecordPivot();
				_customPivot = snap ? HammerSettings.SnapWorld( moved, Mathf.Abs( delta.x ) > 1e-6f, Mathf.Abs( delta.y ) > 1e-6f, Mathf.Abs( delta.z ) > 1e-6f ) : moved;
				_pivotSelectionVersion = Selection.Version;
			}

			// A small cross marks the pivot
			if ( Event.current.type == EventType.Repaint )
			{
				var p = _customPivot ?? position;
				HammerGizmos.Diamond( p, HammerGUI.HandleSize( p ) * HammerGizmos.GizmoScale * 0.07f, new Color( 1.0f, 0.92f, 0.15f ) );
			}
		}

		void BeginTransform( bool extrude )
		{
			HammerTrace.Log( $"BeginTransform extrude={extrude}" );
			_dragging = true;
			_pivotStart = _handlePosition;
			_rotationStart = _handleRotation;
			_hover = null;

			// Each drag is its own undo step, whatever came just before it
			Undo.FlushUndoRecordObjects();
			Undo.IncrementCurrentGroup();
			_dragComponents = Selection.Components.ToArray();
			_dragUndoName = extrude ? "Extrude Selection" : $"{_moveMode} Selection";
			Undo.RecordObjects( _dragComponents, _dragUndoName );

			_extrudeSideFaces = extrude ? ExtrudeSelection() : null;
			TrackDragStart( extrude );

			_startPositions.Clear();
			foreach ( var v in SelectionVertices() )
				_startPositions[v] = v.PositionWorld;
		}

		void EndTransform()
		{
			HammerTrace.Log( "EndTransform" );
			_dragging = false;
			if ( _extrudeSideFaces is not null ) TidyExtrude( _extrudeSideFaces );
			_startPositions.Clear();
			_extrudeSideFaces = null;

			if ( _dragComponents is null )
				return;

			foreach ( var c in _dragComponents )
			{
				if ( c == null ) continue;
				c.Commit();
				EditorUtility.SetDirty( c );
			}

			MeshHealth.AfterEdit( _dragComponents );
			_dragComponents = null;
			RememberDrag();

			// The drag's own selection change (an extrude selects the new faces) mustn't throw
			// away a pivot the user placed: rotating round it again, or Shift+G, needs it.
			// Moving takes the pivot along.
			if ( _customPivot.HasValue && _moveMode == MoveMode.Position )
			{
				RecordPivot( "Move" );
				_customPivot = _handlePosition;
			}
			_pivotSelectionVersion = Selection.Version;
			Undo.FlushUndoRecordObjects();
			Undo.IncrementCurrentGroup();
		}

		string _dragUndoName;

		/// <summary>
		/// After an extrude drag: where the new part didn't move away from the old (an extrude
		/// turned about a hinge edge, or scaled to a point), join the points that ended up on top of
		/// each other and drop the side faces that were squashed to nothing.
		/// </summary>
		void TidyExtrude( List<MeshFace> sideFaces )
		{
			RecordDragStep();

			foreach ( var group in sideFaces.Where( x => x.IsValid ).GroupBy( x => x.Component ) )
			{
				var mesh = group.Key.Mesh;
				var faces = group.Select( x => x.Handle ).ToList();
				var vertices = faces.SelectMany( f => mesh.GetFaceVertices( f ) ).Distinct().ToList();
				if ( vertices.Count == 0 ) continue;

				mesh.MergeVerticesWithinDistance( vertices, 0.01f, false, true, out _ );

				var squashed = mesh.FaceHandles.Where( f => (mesh.CheckFace( f ) & Sandbox.PolygonMesh.FaceProblem.Degenerate) != 0 ).ToList();
				if ( squashed.Count > 0 ) mesh.RemoveFaces( squashed );
				mesh.ComputeFaceTextureCoordinatesFromParameters();
			}

			Selection.RemoveInvalid();
		}

		/// <summary>
		/// Record the dragged meshes again before every step of a drag. A drag spans many editor
		/// frames; Unity turns what changed into an undo step at the end of each one, and undo then
		/// only puts back the values that step saw change. Recording again each step (all in the
		/// same undo group) makes the undo step cover everything since the drag started, so
		/// Ctrl+Z gives back exactly the shape from before it.
		/// </summary>
		void RecordDragStep()
		{
			if ( _dragComponents == null ) return;
			var alive = _dragComponents.Where( x => x != null ).ToArray();
			if ( alive.Length > 0 ) Undo.RecordObjects( alive, _dragUndoName ?? "Edit Mesh" );
		}

		void ApplyTranslate( Vector3 delta )
		{
			RecordDragStep();
			TrackMove( delta );
			foreach ( var entry in _startPositions )
			{
				if ( !entry.Key.IsValid ) continue;
				var c = entry.Key.Component;
				c.Mesh.SetVertexPosition( entry.Key.Handle, c.WorldToSource( entry.Value + delta ) );
			}

			AfterVerticesMoved( HammerSettings.TextureLockComponents );
		}

		void ApplyRotate( Vector3 pivot, Quaternion delta )
		{
			RecordDragStep();
			TrackRotate( pivot, delta );
			foreach ( var entry in _startPositions )
			{
				if ( !entry.Key.IsValid ) continue;
				var c = entry.Key.Component;
				c.Mesh.SetVertexPosition( entry.Key.Handle, c.WorldToSource( pivot + delta * (entry.Value - pivot) ) );
			}

			AfterVerticesMoved( HammerSettings.TextureLockComponents );
		}

		void ApplyScale( Vector3 pivot, Quaternion basis, Vector3 scale )
		{
			RecordDragStep();
			TrackScale( pivot, basis, scale );
			var inverse = Quaternion.Inverse( basis );

			foreach ( var entry in _startPositions )
			{
				if ( !entry.Key.IsValid ) continue;
				var c = entry.Key.Component;
				var local = inverse * (entry.Value - pivot);
				local.Scale( scale );
				c.Mesh.SetVertexPosition( entry.Key.Handle, c.WorldToSource( pivot + basis * local ) );
			}

			AfterVerticesMoved( HammerSettings.TextureLockComponents );
		}

		/// <summary>
		/// Keep texturing right while vertices move, the same way the s&amp;box selection tool does.
		/// </summary>
		void AfterVerticesMoved( bool lockTexture )
		{
			HammerTrace.Log( "AfterVerticesMoved" );
			var sideFaces = new Dictionary<HammerMesh, HashSet<FaceHandle>>();

			if ( _extrudeSideFaces is not null )
			{
				var extruded = SelectedFaces.GroupBy( x => x.Component ).ToDictionary( g => g.Key, g => g.Select( x => x.Handle ).ToHashSet() );

				foreach ( var group in _extrudeSideFaces.Where( x => x.IsValid ).GroupBy( x => x.Component ) )
				{
					var mesh = group.Key.Mesh;
					var handles = group.Select( x => x.Handle ).ToHashSet();
					var exclude = new HashSet<FaceHandle>( handles );
					if ( extruded.TryGetValue( group.Key, out var newFaces ) )
						exclude.UnionWith( newFaces );

					foreach ( var face in group )
					{
						if ( !mesh.TextureWrapFromNeighbour( face.Handle, exclude ) )
							mesh.TextureAlignToGrid( mesh.Transform, face.Handle );
					}

					sideFaces[group.Key] = handles;
				}
			}

			var components = _startPositions.Keys.Select( x => x.Component ).Where( x => x != null ).Distinct().ToList();

			if ( !lockTexture )
			{
				foreach ( var c in components )
					c.Mesh.ComputeFaceTextureCoordinatesFromParameters();
			}
			else
			{
				foreach ( var group in _startPositions.Keys.Where( x => x.IsValid ).GroupBy( x => x.Component ) )
				{
					var mesh = group.Key.Mesh;
					var faces = new HashSet<FaceHandle>();

					foreach ( var vertex in group )
					{
						if ( mesh.GetFacesConnectedToVertex( vertex.Handle, out var connected ) )
							faces.UnionWith( connected );
					}

					if ( sideFaces.TryGetValue( group.Key, out var excluded ) )
						faces.ExceptWith( excluded );

					if ( faces.Count > 0 )
						mesh.ComputeFaceTextureParametersFromCoordinates( faces );
				}
			}

			RebuildNow( components );
		}

		/// <summary>
		/// Shift-drag: extrude faces, or extend/bevel edges, then move the result.
		/// Returns the new side faces so their texturing can follow along.
		/// </summary>
		List<MeshFace> ExtrudeSelection()
		{
			var connecting = new List<MeshFace>();

			if ( _mode == EditMode.Face )
			{
				var faces = SelectedFaces.ToArray();
				Selection.Clear();

				foreach ( var group in faces.GroupBy( x => x.Component ) )
				{
					group.Key.Mesh.ExtrudeFaces( group.Select( x => x.Handle ).ToArray(), out var newFaces, out var newConnectingFaces );

					foreach ( var f in newFaces )
						Selection.Add( new MeshFace( group.Key, f ) );

					foreach ( var f in newConnectingFaces )
						connecting.Add( new MeshFace( group.Key, f ) );
				}
			}
			else if ( _mode == EditMode.Edge )
			{
				var edges = SelectedEdges.ToArray();
				var extrudeWidth = HammerSettings.GridSize * 0.25f;
				Selection.Clear();

				foreach ( var group in edges.GroupBy( x => x.Component ) )
				{
					var mesh = group.Key.Mesh;
					var handles = group.Select( x => x.Handle ).ToList();

					if ( group.Any( x => !x.IsOpen ) )
					{
						var newFaces = new List<FaceHandle>();
						if ( !mesh.BevelEdges( handles, Sandbox.PolygonMesh.BevelEdgesMode.LeaveOriginalEdges, 1, extrudeWidth, 0.0f, null, null, newFaces ) )
							continue;

						foreach ( var he in handles ) Selection.Add( new MeshEdge( group.Key, he ) );
						foreach ( var f in newFaces ) connecting.Add( new MeshFace( group.Key, f ) );
					}
					else
					{
						if ( !mesh.ExtendEdges( handles, 0.0f, out var newEdges, out var newFaces ) )
							continue;

						foreach ( var f in newFaces ) connecting.Add( new MeshFace( group.Key, f ) );
						foreach ( var he in newEdges ) Selection.Add( new MeshEdge( group.Key, he ) );
					}
				}
			}

			else if ( _mode == EditMode.Vertex )
			{
				var vertices = SelectedVertices.ToArray();
				Selection.Clear();

				foreach ( var group in vertices.GroupBy( x => x.Component ) )
				{
					group.Key.Mesh.ExtendOrExtrudeVertices( group.Select( x => x.Handle ).ToList(), 0.0f, HammerSettings.GridSize,
						out var modified, out _, out var newFaces );

					foreach ( var v in modified ) Selection.Add( new MeshVertex( group.Key, v ) );
					foreach ( var f in newFaces ) connecting.Add( new MeshFace( group.Key, f ) );
				}
			}

			return connecting;
		}
	}
}
