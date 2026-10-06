using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HammerUnity.EditorTools
{
	partial class HammerMeshTool
	{
		const float VertexSnapPixels = 12;

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

			if ( !HammerSettings.SnapEnabled ) return false;

			if ( HammerSettings.VertexSnap && NearestVertex( mouse, objects, out target ) )
				return true;

			if ( HammerSettings.SurfaceSnap && objects != null && objects.Count > 0 && OnSurface( mouse, pivotStart, objects, out target ) )
				return true;

			return false;
		}

		/// <summary>
		/// Closest vertex on screen to the mouse, not counting what's being moved.
		/// </summary>
		bool NearestVertex( Vector2 mouse, ICollection<HammerMesh> movingObjects, out Vector3 world )
		{
			world = default;
			var moving = movingObjects == null ? SelectionVertices() : null;
			var best = VertexSnapPixels;
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
