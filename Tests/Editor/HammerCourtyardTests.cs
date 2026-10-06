using System.Linq;
using HammerUnity.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace HammerUnity.Tests
{
	/// <summary>
	/// The example garden builds, and every mesh in it comes out sound.
	/// </summary>
	public class HammerCourtyardTests
	{
		GameObject _root;

		[OneTimeSetUp]
		public void Build() => _root = HammerTutorialMap.BuildCourtyard();

		[OneTimeTearDown]
		public void TearDown()
		{
			if ( _root != null ) Object.DestroyImmediate( _root );
			Selection.objects = new Object[0];
		}

		[Test]
		public void EveryMeshIsSound()
		{
			var meshes = _root.GetComponentsInChildren<HammerMesh>();
			Assert.Greater( meshes.Length, 60 );
			foreach ( var m in meshes )
			{
				Assert.Greater( m.Mesh.FaceHandles.Count(), 0, m.name );
				var bad = MeshHealth.BadFaces( m );
				Assert.AreEqual( 0, bad.Count, $"{m.name} has broken faces: " + string.Join( ", ", bad.Select( b => $"{b.Value} at {m.Mesh.GetFaceCenter( b.Key )} ({m.Mesh.GetFaceVertices( b.Key ).Length} sides)" ) ) );
				Assert.IsTrue( m.GetComponent<MeshRenderer>().sharedMaterials.All( x => x != null ), $"{m.name} is missing a material" );
			}
		}

		[Test]
		public void TheGardenHasItsPieces()
		{
			var meshes = _root.GetComponentsInChildren<HammerMesh>();
			Assert.Greater( meshes.First( m => m.name == "Fountain" ).Mesh.FaceHandles.Count(), 1000, "the fountain's turned tiers" );
			Assert.Greater( meshes.Count( m => m.name == "Tree" ), 15 );
			Assert.IsTrue( meshes.Any( m => m.name == "Triumphal Arch" ) );
			Assert.IsTrue( meshes.Any( m => m.name.StartsWith( "Front Wall" ) ) );
		}
	}
}
