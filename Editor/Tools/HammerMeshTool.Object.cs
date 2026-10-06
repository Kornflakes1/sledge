using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HammerUnity.EditorTools
{
	partial class HammerMeshTool
	{
		Vector3 _objectPivotStart;
		Quaternion _objectRotationStart;
		Vector3 _objectHandleScale = Vector3.one;
		bool _objectDragging;
		(Transform transform, Vector3 position, Quaternion rotation, Vector3 scale)[] _objectStart;

		/// <summary>
		/// Object mode: Unity handles picking; we draw a grid-snapped transform handle for the
		/// selected objects and outline their meshes.
		/// </summary>
		void ObjectModeGUI( HammerView view )
		{
			// In the Hammer window there's no Unity picking, so do click selection ourselves
			if ( view.Window is not SceneView )
				ObjectPickGUI();

			var transforms = UnityEditor.Selection.transforms;

			if ( Event.current.type == EventType.Repaint )
			{
				if ( !DrawsInCamera( view ) )
					DrawSelectedObjects();

				DrawDimensions();
			}

			if ( transforms.Length == 0 )
				return;

			if ( _objectDragging && GUIUtility.hotControl == 0 )
				_objectDragging = false;

			var snap = HammerSettings.GridSnap ^ (Event.current.control || Event.current.command);
			// Where rotate and scale turn round: the origin, or a pivot placed with the Pivot
			// tool (Insert), the Pivot buttons or Tab
			var pivot = ObjectPivot();
			var rotation = Tools.pivotRotation == PivotRotation.Local ? Tools.handleRotation : Workplane.Rotation;

			if ( !_objectDragging )
			{
				_objectPivotStart = pivot;
				_objectRotationStart = rotation;
				_objectHandleScale = Vector3.one;
			}

			EditorGUI.BeginChangeCheck();

			switch ( _moveMode )
			{
				case MoveMode.Position:
				{
					var p = HammerGizmos.PositionHandle( _objectDragging ? _objectPivotStart + (_objectStart != null ? CurrentObjectOffset() : Vector3.zero) : pivot, rotation );
					if ( EditorGUI.EndChangeCheck() )
					{
						BeginObjectDrag( "Move" );
						var target = p;

						// Only snap the axes being dragged, so an off-grid object doesn't jump sideways
						var moved = p - _objectPivotStart;
						if ( snap ) target = HammerSettings.SnapWorld( target, Mathf.Abs( moved.x ) > 1e-6f, Mathf.Abs( moved.y ) > 1e-6f, Mathf.Abs( moved.z ) > 1e-6f );

						// Vertex / surface snapping beat the grid
						var movingMeshes = _objectStart.SelectMany( x => x.transform.GetComponentsInChildren<HammerMesh>() ).ToList();
						if ( TrySnapMove( _objectPivotStart, movingMeshes, out var snapped ) )
							target = snapped;

						var delta = target - _objectPivotStart;
						foreach ( var s in _objectStart )
							s.transform.position = s.position + delta;

						// A placed pivot travels with the objects
						if ( _objectPivot.HasValue ) _objectPivot = _objectPivotStart + delta;
					}
					break;
				}

				case MoveMode.Rotate:
				{
					var r = HammerGizmos.RotationHandle( _objectDragging ? _objectRotationStart : rotation, pivot );
					if ( EditorGUI.EndChangeCheck() )
					{
						BeginObjectDrag( "Rotate" );
						var delta = r * Quaternion.Inverse( _objectRotationStart );
						if ( snap )
						{
							delta.ToAngleAxis( out var angle, out var axis );
							delta = Quaternion.AngleAxis( Mathf.Round( angle / HammerSettings.AngleSnap ) * HammerSettings.AngleSnap, axis );
						}

						foreach ( var s in _objectStart )
						{
							s.transform.position = _objectPivotStart + delta * (s.position - _objectPivotStart);
							s.transform.rotation = delta * s.rotation;
						}

						_objectRotationStart = delta * _objectRotationStart;
						foreach ( var i in Enumerable.Range( 0, _objectStart.Length ) )
						{
							var s = _objectStart[i];
							_objectStart[i] = (s.transform, s.transform.position, s.transform.rotation, s.scale);
						}
					}
					break;
				}

				case MoveMode.Scale:
				{
					var scale = HammerGizmos.ScaleHandle( _objectHandleScale, pivot, rotation, HammerGUI.HandleSize( pivot ) );
					if ( EditorGUI.EndChangeCheck() )
					{
						BeginObjectDrag( "Scale" );
						_objectHandleScale = scale;
						foreach ( var s in _objectStart )
							s.transform.localScale = Vector3.Scale( s.scale, scale );
					}
					break;
				}

				case MoveMode.Pivot:
				{
					// Moves the pivot only; Set Origin To Pivot (Ctrl+D) makes it the origin
					var p = HammerGizmos.PositionHandle( pivot, Quaternion.identity, pivotDiamond: true );
					if ( EditorGUI.EndChangeCheck() )
					{
						var moved = p - pivot;
						SetObjectPivot( snap ? HammerSettings.SnapWorld( p, Mathf.Abs( moved.x ) > 1e-6f, Mathf.Abs( moved.y ) > 1e-6f, Mathf.Abs( moved.z ) > 1e-6f ) : p );
					}
					break;
				}

				case MoveMode.Select:
					EditorGUI.EndChangeCheck();
					BoxResizeGUI( transforms, snap );
					break;

				default:
					EditorGUI.EndChangeCheck();
					break;
			}
		}

		// ── Select mode: Hammer's box handles, drag a side of the selection to resize it ──

		int _resizeAxis = -1;
		int _resizeSign;
		Bounds _resizeStartBounds;
		readonly Dictionary<HammerMesh, List<(HalfEdgeMesh.VertexHandle vertex, Vector3 world)>> _resizeStart = new();

		static Bounds MeshBounds( IEnumerable<HammerMesh> meshes )
		{
			var first = true;
			var b = default( Bounds );
			foreach ( var c in meshes )
			{
				foreach ( var v in c.Mesh.VertexHandles )
				{
					var p = c.SourceToWorld( c.Mesh.GetVertexPosition( v ) );
					if ( first ) { b = new Bounds( p, Vector3.zero ); first = false; }
					else b.Encapsulate( p );
				}
			}
			return b;
		}

		void BoxResizeGUI( Transform[] transforms, bool snap )
		{
			var meshes = transforms.SelectMany( t => t.GetComponentsInChildren<HammerMesh>() ).Distinct().ToList();
			if ( meshes.Count == 0 ) return;

			var bounds = MeshBounds( meshes );
			// Hammer's axis colours by Unity axis: right is Hammer Y (green), up is Z (blue),
			// forward is X (red)
			Color[] colors = { AxisY, AxisZ, AxisX };
			var repaint = Event.current.type == EventType.Repaint;
			var camera = HammerGUI.Camera;

			for ( int axis = 0; axis < 3; axis++ )
			{
				// The axis pointing into a 2D view would put both its balls on the middle: Hammer
				// leaves it out
				var axisDir = Vector3.zero;
				axisDir[axis] = 1;
				if ( camera != null && camera.orthographic && Mathf.Abs( Vector3.Dot( axisDir, camera.transform.forward ) ) > 0.99f ) continue;

				for ( int sign = -1; sign <= 1; sign += 2 )
				{
					var dir = Vector3.zero;
					dir[axis] = sign;
					var position = bounds.center + dir * bounds.extents[axis];

					var size = HammerGUI.HandleSize( position );

					// Hammer's look: a dotted line out from the middle, a small cone just inside
					// the side pointing out, and a ball on the side
					if ( repaint )
					{
						var zTest = Handles.zTest;
						Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
						Handles.color = new Color( colors[axis].r, colors[axis].g, colors[axis].b, 0.85f );
						Handles.DrawDottedLine( bounds.center, position, 4 );
						Handles.color = new Color( colors[axis].r * 0.6f, colors[axis].g * 0.6f, colors[axis].b * 0.6f, 1 );
						Handles.ConeHandleCap( 0, position - dir * size * 0.17f, Quaternion.LookRotation( dir ), size * 0.13f, EventType.Repaint );
						Handles.zTest = zTest;
					}

					EditorGUI.BeginChangeCheck();
					Handles.color = colors[axis];
					var moved = HammerGizmos.Slider( position, dir, size * 0.16f, BallCap );
					if ( !EditorGUI.EndChangeCheck() ) continue;

					if ( _resizeAxis < 0 )
					{
						Undo.FlushUndoRecordObjects();
						Undo.IncrementCurrentGroup();
						_resizeAxis = axis;
						_resizeSign = sign;
						_resizeStartBounds = bounds;
						_resizeStart.Clear();
						foreach ( var c in meshes )
							_resizeStart[c] = c.Mesh.VertexHandles.Select( v => (v, c.SourceToWorld( c.Mesh.GetVertexPosition( v ) )) ).ToList();
					}

					if ( axis != _resizeAxis || sign != _resizeSign ) continue;
					ApplyResize( meshes, moved[axis], snap );
				}
			}

			if ( _resizeAxis >= 0 && GUIUtility.hotControl == 0 )
			{
				foreach ( var c in meshes ) { c.Commit(); EditorUtility.SetDirty( c ); }
				MeshHealth.AfterEdit( meshes );
				_resizeAxis = -1;
				_resizeStart.Clear();
				Undo.FlushUndoRecordObjects();
				Undo.IncrementCurrentGroup();
			}
		}

		/// <summary>
		/// A shaded ball (Hammer's resize handles), drawn over everything.
		/// </summary>
		static void BallCap( int id, Vector3 position, Quaternion rotation, float size, EventType eventType )
		{
			if ( eventType != EventType.Repaint ) return;
			var zTest = Handles.zTest;
			Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
			Handles.SphereHandleCap( id, position, rotation, size * 0.9f, EventType.Repaint );
			Handles.zTest = zTest;
		}

		void ApplyResize( List<HammerMesh> meshes, float side, bool snap )
		{
			var axis = _resizeAxis;
			var sign = _resizeSign;
			var start = _resizeStartBounds;
			var grid = HammerSettings.GridSize * SourceSpace.UnitScale;

			if ( snap ) side = Mathf.Round( side / grid ) * grid;

			// The opposite side stays put; everything stretches from it
			var anchor = start.center[axis] - sign * start.extents[axis];
			var startSize = start.size[axis];
			if ( startSize < 1e-5f ) return;
			var newSize = Mathf.Max( (side - anchor) * sign, grid );
			var factor = newSize / startSize;

			// Recorded every step so undo covers the whole drag (see RecordDragStep)
			Undo.RecordObjects( meshes.ToArray(), "Resize" );

			foreach ( var c in meshes )
			{
				if ( !_resizeStart.TryGetValue( c, out var verts ) ) continue;
				foreach ( var (vertex, world) in verts )
				{
					var p = world;
					p[axis] = anchor + (world[axis] - anchor) * factor;
					c.Mesh.SetVertexPosition( vertex, c.WorldToSource( p ) );
				}
				c.Mesh.ComputeFaceTextureCoordinatesFromParameters();
			}

			RebuildNow( meshes );
		}

		/// <summary>
		/// Put the object's origin at a world point without moving its geometry.
		/// </summary>
		static void MoveOrigin( HammerMesh c, Vector3 world )
		{
			var mesh = c.Mesh;
			var local = c.WorldToSource( world );
			mesh.ApplyTransform( new Sandbox.Transform( -local ) );
			c.transform.position = world;
			mesh.SetTransform( c.WorldTransform );
			RebuildNow( new[] { c } );
		}

		int _objectPickId;
		bool _objectMouseDown;
		bool _objectRegion;
		bool _objectMiddle;
		Vector2 _objectDownPosition;
		readonly List<Vector2> _objectLasso = new();

		bool ObjectLassoing => _objectMiddle || HammerSettings.LassoSelect;

		/// <summary>
		/// Meshes mode clicks: a click picks the object under the mouse; a drag from empty space
		/// selects every object inside the box (or the lasso: middle drag, or Lasso on).
		/// </summary>
		void ObjectPickGUI()
		{
			var e = Event.current;
			_objectPickId = GUIUtility.GetControlID( FocusType.Passive );

			if ( e.type == EventType.Layout )
				HandleUtility.AddDefaultControl( _objectPickId );

			switch ( e.GetTypeForControl( _objectPickId ) )
			{
				case EventType.MouseDown:
				{
					var middle = e.button == 2 && !e.alt;
					if ( (e.button != 0 && !middle) || (e.alt && !middle) || GUIUtility.hotControl != 0 || (!middle && HandleUtility.nearestControl != _objectPickId) )
						break;

					GUIUtility.hotControl = _objectPickId;
					_objectMouseDown = true;
					_objectRegion = false;
					_objectMiddle = middle;
					_objectDownPosition = e.mousePosition;
					_objectLasso.Clear();
					_objectLasso.Add( e.mousePosition );
					e.Use();
					break;
				}

				case EventType.MouseDrag when _objectMouseDown && GUIUtility.hotControl == _objectPickId:
					if ( !_objectRegion && Vector2.Distance( _objectDownPosition, e.mousePosition ) > 4 )
						_objectRegion = true;
					if ( ObjectLassoing && Vector2.Distance( _objectLasso[^1], e.mousePosition ) > 3 )
						_objectLasso.Add( e.mousePosition );
					e.Use();
					HammerViews.RepaintAll();
					break;

				case EventType.MouseUp when _objectMouseDown && GUIUtility.hotControl == _objectPickId:
					GUIUtility.hotControl = 0;
					_objectMouseDown = false;
					if ( _objectRegion )
					{
						_objectLasso.Add( e.mousePosition );
						var lasso = ObjectLassoing && _objectLasso.Count > 2 ? _objectLasso.ToArray() : null;
						var rect = lasso != null ? RectFromPoints( lasso.Aggregate( Vector2.Min ), lasso.Aggregate( Vector2.Max ) ) : RectFromPoints( _objectDownPosition, e.mousePosition );
						ObjectRegionSelect( rect, lasso, e.shift, e.control || e.command );
					}
					else if ( !_objectMiddle )
					{
						ObjectClick( e );
					}
					_objectRegion = false;
					_objectMiddle = false;
					e.Use();
					HammerViews.RepaintAll();
					break;
			}

			if ( _objectRegion && e.type == EventType.Repaint )
				DrawSelectionRegion( _objectDownPosition, e.mousePosition, ObjectLassoing ? _objectLasso : null );
		}

		void ObjectClick( Event e )
		{
			GameObject picked = null;
			if ( MeshPicking.PickFace( e.mousePosition, MeshPicking.VisibleMeshes(), out var hit ) )
				picked = hit.Face.Component.gameObject;
			else
				picked = HandleUtility.PickGameObject( e.mousePosition, false );

			if ( picked == null )
			{
				if ( !e.shift && !(e.control || e.command) )
					UnityEditor.Selection.objects = new Object[0];
			}
			else if ( e.shift || e.control || e.command )
			{
				var list = UnityEditor.Selection.objects.ToList();
				if ( list.Contains( picked ) ) list.Remove( picked );
				else list.Add( picked );
				UnityEditor.Selection.objects = list.ToArray();
			}
			else
			{
				UnityEditor.Selection.activeGameObject = picked;
			}
		}

		/// <summary>
		/// Every visible mesh wholly inside the box (or lasso): all corners of its bounds inside.
		/// Shift adds, Ctrl takes away.
		/// </summary>
		void ObjectRegionSelect( Rect rect, Vector2[] lasso, bool add, bool remove )
		{
			var found = new List<GameObject>();
			foreach ( var c in MeshPicking.VisibleMeshes() )
			{
				var b = c.GetComponent<MeshRenderer>() is { } r ? r.bounds : new Bounds( c.transform.position, Vector3.zero );
				var inside = true;
				for ( int i = 0; i < 8 && inside; i++ )
				{
					var corner = new Vector3( (i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z );
					var g = HammerGUI.WorldToGUI( corner );
					var p = new Vector2( g.x, g.y );
					inside = g.z > 0 && rect.Contains( p ) && (lasso == null || InsidePolygon( p, lasso ));
				}
				if ( inside ) found.Add( c.gameObject );
			}

			var list = add || remove ? UnityEditor.Selection.objects.ToList() : new List<Object>();
			foreach ( var go in found )
			{
				if ( remove ) list.Remove( go );
				else if ( !list.Contains( go ) ) list.Add( go );
			}
			UnityEditor.Selection.objects = list.ToArray();
		}

		Vector3 CurrentObjectOffset()
		{
			var s = _objectStart[0];
			return s.transform.position - s.position;
		}

		/// <param name="copy">Move a copy (Shift+drag); by default, whether Shift is held.</param>
		void BeginObjectDrag( string name, bool? copy = null )
		{
			if ( _objectDragging )
			{
				// Every step of a drag is recorded again (see RecordDragStep), so undo covers all of it
				Undo.RecordObjects( _objectStart.Select( x => (Object)x.transform ).Where( x => x != null ).ToArray(), $"{name} Objects" );
				return;
			}

			_objectDragging = true;
			Undo.FlushUndoRecordObjects();
			Undo.IncrementCurrentGroup();

			// Hammer: Shift+drag in Meshes mode moves a copy, leaving the original behind
			if ( copy ?? (Event.current != null && Event.current.shift) )
				DuplicateSelectedObjects();

			var transforms = UnityEditor.Selection.transforms;
			Undo.RecordObjects( transforms, $"{name} Objects" );
			_objectStart = transforms.Select( t => (t, t.position, t.rotation, t.localScale) ).ToArray();

			var renderers = transforms.SelectMany( t => t.GetComponentsInChildren<Renderer>() ).ToList();
			_objectBoundsStart = renderers.Count > 0 ? renderers[0].bounds : new Bounds( _objectPivotStart, Vector3.zero );
			foreach ( var r in renderers ) _objectBoundsStart.Encapsulate( r.bounds );
		}

		static void DuplicateSelectedObjects()
		{
			var copies = new List<Object>();
			foreach ( var t in UnityEditor.Selection.transforms )
			{
				var go = Object.Instantiate( t.gameObject, t.parent );
				go.name = GameObjectUtility.GetUniqueNameForSibling( t.parent, t.name );
				go.transform.SetPositionAndRotation( t.position, t.rotation );
				go.transform.localScale = t.localScale;
				Undo.RegisterCreatedObjectUndo( go, "Duplicate" );
				copies.Add( go );
			}

			if ( copies.Count > 0 )
				UnityEditor.Selection.objects = copies.ToArray();
		}

		static void DrawSelectedObjects()
		{
			foreach ( var t in UnityEditor.Selection.transforms )
			{
				foreach ( var component in t.GetComponentsInChildren<HammerMesh>() )
				{
					DrawObjectWire( component );
					MeshHealth.Draw( component, Lift );
				}
			}
		}

		/// <summary>
		/// A selected mesh: an orange tint in the 3D views (no edges, like Hammer), its white
		/// wireframe in the 2D views.
		/// </summary>
		static void DrawObjectWire( HammerMesh component )
		{
			var camera = HammerGUI.Camera;
			if ( camera == null ) return;

			if ( !camera.orthographic )
			{
				OverlayFill.Draw( component, 0, 0, _ => true, new Color( 1.0f, 0.55f, 0.08f, 0.22f ) );
				return;
			}

			var zTest = Handles.zTest;
			Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
			Handles.color = new Color( 1, 1, 1, 0.95f );
			Handles.DrawLines( MeshEdgeCache.WorldEdges( component ) );
			Handles.zTest = zTest;
		}
	}
}
