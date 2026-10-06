namespace NativeEngine
{
	public enum GenerateUVsMode_t
	{
		Lscm = 0,
		Planar = 1,
		Conformal = 2,
	}

	public enum AlignEdgeUV_t
	{
		None = 0,
		U = 1,
		V = 2,
	}
}

namespace Sandbox
{
	using NativeEngine;

	/// <summary>
	/// Managed replacement for the native s&amp;box UV generation.
	/// </summary>
	internal static class MeshUtils
	{
		/// <summary>
		/// Flatten a triangle island into UV space. s&amp;box does this natively with LSCM; here the
		/// island is projected onto its area-weighted average plane, which is exact for planar
		/// islands and a reasonable approximation for gently curved ones. If an alignment edge is
		/// given the result is rotated so that edge runs along U or V.
		/// </summary>
		public static Vector2[] GenerateUVsForTriangles(
			Span<Vector3> vertexPositions,
			Span<uint> triangleVertexIndices,
			Span<int> triangleFaceIds,
			GenerateUVsMode_t generationMode = GenerateUVsMode_t.Lscm,
			AlignEdgeUV_t edgeAlignMode = AlignEdgeUV_t.None,
			int alignEdgeVertexIndexA = 0,
			int alignEdgeVertexIndexB = 0 )
		{
			if ( vertexPositions.Length == 0 || triangleVertexIndices.Length == 0 )
				return Array.Empty<Vector2>();

			if ( (triangleVertexIndices.Length % 3) != 0 )
				throw new ArgumentException( "triangleVertexIndices length must be a multiple of 3.", nameof( triangleVertexIndices ) );

			var normal = Vector3.Zero;
			for ( int i = 0; i < triangleVertexIndices.Length; i += 3 )
			{
				var a = vertexPositions[(int)triangleVertexIndices[i]];
				var b = vertexPositions[(int)triangleVertexIndices[i + 1]];
				var c = vertexPositions[(int)triangleVertexIndices[i + 2]];
				normal += Vector3.Cross( b - a, c - a );
			}

			normal = normal.LengthSquared > 1e-12f ? normal.Normal : new Vector3( 0, 0, 1 );

			var axisU = MathF.Abs( normal.z ) > 0.9f ? Vector3.Cross( new Vector3( 0, 1, 0 ), normal ) : Vector3.Cross( new Vector3( 0, 0, 1 ), normal );
			axisU = axisU.Normal;
			var axisV = Vector3.Cross( normal, axisU );

			var uvs = new Vector2[vertexPositions.Length];
			for ( int i = 0; i < uvs.Length; i++ )
				uvs[i] = new Vector2( Vector3.Dot( vertexPositions[i], axisU ), -Vector3.Dot( vertexPositions[i], axisV ) );

			if ( edgeAlignMode != AlignEdgeUV_t.None &&
				alignEdgeVertexIndexA >= 0 && alignEdgeVertexIndexA < uvs.Length &&
				alignEdgeVertexIndexB >= 0 && alignEdgeVertexIndexB < uvs.Length )
			{
				var dir = uvs[alignEdgeVertexIndexB] - uvs[alignEdgeVertexIndexA];
				if ( dir.LengthSquared > 1e-12f )
				{
					var current = MathF.Atan2( dir.y, dir.x );
					var target = edgeAlignMode == AlignEdgeUV_t.U ? 0.0f : MathF.PI * 0.5f;
					var angle = target - current;
					var cos = MathF.Cos( angle );
					var sin = MathF.Sin( angle );
					var pivot = uvs[alignEdgeVertexIndexA];

					for ( int i = 0; i < uvs.Length; i++ )
					{
						var d = uvs[i] - pivot;
						uvs[i] = pivot + new Vector2( d.x * cos - d.y * sin, d.x * sin + d.y * cos );
					}
				}
			}

			return uvs;
		}
	}
}
