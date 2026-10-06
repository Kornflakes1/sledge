using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HammerUnity.EditorTools
{
	partial class HammerMeshTool
	{
		const float VertexSnapPixels = 12;

		// Hammer shows the vertex it's about to snap to: a red ring as the mouse nears it, then
		// green with a yellow dot once it has snapped
		const float VertexPreviewPixels = 36;
		Vector3? _snapCandidate;
		bool _snappedToVertex;
		static readonly Color SnapNearColor = new( 0.95f, 0.18f, 0.15f );
		static readonly Color SnapOnColor = new( 0.2f, 0.9f, 0.25f );
		static readonly Color SnapDotColor = new( 1.0f, 0.88f, 0.15f );

		/// <summary>The vertex snap marker, while a move is being dragged.</summary>
		void DrawSnapMarker()
		{
			if ( !(_dragging || _objectDragging) ) { _snapCandidate = null; return; }
			if ( !_snapCandidate.HasValue ) return;

			var p = _snapCandidate.Value;
			var camera = HammerGUI.Camera;
			if ( camera == null ) return;
			var normal = camera.orthographic ? camera.transform.forward : (p - camera.transform.position).normalized;
			var radius = HammerGUI.HandleSize( p ) * 0.022f;

			var z = UnityEditor.Handles.zTest;
			UnityEditor.Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
			if ( _snappedToVertex )
			{
				UnityEditor.Handles.color = SnapDotColor;
				UnityEditor.Handles.DrawSolidDisc( p, normal, radius * 0.6f );
			}
			UnityEditor.Handles.color = _snappedToVertex ? SnapOnColor : SnapNearColor;
			UnityEditor.Handles.DrawWireDisc( p, normal, radius, 3.0f );
			UnityEditor.Handles.zTest = z;
		}

		/// <summary>
		/// Hammer's snap modes while moving: a vertex of other geometry near the mouse wins, then
		/// (objects only) the surface under the mouse. Returns false to fall back to the grid.
		/// </summary>
		/// <param name="pivotStart">Where the handle was when the drag started.</param>
		/// <param name="objects">The objects being moved (object mode), or null for elements.</param>
		bool TrySnapMove( Vector3 pivotStart, ICollection<HammerMesh> objects, out Vector3 target )
		{
			target = default;
			var mouse = Event.current.mousePosition;

			// Hammer: dragging the gizmo's purple centre sticks the selection to surfaces
			if ( HammerGizmos.CentreDragging && !(HammerGUI.Camera?.orthographic ?? true) )
			{
				if ( objects != null && objects.Count > 0 ) { if ( OnSurface( mouse, pivotStart, objects, out target ) ) return true; }
				else if ( ElementOnSurface( mouse, out target ) ) return true;
			}

			_snapCandidate = null;
			_snappedToVertex = false;
			if ( !HammerSettings.SnapEnabled ) return false;

			if ( HammerSettings.VertexSnap && NearestVertex( mouse, objects, VertexPreviewPixels, out var vertex, out var pixels ) )
			{
				_snapCandidate = vertex;
				if ( pixels < VertexSnapPixels )
				{
					_snappedToVertex = true;
					target = vertex;
					return true;
				}
			}

			if ( HammerSettings.SurfaceSnap && objects != null && objects.Count > 0 && OnSurface( mouse, pivotStart, objects, out target ) )
				return true;

			return false;
		}

		/// <summary>
		/// Closest vertex on screen to the mouse, not counting what's being moved.
		/// </summary>
		bool NearestVertex( Vector2 mouse, ICollection<HammerMesh> movingObjects, float within, out Vector3 world, out float pixels )
		{
			world = default;
			var moving = movingObjects == null ? SelectionVertices() : null;
			var best = within;
			var found = false;

			foreach ( var component in MeshPicking.VisibleMeshes() )
			{
				if ( movingObjects != null && movingObjects.Contains( component ) ) continue;

				var mesh = component.Mesh;
				foreach ( var v in mesh.VertexHandles )
				{
					if ( moving != null && moving.Contains( new MeshVertex( component, v ) ) ) continue;

					var p = component.SourceToWorld( mesh.GetVertexPosition( v ) );
					var g = HammerGUI.WorldToGUI( p );
					if ( g.z < 0 ) continue;

					var d = Vector2.Distance( mouse, g );
					if ( d < best )
					{
						best = d;
						world = p;
						found = true;
					}
				}
			}

			pixels = best;
			return found;
		}

		/// <summary>
		/// Stand the moving objects on the surface under the mouse: the side of their bounds facing
		/// the surface touches it.
		/// </summary>
		bool OnSurface( Vector2 mouse, Vector3 pivotStart, ICollection<HammerMesh> objects, out Vector3 target )
		{
			target = default;
			var others = MeshPicking.VisibleMeshes().Where( m => !objects.Contains( m ) ).ToList();
			if ( !MeshPicking.RaycastFace( HammerGUI.GUIToRay( mouse ), others, out var hit ) )
				return false;

			var bounds = _objectBoundsStart;
			var n = hit.Normal.normalized;
			var extent = Mathf.Abs( bounds.extents.x * n.x ) + Mathf.Abs( bounds.extents.y * n.y ) + Mathf.Abs( bounds.extents.z * n.z );

			// Bounds centre on the surface, lifted by half its size along the normal; the handle
			// keeps its place relative to the bounds
			target = hit.Point + n * extent + (pivotStart - bounds.center);
			return true;
		}

		Bounds _objectBoundsStart;

		/// <summary>
		/// The point on another mesh under the mouse, for moving elements by the gizmo centre.
		/// </summary>
		bool ElementOnSurface( Vector2 mouse, out Vector3 target )
		{
			target = default;
			var editing = Selection.Components.ToHashSet();
			var others = MeshPicking.VisibleMeshes().Where( m => !editing.Contains( m ) ).ToList();
			if ( !MeshPicking.RaycastFace( HammerGUI.GUIToRay( mouse ), others, out var hit ) ) return false;
			target = hit.Point;
			return true;
		}
	}
}
