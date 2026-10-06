using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HammerUnity
{
	/// <summary>
	/// Maps the material names stored on polygon mesh faces to Unity materials.
	/// <para>
	/// Faces store a string key. In the editor the key is <c>guid:&lt;asset guid&gt;</c> so it
	/// survives renames; every <see cref="HammerMesh"/> also serializes its key → material
	/// bindings so the mapping works in builds without the AssetDatabase.
	/// </para>
	/// </summary>
	public static class HammerMaterials
	{
		/// <summary>
		/// Key s&amp;box uses for faces with no explicit material.
		/// </summary>
		public const string DefaultKey = "materials/dev/reflectivity_30.vmat";

		static readonly Dictionary<string, UnityEngine.Material> _registry = new();
		static UnityEngine.Material _default;

		/// <summary>
		/// Fallback lookup, set by the editor to resolve asset GUID keys.
		/// </summary>
		public static Func<string, UnityEngine.Material> EditorResolver { get; set; }

		/// <summary>
		/// Turns a Unity material into a key, set by the editor to produce GUID keys.
		/// </summary>
		public static Func<UnityEngine.Material, string> EditorKeyProvider { get; set; }

		static HammerMaterials()
		{
			Sandbox.Material.Resolver = Resolve;
		}

		public static void Register( string key, UnityEngine.Material material )
		{
			if ( string.IsNullOrEmpty( key ) || material == null )
				return;

			_registry[key] = material;
		}

		public static UnityEngine.Material Resolve( string key )
		{
			if ( string.IsNullOrEmpty( key ) )
				return null;

			if ( key == DefaultKey )
				return Default;

			if ( _registry.TryGetValue( key, out var material ) && material != null )
				return material;

			material = EditorResolver?.Invoke( key );
			if ( material != null )
				_registry[key] = material;

			return material;
		}

		/// <summary>
		/// Get the face material for a Unity material, registering it.
		/// </summary>
		public static Sandbox.Material Get( UnityEngine.Material material )
		{
			if ( material == null )
				return null;

			var key = EditorKeyProvider?.Invoke( material );
			if ( string.IsNullOrEmpty( key ) )
				key = "name:" + material.name;

			Register( key, material );
			return Sandbox.Material.Load( key );
		}

		internal static UnityEngine.Material ResolveOrDefault( Sandbox.Material material )
		{
			// Make sure the resolver is hooked up before the first lookup
			Sandbox.Material.Resolver ??= Resolve;
			return material?.Asset != null ? material.Asset : Default;
		}

		/// <summary>
		/// A grey grid material, like Hammer's dev textures.
		/// </summary>
		/// <summary>
		/// The material asset faces with no material get (set by the editor: s&amp;box's default,
		/// Dev Reflectivity 30). Being an asset, it's saved with the scene and goes into builds;
		/// the made-up grid below is only a fallback without it.
		/// </summary>
		public static UnityEngine.Material DefaultAsset;

		/// <summary>Finds <see cref="DefaultAsset"/> when first needed (the editor sets this).</summary>
		public static System.Func<UnityEngine.Material> DefaultAssetProvider;

		public static UnityEngine.Material Default
		{
			get
			{
				if ( DefaultAsset == null && DefaultAssetProvider != null )
					DefaultAsset = DefaultAssetProvider();
				if ( DefaultAsset != null )
					return DefaultAsset;

				if ( _default != null )
					return _default;

				// Built-in pipeline: use the vertex blend shader so painted colours show up.
				// Scriptable pipelines: their default lit shader.
				var pipeline = GraphicsSettings.currentRenderPipeline;
				var shader = pipeline != null && pipeline.defaultMaterial != null
					? pipeline.defaultMaterial.shader
					: Shader.Find( "Hammer/Vertex Blend" ) ?? Shader.Find( "Standard" );

				// A built game only has the shaders it was built with: try a few that usually are
				if ( shader == null ) shader = Shader.Find( "Unlit/Texture" ) ?? Shader.Find( "Sprites/Default" );
				if ( shader == null ) return null;

				_default = new UnityEngine.Material( shader )
				{
					name = "Hammer Dev Grid",
					hideFlags = HideFlags.DontSave,
					mainTexture = CreateGridTexture(),
				};

				_default.color = Color.white;
				return _default;
			}
		}

		static Texture2D CreateGridTexture()
		{
			const int size = 512;
			const int cell = 64;

			var tex = new Texture2D( size, size, TextureFormat.RGBA32, true )
			{
				name = "Hammer Dev Grid",
				hideFlags = HideFlags.DontSave,
				wrapMode = TextureWrapMode.Repeat,
				filterMode = FilterMode.Trilinear,
				anisoLevel = 8,
			};

			var fill = new Color32( 150, 150, 150, 255 );
			var line = new Color32( 120, 120, 120, 255 );
			var major = new Color32( 95, 95, 95, 255 );
			var pixels = new Color32[size * size];

			for ( int y = 0; y < size; y++ )
			{
				for ( int x = 0; x < size; x++ )
				{
					var c = fill;
					if ( x % cell == 0 || y % cell == 0 ) c = line;
					if ( x == 0 || y == 0 || x == size - 1 || y == size - 1 ) c = major;
					pixels[y * size + x] = c;
				}
			}

			tex.SetPixels32( pixels );
			tex.Apply( true, false );
			return tex;
		}
	}
}
