using System;
using System.Collections.Generic;
using System.Linq;
using HalfEdgeMesh;
using UnityEditor;
using UnityEngine;
using S = Sandbox;
using PolygonMesh = Sandbox.PolygonMesh;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Mesh operations bound to Hammer's keys. Each is a port of the matching s&amp;box tool action.
	/// </summary>
	partial class HammerMeshTool
	{
		MeshUndoScope Scope( string name )
		{
			Selection.RecordNow();
			return new( name, Selection.Components );
		}
		MeshUndoScope Scope( string name, IEnumerable<HammerMesh> components )
		{
			Selection.RecordNow();
			return new( name, components );
		}

		float Grid => HammerSettings.GridSize;

		/// <summary>
		/// Remove an object whose mesh has just been pulled into <paramref name="keep"/>, inside an
		/// undo scope. Destroying with undo makes Unity close off the recorded changes there and
		/// then, so the kept mesh is written out first (or its change would be lost from the undo
		/// step) and recorded again for whatever comes next.
		/// </summary>
		static void DestroyMergedObject( HammerMesh keep, GameObject go, string name )
		{
			keep.Commit();
			Undo.DestroyObjectImmediate( go );
			Undo.RecordObject( keep, name );
		}

		void Done()
		{
			_typedKind = DragKind.None;
			Selection.RemoveInvalid();
			HammerViews.RepaintAll();
		}

		// ─────────────────────────────── Common ───────────────────────────────

		public void SelectAll()
		{
			var components = EditMeshes();
			Selection.Clear();

			foreach ( var c in components )
			{
				var mesh = c.Mesh;
				switch ( _mode )
				{
					case EditMode.Vertex:
						foreach ( var v in mesh.VertexHandles ) Selection.Add( new MeshVertex( c, v ) );
						break;
					case EditMode.Edge:
						foreach ( var e in mesh.HalfEdgeHandles )
						{
							var o = mesh.GetOppositeHalfEdge( e );
							if ( o.IsValid && e.Index > o.Index ) continue;
							Selection.Add( new MeshEdge( c, e ) );
						}
						break;
					case EditMode.Face:
						foreach ( var f in mesh.FaceHandles )
						{
							if ( mesh.IsFaceHidden( f ) ) continue;
							Selection.Add( new MeshFace( c, f ) );
						}
						break;
				}
			}

			Done();
		}

		public void InvertSelection()
		{
			var current = Selection.ToHashSet();
			SelectAll();
			foreach ( var e in current ) Selection.Remove( e );
			Done();
		}

		public void Delete()
		{
			switch ( _mode )
			{
				case EditMode.Vertex:
					using ( Scope( "Delete Vertices" ) )
						foreach ( var g in SelectedVertices.GroupBy( x => x.Component ).ToList() )
							g.Key.Mesh.RemoveVertices( g.Select( x => x.Handle ).ToList() );
					break;

				case EditMode.Edge:
					using ( Scope( "Delete Edges" ) )
						foreach ( var g in SelectedEdges.GroupBy( x => x.Component ).ToList() )
							g.Key.Mesh.RemoveEdges( g.Select( x => x.Handle ).ToList() );
					break;

				case EditMode.Face:
					using ( Scope( "Delete Faces" ) )
						foreach ( var g in SelectedFaces.GroupBy( x => x.Component ).ToList() )
							g.Key.Mesh.RemoveFaces( g.Select( x => x.Handle ).ToList() );
					break;

				case EditMode.Object:
					foreach ( var go in UnityEditor.Selection.gameObjects )
						Undo.DestroyObjectImmediate( go );
					break;
			}

			Selection.Clear();
			Done();
		}

		public void SnapToGrid()
		{
			var grid = Grid;
			if ( grid <= 0 ) return;

			using ( Scope( "Snap To Grid" ) )
			{
				foreach ( var v in SelectionVertices() )
				{
					var world = HammerSettings.SnapWorld( v.PositionWorld );
					v.Component.Mesh.SetVertexPosition( v.Handle, v.Component.WorldToSource( world ) );
				}

				foreach ( var c in Selection.Components )
					c.Mesh.ComputeFaceTextureCoordinatesFromParameters();
			}

			Done();
		}

		/// <summary>
		/// Centre the views on the selection (Shift+A, s&amp;box's "Frame Selection"). In the Hammer
		/// window every view centres on it, like Hammer; with no elements selected, frames the
		/// selected objects.
		/// </summary>
		public void FrameSelection()
		{
			Bounds bounds;
			var vertices = _mode is EditMode.Object or EditMode.Primitive or EditMode.Paint ? new HashSet<MeshVertex>() : SelectionVertices();

			if ( vertices.Count > 0 )
			{
				bounds = new Bounds( vertices.First().PositionWorld, Vector3.zero );
				foreach ( var v in vertices ) bounds.Encapsulate( v.PositionWorld );
			}
			else
			{
				var renderers = UnityEditor.Selection.gameObjects.SelectMany( x => x.GetComponentsInChildren<Renderer>() ).ToList();
				if ( renderers.Count == 0 )
				{
					var transforms = UnityEditor.Selection.transforms;
					if ( transforms.Length == 0 ) return;
					bounds = new Bounds( transforms[0].position, Vector3.one );
					foreach ( var t in transforms ) bounds.Encapsulate( t.position );
				}
				else
				{
					bounds = renderers[0].bounds;
					foreach ( var r in renderers ) bounds.Encapsulate( r.bounds );
				}
			}

			bounds.Expand( 0.25f );

			var view = _view ?? HammerViews.Current;
			if ( view?.Window is HammerWindow window ) window.FrameAll( bounds );
			else if ( view?.Frame != null ) view.Frame( bounds );
			else SceneView.lastActiveSceneView?.Frame( bounds, false );
		}

		public void GrowSelection()
		{
			var add = new List<IMeshElement>();

			foreach ( var element in Selection.ToList() )
			{
				if ( !element.IsValid ) continue;
				var c = element.Component;
				var mesh = c.Mesh;

				switch ( element )
				{
					case MeshVertex v:
						mesh.GetEdgesConnectedToVertex( v.Handle, out var edges );
						foreach ( var e in edges )
						{
							mesh.GetEdgeVertices( e, out var a, out var b );
							add.Add( new MeshVertex( c, a ) );
							add.Add( new MeshVertex( c, b ) );
						}
						break;

					case MeshEdge edge:
						mesh.GetEdgeVertices( edge.Handle, out var va, out var vb );
						mesh.GetEdgesConnectedToVertex( va, out var ea );
						mesh.GetEdgesConnectedToVertex( vb, out var eb );
						foreach ( var e in ea.Concat( eb ) ) add.Add( new MeshEdge( c, e ) );
						break;

					case MeshFace face:
						foreach ( var e in mesh.GetFaceEdges( face.Handle ) )
						{
							mesh.GetFacesConnectedToEdge( e, out var fa, out var fb );
							if ( fa.IsValid ) add.Add( new MeshFace( c, fa ) );
							if ( fb.IsValid ) add.Add( new MeshFace( c, fb ) );
						}
						break;
				}
			}

			foreach ( var e in add ) Selection.Add( e );
			Done();
		}

		public void ShrinkSelection()
		{
			var keep = new List<IMeshElement>();
			var selected = Selection.ToHashSet();

			foreach ( var element in selected )
			{
				if ( !element.IsValid ) continue;
				var c = element.Component;
				var mesh = c.Mesh;

				switch ( element )
				{
					case MeshVertex v:
					{
						mesh.GetEdgesConnectedToVertex( v.Handle, out var edges );
						var interior = edges.All( e =>
						{
							mesh.GetEdgeVertices( e, out var a, out var b );
							return selected.Contains( new MeshVertex( c, a ) ) && selected.Contains( new MeshVertex( c, b ) );
						} );
						if ( interior ) keep.Add( v );
						break;
					}

					case MeshEdge edge:
					{
						mesh.GetEdgeVertices( edge.Handle, out var va, out var vb );
						mesh.GetEdgesConnectedToVertex( va, out var ea );
						mesh.GetEdgesConnectedToVertex( vb, out var eb );
						if ( ea.Concat( eb ).All( e => selected.Contains( new MeshEdge( c, e ) ) ) )
							keep.Add( edge );
						break;
					}

					case MeshFace face:
					{
						var interior = true;
						foreach ( var e in mesh.GetFaceEdges( face.Handle ) )
						{
							mesh.GetFacesConnectedToEdge( e, out var fa, out var fb );
							var other = fa == face.Handle ? fb : fa;
							if ( !other.IsValid || !selected.Contains( new MeshFace( c, other ) ) )
							{
								interior = false;
								break;
							}
						}
						if ( interior ) keep.Add( face );
						break;
					}
				}
			}

			Selection.Clear();
			foreach ( var e in keep ) Selection.Add( e );
			Done();
		}

		public void SelectLoop()
		{
			if ( _mode == EditMode.Edge )
			{
				var groups = SelectedEdges.GroupBy( x => x.Component ).ToList();
				Selection.Clear();
				foreach ( var g in groups )
				{
					g.Key.Mesh.FindEdgeLoopForEdges( g.Select( x => x.Handle ).ToArray(), out var loop );
					foreach ( var e in loop ) Selection.Add( new MeshEdge( g.Key, e ) );
				}
			}
			else if ( _mode == EditMode.Face )
			{
				SelectFaceLoop();
			}

			Done();
		}

		public void SelectRing()
		{
			var edges = SelectedEdges.ToList();
			Selection.Clear();
			foreach ( var edge in edges )
			{
				edge.Component.Mesh.FindEdgeRing( edge.Handle, out var ring );
				foreach ( var e in ring ) Selection.Add( new MeshEdge( edge.Component, e ) );
			}

			Done();
		}

		public void SelectRibs()
		{
			var groups = SelectedEdges.GroupBy( x => x.Component ).ToList();
			Selection.Clear();

			foreach ( var g in groups )
			{
				var mesh = g.Key.Mesh;
				mesh.FindEdgeIslands( g.Select( x => x.Handle ).ToArray(), out var islands );
				foreach ( var island in islands )
				{
					var count = mesh.FindEdgeRibs( island, out var left, out var right );
					for ( int i = 0; i < count; i++ )
					{
						foreach ( var e in left[i] ) Selection.Add( new MeshEdge( g.Key, e ) );
						foreach ( var e in right[i] ) Selection.Add( new MeshEdge( g.Key, e ) );
					}
				}
			}

			Done();
		}

		void SelectFaceLoop()
		{
			var faces = SelectedFaces.ToArray();
			var loop = faces.ToHashSet();

			foreach ( var g in faces.GroupBy( x => x.Component ) )
			{
				var mesh = g.Key.Mesh;
				var handles = g.Select( x => x.Handle ).ToHashSet();

				foreach ( var face in g )
				{
					foreach ( var edge in mesh.GetFaceEdges( face.Handle ) )
					{
						mesh.GetFacesConnectedToEdge( edge, out var fa, out var fb );
						var neighbour = fa == face.Handle ? fb : fa;
						if ( !neighbour.IsValid || !handles.Contains( neighbour ) ) continue;
						ExtendFaceLoop( g.Key, face.Handle, neighbour, loop );
					}
				}
			}

			foreach ( var f in loop ) Selection.Add( f );
		}

		static void ExtendFaceLoop( HammerMesh component, FaceHandle current, FaceHandle previous, HashSet<MeshFace> loop )
		{
			var mesh = component.Mesh;
			while ( current.IsValid )
			{
				var edges = mesh.GetFaceEdges( current );
				if ( edges.Length != 4 ) return;

				var incoming = -1;
				for ( var i = 0; i < edges.Length; i++ )
				{
					mesh.GetFacesConnectedToEdge( edges[i], out var fa, out var fb );
					if ( (fa == current && fb == previous) || (fb == current && fa == previous) )
					{
						incoming = i;
						break;
					}
				}

				if ( incoming < 0 ) return;

				mesh.GetFacesConnectedToEdge( edges[(incoming + 2) % 4], out var oa, out var ob );
				var next = oa == current ? ob : oa;
				if ( !next.IsValid || mesh.IsFaceHidden( next ) ) return;
				if ( !loop.Add( new MeshFace( component, next ) ) ) return;

				previous = current;
				current = next;
			}
		}

		// ─────────────────────────────── Vertices ───────────────────────────────

		public void ConnectVertices()
		{
			using ( Scope( "Connect Vertices" ) )
			{
				foreach ( var g in SelectedVertices.GroupBy( x => x.Component ).ToList() )
				{
					var mesh = g.Key.Mesh;
					var selected = g.Select( x => x.Handle ).ToHashSet();
					var pairs = new List<(VertexHandle, VertexHandle)>();

					foreach ( var v in g )
					{
						if ( !mesh.GetFacesConnectedToVertex( v.Handle, out var faces ) ) continue;

						foreach ( var f in faces )
						{
							var fv = mesh.FindFaceVertexConnectedToVertex( v.Handle, f );
							var next = mesh.GetNextVertexInFace( fv );
							while ( next != fv )
							{
								var nv = mesh.GetVertexConnectedToFaceVertex( next );
								if ( selected.Contains( nv ) )
								{
									pairs.Add( (v.Handle, nv) );
									break;
								}
								next = mesh.GetNextVertexInFace( next );
							}
						}
					}

					var changed = false;
					foreach ( var (a, b) in pairs )
						changed |= mesh.ConnectVertices( a, b, out _ );

					if ( changed )
						mesh.ComputeFaceTextureCoordinatesFromParameters();
				}
			}

			Done();
		}

		public void BevelVertices()
		{
			var fit = new List<float>();

			using ( Scope( "Bevel Vertices" ) )
			{
				var groups = SelectedVertices.GroupBy( x => x.Component ).ToList();
				Selection.Clear();
				foreach ( var g in groups )
				{
					var indices = g.Select( x => x.Index ).ToList();
					List<VertexHandle> newVertices = null;
					var used = FitDistance( g.Key, Grid, ( mesh, d ) => mesh.BevelVertices( indices.Select( mesh.VertexHandleFromIndex ).ToArray(), d, out newVertices ) );
					fit.Add( used );
					if ( used <= 0 ) continue;
					foreach ( var v in newVertices ) Selection.Add( new MeshVertex( g.Key, v ) );
				}
			}

			ReportFit( "Bevel", fit );
			Done();
		}

		/// <summary>
		/// The bevel profile: 0.5 is a true quarter circle (1 squares it off, 0 cuts it in).
		/// </summary>
		const float RoundShape = 0.5f;

		/// <summary>
		/// Is the edge between two faces lying in the same plane (a cut across a flat surface)?
		/// </summary>
		static bool IsFlatEdge( PolygonMesh mesh, HalfEdgeHandle edge )
		{
			mesh.GetFacesConnectedToEdge( edge, out var a, out var b );
			if ( !a.IsValid || !b.IsValid ) return false;
			mesh.ComputeFaceNormal( a, out var na );
			mesh.ComputeFaceNormal( b, out var nb );
			return S.Vector3.Dot( na, nb ) > 0.999f;
		}

		/// <summary>
		/// Run an operation that works at a distance (bevels). If it leaves folded or squashed faces
		/// the mesh didn't already have (the distance is more than the faces beside it can take), put
		/// the mesh back and try half the distance, a few times. Returns the distance used, or 0 if
		/// nothing fitted (the mesh is left as it was).
		/// </summary>
		static float FitDistance( HammerMesh component, float distance, Func<PolygonMesh, float, bool> operation )
		{
			var original = component.Mesh;
			var data = original.ToData();
			var broken = original.FindBadFaces( includeNonPlanar: false ).Count;

			for ( int attempt = 0; attempt < 5; attempt++, distance *= 0.5f )
			{
				var mesh = attempt == 0 ? original : Restore();
				bool worked;
				try
				{
					worked = operation( mesh, distance );
				}
				catch ( Exception e )
				{
					// Some shapes trip up the bevel itself (open borders, corners too small for
					// it): treat it like not fitting, put the mesh back and try smaller
					Debug.LogWarning( $"Hammer: {e.GetType().Name} in a bevel, tried smaller: {e.Message}" );
					worked = false;
				}
				if ( worked && mesh.FindBadFaces( includeNonPlanar: false ).Count <= broken )
					return distance;
			}

			Restore();
			return 0;

			PolygonMesh Restore()
			{
				component.Mesh = PolygonMesh.FromData( data );
				return component.Mesh;
			}
		}

		/// <summary>
		/// Tell the user when an operation had to shrink to fit, or couldn't fit at all.
		/// </summary>
		void ReportFit( string operation, List<float> used )
		{
			if ( used.Count == 0 ) return;
			if ( used.All( x => x <= 0 ) )
				MeshHealth.Report( $"⚠ {operation} doesn't fit: it's wider than the faces next to it. Try a smaller grid size." );
			else if ( used.Any( x => x <= 0 || x < Grid - 1e-4f ) )
				MeshHealth.Report( $"⚠ {operation} was narrowed to {used.Where( x => x > 0 ).Min():0.##} to fit the faces next to it." );
		}

		public void MergeVertices()
		{
			// Merge's Range: within a distance instead of all into one
			if ( !HammerSettings.MergeInfinite && SelectedVertices.Select( x => x.Component ).Distinct().Count() == 1 )
			{
				MergeVerticesWithinDistance();
				return;
			}

			var vertices = SelectedVertices.ToList();
			if ( vertices.Count < 2 ) return;

			var components = vertices.Select( x => x.Component ).Distinct().ToList();
			var target = components[0];
			var handles = new List<VertexHandle>();

			using ( Scope( "Merge Vertices", components ) )
			{
				foreach ( var g in vertices.GroupBy( x => x.Component ) )
				{
					if ( g.Key == target )
					{
						handles.AddRange( g.Select( x => x.Handle ) );
						continue;
					}

					// Pull the other mesh in, like s&box does, then remove its object
					var transform = target.WorldTransform.ToLocal( g.Key.WorldTransform );
					target.Mesh.MergeMesh( g.Key.Mesh, transform, out var remap, out _, out _ );
					handles.AddRange( g.Select( x => remap[x.Handle] ) );
					DestroyMergedObject( target, g.Key.gameObject, "Merge Vertices" );
				}

				// Removing the other objects closed off the selection's recording
				Selection.RecordNow( force: true );
				Selection.Clear();

				// A distance of -1 collapses all the vertices to their centre
				if ( target.Mesh.MergeVerticesWithinDistance( handles, -1.0f, true, false, out var final ) > 0 )
				{
					foreach ( var v in final ) Selection.Add( new MeshVertex( target, v ) );
					target.Mesh.ComputeFaceTextureCoordinatesFromParameters();
				}
			}

			Done();
		}

		public void SnapToLastVertex()
		{
			var vertices = SelectedVertices.ToList();
			if ( vertices.Count < 2 ) return;

			using ( Scope( "Snap Vertices" ) )
			{
				var position = vertices[^1].PositionWorld;
				foreach ( var v in vertices )
					v.Component.Mesh.SetVertexPosition( v.Handle, v.Component.WorldToSource( position ) );
			}

			Done();
		}

		public void WeldUVs()
		{
			using ( Scope( "Weld UVs" ) )
			{
				if ( _mode == EditMode.Vertex )
				{
					foreach ( var g in SelectedVertices.GroupBy( x => x.Component ).ToList() )
						g.Key.Mesh.AverageVertexUVs( g.Select( x => x.Handle ).ToList() );
				}
				else if ( _mode == EditMode.Edge )
				{
					foreach ( var g in SelectedEdges.GroupBy( x => x.Component ).ToList() )
						g.Key.Mesh.AverageEdgeUVs( g.Select( x => x.Handle ).ToList() );
				}
			}

			Done();
		}

		// ─────────────────────────────── Edges ───────────────────────────────

		public void QuickBevelEdges()
		{
			var fit = new List<float>();

			using ( Scope( "Bevel Edges" ) )
			{
				var groups = SelectedEdges.GroupBy( x => x.Component ).ToList();
				Selection.Clear();

				foreach ( var g in groups )
				{
					var indices = g.Select( x => x.Index ).ToList();


					// Segments round off a corner; across a flat surface they'd only add extra edges
					// in a straight line, so flat edges always get one (like Hammer)
					var segments = g.All( x => IsFlatEdge( g.Key.Mesh, x.Handle ) ) ? 1 : HammerSettings.BevelSegments;
					List<HalfEdgeHandle> outer = null, inner = null;
					List<FaceHandle> newFaces = null, needUVs = null;

					var used = FitDistance( g.Key, Grid, ( m, d ) =>
					{
						outer = new(); inner = new(); newFaces = new(); needUVs = new();
						return m.BevelEdges( indices.Select( m.HalfEdgeHandleFromIndex ).ToList(), PolygonMesh.BevelEdgesMode.RemoveClosedEdges, segments, d, RoundShape, outer, inner, newFaces, needUVs );
					} );
					fit.Add( used );
					if ( used <= 0 ) continue;

					var mesh = g.Key.Mesh;

					foreach ( var e in inner ) mesh.SetEdgeSmoothing( e, PolygonMesh.EdgeSmoothMode.Default );
					foreach ( var f in needUVs ) mesh.TextureAlignToGrid( mesh.Transform, f );
					mesh.ComputeFaceTextureParametersFromCoordinates( newFaces );

					// Like Hammer: the bevel's outline stays selected (the edges along it and the
					// short ones across its ends), not the in-between segment edges
					foreach ( var e in outer ) Selection.Add( new MeshEdge( g.Key, e ) );
				}
			}

			ReportFit( "Bevel", fit );
			Done();
		}

		public void ConnectEdges()
		{
			using ( Scope( "Connect Edges" ) )
			{
				var groups = SelectedEdges.GroupBy( x => x.Component ).ToList();
				Selection.Clear();
				foreach ( var g in groups )
				{
					var mesh = g.Key.Mesh;
					mesh.ConnectEdges( g.Select( x => x.Handle ).ToArray(), out var newEdges );
					if ( newEdges != null )
						foreach ( var e in newEdges ) Selection.Add( new MeshEdge( g.Key, e ) );
					mesh.ComputeFaceTextureCoordinatesFromParameters();
				}
			}

			Done();
		}

		public void ExtendEdges()
		{
			using ( Scope( "Extend Edges" ) )
			{
				var groups = SelectedEdges.GroupBy( x => x.Component ).ToList();
				Selection.Clear();
				foreach ( var g in groups )
				{
					if ( !g.Key.Mesh.ExtendEdges( g.Select( x => x.Handle ).ToArray(), Grid, out var newEdges, out _ ) )
						continue;
					if ( newEdges != null )
						foreach ( var e in newEdges ) Selection.Add( new MeshEdge( g.Key, e ) );
				}
			}

			Done();
		}

		public void MergeEdges()
		{
			var edges = SelectedEdges.ToList();
			if ( edges.Count != 2 || edges.Any( x => !x.IsOpen ) || edges[0].Component != edges[1].Component )
				return;

			using ( Scope( "Merge Edges" ) )
			{
				var mesh = edges[0].Component.Mesh;
				if ( mesh.MergeEdges( edges[0].Handle, edges[1].Handle, out var merged ) )
					Selection.Set( new MeshEdge( edges[0].Component, merged ) );
			}

			Done();
		}

		public void SplitEdges()
		{
			using ( Scope( "Split Edges" ) )
			{
				var groups = SelectedEdges.GroupBy( x => x.Component ).ToList();
				Selection.Clear();
				foreach ( var g in groups )
				{
					g.Key.Mesh.SplitEdges( g.Select( x => x.Handle ).ToArray(), out var a, out var b );
					if ( a != null ) foreach ( var e in a ) Selection.Add( new MeshEdge( g.Key, e ) );
					if ( b != null ) foreach ( var e in b ) Selection.Add( new MeshEdge( g.Key, e ) );
				}
			}

			Done();
		}

		public void BridgeEdges()
		{
			var edges = SelectedEdges.ToList();
			if ( edges.Count < 2 || edges.Any( x => !x.IsOpen ) ) return;

			var groups = edges.GroupBy( x => x.Component ).ToList();
			if ( groups.Count != 1 ) return;

			using ( Scope( "Bridge Edges" ) )
			{
				var mesh = groups[0].Key.Mesh;
				var handles = groups[0].Select( x => x.Handle ).ToList();
				mesh.FindOpenEdgeIslands( handles, out var islands );

				if ( islands.Count == 2 )
					mesh.BridgeEdges( islands[0], islands[1] );
				else if ( handles.Count == 2 )
					mesh.BridgeEdges( handles[0], handles[1], out _ );
			}

			Done();
		}

		public void DissolveEdges()
		{
			using ( Scope( "Dissolve Edges" ) )
			{
				var groups = SelectedEdges.GroupBy( x => x.Component ).ToList();
				Selection.Clear();
				foreach ( var g in groups )
				{
					g.Key.Mesh.DissolveEdges( g.Select( x => x.Handle ).ToArray(), false, PolygonMesh.DissolveRemoveVertexCondition.InteriorOrColinear );
					g.Key.Mesh.ComputeFaceTextureCoordinatesFromParameters();
				}
			}

			Done();
		}

		public void Collapse()
		{
			if ( _mode == EditMode.Edge )
			{
				using ( Scope( "Collapse Edges" ) )
				{
					var groups = SelectedEdges.GroupBy( x => x.Component ).ToList();
					Selection.Clear();
					foreach ( var g in groups )
						g.Key.Mesh.CollapseEdges( g.Select( x => x.Handle ).ToArray() );
				}
			}
			else if ( _mode == EditMode.Face )
			{
				using ( Scope( "Collapse Faces" ) )
				{
					var faces = SelectedFaces.ToList();
					Selection.Clear();
					foreach ( var f in faces )
					{
						if ( f.IsValid )
							f.Component.Mesh.CollapseFace( f.Handle, out _ );
					}
				}
			}

			Done();
		}

		public void FillHole()
		{
			using ( Scope( "Fill Hole" ) )
			{
				foreach ( var e in SelectedEdges.ToList() )
				{
					if ( e.IsValid && e.IsOpen )
						e.Component.Mesh.CreateFaceInEdgeLoop( e.Handle, out _ );
				}
			}

			Done();
		}

		public void SetEdgeNormals( PolygonMesh.EdgeSmoothMode mode )
		{
			using ( Scope( "Set Normals" ) )
			{
				foreach ( var e in SelectedEdges.ToList() )
					e.Component.Mesh.SetEdgeSmoothing( e.Handle, mode );
			}

			Done();
		}

		public void SnapEdgeToEdge()
		{
			var edges = SelectedEdges.ToList();
			if ( edges.Count != 2 ) return;

			using ( Scope( "Snap Edges" ) )
			{
				var a = edges[0];
				var b = edges[1];

				b.Component.Mesh.GetEdgeVertices( b.Handle, out var bA, out var bB );
				var targetA = b.Component.SourceToWorld( b.Component.Mesh.GetVertexPosition( bA ) );
				var targetB = b.Component.SourceToWorld( b.Component.Mesh.GetVertexPosition( bB ) );

				a.Component.Mesh.GetEdgeVertices( a.Handle, out var aA, out var aB );
				var currentA = a.Component.SourceToWorld( a.Component.Mesh.GetVertexPosition( aA ) );
				var currentB = a.Component.SourceToWorld( a.Component.Mesh.GetVertexPosition( aB ) );

				if ( Vector3.Dot( currentB - currentA, targetB - targetA ) < 0 )
					(targetA, targetB) = (targetB, targetA);

				a.Component.Mesh.SetVertexPosition( aA, a.Component.WorldToSource( targetA ) );
				a.Component.Mesh.SetVertexPosition( aB, a.Component.WorldToSource( targetB ) );
			}

			Done();
		}

		// ─────────────────────────────── Faces ───────────────────────────────

		public void InsetFaces()
		{
			using ( Scope( "Inset Faces" ) )
			{
				var groups = SelectedFaces.GroupBy( x => x.Component ).ToList();
				Selection.Clear();
				foreach ( var g in groups )
				{
					if ( !g.Key.Mesh.InsetFaces( g.Select( x => x.Handle ).ToArray(), Grid, 0, out var result ) )
						continue;
					foreach ( var f in result.InsetFaces ) Selection.Add( new MeshFace( g.Key, f ) );
				}
			}

			Done();
		}

		public void ExtrudeFaces()
		{
			using ( Scope( "Extrude Faces" ) )
			{
				var groups = SelectedFaces.GroupBy( x => x.Component ).ToList();
				Selection.Clear();
				foreach ( var g in groups )
				{
					var mesh = g.Key.Mesh;
					var faces = g.Select( x => x.Handle ).ToArray();

					// Extrude along the average normal by one grid unit
					var normal = S.Vector3.Zero;
					foreach ( var f in faces )
					{
						mesh.ComputeFaceNormal( f, out var n );
						normal += n;
					}

					normal = normal.Normal * Grid;
					mesh.ExtrudeFaces( faces, out var newFaces, out var connecting, normal );

					foreach ( var f in connecting )
					{
						if ( !mesh.TextureWrapFromNeighbour( f ) )
							mesh.TextureAlignToGrid( mesh.Transform, f );
					}

					mesh.ComputeFaceTextureCoordinatesFromParameters();
					foreach ( var f in newFaces ) Selection.Add( new MeshFace( g.Key, f ) );
				}
			}

			Done();
		}

		public void DetachFaces()
		{
			using ( Scope( "Detach Faces" ) )
			{
				var groups = SelectedFaces.GroupBy( x => x.Component ).ToList();
				Selection.Clear();
				foreach ( var g in groups )
				{
					g.Key.Mesh.DetachFaces( g.Select( x => x.Handle ).ToArray(), out var newFaces );
					foreach ( var f in newFaces ) Selection.Add( new MeshFace( g.Key, f ) );
				}
			}

			Done();
		}

		public void CombineFaces()
		{
			using ( Scope( "Combine Faces" ) )
			{
				var groups = SelectedFaces.GroupBy( x => x.Component ).ToList();
				Selection.Clear();
				foreach ( var g in groups )
				{
					g.Key.Mesh.CombineFaces( g.Select( x => x.Handle ).ToArray() );
					g.Key.Mesh.ComputeFaceTextureCoordinatesFromParameters();
				}
			}

			Done();
		}

		public void FlipFaces()
		{
			var components = _mode == EditMode.Object
				? UnityEditor.Selection.gameObjects.SelectMany( x => x.GetComponentsInChildren<HammerMesh>() ).ToList()
				: Selection.Components.ToList();

			using ( Scope( "Flip Faces", components ) )
			{
				foreach ( var c in components )
					c.Mesh.FlipAllFaces();
			}

			Done();
		}

		public void ThickenFaces()
		{
			using ( Scope( "Thicken Faces" ) )
			{
				var groups = SelectedFaces.GroupBy( x => x.Component ).ToList();
				Selection.Clear();
				foreach ( var g in groups )
				{
					g.Key.Mesh.ThickenFaces( g.Select( x => x.Handle ).ToList(), Grid, out var newFaces );
					g.Key.Mesh.ComputeFaceTextureCoordinatesFromParameters();
					foreach ( var f in newFaces ) Selection.Add( new MeshFace( g.Key, f ) );
				}
			}

			Done();
		}

		public void QuadSlice()
		{
			var cuts = HammerSettings.QuadSliceCuts;

			using ( Scope( "Quad Slice" ) )
			{
				var groups = SelectedFaces.GroupBy( x => x.Component ).ToList();
				Selection.Clear();
				foreach ( var g in groups )
				{
					var newFaces = new List<FaceHandle>();
					g.Key.Mesh.QuadSliceFaces( g.Select( x => x.Handle ).ToList(), cuts.x, cuts.y, 60.0f, newFaces );
					g.Key.Mesh.ComputeFaceTextureCoordinatesFromParameters();
					foreach ( var f in newFaces ) Selection.Add( new MeshFace( g.Key, f ) );
				}
			}

			Done();
		}

		public void HideFaces()
		{
			var faces = SelectedFaces.ToList();
			if ( faces.Count == 0 ) return;

			// Hidden faces are kept with the scene (editing only: the game shows them), and undoable
			var components = faces.Select( x => x.Component ).Distinct().ToArray();
			Undo.RecordObjects( components, "Hide Faces" );
			foreach ( var f in faces ) f.Component.Mesh.SetFaceHidden( f.Handle, true );
			foreach ( var c in components ) { c.Commit(); EditorUtility.SetDirty( c ); }

			Selection.Clear();
			Done();
		}

		public void UnhideFaces()
		{
			foreach ( var c in UnityEngine.Object.FindObjectsByType<HammerMesh>( FindObjectsSortMode.None ) )
			{
				if ( !c.Mesh.HasHiddenFaces ) continue;
				Undo.RecordObject( c, "Unhide Faces" );
				c.Mesh.SetHiddenFaceIndices( System.Array.Empty<int>() );
				c.Commit();
				EditorUtility.SetDirty( c );
			}

			Done();
		}

		public void ApplyMaterial()
		{
			var material = HammerMaterials.Get( HammerSettings.ActiveMaterial );

			using ( Scope( "Apply Material" ) )
			{
				foreach ( var g in SelectedFaces.GroupBy( x => x.Component ).ToList() )
				{
					g.Key.Mesh.AssignMaterialToFaces( g.Select( x => x.Handle ).ToList(), material );
					// Keep texel density right for the new texture size
					g.Key.Mesh.ComputeFaceTextureCoordinatesFromParameters( g.Select( x => x.Handle ).ToList() );
				}
			}

			Done();
		}

		/// <summary>
		/// Pick up the material of the face under the cursor as the active material (Shift+RMB in s&amp;box).
		/// </summary>
		public void LiftMaterial( Vector2 mouse )
		{
			if ( !MeshPicking.PickFace( mouse, MeshPicking.VisibleMeshes(), out var hit ) )
				return;

			// The built-in dev grid isn't an asset: it's the "no material" choice
			if ( hit.Face.Material?.Name == HammerMaterials.DefaultKey )
			{
				HammerSettings.ActiveMaterial = null;
				return;
			}

			var material = hit.Face.Material?.Asset;
			if ( material != null && AssetDatabase.Contains( material ) )
				HammerSettings.ActiveMaterial = material;
		}

		public void TextureAlignToGrid()
		{
			using ( Scope( "Align Texture To Grid" ) )
			{
				foreach ( var f in SelectedFaces.ToList() )
					f.Component.Mesh.TextureAlignToGrid( f.Component.Mesh.Transform, f.Handle );
				foreach ( var c in Selection.Components )
					c.Mesh.ComputeFaceTextureCoordinatesFromParameters();
			}

			Done();
		}

		public void TextureAlignToFace()
		{
			using ( Scope( "Align Texture To Face" ) )
			{
				foreach ( var f in SelectedFaces.ToList() )
					f.Component.Mesh.TextureAlignToFace( f.Component.Mesh.Transform, f.Handle );
				foreach ( var c in Selection.Components )
					c.Mesh.ComputeFaceTextureCoordinatesFromParameters();
			}

			Done();
		}

		public void ShiftTexture( Vector2 offset )
		{
			using ( Scope( "Shift Texture" ) )
			{
				foreach ( var f in SelectedFaces.ToList() )
				{
					var mesh = f.Component.Mesh;
					mesh.SetTextureOffset( f.Handle, mesh.GetTextureOffset( f.Handle ) + new S.Vector2( offset.x, offset.y ) );
				}
				foreach ( var c in Selection.Components )
					c.Mesh.ComputeFaceTextureCoordinatesFromParameters();
			}

			Done();
		}

		public void ScaleTexture( float factor )
		{
			using ( Scope( "Scale Texture" ) )
			{
				foreach ( var f in SelectedFaces.ToList() )
				{
					var mesh = f.Component.Mesh;
					mesh.SetTextureScale( f.Handle, mesh.GetTextureScale( f.Handle ) * factor );
				}
				foreach ( var c in Selection.Components )
					c.Mesh.ComputeFaceTextureCoordinatesFromParameters();
			}

			Done();
		}

		/// <summary>
		/// Rotate the texture on each selected face around the face normal.
		/// </summary>
		public void RotateTexture( float degrees )
		{
			using ( Scope( "Rotate Texture" ) )
			{
				foreach ( var f in SelectedFaces.ToList() )
				{
					var mesh = f.Component.Mesh;
					mesh.ComputeFaceNormal( f.Handle, out var localNormal );
					var normal = mesh.Transform.Rotation * localNormal;
					var rotation = S.Rotation.FromAxis( normal, degrees );

					mesh.GetFaceTextureParameters( f.Handle, out var u, out var v, out var scale );
					var newU = rotation * new S.Vector3( u.x, u.y, u.z );
					var newV = rotation * new S.Vector3( v.x, v.y, v.z );
					mesh.SetFaceTextureParameters( f.Handle, new S.Vector4( newU.x, newU.y, newU.z, u.w ), new S.Vector4( newV.x, newV.y, newV.z, v.w ), scale );
				}
			}

			Done();
		}

		/// <summary>
		/// Set offset and scale directly on every selected face (the face properties panel).
		/// </summary>
		public void SetTextureOffsetScale( Vector2? offset, Vector2? scale )
		{
			using ( Scope( "Edit Texture" ) )
			{
				foreach ( var f in SelectedFaces.ToList() )
				{
					var mesh = f.Component.Mesh;
					if ( offset.HasValue ) mesh.SetTextureOffset( f.Handle, new S.Vector2( offset.Value.x, offset.Value.y ) );
					if ( scale.HasValue ) mesh.SetTextureScale( f.Handle, new S.Vector2( scale.Value.x, scale.Value.y ) );
				}

				foreach ( var c in Selection.Components )
					c.Mesh.ComputeFaceTextureCoordinatesFromParameters();
			}

			Done();
		}

		public void JustifyTexture( PolygonMesh.TextureJustification justification )
		{
			using ( Scope( "Justify Texture" ) )
			{
				foreach ( var g in SelectedFaces.GroupBy( x => x.Component ).ToList() )
				{
					var mesh = g.Key.Mesh;
					var faces = g.Select( x => x.Handle ).ToList();
					var extents = new PolygonMesh.FaceExtents();
					mesh.UnionExtentsForFaces( faces, mesh.Transform, extents );
					mesh.JustifyFaceTextureParameters( faces, justification, extents );
					mesh.ComputeFaceTextureCoordinatesFromParameters();
				}
			}

			Done();
		}

		/// <summary>
		/// Move the selected faces into a new object (Alt+N).
		/// </summary>
		public void ExtractFaces()
		{
			var groups = SelectedFaces.GroupBy( x => x.Component ).ToList();
			if ( groups.Count == 0 ) return;

			var created = new List<GameObject>();

			using ( Scope( "Extract Faces" ) )
			{
				foreach ( var g in groups )
				{
					var source = g.Key;
					var keep = g.Select( x => x.Index ).ToHashSet();

					var copy = CloneMesh( source, source.name );
					var copyMesh = copy.Mesh;
					copyMesh.RemoveFaces( copyMesh.FaceHandles.Where( f => !keep.Contains( f.Index ) ).ToList() );
					RecenterOrigin( copy );
					copy.Commit();
					created.Add( copy.gameObject );

					if ( keep.Count == source.Mesh.FaceHandles.Count() )
						Undo.DestroyObjectImmediate( source.gameObject );
					else
					{
						// Making the copy closed off the undo recording of the source (before it
						// changed), so record it again or taking the faces out couldn't be undone
						Undo.RecordObject( source, "Extract Faces" );
						source.Mesh.RemoveFaces( g.Select( x => x.Handle ).ToList() );
						// Written out now: the next group's copy closes off the recording again
						source.Commit();
					}
				}
			}

			// Making the copies closed off the selection's recording: record it again
			Selection.RecordNow( force: true );
			Selection.Clear();
			foreach ( var go in created )
			{
				var c = go.GetComponent<HammerMesh>();
				foreach ( var f in c.Mesh.FaceHandles ) Selection.Add( new MeshFace( c, f ) );
			}

			UnityEditor.Selection.objects = created.ToArray();
			Done();
		}

		sealed class FaceClipboard
		{
			public S.Vector3[] Vertices;
			public (int[] Indices, string Material, S.Vector4 AxisU, S.Vector4 AxisV, S.Vector2 Scale)[] Faces;
			public S.Transform Source;
		}

		static FaceClipboard _clipboard;

		/// <summary>
		/// Ctrl+C: copy the selected faces with their materials and texturing.
		/// </summary>
		public void CopyFaces()
		{
			var faces = SelectedFaces.ToList();
			if ( faces.Count == 0 ) return;

			var source = faces[0].Component;
			var vertices = new List<S.Vector3>();
			var index = new Dictionary<S.Vector3, int>();
			var data = new List<(int[], string, S.Vector4, S.Vector4, S.Vector2)>();

			foreach ( var f in faces )
			{
				var mesh = f.Component.Mesh;
				// Everything is stored relative to the first object so multi-object copies keep their layout
				var toSource = source.WorldTransform.ToLocal( f.Component.WorldTransform );
				var indices = new List<int>();

				foreach ( var v in mesh.GetFaceVertices( f.Handle ) )
				{
					var p = toSource.PointToWorld( mesh.GetVertexPosition( v ) );
					if ( !index.TryGetValue( p, out var i ) )
					{
						i = vertices.Count;
						vertices.Add( p );
						index[p] = i;
					}
					indices.Add( i );
				}

				mesh.GetFaceTextureParameters( f.Handle, out var u, out var vAxis, out var scale );
				data.Add( (indices.ToArray(), f.Material?.Name, u, vAxis, scale) );
			}

			_clipboard = new FaceClipboard { Vertices = vertices.ToArray(), Faces = data.ToArray(), Source = source.WorldTransform };
		}

		/// <summary>
		/// Ctrl+V: paste copied faces into the first selected mesh, in place.
		/// </summary>
		public void PasteFaces()
		{
			if ( _clipboard == null ) return;

			var target = Selection.Components.FirstOrDefault() ?? EditMeshes().FirstOrDefault();
			if ( target == null ) return;

			using ( Scope( "Paste Faces", new[] { target } ) )
			{
				var mesh = target.Mesh;
				var toTarget = target.WorldTransform.ToLocal( _clipboard.Source );
				var vertices = mesh.AddVertices( _clipboard.Vertices.Select( x => toTarget.PointToWorld( x ) ).ToArray() );
				Selection.Clear();

				foreach ( var face in _clipboard.Faces )
				{
					if ( face.Indices.Length < 3 ) continue;
					var handle = mesh.AddFace( face.Indices.Select( i => vertices[i] ).ToArray() );
					if ( !handle.IsValid ) continue;

					mesh.SetFaceMaterial( handle, string.IsNullOrEmpty( face.Material ) ? null : S.Material.Load( face.Material ) );
					mesh.SetFaceTextureParameters( handle, face.AxisU, face.AxisV, face.Scale );
					Selection.Add( new MeshFace( target, handle ) );
				}

				mesh.ComputeFaceTextureCoordinatesFromParameters();
			}

			Done();
		}

		// ─────────────────────────────── Objects ───────────────────────────────

		// ─────────────────────────────── Mesh health ───────────────────────────────

		/// <summary>
		/// Meshes being worked on: the selected objects in Meshes mode, the meshes with selected
		/// elements otherwise.
		/// </summary>
		public List<HammerMesh> HealthMeshes() => _mode == EditMode.Object ? SelectedObjectMeshes().ToList() : EditMeshes();

		public void RemoveBrokenFaces()
		{
			var meshes = HealthMeshes();
			using ( Scope( "Remove Broken Faces", meshes ) )
			{
				Selection.Clear();
				foreach ( var c in meshes ) c.Mesh.RemoveBrokenFaces();
			}
			Done();
		}

		/// <summary>
		/// Flatten bent faces: the selected faces in face mode, otherwise every bent face.
		/// </summary>
		public void MakePlanar()
		{
			var meshes = HealthMeshes();
			using ( Scope( "Make Faces Planar", meshes ) )
			{
				foreach ( var c in meshes )
				{
					var faces = _mode == EditMode.Face && SelectedFaces.Any( f => f.Component == c )
						? SelectedFaces.Where( f => f.Component == c ).Select( f => f.Handle )
						: c.Mesh.FaceHandles;
					c.Mesh.MakeFacesPlanar( faces );
					c.Mesh.ComputeFaceTextureCoordinatesFromParameters();
				}
			}
			Done();
		}

		public void WeldVertices()
		{
			var meshes = HealthMeshes();
			using ( Scope( "Weld Vertices", meshes ) )
			{
				Selection.Clear();
				foreach ( var c in meshes ) c.Mesh.WeldCoincidentVertices();
			}
			Done();
		}

		/// <summary>
		/// Everything at once: weld doubled vertices, flatten bent faces, drop broken ones.
		/// </summary>
		public void CleanUp()
		{
			var meshes = HealthMeshes();
			using ( Scope( "Clean Up Mesh", meshes ) )
			{
				Selection.Clear();
				foreach ( var c in meshes )
				{
					c.Mesh.WeldCoincidentVertices();
					c.Mesh.MakeFacesPlanar( c.Mesh.FaceHandles );
					c.Mesh.RemoveBrokenFaces();
					c.Mesh.ComputeFaceTextureCoordinatesFromParameters();
				}
			}
			Done();
		}

		IEnumerable<HammerMesh> SelectedObjectMeshes() =>
			UnityEditor.Selection.gameObjects.Select( x => x.GetComponent<HammerMesh>() ).Where( x => x != null );

		/// <summary>
		/// Combine the selected meshes into the first one (M in object mode).
		/// </summary>
		public void MergeMeshes()
		{
			var meshes = SelectedObjectMeshes().ToList();
			if ( meshes.Count < 2 ) return;

			var target = UnityEditor.Selection.activeGameObject?.GetComponent<HammerMesh>() ?? meshes[0];

			using ( Scope( "Merge Meshes", new[] { target } ) )
			{
				foreach ( var other in meshes.Where( x => x != target ) )
				{
					var transform = target.WorldTransform.ToLocal( other.WorldTransform );
					target.Mesh.MergeMesh( other.Mesh, transform, out _, out _, out _ );
					DestroyMergedObject( target, other.gameObject, "Merge Meshes" );
				}
			}

			UnityEditor.Selection.activeGameObject = target.gameObject;
			Done();
		}

		/// <summary>
		/// Split each selected mesh into one object per connected piece (Alt+N in object mode).
		/// </summary>
		public void SeparateComponents()
		{
			var created = new List<GameObject>();

			foreach ( var source in SelectedObjectMeshes().ToList() )
			{
				var mesh = source.Mesh;
				mesh.FindFaceIslands( mesh.FaceHandles.ToList(), out var islands );

				if ( islands.Count <= 1 )
				{
					created.Add( source.gameObject );
					continue;
				}

				foreach ( var island in islands )
				{
					var keep = island.Select( x => x.Index ).ToHashSet();
					var copy = CloneMesh( source, source.name );
					copy.Mesh.RemoveFaces( copy.Mesh.FaceHandles.Where( f => !keep.Contains( f.Index ) ).ToList() );
					RecenterOrigin( copy );
					copy.Commit();
					created.Add( copy.gameObject );
				}

				Undo.DestroyObjectImmediate( source.gameObject );
			}

			UnityEditor.Selection.objects = created.ToArray();
			Done();
		}

		/// <summary>
		/// Boolean the other selected meshes into the active one, removing them.
		/// </summary>
		public void Boolean( S.PolygonMesh.BooleanOperation operation )
		{
			var meshes = SelectedObjectMeshes().ToList();
			var target = UnityEditor.Selection.activeGameObject?.GetComponent<HammerMesh>();
			if ( target == null || meshes.Count < 2 ) return;

			using ( Scope( $"Boolean {operation}", new[] { target } ) )
			{
				foreach ( var other in meshes.Where( x => x != target ) )
				{
					var relative = target.WorldTransform.ToLocal( other.WorldTransform );
					if ( target.Mesh.PerformBoolean( other.Mesh, relative, operation ) )
						DestroyMergedObject( target, other.gameObject, $"Boolean {operation}" );
				}

				target.Mesh.ComputeFaceTextureCoordinatesFromParameters();
			}

			UnityEditor.Selection.activeGameObject = target.gameObject;
			Done();
		}

		/// <summary>
		/// Arrow keys: move the selection one grid step along the view's closest axes.
		/// </summary>
		public void Nudge( Vector2 direction )
		{
			var camera = (_view ?? HammerViews.Current)?.Camera ?? SceneView.lastActiveSceneView?.camera;
			if ( camera == null ) return;

			var right = NearestAxis( camera.transform.right );
			var up = NearestAxis( camera.transform.up );
			var step = HammerSettings.GridSize * SourceSpace.UnitScale;
			var delta = (right * direction.x + up * direction.y) * step;

			if ( _mode == EditMode.Object )
			{
				Undo.RecordObjects( UnityEditor.Selection.transforms, "Nudge" );
				foreach ( var t in UnityEditor.Selection.transforms )
					t.position += delta;
				return;
			}

			using ( Scope( "Nudge" ) )
			{
				foreach ( var v in SelectionVertices() )
					v.Component.Mesh.SetVertexPosition( v.Handle, v.Component.WorldToSource( v.PositionWorld + delta ) );

				foreach ( var c in Selection.Components )
				{
					if ( HammerSettings.TextureLockComponents )
						c.Mesh.ComputeFaceTextureParametersFromCoordinates();
					else
						c.Mesh.ComputeFaceTextureCoordinatesFromParameters();
				}
			}

			Done();
		}

		public void CenterOrigin()
		{
			var meshes = SelectedObjectMeshes().ToList();
			if ( meshes.Count == 0 ) return;

			foreach ( var c in meshes )
			{
				Undo.RecordObjects( new UnityEngine.Object[] { c, c.transform }, "Center Origin" );
				RecenterOrigin( c );
				c.Commit();
			}

			Done();
		}

		/// <summary>
		/// Move the object's origin to the centre of its geometry without moving the geometry.
		/// </summary>
		static void RecenterOrigin( HammerMesh c )
		{
			var mesh = c.Mesh;
			var bounds = mesh.CalculateBounds();
			var center = bounds.Center;
			var worldCenter = c.SourceToWorld( center );

			mesh.ApplyTransform( new S.Transform( -center ) );
			c.transform.position = worldCenter;
			mesh.SetTransform( c.WorldTransform );
		}

		/// <summary>
		/// Duplicate a mesh object (with undo), keeping its transform and settings.
		/// </summary>
		static HammerMesh CloneMesh( HammerMesh source, string name )
		{
			var go = UnityEngine.Object.Instantiate( source.gameObject, source.transform.parent );
			go.name = GameObjectUtility.GetUniqueNameForSibling( source.transform.parent, name );
			Undo.RegisterCreatedObjectUndo( go, "Create Mesh" );

			var copy = go.GetComponent<HammerMesh>();
			copy.Mesh = S.PolygonMesh.FromData( source.Mesh.ToData() );
			return copy;
		}
	}
}
