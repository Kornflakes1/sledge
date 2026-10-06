using System.IO;
using NUnit.Framework;
using Sandbox.Primitives;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using S = Sandbox;

namespace HammerUnity.Tests
{
	/// <summary>
	/// Opening a saved scene with Hammer meshes must not count as changing it (otherwise Unity
	/// asks to save every time you switch scenes).
	/// </summary>
	public class HammerSceneTests
	{
		const string Path = "Assets/__HammerSceneTest.unity";

		[TearDown]
		public void TearDown()
		{
			AssetDatabase.DeleteAsset( Path );
		}

		[Test]
		public void ReopenedSceneIsNotDirty()
		{
			var scene = EditorSceneManager.NewScene( NewSceneSetup.EmptyScene, NewSceneMode.Single );
			try
			{
				var go = new GameObject( "Box" );
				SceneManager.MoveGameObjectToScene( go, scene );
				var c = go.AddComponent<HammerMesh>();
				c.Mesh = new BlockPrimitive().CreateMesh( new S.BBox( new S.Vector3( -32 ), new S.Vector3( 32 ) ) );
				Assert.That( EditorSceneManager.SaveScene( scene, Path ), Is.True );
			}
			finally
			{
				EditorSceneManager.NewScene( NewSceneSetup.EmptyScene, NewSceneMode.Single );
			}

			var reopened = EditorSceneManager.OpenScene( Path, OpenSceneMode.Single );
			try
			{
				var mesh = Object.FindFirstObjectByType<HammerMesh>();
				Assert.That( mesh, Is.Not.Null );
				Assert.That( mesh.GetComponent<MeshFilter>().sharedMesh, Is.Not.Null, "the baked mesh came back with the scene" );
				Assert.That( mesh.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThan( 0 ) );
				Assert.That( reopened.isDirty, Is.False, "opening the scene marked it as changed" );

				// A duplicate must get its own mesh, not edit the original's
				var copy = Object.Instantiate( mesh.gameObject );
				SceneManager.MoveGameObjectToScene( copy, reopened );
				Assert.That( copy.GetComponent<MeshFilter>().sharedMesh, Is.Not.SameAs( mesh.GetComponent<MeshFilter>().sharedMesh ) );
			}
			finally
			{
				EditorSceneManager.NewScene( NewSceneSetup.EmptyScene, NewSceneMode.Single );
			}
		}
	}
}
