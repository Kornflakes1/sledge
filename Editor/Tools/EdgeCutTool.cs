using System.Collections.Generic;
using System.Linq;
using HalfEdgeMesh;
using UnityEditor;
using UnityEngine;
using S = Sandbox;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// C. Knife tool: click points on vertices, edges and faces to draw a cut, Enter to apply.
	/// A single point on an edge splits that edge. Port of the s&amp;box edge cut tool.
	/// </summary>
	public sealed class EdgeCutTool : SubTool
	{
		const float SampleRadius = 8.0f;

		struct CutPoint
		{
			public MeshFace Face;
			public MeshVertex Vertex;
			public MeshEdge Edge;
			public Vector3 WorldPosition;
			public S.Vector3 BasePosition;

			public bool IsValid => Face.IsValid;
			public HammerMesh Component => Face.Component;

			public static CutPoint OnVertex( MeshFace face, MeshVertex vertex ) => new()
			{
				Face = face,
				Vertex = vertex,
				WorldPosition = vertex.PositionWorld,
				BasePosition = vertex.PositionLocal,
			};

			public static CutPoint OnEdge( MeshFace face, MeshEdge edge, Vector3 world )
			{
				var mesh = edge.Component.Mesh;
				var local = face.Component.WorldToSource( world );
				mesh.GetVerticesConnectedToEdge( edge.Handle, out var a, out var b );
				mesh.ComputeClosestPointOnEdge( a, b, local, out var t );
				var pa = mesh.GetVertexPosition( a );
				var pb = mesh.GetVertexPosition( b );

				return new CutPoint
				{
					Face = face,
					Edge = edge,
					WorldPosition = world,
					BasePosition = pa + (pb - pa) * t,
				};
			}

			public static CutPoint OnFace( MeshFace face, Vector3 world ) => new()
			{
				Face = face,
				WorldPosition = world,
				BasePosition = face.Component.WorldToSource( world ),
			};

			public List<FaceHandle> ConnectedFaces()
			{
				var mesh = Component.Mesh;
				if ( Vertex.IsValid )
				{
					mesh.GetFacesConnectedToVertex( Vertex.Handle, out var faces );
					return faces;
				}

				if ( Edge.IsValid )
				{
					mesh.GetFacesConnectedToEdge( Edge.Handle, out var a, out var b );
					return new List<FaceHandle> { a, b };
				}

				return new List<FaceHandle> { Face.Handle };
			}
		}

		readonly List<CutPoint> _points = new();
		CutPoint _preview;

		// Loop cut mode (V): one click cuts right round a strip of quads
		List<CutPoint> _loop;
		bool _loopClosed;

		// Settings (static, so they're kept between uses and undoable while the tool is open)
		static bool _loopMode;
		static bool _uniformOffset;
		static bool _flipUniformOffset;
		static bool _selectionOnly;

		// What Selection only allows: the faces selected when the tool opened, or else the meshes
		HashSet<MeshFace> _allowedFaces;
		HashSet<HammerMesh> _allowedMeshes;

		public override string Title => "Edge Cut";
		public override string Help => "Click on vertices, edges and faces to draw the cut. Enter applies, Esc cancels.";

		public override (string Key, string Operation)[] Keys => new[]
		{
			("LMouse", "Add cut"),
			("V", "Toggle loop cut mode"),
			("Shift", "Aligned snap"),
			("Ctrl", "Toggle snapping"),
			("Esc", "Cancel and close tool"),
			("B", "Place cut"),
			("Space", "Close tool"),
			("Enter", "Close tool"),
		};

		public static void Open( HammerMeshTool tool )
		{
			var cut = new EdgeCutTool();
			var faces = tool.SelectedFaces.ToHashSet();
			cut._allowedFaces = faces.Count > 0 ? faces : null;
			cut._allowedMeshes = tool.Selection.Components
				.Concat( UnityEditor.Selection.gameObjects.SelectMany( g => g.GetComponentsInChildren<HammerMesh>() ) )
				.ToHashSet();
			tool.BeginSubTool( cut );
		}

		public override void OnOverlayGUI()
		{
			_loopMode = EditorGUILayout.ToggleLeft( "Loop cut mode [V]", _loopMode );
			using ( new EditorGUI.DisabledScope( !_loopMode ) )
			using ( new EditorGUI.IndentLevelScope() )
			{
				_uniformOffset = EditorGUILayout.ToggleLeft( "Uniform Offset [E]", _uniformOffset );
				using ( new EditorGUI.DisabledScope( !_uniformOffset ) )
					_flipUniformOffset = EditorGUILayout.ToggleLeft( "Flip Uniform Offset [F]", _flipUniformOffset );
			}

			GUILayout.Space( 4 );
			GUILayout.Label( "Selection mode", EditorStyles.miniLabel );
			_selectionOnly = EditorGUILayout.ToggleLeft( "Selection only [G]", _selectionOnly );
		}

		void Changed()
		{
			_loop = null;
			HammerViews.RepaintAll();
		}

		public void ToggleLoopMode() { _loopMode = !_loopMode; Changed(); }
		public void ToggleUniformOffset() { _uniformOffset = !_uniformOffset; Changed(); }
		public void ToggleFlipUniformOffset() { _flipUniformOffset = !_flipUniformOffset; if ( _flipUniformOffset ) _uniformOffset = true; Changed(); }
		public void ToggleSelectionOnly() { _selectionOnly = !_selectionOnly; Changed(); }

		/// <summary>B: cut what's been drawn and stay in the tool for the next one.</summary>
		public void PlaceCut() => ApplyCore( false );

		bool Allowed( MeshFace face )
		{
			if ( !_selectionOnly ) return true;
			if ( _allowedFaces != null ) return _allowedFaces.Contains( face );
			return _allowedMeshes == null || _allowedMeshes.Count == 0 || _allowedMeshes.Contains( face.Component );
		}

		static bool Snapping( Event e ) => HammerSettings.GridSnap ^ (e.control || e.command);

		/// <summary>
		/// Add a cut point on an edge, as if clicked. For tests and scripting.
		/// </summary>
		internal void AddEdgePoint( MeshFace face, MeshEdge edge, Vector3 world ) => _points.Add( CutPoint.OnEdge( face, edge, world ) );

		/// <summary>
		/// Add a cut point on a vertex, as if clicked. For tests and scripting.
		/// </summary>
		internal void AddVertexPoint( MeshFace face, MeshVertex vertex ) => _points.Add( CutPoint.OnVertex( face, vertex ) );

		/// <summary>
		/// Add a cut point inside a face, as if clicked. For tests and scripting.
		/// </summary>
		internal void AddFacePoint( MeshFace face, Vector3 world ) => _points.Add( CutPoint.OnFace( face, world ) );

		public override void OnViewGUI( HammerView view )
		{
			var e = Event.current;
			var id = GUIUtility.GetControlID( FocusType.Passive );

			if ( e.type == EventType.Layout )
				HandleUtility.AddDefaultControl( id );

			if ( e.type == EventType.MouseMove || e.type == EventType.Layout || e.type == EventType.MouseDrag )
			{
				_preview = FindCutPoint( e.mousePosition, Snapping( e ), e.shift );

				// Each new point has to share a face with the last one
				if ( _points.Count > 0 && _preview.IsValid && !FindSharedFace( _points[^1], _preview ).IsValid )
					_preview = default;

				_loop = _loopMode && _points.Count == 0 && _preview.IsValid && _preview.Edge.IsValid && !_preview.Vertex.IsValid
					? BuildLoop( _preview, out _loopClosed )
					: null;

				if ( e.type == EventType.MouseMove )
					view.Repaint();
			}

			if ( e.GetTypeForControl( id ) == EventType.MouseDown && e.button == 0 && !e.alt )
			{
				if ( _loop != null )
				{
					// Loop mode: the whole loop in one go, then ready for the next
					_points.AddRange( _loop );
					if ( _loopClosed ) _points.Add( _loop[0] );
					_loop = null;
					ApplyCore( false );
				}
				else if ( _preview.IsValid )
				{
					_points.Add( _preview );
				}

				e.Use();
			}

			if ( e.type == EventType.KeyDown && e.keyCode == KeyCode.Space )
			{
				Apply();
				e.Use();
				return;
			}

			// Ctrl and Shift change the snapping: show it straight away
			if ( e.type == EventType.KeyDown || e.type == EventType.KeyUp )
				view.Repaint();

			if ( e.type == EventType.Repaint )
				Draw();
		}

		CutPoint FindCutPoint( Vector2 mouse, bool snap, bool aligned )
		{
			if ( !MeshPicking.PickFace( mouse, MeshPicking.VisibleMeshes(), out var hit ) )
				return default;

			var face = hit.Face;
			if ( !Allowed( face ) ) return default;
			var component = face.Component;
			var mesh = component.Mesh;

			// Shift: only points that line up with the last one (along a world axis, or square
			// across from the edge it's on)
			if ( aligned && _points.Count > 0 )
				return FindAlignedPoint( face, mouse );

			// Closest vertex of the face under the cursor
			var bestDistance = SampleRadius;
			MeshVertex bestVertex = default;
			foreach ( var v in mesh.GetFaceVertices( face.Handle ) )
			{
				var gui = HammerGUI.WorldToGUI2( component.SourceToWorld( mesh.GetVertexPosition( v ) ) );
				var d = Vector2.Distance( gui, mouse );
				if ( d < bestDistance )
				{
					bestDistance = d;
					bestVertex = new MeshVertex( component, v );
				}
			}

			if ( bestVertex.IsValid )
				return CutPoint.OnVertex( face, bestVertex );

			// Then the closest edge
			bestDistance = SampleRadius;
			MeshEdge bestEdge = default;
			Vector3 bestPoint = default;
			if ( mesh.GetFaceVerticesConnectedToFace( face.Handle, out var edges ) )
			{
				foreach ( var he in edges )
				{
					var line = mesh.GetEdgeLine( he );
					var a = component.SourceToWorld( line.Start );
					var b = component.SourceToWorld( line.End );
					var closest = ClosestPointOnSegment( hit.Point, a, b );
					var d = Vector2.Distance( HammerGUI.WorldToGUI2( closest ), mouse );
					if ( d < bestDistance )
					{
						bestDistance = d;
						bestEdge = new MeshEdge( component, he );
						bestPoint = closest;
					}
				}
			}

			if ( bestEdge.IsValid )
			{
				if ( snap )
					bestPoint = SnapAlongEdge( bestEdge, bestPoint );

				return CutPoint.OnEdge( face, bestEdge, bestPoint );
			}

			// Interior points only make sense once the cut has started
			if ( _points.Count == 0 ) return default;
			return CutPoint.OnFace( face, snap ? SnapInFace( face, hit.Point ) : hit.Point );
		}

		/// <summary>
		/// A point inside a face, snapped to the grid along the face (and kept on it).
		/// </summary>
		static Vector3 SnapInFace( MeshFace face, Vector3 point )
		{
			var normal = face.NormalWorld;
			var snapped = HammerSettings.SnapWorld( point, Mathf.Abs( normal.x ) < 0.9f, Mathf.Abs( normal.y ) < 0.9f, Mathf.Abs( normal.z ) < 0.9f );
			snapped -= normal * Vector3.Dot( snapped - point, normal );
			return MeshPicking.PickFace( HammerGUI.WorldToGUI2( snapped ), new List<HammerMesh> { face.Component }, out var check ) && check.Face.Handle == face.Handle ? snapped : point;
		}

		/// <summary>
		/// Shift: where the face's edges meet the world axis planes through the last point, or the
		/// plane square to the edge the last point is on. The one nearest the mouse.
		/// </summary>
		CutPoint FindAlignedPoint( MeshFace face, Vector2 mouse )
		{
			var previous = _points[^1];
			if ( previous.Component != face.Component ) return default;

			var planes = new List<Vector3> { Vector3.right, Vector3.up, Vector3.forward };
			if ( previous.Edge.IsValid )
			{
				previous.Edge.GetWorldPoints( out var ea, out var eb );
				if ( (eb - ea).sqrMagnitude > 1e-10f ) planes.Add( (eb - ea).normalized );
			}

			var component = face.Component;
			var mesh = component.Mesh;
			var origin = previous.WorldPosition;
			var best = float.MaxValue;
			CutPoint result = default;

			if ( !mesh.GetFaceVerticesConnectedToFace( face.Handle, out var edges ) ) return default;
			foreach ( var he in edges )
			{
				var line = mesh.GetEdgeLine( he );
				var a = component.SourceToWorld( line.Start );
				var b = component.SourceToWorld( line.End );
				foreach ( var n in planes )
				{
					var denom = Vector3.Dot( b - a, n );
					if ( Mathf.Abs( denom ) < 1e-6f ) continue;
					var t = Vector3.Dot( origin - a, n ) / denom;
					if ( t < -1e-4f || t > 1 + 1e-4f ) continue;
					var p = a + (b - a) * Mathf.Clamp01( t );
					if ( (p - origin).sqrMagnitude < 1e-8f ) continue;
					var d = Vector2.Distance( HammerGUI.WorldToGUI2( p ), mouse );
					if ( d < best )
					{
						best = d;
						result = CutPoint.OnEdge( face, new MeshEdge( component, he ), p );
					}
				}
			}

			return result;
		}

		/// <summary>
		/// The loop through the edge under the mouse: across each quad to the opposite edge, both
		/// ways, until it comes round or meets something that isn't a quad. The cut is the same
		/// fraction along every edge, or (Uniform Offset) the same distance from one side.
		/// </summary>
		List<CutPoint> BuildLoop( CutPoint start, out bool closed )
		{
			closed = false;
			var component = start.Component;
			var mesh = component.Mesh;
			var startFace = start.Face.Handle;
			var startEdge = start.Edge.Handle;

			mesh.GetVerticesConnectedToEdge( startEdge, startFace, out var va, out var vb );
			var pa = component.SourceToWorld( mesh.GetVertexPosition( va ) );
			var pb = component.SourceToWorld( mesh.GetVertexPosition( vb ) );
			var length = Vector3.Distance( pa, pb );
			if ( length < 1e-6f ) return null;
			var t = Mathf.Clamp( Vector3.Dot( start.WorldPosition - pa, pb - pa ) / (length * length), 0.01f, 0.99f );

			// Uniform: the distance from the start side (or the end side, flipped)
			var fromStart = t * length;
			var fromEnd = (1 - t) * length;

			var points = new List<CutPoint> { EdgePoint( component, startFace, startEdge, t ) };
			var visited = new HashSet<HalfEdgeHandle> { startEdge };
			var opposite = mesh.GetOppositeHalfEdge( startEdge );
			if ( opposite.IsValid ) visited.Add( opposite );

			float? Fixed( bool firstWay ) => !_uniformOffset ? null : (firstWay != _flipUniformOffset ? fromStart : -fromEnd);

			closed = Walk( component, startFace, startEdge, t, Fixed( true ), visited, points, true );
			if ( !closed )
			{
				mesh.GetFacesConnectedToEdge( startEdge, out var fa, out var fb );
				var other = fa == startFace ? fb : fa;
				// The other way the edge runs backwards: its start is this way's end
				if ( other.IsValid )
					closed = Walk( component, other, startEdge, 1 - t, Fixed( false ) is float f ? -f : null, visited, points, false );
			}

			return points.Count >= 2 ? points : null;
		}

		/// <param name="fixedDistance">Uniform offset: the distance from each edge's start when
		/// positive, from its end when negative; null keeps the fraction.</param>
		bool Walk( HammerMesh component, FaceHandle face, HalfEdgeHandle edge, float t, float? fixedDistance, HashSet<HalfEdgeHandle> visited, List<CutPoint> points, bool append )
		{
			var mesh = component.Mesh;
			var currentFace = face;
			var currentEdge = edge;
			var currentT = t;

			for ( int guard = 0; guard < 10000; guard++ )
			{
				if ( !Allowed( new MeshFace( component, currentFace ) ) ) return false;

				mesh.GetEdgesConnectedToFace( currentFace, out var faceEdges );
				if ( faceEdges.Count != 4 ) return false;

				var index = faceEdges.FindIndex( e => e == currentEdge || e == mesh.GetOppositeHalfEdge( currentEdge ) );
				if ( index < 0 ) return false;

				var nextEdge = faceEdges[(index + 2) % 4];
				var oppositeNext = mesh.GetOppositeHalfEdge( nextEdge );
				if ( visited.Contains( nextEdge ) || (oppositeNext.IsValid && visited.Contains( oppositeNext )) )
					return true;

				// The opposite side of a quad runs the other way
				float nextT;
				if ( fixedDistance is float d )
				{
					mesh.GetVerticesConnectedToEdge( nextEdge, currentFace, out var na, out var nb );
					var len = Vector3.Distance( component.SourceToWorld( mesh.GetVertexPosition( na ) ), component.SourceToWorld( mesh.GetVertexPosition( nb ) ) );
					var along = len > 1e-6f ? Mathf.Clamp( Mathf.Abs( d ) / len, 0.01f, 0.99f ) : 0.5f;
					nextT = d >= 0 ? 1 - along : along;
				}
				else
				{
					nextT = 1 - currentT;
				}

				var cut = EdgePoint( component, currentFace, nextEdge, nextT );
				if ( append ) points.Add( cut );
				else points.Insert( 0, cut );

				visited.Add( nextEdge );
				if ( oppositeNext.IsValid ) visited.Add( oppositeNext );

				mesh.GetFacesConnectedToEdge( nextEdge, out var fa, out var fb );
				var next = fa == currentFace ? fb : fa;
				if ( !next.IsValid ) return false;

				currentFace = next;
				currentEdge = nextEdge;
				currentT = 1 - nextT;
			}

			return false;
		}

		static CutPoint EdgePoint( HammerMesh component, FaceHandle face, HalfEdgeHandle edge, float t )
		{
			var mesh = component.Mesh;
			mesh.GetVerticesConnectedToEdge( edge, face, out var va, out var vb );
			var pa = component.SourceToWorld( mesh.GetVertexPosition( va ) );
			var pb = component.SourceToWorld( mesh.GetVertexPosition( vb ) );
			return CutPoint.OnEdge( new MeshFace( component, face ), new MeshEdge( component, edge ), Vector3.Lerp( pa, pb, t ) );
		}

		/// <summary>For tests: the loop the tool would cut through this edge point.</summary>
		internal List<Vector3> LoopThrough( MeshFace face, MeshEdge edge, Vector3 world, out bool closed )
		{
			var loop = BuildLoop( CutPoint.OnEdge( face, edge, world ), out closed );
			return loop?.Select( p => p.WorldPosition ).ToList();
		}

		/// <summary>For tests: a loop mode click on this edge point.</summary>
		internal bool CutLoop( MeshFace face, MeshEdge edge, Vector3 world )
		{
			var loop = BuildLoop( CutPoint.OnEdge( face, edge, world ), out var closed );
			if ( loop == null ) return false;
			_points.AddRange( loop );
			if ( closed ) _points.Add( loop[0] );
			ApplyCore( false );
			return true;
		}

		internal static void SetLoopOptions( bool loop, bool uniform, bool flip, bool selectionOnly )
		{
			_loopMode = loop;
			_uniformOffset = uniform;
			_flipUniformOffset = flip;
			_selectionOnly = selectionOnly;
		}

		/// <summary>
		/// Snap a point on an edge to where the edge crosses grid lines, so cuts land on the grid.
		/// </summary>
		static Vector3 SnapAlongEdge( MeshEdge edge, Vector3 point )
		{
			edge.GetWorldPoints( out var a, out var b );
			var snapped = HammerSettings.SnapWorld( point );
			var t = Vector3.Dot( snapped - a, b - a ) / Mathf.Max( (b - a).sqrMagnitude, 1e-8f );
			var onEdge = a + (b - a) * Mathf.Clamp01( t );

			// Only take the snap if it stays close to where the user pointed
			var pixels = Vector2.Distance( HammerGUI.WorldToGUI2( onEdge ), HammerGUI.WorldToGUI2( point ) );
			return pixels < 12 ? onEdge : point;
		}

		static Vector3 ClosestPointOnSegment( Vector3 p, Vector3 a, Vector3 b )
		{
			var ab = b - a;
			var t = Mathf.Clamp01( Vector3.Dot( p - a, ab ) / Mathf.Max( ab.sqrMagnitude, 1e-10f ) );
			return a + ab * t;
		}

		static MeshFace FindSharedFace( CutPoint a, CutPoint b )
		{
			if ( !a.IsValid || !b.IsValid || a.Component != b.Component )
				return default;

			var facesB = b.ConnectedFaces();
			foreach ( var f in a.ConnectedFaces() )
			{
				if ( f.IsValid && facesB.Contains( f ) )
					return new MeshFace( a.Component, f );
			}

			return default;
		}

		static readonly Color PointColor = new( 1.0f, 0.92f, 0.15f );
		static readonly Color PreviewColor = new( 1.0f, 0.62f, 0.1f );

		/// <summary>
		/// Hammer's look: the edge under the cursor in green with how far the point is from each
		/// end, a faint orange guide straight across the face, and the cut so far in yellow.
		/// </summary>
		void Draw()
		{
			Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

			// The loop that a click would cut
			if ( _loop != null )
			{
				var line = _loop.Select( p => p.WorldPosition ).ToList();
				if ( _loopClosed ) line.Add( line[0] );
				Handles.color = new Color( 1.0f, 0.55f, 0.1f, 0.35f );
				Handles.DrawAAPolyLine( 2.0f, line.ToArray() );
				Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
				Handles.color = new Color( 1.0f, 0.55f, 0.1f );
				Handles.DrawAAPolyLine( 3.0f, line.ToArray() );
				Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
			}

			Handles.color = PointColor;
			for ( int i = 1; i < _points.Count; i++ )
				Handles.DrawAAPolyLine( 3.0f, _points[i - 1].WorldPosition, _points[i].WorldPosition );

			if ( _preview.IsValid )
			{
				var p = _preview.WorldPosition;

				if ( _preview.Edge.IsValid )
				{
					_preview.Edge.GetWorldPoints( out var a, out var b );
					Handles.color = new Color( 0.2f, 1.0f, 0.2f );
					Handles.DrawAAPolyLine( 4.0f, a, b );
					DrawPerpendicularGuide( _preview, a, b );
				}

				if ( _points.Count > 0 )
				{
					Handles.color = PointColor;
					Handles.DrawAAPolyLine( 3.0f, _points[^1].WorldPosition, p );
				}

				Square( p, PreviewColor, 0.045f );

				if ( _preview.Edge.IsValid && !_preview.Vertex.IsValid )
				{
					// Distance to each end of the edge, in Hammer units
					_preview.Edge.GetWorldPoints( out var a, out var b );
					var toA = Vector3.Distance( p, a ) / SourceSpace.UnitScale;
					var toB = Vector3.Distance( p, b ) / SourceSpace.UnitScale;
					HammerGUI.OutlinedLabel( p, $"({toA:F0} : {toB:F0})", Color.white, 12 );
				}
				else if ( _points.Count > 0 && !_preview.Vertex.IsValid )
				{
					var length = Vector3.Distance( _points[^1].WorldPosition, p ) / SourceSpace.UnitScale;
					HammerGUI.OutlinedLabel( p, $"{length:F0}", Color.white, 12 );
				}
			}

			foreach ( var point in _points )
				Square( point.WorldPosition, PointColor, 0.032f );
		}

		/// <summary>A square with a dark rim.</summary>
		static void Square( Vector3 p, Color color, float size )
		{
			var s = HammerGUI.HandleSize( p ) * size;
			Handles.color = new Color( 0.1f, 0.08f, 0.02f );
			Handles.DotHandleCap( 0, p, Quaternion.identity, s * 1.3f, EventType.Repaint );
			Handles.color = color;
			Handles.DotHandleCap( 0, p, Quaternion.identity, s, EventType.Repaint );
		}

		/// <summary>
		/// From the point, straight across the face (square to the edge) to the far side: where a
		/// cut from here would go if carried on.
		/// </summary>
		static void DrawPerpendicularGuide( CutPoint point, Vector3 a, Vector3 b )
		{
			var component = point.Component;
			var mesh = component.Mesh;
			var face = point.Face.Handle;
			mesh.ComputeFaceNormal( face, out var n );
			var normal = component.SourceDirectionToWorld( n ).normalized;
			var corners = mesh.GetFaceVertices( face ).Select( v => component.SourceToWorld( mesh.GetVertexPosition( v ) ) ).ToList();
			if ( corners.Count < 3 ) return;

			var across = Vector3.Cross( normal, (b - a).normalized ).normalized;
			var start = point.WorldPosition;
			var center = corners.Aggregate( Vector3.zero, ( x, y ) => x + y ) / corners.Count;
			if ( Vector3.Dot( center - start, across ) < 0 ) across = -across;

			// The nearest face side the guide meets
			var reach = 0.0f;
			foreach ( var c in corners ) reach = Mathf.Max( reach, Vector3.Dot( c - start, across ) );
			for ( int i = 0; i < corners.Count; i++ )
			{
				var p0 = corners[i];
				var p1 = corners[(i + 1) % corners.Count];
				var side = p1 - p0;
				var sideNormal = Vector3.Cross( normal, side );
				var denom = Vector3.Dot( sideNormal, across );
				if ( Mathf.Abs( denom ) < 1e-8f ) continue;
				var t = Vector3.Dot( sideNormal, p0 - start ) / denom;
				if ( t <= 1e-4f || t >= reach ) continue;
				var hit = start + across * t;
				var along = Vector3.Dot( hit - p0, side ) / Mathf.Max( side.sqrMagnitude, 1e-10f );
				if ( along >= -1e-3f && along <= 1 + 1e-3f ) reach = t;
			}

			Handles.color = new Color( 1.0f, 0.6f, 0.15f, 0.5f );
			Handles.DrawAAPolyLine( 2.0f, start, start + across * reach );
		}

		public override void Apply() => ApplyCore( true );

		void ApplyCore( bool close )
		{
			if ( _points.Count == 0 )
			{
				if ( close ) Cancel();
				return;
			}

			var components = _points.Select( x => x.Component ).Where( x => x != null ).Distinct().ToArray();
			Undo.RecordObjects( components, "Edge Cut" );
			Tool.Selection.Clear();

			if ( _points.Count == 1 )
			{
				var point = _points[0];
				if ( point.Edge.IsValid )
				{
					var table = new SortedSet<HalfEdgeHandle>( Comparer<HalfEdgeHandle>.Create( ( x, y ) => x.Index.CompareTo( y.Index ) ) );
					var v = AddCutToEdge( point.Edge, point.BasePosition, table );
					point.Component.Mesh.ComputeFaceTextureCoordinatesFromParameters();
					if ( v.IsValid ) Tool.Selection.Add( new MeshVertex( point.Component, v ) );
				}
			}
			else
			{
				ApplyCut( out var edges );
				foreach ( var e in edges ) Tool.Selection.Add( e );
			}

			foreach ( var c in components )
			{
				c.Commit();
				EditorUtility.SetDirty( c );
			}

			var mode = _points.Count == 1 ? EditMode.Vertex : EditMode.Edge;
			_points.Clear();
			_preview = default;
			_loop = null;
			if ( !close )
			{
				Undo.FlushUndoRecordObjects();
				Undo.IncrementCurrentGroup();
				HammerViews.RepaintAll();
				return;
			}
			Close();
			Tool.Mode = mode;
		}

		void ApplyCut( out List<MeshEdge> cutEdges )
		{
			cutEdges = new List<MeshEdge>();

			var components = new List<HammerMesh>();
			foreach ( var p in _points )
			{
				if ( p.Component != null && !components.Contains( p.Component ) )
					components.Add( p.Component );
			}

			var comparer = Comparer<HalfEdgeHandle>.Create( ( x, y ) => x.Index.CompareTo( y.Index ) );
			var tables = components.Select( _ => new SortedSet<HalfEdgeHandle>( comparer ) ).ToList();

			int startIndex = 0;
			MeshVertex startVertex = default;
			MeshEdge edgeToRemove = default;

			while ( startIndex < _points.Count )
			{
				while ( !startVertex.IsValid && startIndex < _points.Count )
				{
					var start = _points[startIndex];
					if ( start.IsValid )
					{
						if ( start.Edge.IsValid )
						{
							var table = tables[components.IndexOf( start.Component )];
							startVertex = new MeshVertex( start.Component, AddCutToEdge( start.Edge, start.BasePosition, table ) );
							break;
						}

						if ( start.Vertex.IsValid )
						{
							startVertex = start.Vertex;
							break;
						}
					}

					startIndex++;
				}

				int endIndex = startIndex + 1;
				MeshVertex endVertex = default;

				if ( endIndex < _points.Count && startVertex.IsValid )
				{
					var end = _points[endIndex];
					if ( end.Face.IsValid && end.Component == startVertex.Component )
					{
						var component = startVertex.Component;
						var mesh = component.Mesh;
						var table = tables[components.IndexOf( component )];

						var target = mesh.CreateEdgesConnectingVertexToPoint( startVertex.Handle, end.BasePosition, out var segmentEdges, out var isLastConnector, table );

						if ( edgeToRemove.IsValid && !segmentEdges.Contains( edgeToRemove.Handle ) )
						{
							mesh.GetVerticesConnectedToEdge( edgeToRemove.Handle, out var a, out var b );
							mesh.DissolveEdge( edgeToRemove.Handle );
							table.Remove( edgeToRemove.Handle );
							mesh.RemoveColinearVertexAndUpdateTable( a, table );
							mesh.RemoveColinearVertexAndUpdateTable( b, table );
						}

						if ( !end.Vertex.IsValid && !end.Edge.IsValid && segmentEdges.Count > 1 && isLastConnector )
							edgeToRemove = new MeshEdge( component, segmentEdges[^1] );

						endVertex = new MeshVertex( component, target );
					}
				}

				startIndex = endIndex;
				startVertex = endVertex;
			}

			for ( int i = 0; i < components.Count; i++ )
			{
				foreach ( var he in tables[i] )
				{
					if ( he.IsValid )
						cutEdges.Add( new MeshEdge( components[i], he ) );
				}

				components[i].Mesh.ComputeFaceTextureCoordinatesFromParameters();
			}
		}

		static VertexHandle AddCutToEdge( MeshEdge edge, S.Vector3 target, SortedSet<HalfEdgeHandle> table )
		{
			var mesh = edge.Component.Mesh;
			var visited = new List<HalfEdgeHandle>( 32 );
			var current = edge.Handle;
			const float eps = 0.001f;

			while ( current != HalfEdgeHandle.Invalid )
			{
				mesh.GetVerticesConnectedToEdge( current, out var a, out var b );
				var pa = mesh.GetVertexPosition( a );
				var pb = mesh.GetVertexPosition( b );

				var d = pb - pa;
				var div = S.Vector3.Dot( d, d );
				var t = div < 1e-5f ? 0.0f : (S.Vector3.Dot( d, target ) - S.Vector3.Dot( d, pa )) / div;

				VertexHandle next;
				if ( t > 1 + eps ) next = b;
				else if ( t < -eps ) next = a;
				else if ( t <= eps ) return a;
				else if ( t >= 1 - eps ) return b;
				else
				{
					mesh.AddVertexToEdgeAndUpdateTable( a, b, t, out var v, table );
					return v;
				}

				visited.Add( current );
				var previous = current;
				current = HalfEdgeHandle.Invalid;

				mesh.GetEdgesConnectedToVertex( next, out var edges );
				foreach ( var e in edges )
				{
					if ( !visited.Contains( e ) && mesh.AreEdgesCoLinear( e, previous, 1.0f ) )
					{
						current = e;
						break;
					}
				}
			}

			return VertexHandle.Invalid;
		}

		public override void Cancel()
		{
			_points.Clear();
			Close();
		}
	}
}
