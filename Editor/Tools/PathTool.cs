using System.Collections.Generic;
using System.Linq;
using HalfEdgeMesh;
using UnityEditor;
using UnityEngine;
using S = Sandbox;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Hammer's Path tool, for geometry: click points to lay a path (on surfaces or the grid), and
	/// it's built as a smooth ribbon (roads, trims), rail (a box section) or pipe. Backspace drops
	/// the last point, Enter builds it, Esc cancels.
	/// </summary>
	public sealed class PathTool : SubTool
	{
		public enum Profile { Ribbon, Rail, Pipe }

		public static Profile Shape = Profile.Ribbon;
		public static float Width = 64;
		public static float Height = 16;
		public static float Radius = 8;
		public static int Sides = 8;
		public static int Smoothness = 6;

		/// <summary>How far above the clicked surface the path runs (a ribbon flush with the ground would flicker).</summary>
		public static float Lift = 1;

		readonly List<Vector3> _points = new();

		public override string Title => "Path";
		public override string Help => "Click to add points along the path. Backspace removes the last, Enter builds it, Esc cancels.";

		public static void Open( HammerMeshTool tool ) => tool.BeginSubTool( new PathTool() );

		/// <summary>
		/// For tests and code: the path's points (world space).
		/// </summary>
		public List<Vector3> Points => _points;

		public void RemoveLastPoint()
		{
			if ( _points.Count > 0 ) _points.RemoveAt( _points.Count - 1 );
			HammerViews.RepaintAll();
		}

		public override void OnOverlayGUI()
		{
			Shape = (Profile)GUILayout.Toolbar( (int)Shape, new[] { "Ribbon", "Rail", "Pipe" }, EditorStyles.miniButton );
			if ( Shape == Profile.Pipe )
			{
				Radius = EditorGUILayout.FloatField( "Radius", Radius );
				Sides = Mathf.Clamp( EditorGUILayout.IntField( "Sides", Sides ), 3, 64 );
			}
			else
			{
				Width = EditorGUILayout.FloatField( "Width", Width );
				if ( Shape == Profile.Rail ) Height = EditorGUILayout.FloatField( "Height", Height );
			}
			Smoothness = Mathf.Clamp( EditorGUILayout.IntField( new GUIContent( "Smoothness", "Steps between each pair of points (1: straight segments)" ), Smoothness ), 1, 32 );
			Lift = EditorGUILayout.FloatField( new GUIContent( "Lift", "Height above the clicked surface" ), Lift );
			GUILayout.Label( $"{_points.Count} point{(_points.Count == 1 ? "" : "s")}", EditorStyles.miniLabel );
			if ( _points.Count > 0 && GUILayout.Button( "Remove Last Point (Backspace)" ) ) RemoveLastPoint();
			HammerViews.RepaintAll();
		}

		public override void OnViewGUI( HammerView view )
		{
			var e = Event.current;
			var id = GUIUtility.GetControlID( FocusType.Passive );
			if ( e.type == EventType.Layout ) HandleUtility.AddDefaultControl( id );

			if ( e.GetTypeForControl( id ) == EventType.MouseDown && e.button == 0 && !e.alt && HandleUtility.nearestControl == id )
			{
				if ( Tool.TryGetPlacementPoint( e.mousePosition, true, out var point, out var normal ) )
				{
					point = HammerSettings.GridSnap ? HammerSettings.SnapWorld( point ) : point;
					_points.Add( point + normal.normalized * Lift * SourceSpace.UnitScale );
				}
				e.Use();
			}

			if ( e.type != EventType.Repaint ) return;

			Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
			var curve = Curve();
			Handles.color = new Color( 1.0f, 0.92f, 0.15f );
			if ( curve.Count > 1 ) Handles.DrawAAPolyLine( 3.0f, curve.ToArray() );

			// The profile at each step
			Handles.color = new Color( 1.0f, 0.92f, 0.15f, 0.45f );
			foreach ( var ring in Rings( curve ) )
				Handles.DrawPolyLine( Shape == Profile.Ribbon ? ring : ring.Append( ring[0] ).ToArray() );

			Handles.color = Color.white;
			foreach ( var p in _points )
				Handles.DotHandleCap( 0, p, Quaternion.identity, HammerGUI.HandleSize( p ) * 0.04f, EventType.Repaint );
		}

		/// <summary>
		/// The smoothed path: a Catmull-Rom curve through the points.
		/// </summary>
		List<Vector3> Curve()
		{
			var result = new List<Vector3>();
			var n = _points.Count;
			if ( n == 0 ) return result;
			if ( n == 1 ) { result.Add( _points[0] ); return result; }

			for ( int i = 0; i < n - 1; i++ )
			{
				var p0 = _points[Mathf.Max( i - 1, 0 )];
				var p1 = _points[i];
				var p2 = _points[i + 1];
				var p3 = _points[Mathf.Min( i + 2, n - 1 )];

				for ( int s = 0; s < Smoothness; s++ )
				{
					var t = s / (float)Smoothness;
					var t2 = t * t;
					var t3 = t2 * t;
					result.Add( 0.5f * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3) );
				}
			}

			result.Add( _points[n - 1] );
			return result;
		}

		/// <summary>
		/// The profile placed at each step of the curve, facing along it.
		/// </summary>
		List<Vector3[]> Rings( List<Vector3> curve )
		{
			var rings = new List<Vector3[]>();
			if ( curve.Count < 2 ) return rings;

			var unit = SourceSpace.UnitScale;
			Vector2[] profile = Shape switch
			{
				Profile.Ribbon => new[] { new Vector2( -Width / 2, 0 ), new Vector2( Width / 2, 0 ) },
				Profile.Rail => new[] { new Vector2( -Width / 2, 0 ), new Vector2( Width / 2, 0 ), new Vector2( Width / 2, Height ), new Vector2( -Width / 2, Height ) },
				_ => Enumerable.Range( 0, Sides ).Select( i => new Vector2( Mathf.Cos( i * Mathf.PI * 2 / Sides ), Mathf.Sin( i * Mathf.PI * 2 / Sides ) ) * Radius ).ToArray(),
			};

			for ( int k = 0; k < curve.Count; k++ )
			{
				var tangent = (curve[Mathf.Min( k + 1, curve.Count - 1 )] - curve[Mathf.Max( k - 1, 0 )]).normalized;
				var up = Workplane.Up;
				var right = Vector3.Cross( up, tangent );
				if ( right.sqrMagnitude < 1e-6f ) right = Vector3.Cross( Vector3.forward, tangent );
				right.Normalize();
				var localUp = Vector3.Cross( tangent, right );

				rings.Add( profile.Select( p => curve[k] + (right * p.x + localUp * p.y) * unit ).ToArray() );
			}

			return rings;
		}

		public override void Apply()
		{
			var curve = Curve();
			var rings = Rings( curve );
			if ( rings.Count < 2 )
			{
				Cancel();
				return;
			}

			var all = rings.SelectMany( r => r ).ToList();
			var center = all.Aggregate( Vector3.zero, ( a, b ) => a + b ) / all.Count;
			if ( HammerSettings.GridSnap ) center = HammerSettings.SnapWorld( center );

			var go = new GameObject( Shape.ToString() );
			Undo.RegisterCreatedObjectUndo( go, "Path" );
			go.transform.position = center;
			go.isStatic = true;
			var c = go.AddComponent<HammerMesh>();

			var mesh = new S.PolygonMesh();
			mesh.SetTransform( c.WorldTransform );
			var material = HammerMaterials.Get( HammerSettings.ActiveMaterial ) ?? S.Material.Load( HammerMaterials.DefaultKey );
			var handles = rings.Select( r => mesh.AddVertices( r.Select( c.WorldToSource ).ToArray() ) ).ToList();
			var closed = Shape != Profile.Ribbon;
			var count = rings[0].Length;

			// Would these corners, in this order, make a face pointing along `outward` (world)?
			// Tried on a scratch mesh: taking a wrong face back out of the real one would also
			// delete its corners
			bool FacesOut( VertexHandle[] corners, Vector3 outward )
			{
				var scratch = new S.PolygonMesh();
				var f = scratch.AddFace( scratch.AddVertices( corners.Select( v => mesh.GetVertexPosition( v ) ).ToArray() ) );
				if ( !f.IsValid ) return true;
				scratch.ComputeFaceNormal( f, out var n );
				return Vector3.Dot( c.SourceDirectionToWorld( n ), outward ) >= 0;
			}

			// Adds a face turned to face along `outward`. The sides all wind the same way, worked
			// out from the first one (deciding each on its own could clash with its neighbours)
			bool? flip = null;
			void Face( VertexHandle[] corners, Vector3 outward, bool shareFlip )
			{
				bool reverse;
				if ( shareFlip ) { flip ??= !FacesOut( corners, outward ); reverse = flip.Value; }
				else reverse = !FacesOut( corners, outward );

				var f = mesh.AddFace( reverse ? corners.Reverse().ToArray() : corners );
				if ( f.IsValid ) mesh.SetFaceMaterial( f, material );
			}

			for ( int k = 0; k + 1 < rings.Count; k++ )
			{
				var segments = closed ? count : count - 1;
				for ( int i = 0; i < segments; i++ )
				{
					var j = (i + 1) % count;
					var quad = new[] { handles[k][i], handles[k][j], handles[k + 1][j], handles[k + 1][i] };
					var mid = (rings[k][i] + rings[k][j] + rings[k + 1][i] + rings[k + 1][j]) / 4;
					var outward = Shape == Profile.Ribbon ? Workplane.Up : mid - (curve[k] + curve[k + 1]) * 0.5f - (Shape == Profile.Rail ? Workplane.Up * Height * 0.5f * SourceSpace.UnitScale : Vector3.zero);
					Face( quad, outward, true );
				}
			}

			// Caps on the ends of a rail or pipe
			if ( closed )
			{
				Face( handles[0].ToArray(), curve[0] - curve[1], false );
				Face( handles[^1].ToArray(), curve[^1] - curve[^2], false );
			}

			mesh.SetSmoothingAngle( 40 );
			mesh.TextureAlignToGrid( mesh.Transform );
			mesh.ComputeFaceTextureCoordinatesFromParameters();
			c.SmoothingAngle = 40;
			c.Mesh = mesh;

			UnityEditor.Selection.activeGameObject = go;
			Close();
		}

		public override void Cancel() => Close();
	}
}
