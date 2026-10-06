using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// While a player builds, each scene's Hammer meshes are baked with every face showing: faces
	/// hidden while editing (H) belong in the game.
	/// </summary>
	sealed class HammerBuildProcessor : IProcessSceneWithReport
	{
		public int callbackOrder => 0;

		public void OnProcessScene( Scene scene, BuildReport report )
		{
			// Also called when entering play mode (no report); the meshes handle that themselves
			if ( report == null ) return;

			HammerMesh.BuildingPlayer = true;
			try
			{
				foreach ( var root in scene.GetRootGameObjects() )
					foreach ( var mesh in root.GetComponentsInChildren<HammerMesh>( true ) )
						mesh.ShowHiddenFacesForBuild();
			}
			finally
			{
				HammerMesh.BuildingPlayer = false;
			}
		}
	}
}
