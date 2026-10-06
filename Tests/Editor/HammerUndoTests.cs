using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HammerUnity.EditorTools;
using NUnit.Framework;
using Sandbox.Primitives;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using S = Sandbox;

namespace HammerUnity.Tests
{
	/// <summary>
	/// Every edit must undo back to exactly the shape before it, and redo to exactly the shape
	/// after it, one step at a time, however many edits came before.
	/// </summary>
	public class HammerUndoTests
	{
		HammerMeshTool _tool;
		HammerMesh _c;
		float _grid;
		int _segments;

		[SetUp]
		public void SetUp()
		{
			_grid = HammerSettings.GridSize;
			_segments = HammerSettings.BevelSegments;
			HammerSettings.GridSize = 8;
			HammerSettings.BevelSegments = 1;

			Undo.ClearAll();
			_tool = ScriptableObject.CreateInstance<HammerMeshTool>();

			var go = new GameObject( "Undo Box" );
			_c = go.AddComponent<HammerMesh>();
			_c.Mesh = new BlockPrimitive().CreateMesh( new S.BBox( new S.Vector3( -64 ), new S.Vector3( 64 ) ) );
			Undo.IncrementCurrentGroup();
		}

		[TearDown]
		public void TearDown()
		{
			if ( _c != null ) Object.DestroyImmediate( _c.gameObject );
			Object.DestroyImmediate( _tool );
			Undo.ClearAll();
			HammerSettings.GridSize = _grid;
			HammerSettings.BevelSegments = _segments;
		}

		/// <summary>
		/// The mesh as text: face count and every vertex position, rounded and sorted.
		/// </summary>
		string Shape()
		{
			_c.RebuildIfReloaded();
			var mesh = _c.Mesh;
			var verts = mesh.VertexHandles.Select( v => mesh.GetVertexPosition( v ) ).Select( p => $"{p.x:0.###},{p.y:0.###},{p.z:0.###}" ).OrderBy( x => x );
			var faces = mesh.FaceHandles.Select( f => { mesh.ComputeFaceNormal( f, out var n ); return $"{mesh.GetFaceVertices( f ).Length}:{n.x:0.##},{n.y:0.##},{n.z:0.##}"; } ).OrderBy( x => x );
			return $"{mesh.FaceHandles.Count()} faces [{string.Join( " ", faces )}] {string.Join( " ", verts )}";
		}

		string RenderShape()
		{
			_c.RebuildIfReloaded();
			var m = _c.GetComponent<MeshFilter>().sharedMesh;
			return $"{m.vertexCount} {m.triangles.Length} {m.bounds}";
		}

		static readonly BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

		void Call( string method, params object[] args ) => typeof( HammerMeshTool ).GetMethod( method, Any ).Invoke( _tool, args );

		/// <summary>
		/// A gizmo drag as the tool does it: begin (optionally extruding), move, end.
		/// </summary>
		void Drag( bool extrude, Vector3 delta )
		{
			typeof( HammerMeshTool ).GetField( "_handlePosition", Any ).SetValue( _tool, _tool.SelectionCenter() );
			Call( "BeginTransform", extrude );
			// In steps over several editor frames, like a real drag (Unity turns recorded changes
			// into undo steps at the end of each frame)
			Undo.FlushUndoRecordObjects();
			for ( int i = 1; i <= 4; i++ )
			{
				Call( "ApplyTranslate", delta * (i / 4.0f) );
				HammerMeshTool.RebuildNow( new[] { _c } );
				EditorUtility.SetDirty( _c );
				Undo.FlushUndoRecordObjects();
			}
			Call( "EndTransform" );
			EndFrame();
		}

		void Select( EditMode mode, IEnumerable<IMeshElement> elements )
		{
			_tool.Mode = mode;
			_tool.Selection.Clear();
			foreach ( var e in elements ) _tool.Selection.Add( e );
			EndFrame();
		}

		/// <summary>
		/// What the editor does between user actions: turn recorded changes into undo steps and
		/// start a new undo group.
		/// </summary>
		static void EndFrame()
		{
			Undo.FlushUndoRecordObjects();
			Undo.IncrementCurrentGroup();
		}

		MeshFace Face( Func<S.Vector3, bool> normal ) => new( _c, _c.Mesh.FaceHandles.First( f => { _c.Mesh.ComputeFaceNormal( f, out var n ); return normal( n ); } ) );

		MeshEdge TopEdge() => new( _c, _c.Mesh.HalfEdgeHandles.OrderByDescending( h => _c.Mesh.GetEdgeLine( h ).Center.z * 1000 + _c.Mesh.GetEdgeLine( h ).Center.x ).First() );

		void Op( Action action )
		{
			action();
			EndFrame();
		}

		/// <summary>
		/// Run the steps, then undo them one by one checking each shape, then redo them all.
		/// </summary>
		void Check( params Action[] steps )
		{
			var shapes = new List<string> { Shape() };
			var renders = new List<string> { RenderShape() };

			foreach ( var step in steps )
			{
				step();
				shapes.Add( Shape() );
				renders.Add( RenderShape() );
				Assert.That( shapes[^1], Is.Not.EqualTo( shapes[^2] ), $"step {shapes.Count - 1} changed nothing" );
			}

			for ( int i = steps.Length; i > 0; i-- )
			{
				// Selection changes are undo steps of their own (like Hammer), so keep pressing
				// Ctrl+Z until the mesh changes
				for ( int tries = 0; tries < 4 && Shape() == shapes[i]; tries++ )
					Undo.PerformUndo();

				Assert.That( Shape(), Is.EqualTo( shapes[i - 1] ), $"undoing step {i} should give the shape before it" );
				Assert.That( RenderShape(), Is.EqualTo( renders[i - 1] ), $"undoing step {i} should give the rendered mesh before it" );
			}

			for ( int i = 1; i <= steps.Length; i++ )
			{
				for ( int tries = 0; tries < 4 && Shape() == shapes[i - 1]; tries++ )
					Undo.PerformRedo();

				Assert.That( Shape(), Is.EqualTo( shapes[i] ), $"redoing step {i}" );
			}
		}

		[Test]
		public void ExtrudeSolidEdge()
		{
			Check( () => { Select( EditMode.Edge, new IMeshElement[] { TopEdge() } ); Drag( true, Vector3.up * 0.4f ); } );
		}

		[Test]
		public void EditsThenExtrudeEdge()
		{
			Check(
				() => { Select( EditMode.Face, new IMeshElement[] { Face( n => n.z > 0.9f ) } ); Drag( true, Vector3.up * 0.4f ); },
				() => { Select( EditMode.Vertex, new IMeshElement[] { new MeshVertex( _c, _c.Mesh.VertexHandles.First() ) } ); Drag( false, Vector3.right * 0.2f ); },
				() => { Select( EditMode.Edge, new IMeshElement[] { TopEdge() } ); Drag( true, Vector3.up * 0.4f ); },
				() => { Select( EditMode.Edge, new IMeshElement[] { TopEdge() } ); Drag( false, Vector3.forward * 0.3f ); } );
		}

		[Test]
		public void ExtrudeEdgeTwiceInARow()
		{
			Check(
				() => { Select( EditMode.Edge, new IMeshElement[] { TopEdge() } ); Drag( true, Vector3.up * 0.4f ); },
				() => { Drag( true, Vector3.up * 0.4f ); } );
		}

		[Test]
		public void ExtrudeOpenEdge()
		{
			Check(
				() => Op( () => { Select( EditMode.Face, new IMeshElement[] { Face( n => n.z > 0.9f ) } ); _tool.Delete(); } ),
				() => { Select( EditMode.Edge, new IMeshElement[] { new MeshEdge( _c, _c.Mesh.HalfEdgeHandles.First( h => _c.Mesh.IsEdgeOpen( h ) ) ) } ); Drag( true, Vector3.up * 0.4f ); } );
		}

		[Test]
		public void ExtrudeVertex()
		{
			Check( () => { Select( EditMode.Vertex, new IMeshElement[] { new MeshVertex( _c, _c.Mesh.VertexHandles.First() ) } ); Drag( true, Vector3.up * 0.4f ); } );
		}

		[Test]
		public void SavedDataReloadsToTheSameShape()
		{
			// Edit, then copy the component's saved data into a fresh one (what a scene save +
			// load, a prefab or a domain reload does)
			Select( EditMode.Edge, new IMeshElement[] { TopEdge() } );
			Drag( true, Vector3.up * 0.4f );
			Select( EditMode.Face, new IMeshElement[] { Face( n => n.x > 0.9f ) } );
			_tool.InsetFaces();
			var before = Shape();

			var json = EditorJsonUtility.ToJson( _c );
			var copy = new GameObject( "Copy" ).AddComponent<HammerMesh>();
			try
			{
				EditorJsonUtility.FromJsonOverwrite( json, copy );
				var original = _c;
				_c = copy;
				Assert.That( Shape(), Is.EqualTo( before ) );
				_c = original;
			}
			finally
			{
				Object.DestroyImmediate( copy.gameObject );
			}
		}

		[Test]
		public void Operations()
		{
			Check(
				() => Op( () => { Select( EditMode.Face, new IMeshElement[] { Face( n => n.z > 0.9f ) } ); _tool.InsetFaces(); } ),
				() => Op( () => _tool.ExtrudeFaces() ),
				() => Op( () => { Select( EditMode.Edge, new IMeshElement[] { TopEdge() } ); _tool.QuickBevelEdges(); } ),
				() => Op( () => { Select( EditMode.Face, new IMeshElement[] { Face( n => n.x > 0.9f ) } ); _tool.QuadSlice(); } ),
				() => Op( () => { Select( EditMode.Face, new IMeshElement[] { Face( n => n.y > 0.9f ) } ); _tool.ExtrudeFaces(); } ),
				() => Op( () => { Select( EditMode.Face, new IMeshElement[] { Face( n => n.z < -0.9f ) } ); _tool.Delete(); } ),
				() => Op( () => { Select( EditMode.Edge, _c.Mesh.HalfEdgeHandles.Where( h => _c.Mesh.IsEdgeOpen( h ) ).Take( 1 ).Select( h => (IMeshElement)new MeshEdge( _c, h ) ) ); _tool.FillHole(); } ),
				() => Op( () => { Select( EditMode.Vertex, new IMeshElement[] { new MeshVertex( _c, _c.Mesh.VertexHandles.First() ) } ); _tool.BevelVertices(); } ),
				() => Op( () => { Select( EditMode.Face, new IMeshElement[] { Face( n => n.x < -0.9f ) } ); _tool.FlipFaces(); } ) );
		}
	}
}
