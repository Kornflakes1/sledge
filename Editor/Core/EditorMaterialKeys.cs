using UnityEditor;
using UnityEngine;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// In the editor, faces reference materials by asset GUID so renaming or moving a material
	/// doesn't break meshes.
	/// </summary>
	[InitializeOnLoad]
	static class EditorMaterialKeys
	{
		const string GuidPrefix = "guid:";

		static EditorMaterialKeys()
		{
			HammerMaterials.EditorKeyProvider = KeyFor;
			HammerMaterials.EditorResolver = Resolve;
			HammerMaterials.DefaultAssetProvider = () => AssetDatabase.LoadAssetAtPath<Material>( "Packages/com.hammerunity.meshtools/Runtime/DevTextures/Dev Reflectivity 30.mat" );
		}

		static string KeyFor( Material material )
		{
			if ( material == null )
				return null;

			var path = AssetDatabase.GetAssetPath( material );
			if ( string.IsNullOrEmpty( path ) )
				return null;

			return GuidPrefix + AssetDatabase.AssetPathToGUID( path );
		}

		static Material Resolve( string key )
		{
			if ( key == null || !key.StartsWith( GuidPrefix ) )
				return null;

			var path = AssetDatabase.GUIDToAssetPath( key.Substring( GuidPrefix.Length ) );
			return string.IsNullOrEmpty( path ) ? null : AssetDatabase.LoadAssetAtPath<Material>( path );
		}
	}
}
