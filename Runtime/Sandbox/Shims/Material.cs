namespace Sandbox;

/// <summary>
/// Stand-in for the s&amp;box material resource. Faces reference materials by name; the
/// Unity side resolves the name to a <see cref="UnityEngine.Material"/> when baking.
/// </summary>
public sealed class Material
{
	static readonly Dictionary<string, Material> _cache = new();

	/// <summary>
	/// Resolves a material name to a Unity material. Set by the Unity layer.
	/// </summary>
	public static Func<string, UnityEngine.Material> Resolver { get; set; }

	public string Name { get; }

	UnityEngine.Material _asset;

	Material( string name )
	{
		Name = name;
	}

	public UnityEngine.Material Asset
	{
		get
		{
			if ( _asset == null && Resolver is not null )
				_asset = Resolver( Name );

			return _asset;
		}
	}

	/// <summary>
	/// Size in world units of one tile of the main texture at a texture scale of 1.
	/// Mirrors s&amp;box, where a 512 texel texture covers 512 units at scale 1.
	/// </summary>
	public Vector2 TextureSize
	{
		get
		{
			var tex = Asset != null && Asset.HasProperty( "_MainTex" ) ? Asset.mainTexture : null;
			if ( tex == null && Asset != null && Asset.HasProperty( "_BaseMap" ) ) tex = Asset.GetTexture( "_BaseMap" );
			return tex != null ? new Vector2( tex.width, tex.height ) : new Vector2( 512, 512 );
		}
	}

	public static Material Load( string name )
	{
		if ( string.IsNullOrEmpty( name ) )
			return null;

		if ( !_cache.TryGetValue( name, out var material ) )
		{
			material = new Material( name );
			_cache[name] = material;
		}

		return material;
	}

	/// <summary>
	/// Forget resolved Unity assets so they are looked up again (e.g. after the resolver changes).
	/// </summary>
	public static void ClearResolved()
	{
		foreach ( var m in _cache.Values )
			m._asset = null;
	}

	public override string ToString() => Name;
}
