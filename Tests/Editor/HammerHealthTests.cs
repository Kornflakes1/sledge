using System.Linq;
using HammerUnity.EditorTools;
using NUnit.Framework;
using Sandbox.Primitives;
using UnityEditor;
using UnityEngine;
using S = Sandbox;
using FaceProblem = Sandbox.PolygonMesh.FaceProblem;

namespace HammerUnity.Tests
{
	/// <summary>
	/// Broken faces are found, and the fixes fix them.
	/// </summary>
	public class HammerHealthTests
	{
		static S.PolygonMesh Box() => new BlockPrimitive().CreateMesh( new S.BBox( new S.Vector3( -64 ), new S.Vector3( 64 ) ) );

		[Test]
		public void CleanBoxHasNoProblems()
		{
			Assert.That( Box().FindBadFaces(), Is.Empty );
		}

		[Test]
		public void BentFaceIsFoundAndFlattened()
		{
			var mesh = Box();
			var top = mesh.FaceHandles.First( f => { mesh.ComputeFaceNormal( f, out var n ); return n.z > 0.9f; } );
			var v = mesh.GetFaceVertices( top )[0];
			mesh.SetVertexPosition( v, mesh.GetVertexPosition( v ) + new S.Vector3( 0, 0, 24 ) );

			Assert.That( mesh.CheckFace( top ) & FaceProblem.NonPlanar, Is.EqualTo( FaceProblem.NonPlanar ) );
			mesh.MakeFacesPlanar( new[] { top } );
			Assert.That( mesh.CheckFace( top ) & FaceProblem.NonPlanar, Is.EqualTo( FaceProblem.None ) );
		}

		[Test]
		public void FoldedFaceIsFoundAndRemoved()
		{
			// A bow-tie: the outline crosses itself
			var mesh = new S.PolygonMesh();
			var v = mesh.AddVertices( new[] { new S.Vector3( 0, 0, 0 ), new S.Vector3( 64, 64, 0 ), new S.Vector3( 64, 0, 0 ), new S.Vector3( 0, 64, 0 ) } );
			var face = mesh.AddFace( v );

			Assert.That( mesh.CheckFace( face ) & FaceProblem.SelfIntersecting, Is.EqualTo( FaceProblem.SelfIntersecting ) );
			Assert.That( mesh.RemoveBrokenFaces(), Is.EqualTo( 1 ) );
			Assert.That( mesh.FaceHandles.Count(), Is.EqualTo( 0 ) );
		}

		[Test]
		public void ZeroSizeFaceIsFound()
		{
			var mesh = new S.PolygonMesh();
			var v = mesh.AddVertices( new[] { new S.Vector3( 0, 0, 0 ), new S.Vector3( 64, 0, 0 ), new S.Vector3( 128, 0, 0 ) } );
			var face = mesh.AddFace( v );
			Assert.That( mesh.CheckFace( face ) & FaceProblem.Degenerate, Is.EqualTo( FaceProblem.Degenerate ) );
		}

		[Test]
		public void DoubledVerticesWeld()
		{
			// Two quads side by side with their own copies of the shared corners
			var mesh = new S.PolygonMesh();
			var a = mesh.AddVertices( new[] { new S.Vector3( 0, 0, 0 ), new S.Vector3( 64, 0, 0 ), new S.Vector3( 64, 64, 0 ), new S.Vector3( 0, 64, 0 ) } );
			mesh.AddFace( a );
			var b = mesh.AddVertices( new[] { new S.Vector3( 64, 0, 0 ), new S.Vector3( 128, 0, 0 ), new S.Vector3( 128, 64, 0 ), new S.Vector3( 64, 64, 0 ) } );
			mesh.AddFace( b );

			Assert.That( mesh.WeldCoincidentVertices(), Is.EqualTo( 2 ) );
			Assert.That( mesh.VertexHandles.Count(), Is.EqualTo( 6 ) );
			Assert.That( mesh.FindBadFaces(), Is.Empty );
		}

		[Test]
		public void BevelTwiceIsCleanOrReported()
		{
			// Beveling the result of a bevel again: either the geometry stays sound, or the
			// health check reports it so it can be fixed (never silently broken)
			var go = new GameObject( "Bevel Twice" );
			var tool = ScriptableObject.CreateInstance<HammerMeshTool>();
			var grid = HammerSettings.GridSize;
			try
			{
				HammerSettings.GridSize = 16;
				var c = go.AddComponent<HammerMesh>();
				c.Mesh = Box();
				tool.Mode = EditMode.Edge;
				var top = c.Mesh.HalfEdgeHandles.Where( h => { var l = c.Mesh.GetEdgeLine( h ); return l.Start.z > 63 && l.End.z > 63; } ).ToList();
				foreach ( var h in top ) tool.Selection.Add( new MeshEdge( c, h ) );
				tool.QuickBevelEdges();
				tool.QuickBevelEdges();

				var bad = MeshHealth.BadFaces( c );
				if ( bad.Count > 0 )
				{
					Assert.That( MeshHealth.Warning, Is.Not.Null, "broken faces must be reported" );
					tool.Mode = EditMode.Object;
					Selection.activeGameObject = go;
					tool.CleanUp();
					MeshHealth.Forget( c );
					Assert.That( MeshHealth.BadFaces( c ).Values.Count( p => p != FaceProblem.NonPlanar ), Is.EqualTo( 0 ), "clean up removes broken faces" );
				}
			}
			finally
			{
				HammerSettings.GridSize = grid;
				Object.DestroyImmediate( go );
				Object.DestroyImmediate( tool );
				Selection.objects = new Object[0];
			}
		}
	}
}
