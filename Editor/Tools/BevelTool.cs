using System.Collections.Generic;
using System.Linq;
using HalfEdgeMesh;
using UnityEditor;
using UnityEngine;
using S = Sandbox;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Alt+F. Interactive edge bevel: drag in the scene to set the width, [ and ] for steps,
	/// Enter to apply. Port of the s&amp;box bevel tool.
	/// </summary>
	public sealed class BevelTool : SubTool
	{
		sealed class Target
		{
			public HammerMesh Component;
			public S.PolygonMesh Original;
			public List<int> Edges;
		}

		static int _steps = 1;
		static float _shape = 0.5f; // round, like the quick bevel
		static float _width = 8.0f;
		static bool _soft;

		readonly List<Target> _targets = new();
		readonly Dictionary<HammerMesh, List<HalfEdgeHandle>> _newEdges = new();

		bool _dragging;
		float _dragStartWidth;
		Vector2 _dragStartMouse;

		public override string Title => "Bevel";
		public override string Help => "Drag left/right to set the width, [ ] for steps. Enter applies, Esc cancels.";

		int SegmentCount => Mathf.Max( 1, _steps * 2 );

		public static void Open( HammerMeshTool tool )
		{
			var bevel = new BevelTool();

			foreach ( var g in tool.SelectedEdges.GroupBy( x => x.Component ) )
				bevel._targets.Add( new Target { Component = g.Key, Original = g.Key.Mesh, Edges = g.Select( x => x.Index ).ToList() } );

			if ( bevel._targets.Count == 0 )
				return;

			tool.BeginSubTool( bevel );
		}

		public override void OnEnable()
		{
			_width = Mathf.Max( _width, 0.0625f );
			UpdateBevel();
		}

		public override bool OnBracket( int direction )
		{
			_steps = Mathf.Clamp( _steps + direction, 0, 32 );
			UpdateBevel();
			return true;
		}

		public override void OnOverlayGUI()
		{
			EditorGUI.BeginChangeCheck();
			_width = Mathf.Max( 0.0625f, EditorGUILayout.FloatField( "Width", _width ) );
			_steps = EditorGUILayout.IntSlider( "Steps", _steps, 0, 32 );
			_shape = EditorGUILayout.Slider( "Shape", _shape, 0, 1 );
			_soft = EditorGUILayout.Toggle( "Soft Edges", _soft );
			if ( EditorGUI.EndChangeCheck() )
				UpdateBevel();
		}

		protected override void OnSettingsUndone() => UpdateBevel();

		void UpdateBevel()
		{
			_newEdges.Clear();

			foreach ( var t in _targets )
			{
				if ( t.Component == null ) continue;

				var mesh = Copy( t.Original, out _, out var edgeMap );
				var edges = t.Edges
					.Select( i => t.Original.HalfEdgeHandleFromIndex( i ) )
					.Where( edgeMap.ContainsKey )
					.Select( h => edgeMap[h] )
					.ToList();

				var outer = new List<HalfEdgeHandle>();
				var inner = new List<HalfEdgeHandle>();
				var newFaces = new List<FaceHandle>();
				var needUVs = new List<FaceHandle>();

				if ( !mesh.BevelEdges( edges, S.PolygonMesh.BevelEdgesMode.RemoveClosedEdges, SegmentCount, _width, _shape, outer, inner, newFaces, needUVs ) )
				{
					t.Component.SetPreviewMesh( t.Original );
					continue;
				}

				var smooth = _soft ? S.PolygonMesh.EdgeSmoothMode.Soft : S.PolygonMesh.EdgeSmoothMode.Default;
				foreach ( var e in inner ) mesh.SetEdgeSmoothing( e, smooth );
				foreach ( var f in needUVs ) mesh.TextureAlignToGrid( mesh.Transform, f );
				mesh.ComputeFaceTextureParametersFromCoordinates( newFaces );

				t.Component.SetPreviewMesh( mesh );
				_newEdges[t.Component] = outer.Concat( inner ).ToList();
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
					_dragStartWidth = _width;
					GUIUtility.hotControl = id;
					e.Use();
					break;

				case EventType.MouseDrag when _dragging && GUIUtility.hotControl == id:
				{
					// One grid unit per 20 pixels, snapped to the grid unless Ctrl is held
					var grid = HammerSettings.GridSize;
					var width = _dragStartWidth + (e.mousePosition.x - _dragStartMouse.x) / 20.0f * grid;
					if ( HammerSettings.GridSnap ^ (e.control || e.command) )
						width = Mathf.Round( width / grid ) * grid;

					width = Mathf.Max( 0.0625f, width );
					if ( !Mathf.Approximately( width, _width ) )
					{
						_width = width;
						UpdateBevel();
					}

					e.Use();
					break;
				}

				case EventType.MouseUp when _dragging && GUIUtility.hotControl == id:
					GUIUtility.hotControl = 0;
					_dragging = false;
					e.Use();
					break;

				case EventType.ScrollWheel when e.shift:
					OnBracket( e.delta.y > 0 ? -1 : 1 );
					e.Use();
					break;
			}

			if ( e.type == EventType.Repaint )
			{
				Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
				Handles.color = new Color( 1.0f, 0.92f, 0.15f );

				foreach ( var (component, edges) in _newEdges )
				{
					if ( component == null ) continue;
					var mesh = component.Mesh;
					foreach ( var h in edges )
					{
						if ( !h.IsValid ) continue;
						var line = mesh.GetEdgeLine( h );
						Handles.DrawAAPolyLine( 3.0f, component.SourceToWorld( line.Start ), component.SourceToWorld( line.End ) );
					}
				}

				Handles.BeginGUI();
				GUI.Label( new Rect( e.mousePosition.x + 16, e.mousePosition.y + 8, 200, 20 ), $"Width {_width:0.###}  Steps {_steps}", EditorStyles.whiteBoldLabel );
				Handles.EndGUI();
			}

			view.Repaint();
		}

		public override void Apply()
		{
			var results = _targets.Where( t => t.Component != null ).ToDictionary( t => t.Component, t => t.Component.Mesh );
			var components = results.Keys.ToArray();

			foreach ( var t in _targets )
				if ( t.Component != null ) t.Component.SetPreviewMesh( t.Original );

			Undo.RecordObjects( components, "Bevel Edges" );

			Tool.Selection.Clear();

			foreach ( var c in components )
			{
				c.SetPreviewMesh( results[c] );
				c.Commit();
				EditorUtility.SetDirty( c );

				if ( _newEdges.TryGetValue( c, out var edges ) )
				{
					foreach ( var h in edges )
						if ( h.IsValid ) Tool.Selection.Add( new MeshEdge( c, h ) );
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
