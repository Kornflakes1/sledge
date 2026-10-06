using System;
using System.Collections.Generic;
using System.Linq;
using HalfEdgeMesh;
using UnityEditor;
using UnityEngine;
using S = Sandbox;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Y. Bend open edges into arches, away from the face they belong to. [ ] change the number of
	/// steps, drag to set the height. Port of the s&amp;box edge arch tool.
	/// </summary>
	public sealed class EdgeArchTool : SubTool
	{
		sealed class Target
		{
			public HammerMesh Component;
			public S.PolygonMesh Original;
			public List<int> Edges;
			public List<List<VertexHandle>> Arcs = new();
		}

		static int _steps = 4;
		static float _height = 16;
		static float _offset;

		readonly List<Target> _targets = new();
		bool _dragging;
		float _dragStartHeight;
		Vector2 _dragStartMouse;

		public override string Title => "Edge Arch";
		public override string Help => "Drag up/down to set the arch height, [ ] for steps. Enter applies, Esc cancels.";

		public static void Open( HammerMeshTool tool )
		{
			var arch = new EdgeArchTool();

			foreach ( var g in tool.SelectedEdges.Where( x => x.IsOpen ).GroupBy( x => x.Component ) )
				arch._targets.Add( new Target { Component = g.Key, Original = g.Key.Mesh, Edges = g.Select( x => x.Index ).ToList() } );

			if ( arch._targets.Count == 0 )
			{
				Debug.LogWarning( "Edge Arch works on open edges (edges with a face on one side only)." );
				return;
			}

			tool.BeginSubTool( arch );
			arch.UpdateArch();
		}

		public override bool OnBracket( int direction )
		{
			_steps = Math.Clamp( _steps + direction, 1, 32 );
			UpdateArch();
			return true;
		}

		public override void OnOverlayGUI()
		{
			EditorGUI.BeginChangeCheck();
			_steps = EditorGUILayout.IntSlider( "Steps", _steps, 1, 32 );
			_height = EditorGUILayout.Slider( "Arc Height", _height, -256, 256 );
			_offset = EditorGUILayout.Slider( "Arc Offset", _offset, -256, 256 );
			if ( EditorGUI.EndChangeCheck() )
				UpdateArch();
		}

		readonly struct ArcCurve
		{
			readonly S.Vector3 _start, _a, _b, _end;

			public ArcCurve( S.Vector3 start, S.Vector3 a, S.Vector3 b, S.Vector3 end )
			{
				_start = start;
				_a = a;
				_b = b;
				_end = end;
			}

			public S.Vector3 Evaluate( float t )
			{
				var u = 1.0f - t;
				return (u * u * u) * _start + (3.0f * u * u * t) * _a + (3.0f * u * t * t) * _b + (t * t * t) * _end;
			}
		}

		static ArcCurve ComputeArc( S.PolygonMesh mesh, HalfEdgeHandle edge )
		{
			mesh.GetVerticesConnectedToEdge( edge, out var va, out var vb );
			var start = mesh.GetVertexPosition( va );
			var end = mesh.GetVertexPosition( vb );

			var flip = false;
			var face = mesh.GetHalfEdgeFace( edge );
			if ( !face.IsValid )
			{
				face = mesh.GetHalfEdgeFace( mesh.GetOppositeHalfEdge( edge ) );
				flip = true;
			}

			if ( !face.IsValid )
				return new ArcCurve( start, start, end, end );

			mesh.ComputeFaceNormal( face, out var normal );
			var edgeVector = end - start;
			var direction = edgeVector.Normal;
			var arcDirection = S.Vector3.Cross( normal, direction ).Normal * (flip ? -1.0f : 1.0f);
			var offset = Math.Max( _offset, -edgeVector.Length * 0.75f );
			var lift = arcDirection * _height;

			return new ArcCurve( start, start + lift - direction * offset, end + lift + direction * offset, end );
		}

		static List<VertexHandle> SubdivideEdge( S.PolygonMesh mesh, VertexHandle start, VertexHandle end, int steps )
		{
			var vertices = new List<VertexHandle>( steps + 1 ) { start };
			var current = start;

			for ( int i = 1; i < steps; i++ )
			{
				if ( !mesh.AddVertexToEdge( current, end, 1.0f / (steps - i + 1), out var v ) )
					break;
				vertices.Add( v );
				current = v;
			}

			vertices.Add( end );
			return vertices;
		}

		protected override void OnSettingsUndone() => UpdateArch();

		void UpdateArch()
		{
			foreach ( var t in _targets )
			{
				if ( t.Component == null ) continue;

				var mesh = Copy( t.Original, out _, out var edgeMap );
				t.Arcs.Clear();

				foreach ( var index in t.Edges )
				{
					var originalEdge = t.Original.HalfEdgeHandleFromIndex( index );
					if ( !edgeMap.TryGetValue( originalEdge, out var edge ) || !mesh.IsEdgeOpen( edge ) )
						continue;

					mesh.GetVerticesConnectedToEdge( edge, out var a, out var b );
					var curve = ComputeArc( t.Original, originalEdge );
					var vertices = SubdivideEdge( mesh, a, b, _steps );

					for ( int i = 0; i < vertices.Count; i++ )
						mesh.SetVertexPosition( vertices[i], curve.Evaluate( i / (float)(vertices.Count - 1) ) );

					t.Arcs.Add( vertices );
				}

				mesh.ComputeFaceTextureCoordinatesFromParameters();
				t.Component.SetPreviewMesh( mesh );
			}

			HammerViews.RepaintAll();
		}

		public override void OnViewGUI( HammerView view )
		{
			var e = Event.current;
			var id = GUIUtility.GetControlID( FocusType.Passive );
			if ( e.type == EventType.Layout )
				HandleUtility.AddDefaultControl( id );

			switch ( e.GetTypeForControl( id ) )
			{
				case EventType.MouseDown when e.button == 0 && !e.alt:
					_dragging = true;
					_dragStartMouse = e.mousePosition;
					_dragStartHeight = _height;
					GUIUtility.hotControl = id;
					e.Use();
					break;

				case EventType.MouseDrag when _dragging && GUIUtility.hotControl == id:
				{
					var grid = HammerSettings.GridSize;
					var height = _dragStartHeight - (e.mousePosition.y - _dragStartMouse.y) / 20.0f * grid;
					if ( HammerSettings.GridSnap ^ (e.control || e.command) )
						height = Mathf.Round( height / grid ) * grid;

					if ( !Mathf.Approximately( height, _height ) )
					{
						_height = height;
						UpdateArch();
					}

					e.Use();
					break;
				}

				case EventType.MouseUp when _dragging && GUIUtility.hotControl == id:
					GUIUtility.hotControl = 0;
					_dragging = false;
					e.Use();
					break;
			}

			if ( e.type == EventType.Repaint )
			{
				Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
				Handles.color = new Color( 1.0f, 0.92f, 0.15f );

				foreach ( var t in _targets )
				{
					if ( t.Component == null ) continue;
					var mesh = t.Component.Mesh;
					foreach ( var arc in t.Arcs )
					{
						var points = arc.Where( v => v.IsValid ).Select( v => t.Component.SourceToWorld( mesh.GetVertexPosition( v ) ) ).ToArray();
						if ( points.Length > 1 ) Handles.DrawAAPolyLine( 3.0f, points );
					}
				}
			}
		}

		public override void Apply()
		{
			var results = _targets.Where( t => t.Component != null ).Select( t => (t, t.Component.Mesh) ).ToList();

			foreach ( var t in _targets )
				if ( t.Component != null ) t.Component.SetPreviewMesh( t.Original );

			Undo.RecordObjects( results.Select( x => (UnityEngine.Object)x.t.Component ).ToArray(), "Edge Arch" );
			Tool.Selection.Clear();

			foreach ( var (t, mesh) in results )
			{
				t.Component.SetPreviewMesh( mesh );
				t.Component.Commit();
				EditorUtility.SetDirty( t.Component );

				foreach ( var arc in t.Arcs )
				{
					for ( int i = 1; i < arc.Count; i++ )
					{
						var edge = mesh.FindEdgeConnectingVertices( arc[i - 1], arc[i] );
						if ( edge.IsValid ) Tool.Selection.Add( new MeshEdge( t.Component, edge ) );
					}
				}
			}

			Close();
		}

		public override void Cancel()
		{
			foreach ( var t in _targets )
				if ( t.Component != null ) t.Component.SetPreviewMesh( t.Original );

			Close();
		}
	}
}
