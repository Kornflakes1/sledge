using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using PolygonMesh = Sandbox.PolygonMesh;

namespace HammerUnity
{
	/// <summary>
	/// Converts the triangulated output of a <see cref="PolygonMesh"/> into Unity meshes.
	/// </summary>
	internal static class MeshBaker
	{
		// s&box faces are counter-clockwise in a right-handed space. Mirroring into Unity's
		// left-handed space keeps the geometry but reverses the apparent winding, which is
		// exactly Unity's clockwise front-face rule, so indices need swapping to stay outward.
		static readonly bool FlipWinding = true;

		static readonly List<Vector3> _positions = new();
		static readonly List<Vector3> _normals = new();
		static readonly List<Vector4> _tangents = new();
		static readonly List<Vector2> _uvs = new();
		static readonly List<Color32> _colors = new();
		static readonly List<Vector4> _blends = new();
		static readonly List<int> _indices = new();

		/// <summary>
		/// Fill <paramref name="target"/> from the last <see cref="PolygonMesh.Rebuild"/>, one
		/// submesh per material. Returns the materials in submesh order.
		/// </summary>
		public static UnityEngine.Material[] Bake( PolygonMesh mesh, UnityEngine.Mesh target )
		{
			target.Clear();

			_positions.Clear();
			_normals.Clear();
			_tangents.Clear();
			_uvs.Clear();
			_colors.Clear();
			_blends.Clear();

			var submeshes = mesh.Submeshes;
			var materials = new List<UnityEngine.Material>( submeshes.Count );
			var descriptors = new List<(int start, int count)>( submeshes.Count );
			var allIndices = new List<int>();

			foreach ( var submesh in submeshes )
			{
				if ( submesh.Vertices.Count < 3 || submesh.Indices.Count < 3 )
					continue;

				int baseVertex = _positions.Count;

				foreach ( var v in submesh.Vertices )
				{
					_positions.Add( SourceSpace.ToUnityPosition( v.Position ) );
					_normals.Add( SourceSpace.ToUnityDirection( v.Normal ) );

					var t = SourceSpace.ToUnityDirection( new Sandbox.Vector3( v.Tangent.x, v.Tangent.y, v.Tangent.z ) );
					// Mirroring flips the handedness of the tangent frame
					_tangents.Add( new Vector4( t.x, t.y, t.z, -v.Tangent.w ) );

					_uvs.Add( SourceSpace.ToUnityUV( v.Texcoord ) );
					_colors.Add( new Color32( v.Color.r, v.Color.g, v.Color.b, v.Color.a ) );
					_blends.Add( new Vector4( v.Blend.r, v.Blend.g, v.Blend.b, v.Blend.a ) / 255.0f );
				}

				int start = allIndices.Count;
				var indices = submesh.Indices;

				for ( int i = 0; i + 2 < indices.Count; i += 3 )
				{
					allIndices.Add( baseVertex + indices[i] );
					if ( FlipWinding )
					{
						allIndices.Add( baseVertex + indices[i + 2] );
						allIndices.Add( baseVertex + indices[i + 1] );
					}
					else
					{
						allIndices.Add( baseVertex + indices[i + 1] );
						allIndices.Add( baseVertex + indices[i + 2] );
					}
				}

				descriptors.Add( (start, allIndices.Count - start) );
				materials.Add( HammerMaterials.ResolveOrDefault( submesh.Material ) );
			}

			target.indexFormat = _positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
			target.SetVertices( _positions );
			target.SetNormals( _normals );
			target.SetTangents( _tangents );
			target.SetUVs( 0, _uvs );
			target.SetColors( _colors );
			target.SetUVs( 3, _blends );

			target.subMeshCount = descriptors.Count;
			for ( int i = 0; i < descriptors.Count; i++ )
			{
				_indices.Clear();
				for ( int j = 0; j < descriptors[i].count; j++ )
					_indices.Add( allIndices[descriptors[i].start + j] );

				target.SetTriangles( _indices, i, false );
			}

			target.RecalculateBounds();
			return materials.ToArray();
		}

		/// <summary>
		/// Fill <paramref name="target"/> with the welded collision geometry from the last rebuild.
		/// </summary>
		public static void BakeCollision( PolygonMesh mesh, UnityEngine.Mesh target )
		{
			target.Clear();

			var vertices = mesh.CollisionVertices;
			var indices = mesh.CollisionIndices;

			_positions.Clear();
			foreach ( var v in vertices )
				_positions.Add( SourceSpace.ToUnityPosition( v ) );

			_indices.Clear();
			for ( int i = 0; i + 2 < indices.Count; i += 3 )
			{
				_indices.Add( indices[i] );
				_indices.Add( FlipWinding ? indices[i + 2] : indices[i + 1] );
				_indices.Add( FlipWinding ? indices[i + 1] : indices[i + 2] );
			}

			target.indexFormat = _positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
			target.SetVertices( _positions );
			target.SetTriangles( _indices, 0, true );
		}
	}
}
