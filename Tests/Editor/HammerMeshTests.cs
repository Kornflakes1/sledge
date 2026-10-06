using System.Linq;
using HammerUnity;
using HammerUnity.EditorTools;
using NUnit.Framework;
using Sandbox.Primitives;
using UnityEditor;
using UnityEngine;
using S = Sandbox;

namespace HammerUnity.Tests
{
	public class HammerMeshTests
	{
		GameObject _go;

		[TearDown]
		public void TearDown()
		{
			if ( _go != null )
				Object.DestroyImmediate( _go );
		}

		HammerMesh CreateBox( float size = 64 )
		{
			_go = new GameObject( "Test Box" );
			var component = _go.AddComponent<HammerMesh>();
			var mesh = new BlockPrimitive().CreateMesh( new S.BBox( new S.Vector3( -size / 2 ), new S.Vector3( size / 2 ) ) );
			component.Mesh = mesh;
			return component;
		}

		[Test]
		public void BoxBakesOutwardFacingTriangles()
		{
			var component = CreateBox();
			var mesh = component.RenderMesh;

			Assert.That( mesh, Is.Not.Null );
			Assert.That( mesh.vertexCount, Is.EqualTo( 24 ) );

			var vertices = mesh.vertices;
			var normals = mesh.normals;
			var triangles = mesh.triangles;
			Assert.That( triangles.Length, Is.EqualTo( 36 ) );

			for ( int i = 0; i < triangles.Length; i += 3 )
			{
				var a = vertices[triangles[i]];
				var b = vertices[triangles[i + 1]];
				var c = vertices[triangles[i + 2]];

				// Unity computes face normals as cross(b - a, c - a) for front faces
				var faceNormal = Vector3.Cross( b - a, c - a ).normalized;
				var center = (a + b + c) / 3;

				Assert.That( Vector3.Dot( faceNormal, normals[triangles[i]] ), Is.GreaterThan( 0.99f ), $"triangle {i / 3} winding matches its normal" );
				Assert.That( Vector3.Dot( faceNormal, center ), Is.GreaterThan( 0 ), $"triangle {i / 3} faces outward" );
			}
		}

		[Test]
		public void BoxIsSixtyFourInchesInMetres()
		{
			var component = CreateBox( 64 );
			var size = component.RenderMesh.bounds.size;
			Assert.That( size.x, Is.EqualTo( 64 * SourceSpace.UnitScale ).Within( 1e-4f ) );
			Assert.That( size.y, Is.EqualTo( 64 * SourceSpace.UnitScale ).Within( 1e-4f ) );
			Assert.That( size.z, Is.EqualTo( 64 * SourceSpace.UnitScale ).Within( 1e-4f ) );
		}

		[Test]
		public void CollisionMeshIsBuilt()
		{
			var component = CreateBox();
			var collider = component.GetComponent<MeshCollider>();
			Assert.That( collider, Is.Not.Null );
			Assert.That( collider.sharedMesh, Is.Not.Null );
			Assert.That( collider.sharedMesh.triangles.Length, Is.EqualTo( 36 ) );
		}

		[Test]
		public void MeshSurvivesSerialization()
		{
			var component = CreateBox();
			var top = component.Mesh.FaceHandles.First( f => { component.Mesh.ComputeFaceNormal( f, out var n ); return n.z > 0.9f; } );
			component.Mesh.ExtrudeFaces( new[] { top }, out _, out _, new S.Vector3( 0, 0, 32 ) );
			component.Commit();

			var json = EditorJsonUtility.ToJson( component );

			var other = new GameObject( "Copy" );
			try
			{
				var copy = other.AddComponent<HammerMesh>();
				EditorJsonUtility.FromJsonOverwrite( json, copy );
				Assert.That( copy.Mesh.FaceHandles.Count(), Is.EqualTo( 10 ) );
				copy.RebuildUnityMesh();
				Assert.That( copy.RenderMesh.bounds.size.y, Is.EqualTo( 96 * SourceSpace.UnitScale ).Within( 1e-4f ) );
			}
			finally
			{
				Object.DestroyImmediate( other );
			}
		}

		[Test]
		public void UndoRestoresGeometry()
		{
			var component = CreateBox();
			Undo.ClearAll();

			using ( new MeshUndoScope( "Extrude", new[] { component } ) )
			{
				var top = component.Mesh.FaceHandles.First();
				component.Mesh.ExtrudeFaces( new[] { top }, out _, out _, new S.Vector3( 0, 0, 32 ) );
			}

			Assert.That( component.Mesh.FaceHandles.Count(), Is.EqualTo( 10 ) );

			Undo.PerformUndo();

			Assert.That( component.Mesh.FaceHandles.Count(), Is.EqualTo( 6 ) );
		}

		[Test]
		public void RotationConversionRoundTrips()
		{
			var q = Quaternion.Euler( 20, 75, -30 );
			var back = SourceSpace.ToUnityRotation( SourceSpace.ToSourceRotation( q ) );
			Assert.That( Quaternion.Angle( q, back ), Is.LessThan( 0.01f ) );

			var p = new Vector3( 1.5f, -2, 3 );
			Assert.That( Vector3.Distance( SourceSpace.ToUnityPosition( SourceSpace.ToSourcePosition( p ) ), p ), Is.LessThan( 1e-5f ) );
		}

		[Test]
		public void SourceTransformMatchesUnityTransform()
		{
			var component = CreateBox();
			component.transform.SetPositionAndRotation( new Vector3( 1, 2, 3 ), Quaternion.Euler( 0, 37, 10 ) );

			var local = new S.Vector3( 10, -20, 5 );
			var viaUnity = component.SourceToWorld( local );
			var viaSource = SourceSpace.ToUnityPosition( component.WorldTransform.PointToWorld( local ) );

			Assert.That( Vector3.Distance( viaUnity, viaSource ), Is.LessThan( 1e-4f ) );
		}

		[Test]
		public void RaycastHitsTopFace()
		{
			var component = CreateBox();
			var ray = new Ray( new Vector3( 0, 5, 0 ), Vector3.down );

			Assert.That( MeshPicking.RaycastFace( ray, component, out var hit ), Is.True );
			Assert.That( hit.Point.y, Is.EqualTo( 32 * SourceSpace.UnitScale ).Within( 1e-4f ) );
			Assert.That( hit.Normal.y, Is.GreaterThan( 0.99f ) );

			// From inside, the back faces are culled so nothing is hit
			Assert.That( MeshPicking.RaycastFace( new Ray( Vector3.zero, Vector3.down ), component, out _ ), Is.False );
		}

		[Test]
		public void MaterialsResolveByGuidKey()
		{
			var material = new Material( Shader.Find( "Standard" ) );
			var path = "Assets/__HammerTestMaterial.mat";
			AssetDatabase.CreateAsset( material, path );

			try
			{
				var component = CreateBox();
				var key = HammerMaterials.Get( material );
				Assert.That( key.Name, Does.StartWith( "guid:" ) );

				var face = component.Mesh.FaceHandles.First();
				component.Mesh.SetFaceMaterial( face, key );
				component.Commit();

				var renderer = component.GetComponent<MeshRenderer>();
				Assert.That( renderer.sharedMaterials, Does.Contain( material ) );
			}
			finally
			{
				AssetDatabase.DeleteAsset( path );
			}
		}
	}
}
