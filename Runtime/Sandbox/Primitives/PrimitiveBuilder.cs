namespace Sandbox.Primitives;

/// <summary>
/// Build primitives out of polygons.
/// </summary>
public abstract class PrimitiveBuilder
{
	/// <summary>
	/// A list of vertices and faces.
	/// </summary>
	public sealed class PolygonMesh
	{
		/// <summary>
		/// A list of indices indexing into the <see cref="Vertices"/> list.
		/// </summary>
		public sealed class Face
		{
			private readonly int[] _indices;
			public IReadOnlyList<int> Indices => _indices;
			public string Material { get; set; }

			internal Face( IEnumerable<int> indices )
			{
				_indices = indices.ToArray();
			}
		}

		public List<Vector3> Vertices { get; private init; } = new();
		public List<Face> Faces { get; private init; } = new();

		/// <summary>
		/// Adds a new vertex to the end of the <see cref="Vertices"/> list.
		/// </summary>
		/// <param name="position">Position of the vertex to add.</param>
		/// <returns>The index of the newly added vertex.</returns>
		public int AddVertex( Vector3 position )
		{
			var index = Vertices.FindIndex( x => x.Distance( position ).AlmostEqual( 0.0f ) );
			if ( index >= 0 )
				return index;

			Vertices.Add( position );
			return Vertices.Count - 1;
		}

		/// <summary>
		/// Adds a new face to the end of the <see cref="Faces"/> list.
		/// </summary>
		/// <param name="indices">The vertex indices which define the face, ordered anticlockwise.</param>
		/// <returns>The newly added face.</returns>
		public Face AddFace( params int[] indices )
		{
			if ( indices.Length < 3 )
				return null;

			Faces.Add( new Face( indices ) );
			return Faces[^1];
		}

		/// <summary>
		/// Adds a new face to the end of the <see cref="Faces"/> list and it's vertices to the end of the <see cref="Vertices"/> list.
		/// </summary>
		/// <param name="positions">The vertex positions which define the face, ordered anticlockwise.</param>
		/// <returns>The newly added face.</returns>
		public Face AddFace( params Vector3[] positions )
		{
			if ( positions.Length < 3 )
				return null;

			Faces.Add( new Face( positions.Select( AddVertex ) ) );
			return Faces[^1];
		}

		/// <summary>
		/// Appends another mesh without welding its vertices into this one.
		/// </summary>
		public void AddMesh( PolygonMesh mesh )
		{
			var firstVertex = Vertices.Count;
			Vertices.AddRange( mesh.Vertices );

			foreach ( var face in mesh.Faces )
			{
				Faces.Add( new Face( face.Indices.Select( index => firstVertex + index ) )
				{
					Material = face.Material
				} );
			}
		}
	}

	/// <summary>
	/// Create the primitive in the mesh.
	/// </summary>
	public abstract void Build( PolygonMesh mesh );

	/// <summary>
	/// Setup properties from box.
	/// </summary>
	public abstract void SetFromBox( BBox box );

	/// <summary>
	/// If this primitive is 2D the bounds box will be limited to have no depth.
	/// </summary>
	[Hide]
	public virtual bool Is2D { get => false; }

	/// <summary>
	/// The material to use for this whole primitive. Loaded on demand so builders can be
	/// created without the render system.
	/// </summary>
	[Hide]
	public Material Material
	{
		get => _material ??= Material.Load( "materials/dev/reflectivity_30.vmat" );
		set => _material = value;
	}

	Material _material;

	/// <summary>
	/// Build this primitive into an editable mesh filling <paramref name="box"/>, the same way
	/// the s&amp;box block editor does.
	/// </summary>
	public Sandbox.PolygonMesh CreateMesh( BBox box )
	{
		if ( Is2D )
			box.Maxs.z = box.Mins.z;

		var material = Material;
		var primitive = new PolygonMesh();
		SetFromBox( box );
		Build( primitive );

		var mesh = new Sandbox.PolygonMesh();
		var vertices = mesh.AddVertices( primitive.Vertices.ToArray() );

		foreach ( var face in primitive.Faces )
		{
			var index = mesh.AddFace( face.Indices.Select( x => vertices[x] ).ToArray() );
			mesh.SetFaceMaterial( index, face.Material is null ? material : Material.Load( face.Material ) );
		}

		mesh.TextureAlignToGrid( Transform.Zero );
		mesh.SetSmoothingAngle( 40.0f );

		return mesh;
	}

	/// <summary>
	/// Forward direction of the scene camera, in s&amp;box space. Set by the editor so primitives
	/// like stairs can face away from whoever is placing them.
	/// </summary>
	public static Vector3 CameraForward { get; set; } = Vector3.Forward;
}
