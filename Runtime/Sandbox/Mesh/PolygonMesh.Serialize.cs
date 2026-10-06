using System.IO.Compression;
using System.IO;

namespace Sandbox;

/// <summary>
/// Unity-serializable snapshot of a <see cref="PolygonMesh"/>. Mirrors the sections of the
/// s&amp;box mesh blob: topology plus per-element streams. Texture axes/scale/offset are
/// derived from <see cref="TextureCoord"/> on load, same as in s&amp;box.
/// </summary>
[Serializable]
public sealed class PolygonMeshData
{
	public byte[] Topology = Array.Empty<byte>();
	public UnityEngine.Vector3[] Positions = Array.Empty<UnityEngine.Vector3>();
	public UnityEngine.Color32[] Blends = Array.Empty<UnityEngine.Color32>();
	public UnityEngine.Color32[] Colors = Array.Empty<UnityEngine.Color32>();
	public UnityEngine.Vector2[] TextureCoord = Array.Empty<UnityEngine.Vector2>();
	public int[] MaterialIndex = Array.Empty<int>();
	public int[] EdgeFlags = Array.Empty<int>();
	public string[] Materials = Array.Empty<string>();

	public bool IsEmpty => Topology is null || Topology.Length == 0;

	const int PackVersion = 1;

	/// <summary>
	/// Everything as one compressed string. Unity's undo stores a change to an array as one entry
	/// per element, so redoing an edit on a big mesh replayed tens of thousands of them (half a
	/// minute); a string is a single entry. It's also far smaller in scene files.
	/// </summary>
	public string Pack()
	{
		using var ms = new MemoryStream();
		using ( var deflate = new DeflateStream( ms, CompressionLevel.Fastest, true ) )
		using ( var w = new BinaryWriter( deflate ) )
		{
			w.Write( PackVersion );
			w.Write( Topology.Length ); w.Write( Topology );
			w.Write( Positions.Length ); foreach ( var p in Positions ) { w.Write( p.x ); w.Write( p.y ); w.Write( p.z ); }
			w.Write( Blends.Length ); foreach ( var c in Blends ) { w.Write( c.r ); w.Write( c.g ); w.Write( c.b ); w.Write( c.a ); }
			w.Write( Colors.Length ); foreach ( var c in Colors ) { w.Write( c.r ); w.Write( c.g ); w.Write( c.b ); w.Write( c.a ); }
			w.Write( TextureCoord.Length ); foreach ( var t in TextureCoord ) { w.Write( t.x ); w.Write( t.y ); }
			w.Write( MaterialIndex.Length ); foreach ( var i in MaterialIndex ) w.Write( i );
			w.Write( EdgeFlags.Length ); foreach ( var i in EdgeFlags ) w.Write( i );
			w.Write( Materials.Length ); foreach ( var m in Materials ) w.Write( m ?? string.Empty );
		}
		return Convert.ToBase64String( ms.GetBuffer(), 0, (int)ms.Length );
	}

	public static PolygonMeshData Unpack( string packed )
	{
		var data = new PolygonMeshData();
		if ( string.IsNullOrEmpty( packed ) ) return data;

		using var ms = new MemoryStream( Convert.FromBase64String( packed ) );
		using var deflate = new DeflateStream( ms, CompressionMode.Decompress );
		using var r = new BinaryReader( deflate );

		var version = r.ReadInt32();
		if ( version != PackVersion ) throw new InvalidDataException( $"Unknown Hammer mesh data version {version}" );

		data.Topology = r.ReadBytes( r.ReadInt32() );
		data.Positions = new UnityEngine.Vector3[r.ReadInt32()];
		for ( int i = 0; i < data.Positions.Length; i++ ) data.Positions[i] = new UnityEngine.Vector3( r.ReadSingle(), r.ReadSingle(), r.ReadSingle() );
		data.Blends = new UnityEngine.Color32[r.ReadInt32()];
		for ( int i = 0; i < data.Blends.Length; i++ ) data.Blends[i] = new UnityEngine.Color32( r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte() );
		data.Colors = new UnityEngine.Color32[r.ReadInt32()];
		for ( int i = 0; i < data.Colors.Length; i++ ) data.Colors[i] = new UnityEngine.Color32( r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte() );
		data.TextureCoord = new UnityEngine.Vector2[r.ReadInt32()];
		for ( int i = 0; i < data.TextureCoord.Length; i++ ) data.TextureCoord[i] = new UnityEngine.Vector2( r.ReadSingle(), r.ReadSingle() );
		data.MaterialIndex = new int[r.ReadInt32()];
		for ( int i = 0; i < data.MaterialIndex.Length; i++ ) data.MaterialIndex[i] = r.ReadInt32();
		data.EdgeFlags = new int[r.ReadInt32()];
		for ( int i = 0; i < data.EdgeFlags.Length; i++ ) data.EdgeFlags[i] = r.ReadInt32();
		data.Materials = new string[r.ReadInt32()];
		for ( int i = 0; i < data.Materials.Length; i++ ) data.Materials[i] = r.ReadString();
		return data;
	}
}

public partial class PolygonMesh
{
	/// <summary>
	/// Capture this mesh into a form Unity can serialize.
	/// </summary>
	public PolygonMeshData ToData()
	{
		CleanupUnusedMaterials();

		return new PolygonMeshData
		{
			Topology = Topology.Serialize(),
			Positions = Positions.ToArray().Select( x => new UnityEngine.Vector3( x.x, x.y, x.z ) ).ToArray(),
			Blends = Blends.ToArray().Select( ToUnity ).ToArray(),
			Colors = Colors.ToArray().Select( ToUnity ).ToArray(),
			TextureCoord = TextureCoord.ToArray().Select( x => new UnityEngine.Vector2( x.x, x.y ) ).ToArray(),
			MaterialIndex = MaterialIndex.ToArray(),
			EdgeFlags = EdgeFlags.ToArray(),
			Materials = Enumerable.Range( 0, _materialsById.Count )
				.Select( x => _materialsById[x]?.Name ?? string.Empty )
				.ToArray(),
		};
	}

	/// <summary>
	/// Rebuild a mesh from data captured by <see cref="ToData"/>.
	/// </summary>
	public static PolygonMesh FromData( PolygonMeshData data )
	{
		var mesh = new PolygonMesh();

		if ( data is null || data.IsEmpty )
			return mesh;

		using ( var ms = new MemoryStream( data.Topology ) )
		using ( var br = new BinaryReader( ms ) )
		{
			mesh.Topology.Deserialize( br );
		}

		mesh.Positions.CopyFrom( data.Positions.Select( x => new Vector3( x.x, x.y, x.z ) ).ToArray() );
		mesh.Blends.CopyFrom( data.Blends.Select( FromUnity ).ToArray() );
		mesh.Colors.CopyFrom( data.Colors.Select( FromUnity ).ToArray() );
		mesh.TextureCoord.CopyFrom( data.TextureCoord.Select( x => new Vector2( x.x, x.y ) ).ToArray() );
		mesh.MaterialIndex.CopyFrom( data.MaterialIndex );
		mesh.EdgeFlags.CopyFrom( data.EdgeFlags );

		mesh._materialsById.Clear();
		mesh._materialIdsByName.Clear();
		mesh._materialId = 0;

		foreach ( var name in data.Materials ?? Array.Empty<string>() )
			mesh.AddMaterial( Material.Load( name ) );

		if ( data.TextureCoord.Length > 0 )
			mesh.ComputeFaceTextureParametersFromCoordinates();
		else
			mesh.ComputeFaceTextureCoordinatesFromParameters();

		mesh.IsDirty = true;
		return mesh;
	}

	static UnityEngine.Color32 ToUnity( Color32 c ) => new( c.r, c.g, c.b, c.a );
	static Color32 FromUnity( UnityEngine.Color32 c ) => new( c.r, c.g, c.b, c.a );
}
