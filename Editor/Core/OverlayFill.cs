using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using FaceHandle = HalfEdgeMesh.FaceHandle;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Tints over a mesh's faces (selected faces, a whole selected object) drawn as one mesh,
	/// depth tested against the scene. Drawing them a triangle at a time, each face first
	/// checked for being hidden behind something, took seconds on big meshes.
	/// </summary>
	public static class OverlayFill
	{
		sealed class Entry
		{
			public object Source;
			public int Triangulation;
			public int Key;
			public Mesh Mesh;
		}

		static readonly Dictionary<(HammerMesh, int), Entry> _cache = new();
		static Material _material;
		static readonly int ColorId = Shader.PropertyToID( "_Color" );
		static readonly int ZTestId = Shader.PropertyToID( "_ZTest" );

		static Material Material
		{
			get
			{
				if ( _material == null )
				{
					var shader = Shader.Find( "Hidden/Hammer/Overlay" );
					if ( shader == null ) return null;
					_material = new Material( shader ) { hideFlags = HideFlags.HideAndDontSave };
				}
				return _material;
			}
		}

		/// <summary>
		/// Tint the faces <paramref name="include"/> picks. <paramref name="slot"/> keeps different
		/// tints of one mesh apart; <paramref name="key"/> changes when the picked faces do.
		/// </summary>
		public static void Draw( HammerMesh component, int slot, int key, Func<FaceHandle, bool> include, Color color, CompareFunction zTest = CompareFunction.LessEqual )
		{
			if ( component == null || Event.current?.type != EventType.Repaint ) return;
			var material = Material;
			if ( material == null ) return;

			var polygon = component.Mesh;
			if ( !_cache.TryGetValue( (component, slot), out var entry ) || entry.Mesh == null
				|| !ReferenceEquals( entry.Source, polygon ) || entry.Triangulation != polygon.TriangulationVersion || entry.Key != key )
			{
				if ( entry?.Mesh != null ) UnityEngine.Object.DestroyImmediate( entry.Mesh );
				entry = new Entry { Source = polygon, Triangulation = polygon.TriangulationVersion, Key = key, Mesh = Build( polygon, include ) };
				_cache[(component, slot)] = entry;
			}

			if ( entry.Mesh.vertexCount == 0 ) return;

			SetPass( color, zTest );
			Graphics.DrawMeshNow( entry.Mesh, component.transform.localToWorldMatrix );
		}

		/// <summary>
		/// Set up drawing in a flat colour (for GL drawing of other overlays).
		/// </summary>
		public static bool SetPass( Color color, CompareFunction zTest )
		{
			var material = Material;
			if ( material == null ) return false;
			material.SetColor( ColorId, color );
			material.SetFloat( ZTestId, (float)zTest );
			material.SetPass( 0 );
			return true;
		}

		static Mesh Build( Sandbox.PolygonMesh polygon, Func<FaceHandle, bool> include )
		{
			var faces = polygon.TriangleFaces;
			var indices = polygon.CollisionIndices;
			var vertices = polygon.CollisionVertices;
			var positions = new List<Vector3>();
			var triangles = new List<int>();
			var normals = new Dictionary<int, Vector3>();

			for ( int tri = 0; tri < faces.Count && tri * 3 + 2 < indices.Count; tri++ )
			{
				var face = faces[tri];
				if ( polygon.IsFaceHidden( face ) || !include( face ) ) continue;

				if ( !normals.TryGetValue( face.Index, out var normal ) )
				{
					polygon.ComputeFaceNormal( face, out var n );
					normals[face.Index] = normal = SourceSpace.ToUnityDirection( n );
				}

				var a = SourceSpace.ToUnityPosition( vertices[indices[tri * 3]] );
				var b = SourceSpace.ToUnityPosition( vertices[indices[tri * 3 + 1]] );
				var c = SourceSpace.ToUnityPosition( vertices[indices[tri * 3 + 2]] );

				// Unity's front faces wind clockwise as seen: point them out the way the face does
				if ( Vector3.Dot( Vector3.Cross( b - a, c - a ), normal ) < 0 ) (b, c) = (c, b);

				var i = positions.Count;
				positions.Add( a );
				positions.Add( b );
				positions.Add( c );
				triangles.Add( i );
				triangles.Add( i + 1 );
				triangles.Add( i + 2 );
			}

			var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
			mesh.SetVertices( positions );
			mesh.SetTriangles( triangles, 0 );
			return mesh;
		}
	}
}
