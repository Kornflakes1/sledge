using System.Collections.Generic;
using System.Linq;
using HalfEdgeMesh;
using UnityEditor;
using UnityEngine;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Hammer's Mesh Projection: drop the selected mesh (or its selected parts) onto the world along
	/// a direction, every point landing on whatever it meets (a road draped over terrain, a trim
	/// laid on a curved wall). Space projects again, Enter keeps it, Esc puts it back.
	/// </summary>
	public sealed class MeshProjectionTool : SubTool
	{
		public enum Direction { Down, View, Facing }

		public static Direction ProjectDirection = Direction.Down;

		/// <summary>How far above the surface each point stops, in units.</summary>
		public static float Offset;

		sealed class Target
		{
			public HammerMesh Component;
			public List<(VertexHandle Vertex, Vector3 Start)> Vertices = new();
			public Vector3 Facing;
		}

		readonly List<Target> _targets = new();
		Vector3 _viewDirection = Vector3.down;

		public override string Title => "Mesh Projection";
		public override string Help => "Points are dropped onto the world along the direction. Space projects again, Enter keeps it, Esc puts it back.";

		public static void Open( HammerMeshTool tool )
		{
			var p = new MeshProjectionTool();

			var groups = tool.Mode == EditMode.Object
				? UnityEditor.Selection.gameObjects.SelectMany( g => g.GetComponentsInChildren<HammerMesh>() ).Distinct()
					.Select( c => (c, c.Mesh.VertexHandles.ToList()) ).ToList()
				: tool.SelectionVertices().GroupBy( v => v.Component ).Select( g => (g.Key, g.Select( v => v.Handle ).ToList()) ).ToList();

			foreach ( var (c, vertices) in groups )
			{
				var t = new Target { Component = c };
				foreach ( var v in vertices ) t.Vertices.Add( (v, c.SourceToWorld( c.Mesh.GetVertexPosition( v ) )) );

				// Facing: the average normal of the selected faces (or the whole mesh)
				var faces = tool.Mode == EditMode.Face ? tool.SelectedFaces.Where( f => f.Component == c ).Select( f => f.Handle ).ToList() : c.Mesh.FaceHandles.ToList();
				foreach ( var f in faces ) { c.Mesh.ComputeFaceNormal( f, out var n ); t.Facing += c.SourceDirectionToWorld( n ); }
				p._targets.Add( t );
			}

			if ( p._targets.Count == 0 ) return;

			var camera = HammerGUI.Camera;
			if ( camera != null ) p._viewDirection = camera.transform.forward;

			Undo.RecordObjects( p._targets.Select( t => (Object)t.Component ).ToArray(), "Mesh Projection" );
			tool.BeginSubTool( p );
			p.Project();
		}

		Vector3 DirectionFor( Target t ) => ProjectDirection switch
		{
			Direction.View => _viewDirection.normalized,
			Direction.Facing => t.Facing.sqrMagnitude > 1e-8f ? -t.Facing.normalized : -Workplane.Up,
			_ => -Workplane.Up,
		};

		protected override void OnSettingsUndone() => Project();

		/// <summary>
		/// Drop every point from where it started.
		/// </summary>
		public void Project()
		{
			var moving = _targets.Select( t => t.Component ).ToHashSet();
			var others = Object.FindObjectsByType<HammerMesh>( FindObjectsSortMode.None ).Where( c => c.isActiveAndEnabled && !moving.Contains( c ) ).ToList();

			foreach ( var t in _targets )
			{
				if ( t.Component == null ) continue;
				var dir = DirectionFor( t );
				var mesh = t.Component.Mesh;

				foreach ( var (v, start) in t.Vertices )
				{
					var target = start;
					if ( Drop( start, dir, others, out var hit ) )
						target = hit - dir * Offset * SourceSpace.UnitScale;
					mesh.SetVertexPosition( v, t.Component.WorldToSource( target ) );
				}

				mesh.ComputeFaceTextureCoordinatesFromParameters();
			}

			HammerMeshTool.RebuildNow( _targets.Select( t => t.Component ) );
			HammerViews.RepaintAll();
		}

		/// <summary>
		/// Where a point dropped along a direction lands. A point right on the seam between two
		/// surfaces can slip between their triangles, so it's tried again a hair to each side.
		/// </summary>
		static bool Drop( Vector3 start, Vector3 dir, List<HammerMesh> others, out Vector3 hit )
		{
			var side = Vector3.Cross( dir, Mathf.Abs( dir.y ) < 0.9f ? Vector3.up : Vector3.right ).normalized;
			var other = Vector3.Cross( dir, side );
			var nudge = 0.0005f;
			var tries = new[]
			{
				Vector3.zero, side, -side, other, -other,
				side + other, side - other, -side + other, -side - other, // corners need both
			};
			foreach ( var offset in tries.Select( t => t * nudge ) )
			{
				if ( MeshPicking.RaycastFace( new Ray( start + offset - dir * 0.001f, dir ), others, out var h ) )
				{
					hit = h.Point - offset;
					return true;
				}
			}

			hit = start;
			return false;
		}

		public override void OnOverlayGUI()
		{
			EditorGUI.BeginChangeCheck();
			ProjectDirection = (Direction)GUILayout.Toolbar( (int)ProjectDirection, new[] { "Down", "View", "Facing" }, EditorStyles.miniButton );
			Offset = EditorGUILayout.FloatField( new GUIContent( "Offset", "How far above the surface the points stop" ), Offset );
			if ( EditorGUI.EndChangeCheck() ) Project();

			if ( GUILayout.Button( "Project Again (Space)" ) )
			{
				var camera = HammerGUI.Camera;
				if ( camera != null ) _viewDirection = camera.transform.forward;
				Project();
			}
		}

		public override void OnViewGUI( HammerView view )
		{
			if ( Event.current.type != EventType.Repaint ) return;

			// Where each point came from
			Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
			Handles.color = new Color( 0.4f, 0.8f, 1.0f, 0.5f );
			foreach ( var t in _targets )
			{
				if ( t.Component == null ) continue;
				foreach ( var (v, start) in t.Vertices )
					if ( v.IsValid ) Handles.DrawDottedLine( start, t.Component.SourceToWorld( t.Component.Mesh.GetVertexPosition( v ) ), 3 );
			}
		}

		public override void Apply()
		{
			foreach ( var t in _targets )
			{
				if ( t.Component == null ) continue;
				t.Component.Commit();
				EditorUtility.SetDirty( t.Component );
			}

			MeshHealth.AfterEdit( _targets.Select( t => t.Component ) );
			Close();
		}

		public override void Cancel()
		{
			foreach ( var t in _targets )
			{
				if ( t.Component == null ) continue;
				foreach ( var (v, start) in t.Vertices )
					t.Component.Mesh.SetVertexPosition( v, t.Component.WorldToSource( start ) );
				t.Component.Mesh.ComputeFaceTextureCoordinatesFromParameters();
				t.Component.Commit();
			}

			Close();
		}
	}
}
