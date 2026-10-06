using HalfEdgeMesh;

namespace Sandbox;

/// <summary>
/// Problems a face can end up with after edits (folded by a bevel, dragged out of flat...), and
/// fixes for them. Not part of s&amp;box; added for the Unity port.
/// </summary>
public partial class PolygonMesh
{
	[Flags]
	public enum FaceProblem
	{
		None = 0,
		/// <summary>No area, or fewer than three distinct corners.</summary>
		Degenerate = 1 << 0,
		/// <summary>Corners not on one plane: the face is bent.</summary>
		NonPlanar = 1 << 1,
		/// <summary>The outline crosses itself: the face is folded over.</summary>
		SelfIntersecting = 1 << 2,
		/// <summary>The face can't be turned into triangles cleanly.</summary>
		BadTriangulation = 1 << 3,
	}

	/// <summary>
	/// What's wrong with a face, if anything. Bent faces count if any corner is further than
	/// <paramref name="planarTolerance"/> units (or 0.5% of the face's size) off its plane.
	/// </summary>
	public FaceProblem CheckFace( FaceHandle hFace, float planarTolerance = 0.05f )
	{
		if ( !hFace.IsValid )
			return FaceProblem.Degenerate;

		var vertices = GetFaceVertices( hFace );
		if ( vertices.Length < 3 )
			return FaceProblem.Degenerate;

		var points = vertices.Select( v => GetVertexPosition( v ) ).ToArray();
		var problems = FaceProblem.None;

		// Newell normal: its length is twice the area
		var newell = Vector3.Zero;
		var center = Vector3.Zero;
		var size = 0.0f;
		for ( int i = 0; i < points.Length; i++ )
		{
			var a = points[i];
			var b = points[(i + 1) % points.Length];
			newell += new Vector3( (a.y - b.y) * (a.z + b.z), (a.z - b.z) * (a.x + b.x), (a.x - b.x) * (a.y + b.y) );
			center += a;
			var length = a.Distance( b );
			size = MathF.Max( size, length );
			if ( length < 1e-4f ) problems |= FaceProblem.Degenerate;
		}

		center /= points.Length;
		var area = newell.Length * 0.5f;
		if ( area < 1e-3f )
		{
			// No area: either flat as a line, or folded so its halves cancel out (a bow-tie)
			problems |= FaceProblem.Degenerate;
			for ( int i = 2; i < points.Length; i++ )
			{
				var cross = (points[1] - points[0]).Cross( points[i] - points[0] );
				if ( cross.Length > 1e-3f && IsOutlineSelfIntersecting( points, cross.Normal ) )
					return problems | FaceProblem.SelfIntersecting;
			}
			return problems;
		}

		var normal = newell.Normal;

		var tolerance = MathF.Max( planarTolerance, size * 0.005f );
		foreach ( var p in points )
		{
			if ( MathF.Abs( (p - center).Dot( normal ) ) > tolerance )
			{
				problems |= FaceProblem.NonPlanar;
				break;
			}
		}

		if ( IsOutlineSelfIntersecting( points, normal ) )
			problems |= FaceProblem.SelfIntersecting;

		if ( !IsFaceShapeValid( hFace ) )
			problems |= FaceProblem.BadTriangulation;

		return problems;
	}

	/// <summary>
	/// Do any two non-neighbouring edges of the outline cross, seen along the normal?
	/// </summary>
	static bool IsOutlineSelfIntersecting( Vector3[] points, Vector3 normal )
	{
		var n = points.Length;
		if ( n < 4 ) return false;

		// Flatten onto the face plane
		var u = MathF.Abs( normal.x ) < 0.9f ? normal.Cross( new Vector3( 1, 0, 0 ) ).Normal : normal.Cross( new Vector3( 0, 1, 0 ) ).Normal;
		var v = normal.Cross( u );
		var flat = points.Select( p => new Vector2( p.Dot( u ), p.Dot( v ) ) ).ToArray();

		static float Cross( Vector2 a, Vector2 b, Vector2 c ) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

		for ( int i = 0; i < n; i++ )
		{
			var a1 = flat[i];
			var a2 = flat[(i + 1) % n];

			for ( int j = i + 2; j < n; j++ )
			{
				if ( i == 0 && j == n - 1 ) continue; // neighbours through the wrap

				var b1 = flat[j];
				var b2 = flat[(j + 1) % n];

				var d1 = Cross( a1, a2, b1 );
				var d2 = Cross( a1, a2, b2 );
				var d3 = Cross( b1, b2, a1 );
				var d4 = Cross( b1, b2, a2 );

				const float eps = 1e-5f;
				if ( ((d1 > eps && d2 < -eps) || (d1 < -eps && d2 > eps)) && ((d3 > eps && d4 < -eps) || (d3 < -eps && d4 > eps)) )
					return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Every face with a problem. Bent quads don't count: a quad always splits into the same two
	/// triangles, so sculpted and displaced surfaces made of them are fine. Bent faces with more
	/// corners can split unpredictably, so those are reported.
	/// </summary>
	public Dictionary<FaceHandle, FaceProblem> FindBadFaces( bool includeNonPlanar = true )
	{
		var result = new Dictionary<FaceHandle, FaceProblem>();
		foreach ( var hFace in FaceHandles )
		{
			var p = CheckFace( hFace );
			if ( !includeNonPlanar || GetFaceVertices( hFace ).Length <= 4 ) p &= ~FaceProblem.NonPlanar;
			if ( p != FaceProblem.None ) result[hFace] = p;
		}
		return result;
	}

	/// <summary>
	/// Remove degenerate, folded and untriangulatable faces, then anything left dangling.
	/// Bent (non-planar) faces are kept: <see cref="MakeFacesPlanar"/> fixes those. Returns how
	/// many faces went.
	/// </summary>
	public int RemoveBrokenFaces()
	{
		var bad = FindBadFaces( includeNonPlanar: false ).Keys.ToList();
		foreach ( var hFace in bad )
			Topology.RemoveFace( hFace, true );

		RemoveBadGeometry();
		IsDirty = true;
		return bad.Count;
	}

	/// <summary>
	/// Flatten faces onto their best-fit plane by moving their corners (which also moves those
	/// corners on neighbouring faces). Faces that are already flat are left alone.
	/// </summary>
	public int MakeFacesPlanar( IEnumerable<FaceHandle> faces )
	{
		var count = 0;
		foreach ( var hFace in faces.ToList() )
		{
			if ( !hFace.IsValid || (CheckFace( hFace ) & FaceProblem.NonPlanar) == 0 )
				continue;

			ComputeFaceNormal( hFace, out var normal );
			var vertices = GetFaceVertices( hFace );
			var center = Vector3.Zero;
			foreach ( var v in vertices ) center += GetVertexPosition( v );
			center /= vertices.Length;

			foreach ( var v in vertices )
			{
				var p = GetVertexPosition( v );
				SetVertexPosition( v, p - normal * (p - center).Dot( normal ) );
			}

			count++;
		}

		if ( count > 0 ) IsDirty = true;
		return count;
	}

	/// <summary>
	/// Merge vertices sitting on top of each other (within <paramref name="distance"/> units).
	/// Returns how many vertices went.
	/// </summary>
	public int WeldCoincidentVertices( float distance = 0.01f )
	{
		var before = VertexHandles.Count();
		MergeVerticesWithinDistance( VertexHandles.ToList(), distance, false, true, out _ );
		var removed = before - VertexHandles.Count();
		if ( removed > 0 ) IsDirty = true;
		return removed;
	}
}
