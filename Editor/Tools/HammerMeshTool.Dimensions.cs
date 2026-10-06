using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HammerUnity.EditorTools
{
	partial class HammerMeshTool
	{
		// Hammer's axis colours. Its X/Y/Z are Unity's Z/-X/Y.
		static readonly Color AxisX = new( 0.95f, 0.35f, 0.3f );
		static readonly Color AxisY = new( 0.45f, 0.9f, 0.35f );
		static readonly Color AxisZ = new( 0.45f, 0.6f, 1.0f );

		/// <summary>
		/// World bounds of the current selection, if there is one.
		/// </summary>
		bool SelectionBounds( out Bounds bounds )
		{
			bounds = default;
			var points = new List<Vector3>();

			if ( _mode == EditMode.Object )
			{
				foreach ( var r in UnityEditor.Selection.gameObjects.SelectMany( x => x.GetComponentsInChildren<MeshRenderer>() ) )
				{
					points.Add( r.bounds.min );
					points.Add( r.bounds.max );
				}
			}
			else
			{
				return SelectionVertexBounds( out bounds );
			}

			if ( points.Count == 0 )
				return false;

			bounds = new Bounds( points[0], Vector3.zero );
			foreach ( var p in points ) bounds.Encapsulate( p );
			return true;
		}

		/// <summary>
		/// Hammer's size readout: the selection's box with each axis' length in its colour.
		/// </summary>
		void DrawDimensions()
		{
			if ( Event.current.type != EventType.Repaint || !SelectionBounds( out var b ) )
				return;

			var camera = HammerGUI.Camera;
			if ( camera == null ) return;
			var forward = camera.transform.forward;

			var zTest = Handles.zTest;
			Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

			var min = b.min;
			var max = b.max;

			var corners = new Vector3[8];
			for ( int i = 0; i < 8; i++ )
				corners[i] = new Vector3( (i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z );

			// Hammer's orange box round selected objects in 3D. Not round elements, where it would
			// lie on unselected edges and make them look selected, and not in the 2D views, where
			// the selection shows as white wireframe that the box would cover
			if ( !camera.orthographic && _mode == EditMode.Object )
			{
				Handles.color = new Color( 1.0f, 0.62f, 0.1f, 0.95f );
				int[] lines = { 0, 1, 2, 3, 4, 5, 6, 7, 0, 2, 1, 3, 4, 6, 5, 7, 0, 4, 1, 5, 2, 6, 3, 7 };
				Handles.DrawLines( corners, lines );
			}

			// Measure from the corner nearest the camera, like Hammer (the 2D views: the corner at
			// their bottom-left, so the labels sit on the box's outline)
			var origin = camera.orthographic
				? corners.OrderBy( c => Vector3.Dot( c - b.center, camera.transform.up ) + Vector3.Dot( c - b.center, camera.transform.right ) + Vector3.Dot( c - b.center, forward ) * 0.5f ).First()
				: corners.OrderBy( c => (c - camera.transform.position).sqrMagnitude ).First();

			// Measure lines lie along the box's edges nearest the camera, solid in the axis colours
			// like Hammer; round elements in 3D, dotted so they aren't mistaken for selected edges
			var dotted = _mode != EditMode.Object;

			// In the 2D views the resize balls sit on the middle of each side, right where the
			// numbers go: Hammer puts the number just to the right of the ball
			var beside = camera.orthographic && _mode == EditMode.Object && _moveMode == MoveMode.Select ? 22f : 0f;
			Axis( origin, new Vector3( 0, 0, origin.z == min.z ? b.size.z : -b.size.z ), AxisX, forward, dotted, beside );
			Axis( origin, new Vector3( origin.x == min.x ? b.size.x : -b.size.x, 0, 0 ), AxisY, forward, dotted, beside );
			Axis( origin, new Vector3( 0, origin.y == min.y ? b.size.y : -b.size.y, 0 ), AxisZ, forward, dotted, beside );

			Handles.zTest = zTest;
		}

		/// <param name="dotted">Dotted (3D views), so the measure lines can't be mistaken for edges.</param>
		static void Axis( Vector3 origin, Vector3 span, Color color, Vector3 forward, bool dotted, float beside = 0 )
		{
			var length = span.magnitude;
			if ( length < 1e-5f ) return;

			// Seen end-on in a 2D view: nothing to show
			if ( Mathf.Abs( Vector3.Dot( span / length, forward ) ) > 0.99f ) return;

			Handles.color = color;
			if ( dotted ) Handles.DrawDottedLine( origin, origin + span, 4 );
			else Handles.DrawLine( origin, origin + span, 2.0f * EditorGUIUtility.pixelsPerPoint );

			var units = length / SourceSpace.UnitScale;
			HammerGUI.OutlinedLabel( origin + span * 0.5f, units.ToString( units < 10 ? "0.##" : "0.#" ), Color.Lerp( color, Color.white, 0.35f ), beside );
		}
	}
}
